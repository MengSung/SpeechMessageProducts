// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/RuntimeHealthWhoAmIProductClientTests.cs
// 用途：先以 RED tests 定義 ORG-CALL-00003 runtime.health.whoami 的產品端 DTO 邊界。
//
// 安全與生命週期契約：
// - 每個案例只使用測試 private、純記憶體 executor；不建立 CE、HTTP、Data8、connector lease、Session、
//   cache、credential、timer、subscription 或 background work。
// - 測試要求 client 只派送固定 operation、零 parameters、零 idempotency key，並在 operation、CE 版本、
//   discriminator 或三個 GUID 不符時 fail closed，不回傳 raw executor error。
// - A/B 交錯測試以不同 profile/workload/GUID marker 驗證 singleton 不保留上一個 request、response 或 token；
//   fake 不註冊 cancellation callback，因此不取得任何需 Dispose 的 CTS 資源所有權。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.RuntimeHealth;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 runtime health WhoAmI ProductClient 的固定 dispatch、輸入界限、回應關聯、A/B 隔離、取消傳遞與 DI
/// registration。這些測試只證明本機產品端邊界，不形成 ChurchReport consumer migration、CE evidence、feature gate、
/// traffic、P7.5 removal 或 P8 readiness 證據。
/// </summary>
public sealed class RuntimeHealthWhoAmIProductClientTests
{
    /// <summary>
    /// 保護正常 health check 只派送唯一的 server-owned operation，且不讓 caller 加入 parameters 或 idempotency key。
    /// 故障注入為 test-private 正常 WhoAmI envelope；決定性斷言是 trimmed deployment routing、原樣 cancellation
    /// token 與三個新 DTO GUID 均正確，證明產品端沒有接觸 CRM SDK、endpoint、credential 或 transport object。
    /// </summary>
    [Fact]
    public async Task Check_async_dispatches_the_fixed_operation_and_maps_the_complete_identity()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(_ => CreateSuccess("9.1", "A"));
        var client = new RuntimeHealthWhoAmIClient(executor);

        var identity = await client.CheckAsync(
            " profile-A ",
            " workload-A ",
            cancellationSource.Token);

        executor.CallCount.Should().Be(1);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.ProfileAlias.Should().Be("profile-A");
        executor.LastRequest.WorkloadSubjectId.Should().Be("workload-A");
        executor.LastRequest.CapabilityOperationId.Should().Be(OperationIds.RuntimeHealthWhoAmI);
        executor.LastRequest.IdempotencyKey.Should().BeNull();
        executor.LastRequest.Parameters.Should().BeEmpty();
        executor.LastCancellationToken.Should().Be(cancellationSource.Token);
        identity.UserId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        identity.BusinessUnitId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        identity.OrganizationId.Should().Be(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
    }

    /// <summary>
    /// 保護空白或超限 routing scalar 在 executor/transport 之前被拒絕。故障注入是空白 profile、超過 128 bytes
    /// 的 profile、含孤立 surrogate 的無效 UTF-8 profile、空白 workload 與超過 256 bytes 的 workload；決定性斷言為
    /// 固定 argument exception 與零 dispatch，
    /// 防止 singleton 補用上一筆 profile/workload、建立 retry 或把 caller 值寫入 shared state。
    /// </summary>
    [Fact]
    public async Task Check_async_rejects_invalid_routing_values_before_dispatch()
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var client = new RuntimeHealthWhoAmIClient(executor);

        var blankProfile = () => client.CheckAsync("  ", "workload-A");
        var longProfile = () => client.CheckAsync(new string('p', 129), "workload-A");
        var invalidUtf8Profile = () => client.CheckAsync(new string('\uD800', 1), "workload-A");
        var blankWorkload = () => client.CheckAsync("profile-A", " ");
        var longWorkload = () => client.CheckAsync("profile-A", new string('w', 257));

