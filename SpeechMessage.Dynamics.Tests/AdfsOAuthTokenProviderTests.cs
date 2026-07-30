// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs
// 目的：驗證 ADFS OAuth token 提供者只接受明確的 bearer／refresh-token 秘密參考，並維持安全快取與生命週期。
//
// 保姆級教學：
// 1. 這些測試不連真實 ADFS；用 fake HttpMessageHandler 模擬 token endpoint。
// 2. 重點是：
//    - password grant 即使被誤設為啟用也必須 fail closed，且不得解析人類帳密或送出 HTTP
//    - CredentialReferenceName 有值時直接回傳 bearer，不打 token endpoint
//    - refresh-token grant 成功後只在目前 Profile Generation 記憶體內快取 access token
// 3. jesus IFD 正式連線仍需真實 ADFS ClientId；此處只保證程式契約正確。
// ============================================================================

using System.Net;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證單一 Dynamics Profile Generation 所擁有的 ADFS OAuth token provider 安全契約。
/// 測試把秘密解析器、HTTP wrapper、single-flight gate 與暫存目錄視為彼此獨立的 trust boundary：
/// token 只能由明確的秘密參考進入 provider，任何 caller cancellation 都只能取消自己的等待，
/// Generation Dispose 則是唯一可取消共用工作並清除快取／同步資源的 owner。所有檔案測試都限制在
/// 測試專屬暫存目錄並於 finally 確定性清除，避免 RED 階段把 access／refresh token、檔案 handle 或
/// 背景工作遺留到其他測試；有界 response 與共用 HTTP handler 的測試則避免以額外配置換取假性效能。
/// </summary>
public sealed class AdfsOAuthTokenProviderTests
{
    [Fact]
    public async Task Direct_bearer_secret_skips_token_endpoint()
    {
        var called = false;
        var provider = CreateProvider(
            options: new DynamicsWebApiOptions
            {
                AuthMode = DynamicsAuthMode.AdfsOAuth,
                CredentialReferenceName = "PREISSUED_TOKEN",
                AuthorityUri = "https://sts.example.local/adfs",
                ClientId = "client-1",
                ResourceUri = "https://crm.example.local/",
                TimeoutSeconds = 10
            },
            secrets: new Dictionary<string, string>
            {
                ["PREISSUED_TOKEN"] = "preissued-access-token"
            },
            responder: _ =>
            {
                called = true;
                return JsonResponse("""{"access_token":"should-not-use","expires_in":3600}""");
            });

        var token = await provider.GetAccessTokenAsync();

        token.Should().Be("preissued-access-token");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task Password_grant_enabled_still_fails_closed_before_secret_or_http_work()
    {
        var secretResolver = new CountingSecretResolver();
        var clientFactory = new CountingHttpClientFactory();
        await using var provider = new AdfsOAuthTokenProvider(
            Options.Create(
                new DynamicsWebApiOptions
                {
                    AuthMode = DynamicsAuthMode.AdfsOAuth,
                    AuthorityUri = "https://sts.example.local/adfs",
                    ClientId = "client-xyz",
                    ResourceUri = "https://jesus.example.local/",
                    UserNameSecretName = "USER_SECRET",
                    PasswordSecretName = "PASS_SECRET",
                    AllowLocalDevPasswordGrant = true,
                    TimeoutSeconds = 10
                }),
            secretResolver,
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            clientFactory);

        var act = async () => await provider.GetAccessTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*password grant*disabled*");
        secretResolver.ResolveCalls.Should().Be(0,
            "禁止的 password grant 必須在解析人類帳密前 fail closed");
        clientFactory.CreateCalls.Should().Be(0,
            "禁止的 password grant 不得建立 HTTP wrapper、handler 或 socket work");
    }

    [Fact]
    public async Task Refresh_grant_posts_expected_form_and_caches_token()
    {
        var callCount = 0;
        HttpRequestMessage? seen = null;
        string? body = null;

        await using var provider = CreateProvider(
            options: new DynamicsWebApiOptions
            {
                AuthMode = DynamicsAuthMode.AdfsOAuth,
                AuthorityUri = "https://sts.example.local/adfs",
                ClientId = "client-xyz",
                ResourceUri = "https://jesus.example.local/",
                RefreshTokenSecretName = "REFRESH_TOKEN",
                AllowLocalDevPasswordGrant = false,
                TimeoutSeconds = 10
            },
            secrets: new Dictionary<string, string>
            {
                ["REFRESH_TOKEN"] = "refresh-token-input"
            },
            responder: request =>
            {
                callCount++;
                seen = request;
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonResponse("""{"access_token":"adfs-token-001","expires_in":1200,"token_type":"bearer"}""");
            });

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        first.Should().Be("adfs-token-001");
        second.Should().Be("adfs-token-001");
        callCount.Should().Be(1, "token must be cached until near expiry");

        seen.Should().NotBeNull();
        seen!.Method.Should().Be(HttpMethod.Post);
        seen.RequestUri!.AbsoluteUri.Should().Be("https://sts.example.local/adfs/oauth2/token");
        body.Should().NotBeNull();
        body.Should().Contain("grant_type=refresh_token");
        body.Should().Contain("client_id=client-xyz");
        body.Should().Contain("resource=" + Uri.EscapeDataString("https://jesus.example.local/"));
        body.Should().Contain("refresh_token=refresh-token-input");
        body.Should().NotContain("username=");
        body.Should().NotContain("password=");
    }

    [Fact]
    public async Task Password_grant_disabled_fails_closed()
    {
        var provider = CreateProvider(
            options: new DynamicsWebApiOptions
            {
                AuthMode = DynamicsAuthMode.AdfsOAuth,
                AuthorityUri = "https://sts.example.local/adfs",
                ClientId = "client-xyz",
                ResourceUri = "https://jesus.example.local/",
                UserNameSecretName = "USER_SECRET",
                PasswordSecretName = "PASS_SECRET",
                AllowLocalDevPasswordGrant = false,
                TimeoutSeconds = 10
            },
            secrets: new Dictionary<string, string>
            {
                ["USER_SECRET"] = "u",
                ["PASS_SECRET"] = "p"
            },
            responder: _ => JsonResponse("""{"access_token":"x","expires_in":60}"""));

        var act = async () => await provider.GetAccessTokenAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no usable token source*");
    }

    [Fact]
    public async Task Token_endpoint_error_does_not_retain_or_echo_response_body()
    {
        const string sensitiveResponseMarker = "server-response-must-not-be-retained";
        var provider = CreateProvider(
            options: CreateRefreshGrantOptionsWithSecret(),
            secrets: CreateRefreshGrantSecrets(),
            responder: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(sensitiveResponseMarker, Encoding.UTF8, "text/plain")
            });

        var act = async () => await provider.GetAccessTokenAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().NotContain(sensitiveResponseMarker);
        exception.Which.Message.Should().NotContain("BodyPreview");
    }

