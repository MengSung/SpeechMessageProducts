// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/DynamicsProfileRuntimeFactoryTests.cs
// 目的：驗證 crm82／crm91 與不同 Generation 的 Client、Transport、Token Provider、Handler、
//       Cancellation 與 Admission Registration 都有明確且互不共享的生命週期擁有者。
//
// 測試不連真實 CRM／ADFS；所有 Runtime 都關閉 Warm-up，只檢查物件身分、非秘密 Key 與 Dispose 結果。
// ============================================================================

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 固定 Multi-Profile Runtime Factory 的隔離與 rollback 契約。
/// 每個 Generation 必須擁有不同的 Dynamics Client／HTTP Handler／Token Provider，即使兩個 Profile 指向同一 Organization，
/// 也只能共用 Admission Manager；任何可變 Credential、Token Cache、Socket、Cancellation 或 Session 狀態都不可共用。
/// </summary>
public sealed class DynamicsProfileRuntimeFactoryTests
{
    /// <summary>
    /// 證明 crm82 與 crm91 建立出的 Client、Transport、Token Provider、主要 HTTP Handler 與 Token HTTP Handler 都不同物件。
    /// 這是避免跨版本 Token／Cookie／Credential／Socket 狀態污染的核心發布門檻。
    /// </summary>
    [Fact]
    public async Task Crm82_and_crm91_generations_own_distinct_clients_tokens_transports_and_handlers()
    {
        await using var registry = CreateRegistry();
        var factory = CreateFactory(registry);

        await using var crm82 = await factory.CreateAsync(CreateDefinition(
            "crm82",
            "8.2",
            "https://crm82.example.test/Org82/",
            Guid.Parse("82828282-8282-8282-8282-828282828282")), generation: 1, CancellationToken.None);
        await using var crm91 = await factory.CreateAsync(CreateDefinition(
            "crm91",
            "9.1",
            "https://crm91.example.test/Org91/",
            Guid.Parse("91919191-9191-9191-9191-919191919191")), generation: 1, CancellationToken.None);

        var crm82Client = GetPrivateField<IDynamicsWebApiClient>(crm82, "_client");
        var crm91Client = GetPrivateField<IDynamicsWebApiClient>(crm91, "_client");
        var crm82Transport = GetPrivateField<DynamicsHttpTransport>(crm82, "_transport");
        var crm91Transport = GetPrivateField<DynamicsHttpTransport>(crm91, "_transport");
        var crm82TokenProvider = GetPrivateField<AdfsOAuthTokenProvider>(crm82, "_tokenProvider");
        var crm91TokenProvider = GetPrivateField<AdfsOAuthTokenProvider>(crm91, "_tokenProvider");

        crm82Client.Should().NotBeSameAs(crm91Client);
        crm82Transport.Should().NotBeSameAs(crm91Transport);
        crm82TokenProvider.Should().NotBeSameAs(crm91TokenProvider);
        GetHttpHandler(crm82Transport).Should().NotBeSameAs(GetHttpHandler(crm91Transport));
        GetPrivateField<HttpClient>(crm82TokenProvider, "_ownedHttpClient")
            .Should().NotBeSameAs(GetPrivateField<HttpClient>(crm91TokenProvider, "_ownedHttpClient"));
    }