        await blankProfile.Should().ThrowAsync<ArgumentException>();
        await longProfile.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await invalidUtf8Profile.Should().ThrowAsync<ArgumentException>();
        await blankWorkload.Should().ThrowAsync<ArgumentException>();
        await longWorkload.Should().ThrowAsync<ArgumentOutOfRangeException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護成功 envelope 也必須與固定 operation、CE 9.1、WhoAmI discriminator 及三個非空 GUID 完全一致。
    /// 故障注入依序提供錯 operation、錯 CE 版本、錯 branch 與三種空 GUID；決定性斷言是每次均只得到固定
    /// contract exception，避免錯 profile 的身分、raw response 或任一 partial identity 發布到呼叫端。
    /// </summary>
    [Fact]
    public async Task Check_async_rejects_any_mismatched_or_incomplete_success_envelope()
    {
        var executor = new SequencedExecutor(
            OperationExecutionResult.Success(OperationResponseData.ForWhoAmI(
                "runtime.health.another-operation",
                "9.1",
                CreateWireIdentity("A"))),
            CreateSuccess("8.2", "A"),
            OperationExecutionResult.Success(OperationResponseData.ForPackage01FeeRecords(
                OperationIds.RuntimeHealthWhoAmI,
                "9.1",
                Array.Empty<Package01FeeRecord>())),
            OperationExecutionResult.Success(OperationResponseData.ForWhoAmI(
                OperationIds.RuntimeHealthWhoAmI,
                "9.1",
                new WhoAmIResponseData
                {
                    UserId = Guid.Empty,
                    BusinessUnitId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    OrganizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
                })),
            OperationExecutionResult.Success(OperationResponseData.ForWhoAmI(
                OperationIds.RuntimeHealthWhoAmI,
                "9.1",
                new WhoAmIResponseData
                {
                    UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    BusinessUnitId = Guid.Empty,
                    OrganizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
                })),
            OperationExecutionResult.Success(OperationResponseData.ForWhoAmI(
                OperationIds.RuntimeHealthWhoAmI,
                "9.1",
                new WhoAmIResponseData
                {
                    UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    BusinessUnitId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    OrganizationId = Guid.Empty
                })));
        var client = new RuntimeHealthWhoAmIClient(executor);

        for (var index = 0; index < 6; index++)
        {
            var act = () => client.CheckAsync("profile-A", "workload-A");
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Runtime health identity response does not match the requested operation contract.");
        }
    }

    /// <summary>
    /// 保護下游失敗只形成固定、不含 raw executor message 的 product-side failure。故障注入含有刻意敏感樣式的
    /// error message；決定性斷言為 published exception 不含該文字，且 client 不 retry、不 fallback 或建立第二次
    /// dispatch，讓 connector/lease 的錯誤清理仍由 executor 的單一 owner 完成。
    /// </summary>
    [Fact]
    public async Task Check_async_sanitizes_executor_failure_without_retrying()
    {
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Failure(
            "upstream.failure",
            "https://sensitive.example.invalid/ raw-token"));
        var client = new RuntimeHealthWhoAmIClient(executor);