    [Fact]
    public async Task Oversized_token_response_is_rejected_before_unbounded_buffering()
    {
        var oversizedJson = "{\"access_token\":\"" + new string('x', 65_536) + "\"}";
        var provider = CreateProvider(
            options: CreateRefreshGrantOptionsWithSecret(),
            secrets: CreateRefreshGrantSecrets(),
            responder: _ => JsonResponse(oversizedJson));

        var act = async () => await provider.GetAccessTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*maximum supported size*");
    }

    [Fact]
    public async Task Factory_token_client_uses_the_configured_bounded_timeout()
    {
        var options = CreateRefreshGrantOptionsWithSecret();
        options.TimeoutSeconds = 7;
        var factory = new StubHttpClientFactory(_ => JsonResponse("""{"access_token":"token","expires_in":60}"""));
        var provider = new AdfsOAuthTokenProvider(
            Options.Create(options),
            new DictionarySecretResolver(CreateRefreshGrantSecrets()),
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            factory);

        _ = await provider.GetAccessTokenAsync();

        factory.LastClient.Should().NotBeNull();
        factory.LastClient!.Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task Factory_token_client_wrapper_is_disposed_after_token_request()
    {
        var factory = new TrackingHttpClientFactory(
            _ => JsonResponse("""{"access_token":"token","expires_in":60}"""));
        var provider = new AdfsOAuthTokenProvider(
            Options.Create(CreateRefreshGrantOptionsWithSecret()),
            new DictionarySecretResolver(CreateRefreshGrantSecrets()),
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            factory);

        _ = await provider.GetAccessTokenAsync();

        factory.LastClient.Should().NotBeNull();
        factory.LastClient!.IsDisposed.Should().BeTrue();
    }

    /// <summary>
    /// 驗證 Token Provider 的生命週期由 Profile Generation 明確擁有：重複同步／非同步 Dispose 必須安全，
    /// Dispose 後的新要求必須在解析 Secret 或建立 HTTP Client 之前拋出 ObjectDisposedException。
    /// 這個順序可避免已退休 Generation 繼續保留 Token、Semaphore、Handler 或 Socket Pool。
    /// </summary>
    [Fact]
    public async Task Disposed_provider_rejects_new_token_work_and_releases_owned_http_resources()
    {
        var secretResolver = new CountingSecretResolver();
        var clientFactory = new CountingHttpClientFactory();
        var provider = new AdfsOAuthTokenProvider(
            Options.Create(CreateRefreshGrantOptionsWithSecret()),
            secretResolver,
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            clientFactory);

        provider.Dispose();
        await provider.DisposeAsync();
        provider.Dispose();

        var act = async () => await provider.GetAccessTokenAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
        secretResolver.ResolveCalls.Should().Be(0,
            "退休 Generation 不可在 Dispose 後重新讀取 Credential 或 Token");
        clientFactory.CreateCalls.Should().Be(0,
            "Dispose 後必須在建立 HTTP wrapper 或接觸 handler/socket pool 前失敗");
    }


    /// <summary>
    /// 直接覆蓋未注入 <see cref="IHttpClientFactory"/> 的 production-owned handler 分支。Provider constructor 建立
    /// generation 唯一擁有的 HttpClient／SocketsHttpHandler；DisposeAsync 必須先 drain active attempt，再關閉 client、
    /// handler/socket pool 與 CTS。測試只反射取得該 private client 作為 disposal sentinel，不讀取 token、secret、URL、
    /// Session 或跨測試 mutable state；Dispose 後任何 SendAsync 都必須在網路 I/O 前拋出 ObjectDisposedException。
    /// </summary>
    [Fact]
    public async Task Owned_handler_client_is_disposed_with_profile_generation()
    {
        var provider = new AdfsOAuthTokenProvider(
            Options.Create(CreateRefreshGrantOptionsWithSecret()),
            new DictionarySecretResolver(CreateRefreshGrantSecrets()),
            NullLogger<AdfsOAuthTokenProvider>.Instance);
        var ownedClientField = typeof(AdfsOAuthTokenProvider).GetField(
            "_ownedHttpClient",
            BindingFlags.Instance | BindingFlags.NonPublic);
        ownedClientField.Should().NotBeNull();
        var ownedClient = ownedClientField!.GetValue(provider).Should().BeOfType<HttpClient>().Subject;

        await provider.DisposeAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://disposed.invalid/");
        var act = async () => await ownedClient.SendAsync(request);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>
    /// 驗證公開設定契約沒有重新暴露本機 token-store 路徑；設定 reflection 不執行 I/O，也不保存 token 或 profile state。
    /// </summary>
    [Fact]
    public void Public_configuration_contract_does_not_expose_local_token_store_path()
    {
        var configurationTypes = new[]
        {
            typeof(DynamicsWebApiOptions),
            typeof(ProductDynamicsOptions),
            typeof(EmbeddedModeOptions)
        };

        var exposedPropertyCount = configurationTypes
            .SelectMany(type => type.GetProperties())
            .Count(property => string.Equals(
                property.Name,
                "LocalDevTokenStorePath",
                StringComparison.Ordinal));

        exposedPropertyCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 provider source contract 不再參考 legacy token store、檔案路徑解析或 token 持久化 helper。
    /// 此 Release-safe 測試只讀目前 worktree 的 UTF-8 原始碼，不載入 token、不執行 HTTP，也不持有檔案 handle；
    /// source scan 可在 legacy 型別完全刪除後繼續編譯，並以 fail-closed 方式阻止未來重引入跨程序、跨 Generation
    /// 的明文 token retention。掃描成本與檔案大小成線性關係，且只在測試執行期間保留一份短命字串。
    /// </summary>
    [Fact]
    public void Provider_source_has_no_file_token_store_dependency()
    {
        var repositoryRoot = FindRepositoryRoot();
        var providerPath = Path.Combine(
            repositoryRoot,
            "SpeechMessage.Dynamics.WebApi",
            "Runtime",
            "AdfsOAuthTokenProvider.cs");
        var source = File.ReadAllText(providerPath);
        var forbiddenFragments = new[]
        {
            "LocalDevAdfsTokenStore",
            "LocalDevTokenStorePath",
            "ResolveTokenStorePath",
            "TryPersistTokens"
        };

        var violationCount = forbiddenFragments.Count(fragment =>
            source.Contains(fragment, StringComparison.Ordinal));

        violationCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 refresh token 的唯一 owner 是由 RefreshTokenSecretName 指向的 <see cref="ISecretResolver"/>；
    /// 即使測試專屬暫存目錄內存在格式相容的 legacy JSON，缺少秘密參考時也必須在任何 HTTP 呼叫前 fail closed。
    /// 測試以反射設定已排程移除的屬性，因此 Production 移除該 API 後仍可編譯；暫存檔只由本測試建立與清除，
    /// 不跨 Session／Profile 分享，且沒有 timer、watcher 或未完成背景工作。
    /// </summary>
    [Fact]
    public async Task Missing_refresh_token_secret_never_falls_back_to_legacy_token_file()
    {
        var temporaryDirectory = CreateIsolatedTemporaryDirectory();
        var storePath = Path.Combine(temporaryDirectory, "legacy-token.json");
        try
        {
            WriteLegacyTokenFile(storePath);
            var options = CreateRefreshGrantOptions();
            TrySetLegacyTokenStorePath(options, storePath);
            var httpCalled = false;
            await using var provider = CreateProvider(
                options,
                secrets: new Dictionary<string, string>(),
                responder: _ =>
                {
                    httpCalled = true;
                    return JsonResponse("""{"access_token":"unexpected","expires_in":900}""");
                });

            var act = async () => await provider.GetAccessTokenAsync();

            await act.Should().ThrowAsync<InvalidOperationException>();
            httpCalled.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 驗證成功的 refresh-token acquisition 僅更新 Generation-local 記憶體快取，不建立 access／refresh token JSON。
    /// 測試把輸出路徑限制在唯一暫存目錄，並由 finally 擁有 drain 與 recursive cleanup；provider 則由 await using
    /// 確定性 Dispose，確保 HTTP wrapper、Semaphore 與 cancellation registration 回到基準。此斷言不使用全域
    /// FileSystemWatcher，避免額外 thread、buffer 與跨測試事件競爭，改以要求完成後的有界目錄快照驗證零檔案。
    /// </summary>
    [Fact]
    public async Task Token_acquisition_never_creates_json_token_file()
    {
        var temporaryDirectory = CreateIsolatedTemporaryDirectory();
        var storePath = Path.Combine(temporaryDirectory, "created-by-production.json");
        try
        {
            var options = CreateRefreshGrantOptions();
            options.RefreshTokenSecretName = "REFRESH_TOKEN";
            TrySetLegacyTokenStorePath(options, storePath);
            await using var provider = CreateProvider(
                options,
                secrets: new Dictionary<string, string>
                {
                    ["REFRESH_TOKEN"] = "refresh-from-secret-resolver"
                },
                responder: _ => JsonResponse(
                    """{"access_token":"refreshed-access","expires_in":900,"refresh_token":"rotated-refresh"}"""));

            _ = await provider.GetAccessTokenAsync();

            Directory.EnumerateFiles(temporaryDirectory).Any().Should().BeFalse();
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 驗證同一 Profile Generation 的並行 caller 只建立一個 token HTTP acquisition，其他 caller 只加入
    /// single-flight gate 並重用完成後的記憶體快取。測試以明確 release signal 控制唯一 owner，所有 continuation
    /// 非同步執行以避免 inline deadlock；完成後 await using 會 drain provider，沒有遺留 Task、Semaphore waiter、
    /// HttpClient wrapper 或 response。這保留高併發下的一次 I/O 效能，同時不以 static token dictionary 交換吞吐量。
    /// </summary>
    [Fact]
    public async Task Concurrent_callers_share_one_token_request()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        await using var provider = CreateAsyncProvider(
            CreateRefreshGrantOptionsWithSecret(),
            CreateRefreshGrantSecrets(),
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref callCount);
                requestStarted.TrySetResult();
                await releaseRequest.Task.WaitAsync(cancellationToken);
                return JsonResponse("""{"access_token":"shared-token","expires_in":900}""");
            });

        var first = provider.GetAccessTokenAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = provider.GetAccessTokenAsync();
        releaseRequest.TrySetResult();

        _ = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Volatile.Read(ref callCount).Should().Be(1);
    }

    /// <summary>
    /// 驗證等待 single-flight gate 的 caller 可用自己的 cancellation token 立即離開，但不會取消另一個 caller
    /// 已擁有的 token acquisition。第一個要求是唯一 HTTP owner，第二個要求取消後不得建立第二個 request；最後明確
    /// release 並 await 第一個 Task，再由 provider Dispose 清除 gate／CTS。這個分離可避免慢 caller 保留多餘工作，
    /// 也避免一個 Session 的取消破壞其他 caller 共用的 Profile Generation refresh。
    /// </summary>
    [Fact]
    public async Task Cancelled_waiter_does_not_start_or_cancel_the_shared_token_request()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        await using var provider = CreateAsyncProvider(
            CreateRefreshGrantOptionsWithSecret(),
            CreateRefreshGrantSecrets(),
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref callCount);
                requestStarted.TrySetResult();
                await releaseRequest.Task.WaitAsync(cancellationToken);
                return JsonResponse("""{"access_token":"shared-token","expires_in":900}""");
            });

        var owner = provider.GetAccessTokenAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var callerCts = new CancellationTokenSource();
        var waiter = provider.GetAccessTokenAsync(callerCts.Token);
        callerCts.Cancel();

        var waitForCancelledCaller = async () => await waiter;
        await waitForCancelledCaller.Should().ThrowAsync<OperationCanceledException>();
        Volatile.Read(ref callCount).Should().Be(1);

        releaseRequest.TrySetResult();
        _ = await owner.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Read(ref callCount).Should().Be(1);
    }

    /// <summary>
    /// 驗證即使最先建立 single-flight attempt 的 caller 自己取消，也只能離開自己的等待，不能取得共用 HTTP
    /// cancellation ownership。第二個 caller 必須沿用同一個 request 並成功取得 token；generation Dispose 才是唯一可
    /// 中止共用工作的 owner。測試以明確 signal 完成 request，所有 Task、CTS、HttpClient wrapper 與 provider 都在
    /// scope 結束前 drain／Dispose，沒有 fire-and-forget continuation、Session state 或跨測試 token retention。
    /// </summary>
    [Fact]
    public async Task Cancelled_attempt_starter_does_not_cancel_the_generation_owned_token_request()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        await using var provider = CreateAsyncProvider(
            CreateRefreshGrantOptionsWithSecret(),
            CreateRefreshGrantSecrets(),
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref callCount);
                requestStarted.TrySetResult();
                await releaseRequest.Task.WaitAsync(cancellationToken);
                return JsonResponse("""{"access_token":"generation-owned-token","expires_in":900}""");
            });
        using var starterCts = new CancellationTokenSource();