    /// <summary>
    /// 證明 Runtime Dispose 的順序會先停止新執行、等待租約歸零，再回收 Token／Transport，最後釋放 Admission Registration。
    /// 重複 Dispose 不可再次回收相同 Handler 或讓 Registry ReferenceCount 低於零。
    /// </summary>
    [Fact]
    public async Task Disposing_generation_disposes_transport_token_provider_and_admission_registration_once()
    {
        await using var registry = CreateRegistry();
        var factory = CreateFactory(registry);
        var runtime = await factory.CreateAsync(CreateDefinition(
            "crm91",
            "9.1",
            "https://crm91.example.test/Org/",
            Guid.Parse("11111111-aaaa-bbbb-cccc-111111111111")), generation: 1, CancellationToken.None);
        var transport = GetPrivateField<DynamicsHttpTransport>(runtime, "_transport");
        var tokenProvider = GetPrivateField<AdfsOAuthTokenProvider>(runtime, "_tokenProvider");

        registry.EntryCount.Should().Be(1);
        await runtime.DisposeAsync();
        await runtime.DisposeAsync();
        runtime.Dispose();

        registry.EntryCount.Should().Be(0);
        var transportAct = async () => await transport.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://crm91.example.test/"),
            CancellationToken.None);
        var tokenAct = async () => await tokenProvider.GetAccessTokenAsync();
        await transportAct.Should().ThrowAsync<ObjectDisposedException>();
        await tokenAct.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>
    /// 以真正的 <see cref="DynamicsProfileRuntimeFactory"/>、<see cref="DynamicsProfileRuntime"/> 與
    /// <see cref="DynamicsProfileRuntimeManager"/> 驗證「發布後 drain 被 caller 取消，下一次 Replace 重試同一 Runtime」的整合路徑。
    /// 第一個 drain failure 必須讓 Production Runtime 清除自己快取的 faulted <c>_drainTask</c>，但保持 Draining state 與
    /// Execution Lease ownership；第二次 Replace 在舊 Lease 釋放前不得建立 Generation 3，釋放後則必須重新建立 drain attempt、
    /// 完成 Token／Transport／Admission Registration cleanup，再安全發布下一代。測試不連 CRM／ADFS，也不使用真實秘密。
    /// </summary>
    [Fact]
    public async Task Manager_retries_the_real_runtime_after_cancelled_drain_without_allocating_a_third_generation_early()
    {
        await using var registry = CreateRegistry();
        var recordingFactory = new RecordingRuntimeFactory(CreateFactory(registry));
        var initialDefinition = CreateDefinition(
            "crm91",
            "9.1",
            "https://crm91.example.test/Org/",
            Guid.Parse("33333333-aaaa-bbbb-cccc-333333333333"),
            drainTimeout: TimeSpan.FromSeconds(30),
            cancellationGracePeriod: TimeSpan.FromSeconds(30));
        await using var manager = new DynamicsProfileRuntimeManager(
            [initialDefinition],
            recordingFactory);
        await manager.InitializeAsync(CancellationToken.None);
        var originalRuntime = recordingFactory.GetRuntime("crm91", 1);
        originalRuntime.Should().BeOfType<DynamicsProfileRuntime>();
        originalRuntime.TryAcquireExecution(out var heldOriginalLease).Should().BeTrue();
        using var firstReplacementCts = new CancellationTokenSource();
        try
        {
            var firstReplacement = manager.ReplaceAsync(
                initialDefinition,
                firstReplacementCts.Token);
            await WaitUntilAsync(
                () => originalRuntime.State == DynamicsProfileRuntimeState.Draining,
                "production runtime entering the first drain attempt");
            firstReplacementCts.Cancel();

            var observeFirstFailure = async () => await firstReplacement;
            await observeFirstFailure.Should().ThrowAsync<OperationCanceledException>();
            originalRuntime.State.Should().Be(DynamicsProfileRuntimeState.Draining);
            recordingFactory.CreateCount.Should().Be(2);

            var secondReplacement = manager.ReplaceAsync(initialDefinition);

            secondReplacement.IsCompleted.Should().BeFalse();
            recordingFactory.CreateCount.Should().Be(2);
            await heldOriginalLease!.DisposeAsync();
            await secondReplacement;

            originalRuntime.State.Should().Be(DynamicsProfileRuntimeState.Disposed);
            recordingFactory.CreateCount.Should().Be(3);
            recordingFactory.GetRuntime("crm91", 3)
                .State.Should().Be(DynamicsProfileRuntimeState.Active);
        }
        finally
        {
            // 若刻意破壞 Production _drainTask 重設邏輯，第二次 Replace 會提前失敗；
            // 測試仍必須釋放原始 Lease，避免測試本身把 Handler／Registration cleanup 卡到 timeout。
            await heldOriginalLease!.DisposeAsync();
        }

        await manager.DisposeAsync();
        registry.EntryCount.Should().Be(0);
    }