        var act = () => client.CheckAsync("profile-A", "workload-A");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Runtime health identity check failed.");
        executor.CallCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 A/B 非同步交錯完成時不會借用上一筆 profile、workload、GUID 或 response instance。故障注入刻意先完成
    /// B 再完成 A；決定性斷言為兩組 DTO/GUID 各自保持 marker 且 instance 不同，證明 singleton 僅持有 DI executor，
    /// 沒有 last-result cache、Session、static mutable collection、timer 或 background continuation。
    /// </summary>
    [Fact]
    public async Task Check_async_keeps_interleaved_profiles_and_identity_results_request_local()
    {
        var executor = new InterleavingExecutor();
        var client = new RuntimeHealthWhoAmIClient(executor);

        var aTask = client.CheckAsync("profile-A", "workload-A");
        var bTask = client.CheckAsync("profile-B", "workload-B");

        executor.CompleteB(CreateSuccess("9.1", "B"));
        var b = await bTask;
        executor.CompleteA(CreateSuccess("9.1", "A"));
        var a = await aTask;

        a.UserId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        b.UserId.Should().Be(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        a.BusinessUnitId.Should().NotBe(b.BusinessUnitId);
        a.OrganizationId.Should().NotBe(b.OrganizationId);
        a.Should().NotBeSameAs(b);
    }

    /// <summary>
    /// 保護 caller cancellation token 原樣交給 executor。fake 不註冊 callback 或建立 linked source，故本測試能排除
    /// client 吞掉取消、延長 CTS 壽命或以背景 retry 持續使用不確定 transport 的行為。
    /// </summary>
    [Fact]
    public async Task Check_async_forwards_the_caller_cancellation_token_unchanged()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(_ => CreateSuccess("9.1", "A"));
        var client = new RuntimeHealthWhoAmIClient(executor);

        await client.CheckAsync("profile-A", "workload-A", cancellationSource.Token);

        executor.LastCancellationToken.Should().Be(cancellationSource.Token);
    }