        var starter = provider.GetAccessTokenAsync(starterCts.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var follower = provider.GetAccessTokenAsync();
        starterCts.Cancel();

        var waitForCancelledStarter = async () => await starter;
        await waitForCancelledStarter.Should().ThrowAsync<OperationCanceledException>();
        Volatile.Read(ref callCount).Should().Be(1);

        releaseRequest.TrySetResult();
        var token = await follower.WaitAsync(TimeSpan.FromSeconds(5));

        token.Should().Be("generation-owned-token");
        Volatile.Read(ref callCount).Should().Be(1);
    }

    /// <summary>
    /// 驗證 refresh-token grant 仍可用受控秘密參考完成，且送出的表單只從 <see cref="ISecretResolver"/> 取得
    /// refresh token，不建立或讀取任何 token JSON。HTTP response、request content 與 provider 都有唯一且確定性的
    /// Dispose owner；測試只保留短命表單字串做契約檢查，不輸出 token、URL 或 client identifier，並維持既有
    /// bounded timeout、single-flight 與 process-local cache 行為。
    /// </summary>
    [Fact]
    public async Task Refresh_token_grant_posts_expected_form()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        await using var provider = CreateProvider(
            options: new DynamicsWebApiOptions
            {
                AuthMode = DynamicsAuthMode.AdfsOAuth,
                AuthorityUri = "https://sts.example.local/adfs",
                ClientId = "client-xyz",
                ResourceUri = "https://jesus.example.local/",
                RefreshTokenSecretName = "REFRESH_TOKEN",
                AllowLocalDevPasswordGrant = false,
                TimeoutSeconds = 10
            },
            secrets: new Dictionary<string, string>
            {
                ["REFRESH_TOKEN"] = "refresh-abc"
            },
            responder: request =>
            {
                seen = request;
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonResponse("""{"access_token":"refreshed-001","expires_in":900,"refresh_token":"refresh-abc"}""");
            });