    /// <summary>
    /// 證明 Runtime Key 只包含 Alias、Generation、CE Version 與 Canonical Organization Identity，
    /// 不得把 User、LINE ID、Browser Session、JWT、Token、Password 或 Credential Reference 變成 Pool／Cache Key。
    /// </summary>
    [Fact]
    public async Task Runtime_key_contains_no_user_session_jwt_token_or_credential_value()
    {
        const string secretMarker = "credential-marker-must-not-enter-runtime-key";
        await using var registry = CreateRegistry();
        var factory = CreateFactory(registry);
        var definition = CreateDefinition(
            "crm91",
            "9.1",
            "https://crm91.example.test/Org/",
            Guid.Parse("22222222-aaaa-bbbb-cccc-222222222222"),
            credentialReferenceName: secretMarker);

        await using var runtime = await factory.CreateAsync(definition, generation: 7, CancellationToken.None);

        runtime.Key.ProfileAlias.Should().Be("crm91");
        runtime.Key.Generation.Should().Be(7);
        runtime.Key.ToString().Should().NotContain(secretMarker);
        var normalizedKeyText = runtime.Key.ToString().ToLowerInvariant();
        normalizedKeyText.Should().NotContain("session");
        normalizedKeyText.Should().NotContain("jwt");
        normalizedKeyText.Should().NotContain("token");
        normalizedKeyText.Should().NotContain("password");
    }

    /// <summary>
    /// 建立測試用 Admission Registry。In-memory coordinator 不連外，也不保存跨測試 Host 的 static 狀態。
    /// </summary>
    private static OrganizationAdmissionRegistry CreateRegistry()
        => new(
            new InMemoryRuntimeHostSlotCoordinator(),
            NullLogger<OrganizationAdmissionRegistry>.Instance,
            NullLogger<OrganizationAdmissionManager>.Instance);

    /// <summary>
    /// 建立 Runtime Factory，使用空的 Secret Resolver 與 Null LoggerFactory，確保測試只觀察 ownership，
    /// 不把任何真實 Credential／Token／Request Body 寫入記錄或記憶體快取。
    /// </summary>
    private static DynamicsProfileRuntimeFactory CreateFactory(IOrganizationAdmissionRegistry registry)
        => new(
            registry,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            NullLoggerFactory.Instance);

    /// <summary>
    /// 建立不可變 Profile Definition。所有 Namespace 都由 Organization GUID 推導成測試識別，
    /// 避免不同測試 Organization 因重用 Namespace 而觸發非本案例要驗證的反向碰撞。
    /// </summary>
    private static DynamicsProfileDefinition CreateDefinition(
        string alias,
        string ceVersion,
        string organizationBaseUri,
        Guid organizationId,
        string? credentialReferenceName = null,
        TimeSpan? drainTimeout = null,
        TimeSpan? cancellationGracePeriod = null)
    {
        var namespaceSuffix = organizationId.ToString("N");
        return new DynamicsProfileDefinition(
            alias,
            new DynamicsWebApiOptions
            {
                OrganizationBaseUri = organizationBaseUri,
                CeVersion = ceVersion,
                AuthMode = DynamicsAuthMode.Windows,
                CredentialSource = DynamicsCredentialSource.HostIdentity,
                CredentialReferenceName = credentialReferenceName,
                MaxConnectionsPerServer = 2,
                Admission = new OrganizationAdmissionOptions
                {
                    ExpectedOrganizationId = organizationId,
                    AggregateMaxInFlight = 24,
                    MaximumRuntimeHosts = 6,
                    AdmissionNamespaceId = "admission-" + namespaceSuffix,
                    LeaseNamespaceId = "lease-" + namespaceSuffix,
                    RequireDurableHostCoordinator = false
                }
            },
            warmUpOnActivation: false,
            drainTimeout,
            cancellationGracePeriod);
    }