    /// <summary>
    /// 保護 standalone registration 只加入一個 stateless typed client 並重用既有 executor。故障注入是只會在真正
    /// dispatch 時執行的 fake；決定性 descriptor/assertion 證明註冊與解析不做 I/O、不建立 HttpClient、Data8、
    /// cache 或 consumer/gate，transport/lease 的 owner 仍停留在 composition root 的 executor。
    /// </summary>
    [Fact]
    public void Standalone_registration_resolves_one_stateless_client_from_the_existing_executor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDynamicsOperationExecutor>(new RecordingExecutor(_ => CreateSuccess("9.1", "A")));
        services.AddSpeechMessageDynamicsRuntimeHealthWhoAmI();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetServices<IRuntimeHealthWhoAmIClient>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<RuntimeHealthWhoAmIClient>();
    }

    /// <summary>
    /// 保護 Gateway composition root 也顯式註冊同一個 health client，而不需要 ChurchReport consumer 或 feature gate。
    /// 此案例只解析 DI graph，不 dispatch HTTP；決定性斷言是 singleton interface 可解析且仍是 typed client，
    /// 因此 Gateway/Embedded 使用相同產品 contract，不把 endpoint、credential 或 connector state 帶入 API。
    /// </summary>
    [Fact]
    public void Gateway_registration_includes_the_runtime_health_client_without_dispatching()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpeechMessageDynamicsGatewayProductClient(options =>
        {
            options.ConnectionMode = SpeechMessage.Dynamics.Abstractions.Execution.ConnectionMode.DedicatedGateway;
            options.ProfileAlias = "crm91";
            options.Gateway = new SpeechMessage.Dynamics.Abstractions.Configuration.GatewayEndpointOptions
            {
                Endpoint = "https://gateway.example.invalid/",
                ApiPrefix = "/v1"
            };
        });

        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IRuntimeHealthWhoAmIClient>()
            .Should().BeOfType<RuntimeHealthWhoAmIClient>();
    }

    /// <summary>
    /// 建立固定的成功結果。每次均建立新的 WhoAmI wire object，只有三個 allowlisted GUID，沒有 CRM SDK、profile、
    /// endpoint、credential、token、cookie、Entity 或可釋放資源；factory 不執行 CE 或使用上一筆測試資料。
    /// </summary>
    /// <param name="ceVersion">模擬 deployment-selected CE 版本，用於 exact-version 驗證。</param>
    /// <param name="marker">決定目前測試私有 A/B GUID 組合的固定 marker。</param>
    /// <returns>封閉 operation result。</returns>
    private static OperationExecutionResult CreateSuccess(string ceVersion, string marker)
        => OperationExecutionResult.Success(OperationResponseData.ForWhoAmI(
            OperationIds.RuntimeHealthWhoAmI,
            ceVersion,
            CreateWireIdentity(marker)));

    /// <summary>
    /// 建立可明確區分 A/B 的最小 WhoAmI scalar projection。GUID 僅用於本機 contract test，不能成為 production
    /// profile、owner、endpoint、credential 或 authorization selector，也不會被 log 或儲存。
    /// </summary>
    /// <param name="marker">A 或 B 的測試 marker。</param>
    /// <returns>只含三個非空 GUID 的封閉 wire projection。</returns>
    private static WhoAmIResponseData CreateWireIdentity(string marker)
        => marker switch
        {
            "A" => new WhoAmIResponseData
            {
                UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                BusinessUnitId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                OrganizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
            },
            "B" => new WhoAmIResponseData
            {
                UserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                BusinessUnitId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                OrganizationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")
            },
            _ => throw new ArgumentOutOfRangeException(nameof(marker))
        };

    /// <summary>
    /// 只記錄目前測試 request 的 pure in-memory executor。它沒有 static/cache/connector/Session/transport state，
    /// 也不註冊 cancellation callback；測試結束後由 GC 回收，使測試本身不引入跨 request 或資源生命週期假象。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>建立目前測試專屬的同步結果 handler，不捕捉任何 production request 或 credential。</summary>
        /// <param name="handler">將固定 request 對應至封閉 result 的純記憶體委派。</param>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        /// <summary>取得本測試目前的 dispatch 次數，供 fail-fast 與 no-retry assertion 使用。</summary>
        public int CallCount { get; private set; }

        /// <summary>取得同一測試的最後 request；它不是產品 cache，也不會跨 test instance 重用。</summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>取得 executor 原樣收到的 token；fake 不註冊或保存 cancellation callback。</summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>記錄 request 後回傳封閉純值結果，不建立 I/O、Task.Run、timer、lease 或 stream。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_handler(request));
        }
    }

    /// <summary>
    /// 將預先定義的結果依序交付給測試呼叫的 executor。sequence 只由單一測試擁有；若呼叫次數超限立即 fail closed，
    /// 避免 fake 悄悄重用前一筆 response 造成錯誤的 contract pass。
    /// </summary>
    private sealed class SequencedExecutor : IDynamicsOperationExecutor
    {
        private readonly Queue<OperationExecutionResult> _results;

        /// <summary>建立私有、有限的封閉 result sequence，不持有外部資源或跨測試資料。</summary>
        /// <param name="results">目前測試要依序交付的純值 result。</param>
        public SequencedExecutor(params OperationExecutionResult[] results) => _results = new Queue<OperationExecutionResult>(results);

        /// <summary>交付下一個 result；沒有剩餘資料時立即失敗而不 retry 或借用其他 request 的結果。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("The test executor has no remaining result.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    /// <summary>
    /// 以兩個 request-private TaskCompletionSource 模擬 A/B 非同步完成順序。每個 completion 使用
    /// RunContinuationsAsynchronously，避免 SetResult 在 caller stack 重入；不建立 timer、registration、connector
    /// 或任何需 Dispose 的資源。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly TaskCompletionSource<OperationExecutionResult> _aCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _bCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>依 profile marker 回傳只屬於 A 或 B 的 pending result；未知 marker 立即 fail closed。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => request.ProfileAlias switch
            {
                "profile-A" => _aCompletion.Task,
                "profile-B" => _bCompletion.Task,
                _ => throw new InvalidOperationException("Unexpected profile for the isolation test.")
            };

        /// <summary>只完成 A 的 private response，不接觸 B 的 completion。</summary>
        /// <param name="result">A request 專屬的封閉 operation result。</param>
        public void CompleteA(OperationExecutionResult result) => _aCompletion.SetResult(result);

        /// <summary>只完成 B 的 private response，不接觸 A 的 completion。</summary>
        /// <param name="result">B request 專屬的封閉 operation result。</param>
        public void CompleteB(OperationExecutionResult result) => _bCompletion.SetResult(result);
    }
}