        var token = await provider.GetAccessTokenAsync();
        token.Should().Be("refreshed-001");
        seen!.RequestUri!.AbsoluteUri.Should().Be("https://sts.example.local/adfs/oauth2/token");
        body.Should().Contain("grant_type=refresh_token");
        body.Should().Contain("refresh_token=refresh-abc");
        body.Should().Contain("client_id=client-xyz");
    }

    /// <summary>
    /// 建立只供 refresh-token 測試使用的最小 ADFS 設定；此 helper 不解析秘密、不建立 HttpClient，也不持有
    /// Session 或跨 Generation 狀態。呼叫端仍是 options 的唯一 owner，並可在建立 provider 前覆寫秘密參考；
    /// timeout 保持有限，確保錯誤路徑不會形成無界等待或背景工作。
    /// </summary>
    private static DynamicsWebApiOptions CreateRefreshGrantOptions()
        => new()
        {
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            AuthorityUri = "https://sts.example.local/adfs",
            ClientId = "client-xyz",
            ResourceUri = "https://jesus.example.local/",
            AllowLocalDevPasswordGrant = false,
            TimeoutSeconds = 10
        };

    /// <summary>
    /// 將已排程移除的 legacy 路徑屬性以反射設定，讓同一份 RED 測試在屬性存在與移除後都能編譯。
    /// helper 只接觸呼叫端專屬 options instance，不快取 reflection metadata、不跨執行緒分享 mutable state，
    /// 也不開啟檔案；若 Production 已移除屬性即安全地不做任何事，維持 fail-closed 測試語意。
    /// </summary>
    private static void TrySetLegacyTokenStorePath(DynamicsWebApiOptions options, string path)
    {
        var property = typeof(DynamicsWebApiOptions).GetProperty(
            "LocalDevTokenStorePath",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        property?.SetValue(options, path);
    }

    /// <summary>
    /// 建立格式相容但只存在於測試專屬暫存目錄的 legacy refresh-token JSON，用來證明 provider 不得將檔案視為
    /// credential source。檔案 owner 是呼叫端 test 的 finally，內容不寫入 log 或 assertion；同步寫入在呼叫 provider
    /// 前完成，因此沒有 file watcher、共享 stream、競爭或取消中的半寫狀態，且資料量固定以避免記憶體放大。
    /// </summary>
    private static void WriteLegacyTokenFile(string path)
    {
        File.WriteAllText(
            path,
            """
            {
              "RefreshToken": "legacy-refresh-token",
              "AccessToken": "expired-access-token",
              "AccessTokenExpiresAtUtc": "2000-01-01T00:00:00+00:00"
            }
            """);
    }

    /// <summary>
    /// 建立唯一、空白且由單一測試擁有的暫存目錄。路徑不含 Session、Profile、credential 或 token 資訊；
    /// 呼叫端必須在 finally recursive delete，確保 RED 測試即使觸發 Production 寫檔也不遺留檔案、handle 或
    /// 跨測試狀態。每個測試只配置一個小型目錄，以可預測的 I/O 成本換取確定性隔離。
    /// </summary>
    private static string CreateIsolatedTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dynamics-adfs-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 建立支援非同步、可取消 responder 的 provider，供 single-flight 與 caller cancellation 測試精確控制
    /// HTTP owner。Factory 只擁有測試 handler；每次 HttpClient wrapper 仍由 provider request 釋放，handler 不保存
    /// credential、token 或 Session。呼叫端必須 Dispose provider 並完成所有 signal，避免未 drain Task 或 waiter。
    /// </summary>
    private static AdfsOAuthTokenProvider CreateAsyncProvider(
        DynamicsWebApiOptions options,
        IReadOnlyDictionary<string, string> secrets,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        => new(
            Options.Create(options),
            new DictionarySecretResolver(secrets),
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            new AsyncStubHttpClientFactory(responder));

    /// <summary>
    /// 驗證歷史 ADFS probe 已成為純 fail-closed 的退役入口，不再從產品 appsettings 擷取帳密、接受
    /// username/password 參數、執行 Resource Owner Password Credential grant、呼叫 WhoAmI，或把 token／identity／
    /// endpoint 結果寫入檔案與 console。測試只讀 checked-in script 文字，不啟動 PowerShell、不建立 process、
    /// HTTP client、token cache 或 file watcher；主要 assertion 保護操作者只能轉往既有 Public Client authorization-code
    /// 診斷流程，且所有 Local Gateway／CE／browser gate 通過前仍明確維持 Package 1 consumer 關閉。
    /// </summary>
    [Fact]
    public void Legacy_adfs_token_probe_is_retired_without_password_or_result_output_paths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "docs", "scripts", "Invoke-AdfsTokenProbe.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("RETIRED");
        script.Should().Contain("/diagnostics/adfs-authorize");
        script.Should().Contain("Package01FeeReadsEnabled=false");
        script.Should().Contain("throw");
        script.Should().NotMatchRegex(@"(?im)^\s*\[string\]\s*\$(UserName|Password)\b");
        script.Should().NotContain("Read-AppSettingsCrmConnection");
        script.Should().NotContain("grant_type");
        script.Should().NotContain("Invoke-RestMethod");
        script.Should().NotContain("WriteAllText");
        script.Should().NotContain("access_token");
        script.Should().NotContain("WhoAmI");
    }

    private static AdfsOAuthTokenProvider CreateProvider(
        DynamicsWebApiOptions options,
        IReadOnlyDictionary<string, string> secrets,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new StubHttpClientFactory(responder);
        return new AdfsOAuthTokenProvider(
            Options.Create(options),
            new DictionarySecretResolver(secrets),
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            factory);
    }

    private static DynamicsWebApiOptions CreateRefreshGrantOptionsWithSecret()
    {
        var options = CreateRefreshGrantOptions();
        options.RefreshTokenSecretName = "REFRESH_TOKEN";
        return options;
    }

    private static IReadOnlyDictionary<string, string> CreateRefreshGrantSecrets()
        => new Dictionary<string, string>
        {
            ["REFRESH_TOKEN"] = "unit-test-refresh-token"
        };

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// 從目前測試輸出向上尋找同時包含 Dynamics tests 與受管 scripts 的 worktree root。
    /// 這個 fail-closed 探索不依賴 process working directory，避免測試誤讀另一個 checkout；方法只建立短命
    /// <see cref="DirectoryInfo"/>，不持有檔案 handle、背景工作或共享 cache，找不到唯一可信根目錄時立即失敗。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "SpeechMessage.Dynamics.Tests")) &&
                File.Exists(Path.Combine(current.FullName, "docs", "scripts", "Invoke-AdfsTokenProbe.ps1")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "找不到包含 Dynamics tests 與 ADFS probe script 的目前 repository root。");
    }

    /// <summary>
    /// 提供可由測試 signal 與 cancellation token 控制的非同步 HttpClient factory。
    /// factory 是 handler 的唯一 owner；每次建立的 HttpClient wrapper 由 provider 在要求結束時 Dispose，且
    /// disposeHandler=false 防止第一個 caller 釋放仍由後續測試步驟使用的 handler。此 fake 不建立 socket、timer、
    /// cache 或背景 loop，因此 Dispose provider 後不會有額外 drain 責任或跨測試記憶體保留。
    /// </summary>
    private sealed class AsyncStubHttpClientFactory : IHttpClientFactory
    {
        private readonly AsyncStubHandler _handler;

        /// <summary>
        /// 建立以單一非同步 responder 為 trust boundary 的 factory；responder 必須由呼叫端完成或尊重取消，
        /// 避免測試留下永不完成的 Task。Constructor 不執行 I/O，也不複製 request content 或秘密資料。
        /// </summary>
        public AsyncStubHttpClientFactory(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _handler = new AsyncStubHandler(responder);
        }

        /// <summary>
        /// 建立短生命週期 HttpClient wrapper；provider 是 wrapper 的唯一 Dispose owner，factory 保留 handler ownership。
        /// 固定有界 timeout 防止測試在 signal 缺失時無限等待，且不使用共享 DefaultRequestHeaders 保存 caller 狀態。
        /// </summary>
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
    }

    /// <summary>
    /// 將 HttpMessageHandler 的唯一 SendAsync 呼叫轉交給測試提供的非同步 responder。
    /// fake 不快取 request、response、token 或 cancellation registration；每次呼叫的 response ownership 仍由
    /// Production provider 的 using 範圍管理，讓 single-flight／取消測試觀察真實生命週期而不是 mock 計數。
    /// </summary>
    private sealed class AsyncStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        /// <summary>
        /// 保存不可變 responder delegate；delegate 的 signal 與 Task 由個別測試擁有並在 provider Dispose 前完成，
        /// 因此 handler 沒有獨立的 timer、thread、socket 或 cleanup owner。
        /// </summary>
        public AsyncStubHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        /// <summary>
        /// 將 caller cancellation 原樣傳入 responder，證明取消不被 fake 吞掉或轉成另一個全域 token。
        /// 方法不保留 request reference；完成、失敗或取消後，request／response 的 deterministic cleanup 仍由呼叫端負責。
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }

    /// <summary>
    /// 測試用 IHttpClientFactory：固定回傳帶 stub handler 的 HttpClient。
    /// </summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _handler = new StubHandler(responder);
        }

        public HttpClient? LastClient { get; private set; }

        public HttpClient CreateClient(string name)
            => LastClient = new(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class TrackingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TrackingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _handler = new StubHandler(responder);
        }

        public TrackingHttpClient? LastClient { get; private set; }

        public HttpClient CreateClient(string name)
            => LastClient = new TrackingHttpClient(_handler);
    }

    private sealed class TrackingHttpClient : HttpClient
    {
        public TrackingHttpClient(HttpMessageHandler handler)
            : base(handler, disposeHandler: false)
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 計數型 Secret Resolver，用來證明 Dispose 後的 Token 要求會在任何 Credential／Token 解析前失敗。
    /// 實作不保存真實秘密，避免測試本身形成 Credential retention 或記錄敏感內容。
    /// </summary>
    private sealed class CountingSecretResolver : ISecretResolver
    {
        /// <summary>
        /// 取得目前解析次數；測試要求 Dispose 後維持為零。
        /// </summary>
        public int ResolveCalls { get; private set; }

        /// <summary>
        /// 記錄解析嘗試並固定回傳找不到；若生命週期防護正確，本方法不應被呼叫。
        /// </summary>
        public bool TryResolve(string secretReference, out string? secretValue)
        {
            ResolveCalls++;
            secretValue = null;
            return false;
        }
    }

    /// <summary>
    /// 計數型 HttpClientFactory，用來證明已 Dispose 的 Provider 不會重新取得 HTTP wrapper，
    /// 因而不會重新連結到長生命週期 handler pool 或建立新的 Socket 使用者。
    /// </summary>
    private sealed class CountingHttpClientFactory : IHttpClientFactory
    {
        /// <summary>
        /// 取得目前建立 HttpClient wrapper 的次數；正確的 Dispose 防護必須讓此值保持零。
        /// </summary>
        public int CreateCalls { get; private set; }

        /// <summary>
        /// 建立不會連線的測試 HttpClient；若生命週期防護正確，本方法不應被呼叫。
        /// </summary>
        public HttpClient CreateClient(string name)
        {
            CreateCalls++;
            return new HttpClient(new StubHandler(_ => JsonResponse("{}")), disposeHandler: true);
        }
    }
}