    /// <summary>
    /// 等待非同步生命週期狀態在固定兩秒內出現。輪詢只讀取 Runtime 的受鎖 bounded state，
    /// 不保存 Client、Token、Credential、Request 或 Execution Lease，也不以任意長時間 Sleep 掩蓋競態。
    /// </summary>
    /// <param name="condition">完成時回傳 true 的無副作用狀態條件。</param>
    /// <param name="description">逾時時只描述測試生命週期階段，不含 Endpoint、Secret 或使用者資料。</param>
    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {description}.");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 測試用 Factory decorator。實際 Runtime ownership graph 完全由 Production
    /// <see cref="DynamicsProfileRuntimeFactory"/> 建立；decorator 只在短鎖內記錄 Alias／Generation 與物件參考，
    /// 讓測試可取得真實 Runtime 並控制 Execution Lease。它不建立、不 Dispose、不代理 Client／Token／Handler，
    /// 所有最終 cleanup 仍只有 <see cref="DynamicsProfileRuntimeManager"/> 一個 owner。
    /// </summary>
    private sealed class RecordingRuntimeFactory : IDynamicsProfileRuntimeFactory
    {
        private readonly object _gate = new();
        private readonly IDynamicsProfileRuntimeFactory _inner;
        private readonly Dictionary<(string Alias, long Generation), IDynamicsProfileRuntime> _runtimes = new();

        /// <summary>
        /// 建立只負責記錄 Production Factory 結果的 decorator；不接管 inner factory 或其 Runtime 資源 ownership。
        /// </summary>
        public RecordingRuntimeFactory(IDynamicsProfileRuntimeFactory inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// 取得目前 Production Factory 已成功建立的 Runtime 數量；讀取在短鎖內完成，
        /// 用來證明 pending Draining 收斂前沒有配置 Generation 3。
        /// </summary>
        public int CreateCount
        {
            get
            {
                lock (_gate)
                {
                    return _runtimes.Count;
                }
            }
        }

        /// <summary>
        /// 委派 Production Factory 建立完整 Runtime，成功後才記錄非秘密 Alias／Generation key 與 Runtime reference。
        /// 建立失敗時不記錄半完成物件；所有 rollback 仍由 Production Factory 自己確定性處理。
        /// </summary>
        public async Task<IDynamicsProfileRuntime> CreateAsync(
            DynamicsProfileDefinition definition,
            long generation,
            CancellationToken cancellationToken)
        {
            var runtime = await _inner
                .CreateAsync(definition, generation, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                _runtimes.Add((definition.ProfileAlias, generation), runtime);
            }

            return runtime;
        }

        /// <summary>
        /// 依 Alias／Generation 取得已由 Manager 擁有的真實 Runtime；只供測試控制 Lease 與觀察 state，
        /// 呼叫端不得直接 Dispose，避免與 Manager 的 Drain／Shutdown ownership 競爭。
        /// </summary>
        public IDynamicsProfileRuntime GetRuntime(string alias, long generation)
        {
            lock (_gate)
            {
                return _runtimes[(alias, generation)];
            }
        }
    }

    /// <summary>
    /// 透過反射讀取 Runtime／Token Provider 的私有 ownership 欄位。
    /// 測試只比較物件身分，不讀取 Token、Credential 或其他敏感值，也不要求生產 API 暴露除錯用資源。
    /// </summary>
    private static T GetPrivateField<T>(object owner, string fieldName)
        where T : class
    {
        var field = owner.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"{owner.GetType().Name} 必須明確持有 {fieldName}");
        var value = field!.GetValue(owner) as T;
        value.Should().NotBeNull();
        return value!;
    }

    /// <summary>
    /// 取得 DynamicsHttpTransport 內由 HttpClient 擁有的 Primary Handler，
    /// 用來證明不同 Generation 不共享 Socket Pool；測試不呼叫 SendAsync，也不建立網路連線。
    /// </summary>
    private static HttpMessageHandler GetHttpHandler(DynamicsHttpTransport transport)
    {
        var client = GetPrivateField<HttpClient>(transport, "_httpClient");
        var handlerField = typeof(HttpMessageInvoker).GetField(
            "_handler",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? typeof(HttpMessageInvoker).GetField(
                "handler",
                BindingFlags.Instance | BindingFlags.NonPublic);
        handlerField.Should().NotBeNull();
        return (HttpMessageHandler)handlerField!.GetValue(client)!;
    }
}
