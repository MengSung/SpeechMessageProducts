// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Security/AdfsDiagnosticSecurityTests.cs
// 目的：以 Release-safe source contract 與 DEBUG behavioral tests 鎖定 ADFS 診斷端點的資料隔離、
//       OAuth state 單次消費、private/no-store response 與確定性資源清理契約。
// ============================================================================

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChurchReport.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

/// <summary>
/// 驗證即使 Release build 會排除 DEBUG-only <c>DiagnosticsController</c> 型別，安全 source contract 仍會編譯並執行。
/// 測試只讀目前 worktree 的單一 UTF-8 source file，不建立 controller、Session、HTTP client、socket、timer 或背景工作；
/// repository root 探索若無法證明唯一 checkout 即 fail closed，避免誤讀其他工作樹後產生假綠燈。整份 source 只在單一
/// test scope 內短暫保留，換取對磁碟持久化、敏感回顯與 Trace sink 的完整線性掃描，完成後由 GC 回收而無共享 cache。
/// </summary>
public sealed class AdfsDiagnosticSourceContractTests
{
    /// <summary>
    /// 驗證 DiagnosticsController source 不含 local token store、probe 檔案 writer、Session identifier、raw body／exception
    /// 欄位或敏感 Trace。掃描以固定 allow-none 清單 fail closed，不解析或輸出任何實際 URL、token、client identifier、
    /// Session ID、body 或 exception message；Release 不需要載入被條件編譯排除的 controller 型別，因此仍能持續守住
    /// trust boundary。測試沒有併發 mutable state，且所有檔案讀取由 <see cref="File.ReadAllText(string)"/> 即時關閉。
    /// </summary>
    [Fact]
    public void Controller_source_has_no_token_store_probe_file_or_sensitive_output_contract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerPath = Path.Combine(
            repositoryRoot,
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "DiagnosticsController.cs");
        var source = File.ReadAllText(controllerPath);
        var forbiddenFragments = new[]
        {
            "LocalDevAdfsTokenStore",
            "WriteProbeResultAsync",
            "File.WriteAllText",
            "adfs-token-probe",
            "Session.Id",
            "ex.Message",
            "bodyPreview",
            "whoAmIBody",
            "tokenStorePath",
            "Trace.",
            "new SocketsHttpHandler"
        };

        var violationCount = forbiddenFragments.Count(fragment =>
            source.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        violationCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證診斷授權由部署端操作員清單與登入 cookie 內伺服器簽發的聯絡人識別 claim 共同決定。
    /// 測試以反射載入政策 helper，讓 RED 階段在型別尚不存在時仍可編譯；空清單、未驗證身分、缺少／重複
    /// <see cref="ClaimTypes.NameIdentifier"/> 或清單外識別皆必須 fail closed，且 helper 不可讀取 Session、建立
    /// cache、timer、subscription 或保留 request principal。
    /// </summary>
    [Fact]
    public void Diagnostics_operator_authorization_uses_server_issued_contact_claim_and_fails_closed()
    {
        var policyType = typeof(global::ChurchReport.Startup).Assembly.GetType(
            "ChurchReport.Security.DiagnosticsOperatorAuthorization");
        policyType.Should().NotBeNull("診斷端點必須有獨立且可測試的 fail-closed 操作員政策");

        var createAllowlist = policyType!.GetMethod("CreateAllowedContactIds");
        var isAuthorized = policyType.GetMethod("IsAuthorized");
        createAllowlist.Should().NotBeNull();
        isAuthorized.Should().NotBeNull();

        const string allowedContactId = "7a169533-54c6-4ce7-92b8-c4a28918d436";
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Diagnostics:OperatorContactIds:0"] = allowedContactId
            })
            .Build();
        var empty = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var configuredAllowlist = createAllowlist!.Invoke(null, new object[] { configured });
        var emptyAllowlist = createAllowlist.Invoke(null, new object[] { empty });

        static ClaimsPrincipal Principal(params Claim[] claims)
            => new(new ClaimsIdentity(claims, "synthetic-cookie"));

        bool Invoke(ClaimsPrincipal principal, object? allowlist)
            => (bool)(isAuthorized!.Invoke(null, new[] { principal, allowlist }) ?? false);

        Invoke(
                Principal(new Claim(ClaimTypes.NameIdentifier, allowedContactId)),
                configuredAllowlist)
            .Should().BeTrue();
        Invoke(
                Principal(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))),
                configuredAllowlist)
            .Should().BeFalse();
        Invoke(Principal(new Claim(ClaimTypes.Name, allowedContactId)), configuredAllowlist)
            .Should().BeFalse();
        Invoke(
                Principal(
                    new Claim(ClaimTypes.NameIdentifier, allowedContactId),
                    new Claim(ClaimTypes.NameIdentifier, allowedContactId)),
                configuredAllowlist)
            .Should().BeFalse();
        Invoke(new ClaimsPrincipal(new ClaimsIdentity()), configuredAllowlist).Should().BeFalse();
        Invoke(
                Principal(new Claim(ClaimTypes.NameIdentifier, allowedContactId)),
                emptyAllowlist)
            .Should().BeFalse();
    }

    /// <summary>
    /// 驗證 Startup 使用具名 factory client 擁有診斷 ADFS 的長生命週期 handler/socket pool，並保留 Cookie、Redirect、
    /// Proxy、解壓縮及連線數的安全界線。此 release-safe source contract 防止後續重構退回每 callback 建立 handler，
    /// 造成 socket churn、非決定性清理或跨要求 header/cookie 汙染。
    /// </summary>
    [Fact]
    public void Startup_registers_bounded_factory_owned_adfs_diagnostics_client()
    {
        var startupSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Startup.cs"));
        var authorizationSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Security",
            "DiagnosticsOperatorAuthorization.cs"));
        var registrationContract = startupSource + authorizationSource;
        var requiredFragments = new[]
        {
            "DiagnosticsHttpClientName = \"adfs-diagnostics\"",
            "AddHttpClient(DiagnosticsOperatorAuthorization.DiagnosticsHttpClientName",
            "UseCookies = false",
            "AllowAutoRedirect = false",
            "UseProxy = false",
            "AutomaticDecompression = DecompressionMethods.None",
            "MaxConnectionsPerServer",
            "SetHandlerLifetime"
        };

        requiredFragments.Should().OnlyContain(fragment =>
            registrationContract.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// 從目前 test output 向上尋找同時包含 ChurchReport tests 與 Production controller 的 worktree root。
    /// 每一層只建立短命 <see cref="DirectoryInfo"/>，不持有 directory handle、watcher、timer 或 static cache；找到第一個
    /// 完整 sentinel 後立即返回，找不到則 fail closed。此 helper 不接受 caller-controlled 路徑，避免 source-contract
    /// 測試跨越既定 workspace trust boundary，也不會把實際路徑寫入 assertion 訊息。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "ChurchReport.MemberInfo.Tests",
                    "ChurchReport.MemberInfo.Tests.csproj")) &&
                File.Exists(Path.Combine(
                    current.FullName,
                    "SpeechMessageProducts.ChurchReport",
                    "Controllers",
                    "DiagnosticsController.cs")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到目前 ChurchReport worktree root。");
    }
}

#if DEBUG
/// <summary>
/// 將會改變 process-wide current directory 的 DEBUG 診斷測試放入不可平行 collection。
/// 這是測試檔案輸出的唯一協調 owner，可避免現有 RED Production writer 把 probe JSON 寫到其他測試的工作目錄；
/// collection 本身不建立 thread、timer、Session 或網路資源，所有暫存目錄仍由個別 test 的 disposable scope 清理。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AdfsDiagnosticSecurityCollection
{
    /// <summary>
    /// 提供 test class 與 collection definition 共用的固定名稱；名稱不含 Session、identity、URL 或 credential，
    /// 不參與 Production key、cache 或 correlation，也沒有 Dispose 責任。
    /// </summary>
    public const string Name = "ADFS diagnostic security serial collection";
}

/// <summary>
/// 直接執行 DEBUG-only DiagnosticsController，驗證 OAuth state、response cache policy 與敏感輸出邊界。
/// 每個測試建立獨立記憶體 Session 與 controller；需要 HTTP 時只連測試擁有的 loopback server，server 以 bounded request
/// drain、明確 cancellation 與 <see cref="IAsyncDisposable"/> 回收 socket／Task。會觸發現有 probe writer 的路徑先進入
/// process-wide 暫存工作目錄，並在 request 完成後還原與刪除，確保 token、body、Session state、檔案與背景資源不跨測試。
/// </summary>
[Collection(AdfsDiagnosticSecurityCollection.Name)]
public sealed class AdfsDiagnosticBehaviorTests
{
    private const string OAuthStateKey = "Diagnostics.AdfsOAuth.State";
    private const string OAuthStateIssuedAtKey = "Diagnostics.AdfsOAuth.IssuedAtUtcTicks";
    private const string AuthorityMarker = "https://authority-marker.invalid/adfs";
    private const string ResourceMarker = "https://resource-marker.invalid/";
    private const string ClientMarker = "diagnostic-client-marker";
    private const string RedirectMarker = "https://callback-marker.invalid/diagnostics/adfs-callback";
    private const string SessionMarker = "diagnostic-session-marker";
    private const string ResponseBodyMarker = "diagnostic-response-body-marker";
    private const string OAuthState = "synthetic-oauth-state";

    /// <summary>
    /// 驗證 controller 本身宣告具名操作員政策，不能只以一般 <see cref="AuthorizeAttribute"/> 接受任意已登入使用者。
    /// MVC 授權會在 action、Session 與上游 HTTP 之前執行，因此政策名稱是避免非操作員觸發 ADFS／CRM 診斷流量的第一道
    /// fail-closed 邊界；測試不執行網路、Session 寫入或背景工作。
    /// </summary>
    [Fact]
    public void Controller_requires_diagnostics_operator_policy()
    {
        var authorize = typeof(DiagnosticsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        authorize.Policy.Should().Be("diagnostics-operator");
    }

    /// <summary>
    /// 驗證診斷 HTTP client 由 DI factory 注入，controller 不再自行建立 handler/socket pool。Constructor 只保存 host-owned
    /// factory reference，不保存每個要求的 token、Session、request 或 response；實際 wrapper 仍由 action using 決定性釋放。
    /// </summary>
    [Fact]
    public void Controller_constructor_requires_http_client_factory()
    {
        var constructorParameterTypes = typeof(DiagnosticsController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType);

        constructorParameterTypes.Should().Contain(typeof(IHttpClientFactory));
    }

    /// <summary>
    /// 驗證只做 authorize preview 時不建立 OAuth Session state。preview 是純資訊要求，不是 authorization transaction，
    /// 因此 Session store 不應取得 state retention owner；真正 redirect 才能配置一次性 state。測試使用記憶體 Session、
    /// 暫存工作目錄與同步完成的 controller action，不建立 socket 或背景工作；scope Dispose 會還原 current directory 並清除
    /// 現有 RED writer 可能產生的檔案，避免跨 Session 污染與無界磁碟成長。
    /// </summary>
    [Fact]
    public async Task Authorize_preview_does_not_create_oauth_session_state()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        var session = new RecordingSession(SessionMarker);
        var controller = CreateController(session);

        _ = await controller.AdfsAuthorize();

        session.StateSetCount.Should().Be(0);
        session.ContainsOAuthState.Should().BeFalse();
    }

    /// <summary>
    /// 驗證 ADFS 回傳 error 時仍先 read-and-remove OAuth state，使錯誤 callback 成為 terminal consumer 而不是保留可重播 nonce。
    /// Session fake 複製所有 byte[]，避免共享 mutable buffer 掩蓋競爭；action 不需 HTTP，且暫存 scope 確定性清除 RED writer。
    /// 讀取與移除順序錯誤、缺少 cleanup 或重複 owner 都會 fail closed；測試不輸出 error、state 或 Session identifier。
    /// </summary>
    [Fact]
    public async Task Callback_error_path_reads_and_removes_oauth_state()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session);

        _ = await controller.AdfsCallback(null, OAuthState, "synthetic-error", null);

        AssertOAuthStateReadAndRemoved(session);
    }

    /// <summary>
    /// 驗證 callback state mismatch 也會消費 server-owned expected state，避免攻擊者反覆猜測同一 nonce 或讓分散式 Session
    /// 長期保留驗證材料。此 early-return 路徑不建立 HttpClient、socket 或 response stream；暫存工作目錄只隔離現有檔案 writer，
    /// 並於測試完成時清除。測試只斷言 read/remove 計數與布林順序，不把任何 state 或 Session 值寫到結果。
    /// </summary>
    [Fact]
    public async Task Callback_state_mismatch_reads_and_removes_oauth_state()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session);

        _ = await controller.AdfsCallback("synthetic-code", "different-state", null, null);

        AssertOAuthStateReadAndRemoved(session);
    }

    /// <summary>
    /// 驗證 state 正確但缺少 authorization code 時仍 read-and-remove state；缺碼是 terminal protocol failure，不得讓下一個
    /// request 重用同一 Session nonce。測試沒有 outbound I/O、timer 或 cancellation registration，所有 Session bytes 由 fake
    /// 唯一擁有並在 Remove 時釋放；暫存目錄負責現有 RED probe 檔的 drain/cleanup，避免測試本身形成 retention。
    /// </summary>
    [Fact]
    public async Task Callback_missing_code_reads_and_removes_oauth_state()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session);

        _ = await controller.AdfsCallback(null, OAuthState, null, null);

        AssertOAuthStateReadAndRemoved(session);
    }

    /// <summary>
    /// 驗證 token exchange 與 WhoAmI 均成功時 state 仍只消費一次，且成功不會延長 Session nonce 生命週期。
    /// loopback server 是 request/response stream 與 accept Task 的唯一 owner，兩個有界 response 完成後由 await using 停止 listener
    /// 並等待 worker；controller 與 Session 不進入 static cache。暫存 working directory 只吸收 RED 寫檔並於 finally 清除。
    /// </summary>
    [Fact]
    public async Task Callback_success_reads_and_removes_oauth_state()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        await using var server = ScriptedLoopbackServer.Start(
            (HttpStatusCode.OK, """{"access_token":"synthetic-access","refresh_token":"synthetic-refresh","expires_in":900}"""),
            (HttpStatusCode.OK, """{"UserId":"synthetic-user"}"""));
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session, server.BaseUri);

        _ = await controller.AdfsCallback("synthetic-code", OAuthState, null, null);

        AssertOAuthStateReadAndRemoved(session);
    }

    /// <summary>
    /// 驗證 token response 解析發生例外時，state cleanup 仍在 exception response 前完成。loopback server 回傳固定、小型、
    /// 非法 JSON 以觸發可重現錯誤，不連外也不保留 body stream；await using 會取消 accept、關閉 socket 並等待 worker。
    /// Session state 的 read/remove 由單一 fake owner 記錄，暫存目錄則確定性清除 RED writer，避免 exception 路徑洩漏資源。
    /// </summary>
    [Fact]
    public async Task Callback_exception_path_reads_and_removes_oauth_state()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        await using var server = ScriptedLoopbackServer.Start((HttpStatusCode.OK, "not-json"));
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session, server.BaseUri);

        _ = await controller.AdfsCallback("synthetic-code", OAuthState, null, null);

        AssertOAuthStateReadAndRemoved(session);
    }

    /// <summary>
    /// 驗證同一 OAuth state 不能跨兩個 callback 重播。第一個成功 callback 是 state 的唯一 consumer；第二個 request 必須在
    /// 建立 HttpClient、配置 request content 或接觸 loopback server 前 fail closed。server 預留額外 bounded responses 只讓目前
    /// 缺陷能完整回傳而不造成測試 timeout；修正後未使用的 accept 由 Dispose cancellation 結束，所有 socket/Task 都會 drain。
    /// </summary>
    [Fact]
    public async Task OAuth_state_is_consumed_only_once()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        await using var server = ScriptedLoopbackServer.Start(
            (HttpStatusCode.OK, """{"access_token":"synthetic-access","expires_in":900}"""),
            (HttpStatusCode.OK, "{}"),
            (HttpStatusCode.OK, """{"access_token":"synthetic-access","expires_in":900}"""),
            (HttpStatusCode.OK, "{}"));
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session, server.BaseUri);

        _ = await controller.AdfsCallback("synthetic-code", OAuthState, null, null);
        var replay = await controller.AdfsCallback("synthetic-code", OAuthState, null, null);

        IsInvalidStateResult(replay).Should().BeTrue();
    }

    /// <summary>
    /// 驗證超過五分鐘有效窗的 OAuth state 即使 nonce 相符也必須在任何 deployment setting 或 outbound HTTP 前
    /// fail closed。測試 Session 同時 seed state 與過期 timestamp，callback 仍須 exactly-once read-and-remove；
    /// controller 使用不可路由 marker，因此若錯誤進入 token exchange 會使結果不再符合固定 invalid-state contract。
    /// 本案例不建立 listener、timer、共享 cache 或背景工作，所有 Session bytes 都由單一 fake owner 在 Remove 後釋放。
    /// </summary>
    [Fact]
    public async Task Expired_oauth_state_is_rejected_before_token_exchange()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        var session = CreateSessionWithOAuthState(DateTimeOffset.UtcNow.AddMinutes(-10));
        var controller = CreateController(session);

        var result = await controller.AdfsCallback("synthetic-code", OAuthState, null, null);

        IsInvalidStateResult(result).Should().BeTrue();
        AssertOAuthStateReadAndRemoved(session);
    }

    /// <summary>
    /// 驗證 authorize preview response 明確宣告 private,no-store，使瀏覽器、proxy 與共享 cache 不得保存 deployment-owned
    /// authority/resource/client metadata。測試不輸出 header 或 payload 值，只檢查兩個固定 directive；暫存 scope 清除現有 RED
    /// 檔案 writer，Session 與 controller 僅存活於單一 request，沒有 background timer、stream 或取消工作需額外 drain。
    /// </summary>
    [Fact]
    public async Task Authorize_preview_response_is_private_no_store()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        var controller = CreateController(new RecordingSession(SessionMarker));

        _ = await controller.AdfsAuthorize();

        AssertPrivateNoStore(controller);
    }

    /// <summary>
    /// 驗證 callback 的所有 early terminal response 也必須 private,no-store；缺碼路徑可在零 outbound I/O 下證明 header contract，
    /// 並同時避免測試配置不必要 socket。Session state 仍由 callback 消費，暫存工作目錄由 disposable owner 還原與清除；
    /// header assertion 僅回報布林值，不會把 URL、client identifier、Session ID、body 或 exception detail寫入測試結果。
    /// </summary>
    [Fact]
    public async Task Callback_response_is_private_no_store()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session);

        _ = await controller.AdfsCallback(null, OAuthState, null, null);

        AssertPrivateNoStore(controller);
    }

    /// <summary>
    /// 驗證 Session diagnostic response 也必須 private,no-store，避免 shared cache 保留 authentication/session availability 訊號。
    /// 此同步 action 不建立 HTTP client、檔案、timer 或背景 Task；記憶體 Session 是唯一 owner，request 結束即可回收。
    /// assertion 只檢查固定 cache directive 的存在，不序列化或輸出 Session identifier 與 response payload。
    /// </summary>
    [Fact]
    public void Session_response_is_private_no_store()
    {
        var controller = CreateController(new RecordingSession(SessionMarker));

        _ = controller.GetSessionInfo();

        AssertPrivateNoStore(controller);
    }

    /// <summary>
    /// 驗證 Session diagnostic response 不回顯 <see cref="ISession.Id"/>。Session ID 屬伺服器端 correlation/security boundary，
    /// 即使 DEBUG 且已授權也不應成為 response contract；測試將結果序列化後只回傳「是否含 marker」布林值，失敗時不輸出
    /// 實際 identifier。action 沒有 socket、stream、cache 或 background resource，記憶體配置固定且 request 後可直接回收。
    /// </summary>
    [Fact]
    public void Session_response_does_not_echo_session_identifier()
    {
        var controller = CreateController(new RecordingSession(SessionMarker));

        var result = controller.GetSessionInfo();

        ResultContainsAny(result, SessionMarker).Should().BeFalse();
    }

    /// <summary>
    /// 驗證 authorize preview 不回顯 authority、resource、client identifier 或 redirect URI，也不包含會組合這些值的完整 URL。
    /// 測試值全部是隔離 marker，結果檢查只輸出布林，不把 marker 本身交給 assertion formatter；暫存目錄確定性刪除 RED writer，
    /// Session preview 不得取得 state owner。此測試無 outbound I/O，維持有界記憶體與零 background cleanup 負擔。
    /// </summary>
    [Fact]
    public async Task Authorize_preview_does_not_echo_deployment_or_client_values()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        var controller = CreateController(new RecordingSession(SessionMarker));

        var result = await controller.AdfsAuthorize();

        ResultContainsAny(
                result,
                AuthorityMarker,
                ResourceMarker,
                ClientMarker,
                RedirectMarker,
                "authorizeUrl")
            .Should().BeFalse();
    }

    /// <summary>
    /// 驗證成功 callback 只回傳固定診斷分類，不回顯 authority/resource/client/redirect、WhoAmI body 或 token-store metadata。
    /// 兩次 loopback response 都有固定 byte 上限，server Dispose 會停止 listener、取消 accept 並等待 worker；controller 的暫存
    /// token/body 只存在於 request scope，測試結果只比較布林，且 working-directory owner 會清除目前 RED 寫下的 token/probe 檔。
    /// </summary>
    [Fact]
    public async Task Callback_success_does_not_echo_sensitive_configuration_or_body()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        await using var server = ScriptedLoopbackServer.Start(
            (HttpStatusCode.OK, """{"access_token":"synthetic-access","refresh_token":"synthetic-refresh","expires_in":900}"""),
            (HttpStatusCode.OK, ResponseBodyMarker));
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session, server.BaseUri);

        var result = await controller.AdfsCallback("synthetic-code", OAuthState, null, null);

        ResultContainsAny(
                result,
                server.BaseUri.AbsoluteUri,
                ResourceMarker,
                ClientMarker,
                RedirectMarker,
                ResponseBodyMarker,
                "bodyPreview",
                "whoAmIBody",
                "tokenStorePath")
            .Should().BeFalse();
    }

    /// <summary>
    /// 驗證 token endpoint 的失敗 body 不進入 callback JSON。loopback server 回傳一個小型 synthetic marker 並在 response 後關閉
    /// connection；controller 應只保留固定 HTTP category。結果 assertion 只回報 marker 是否存在，避免把 body 寫入 test output；
    /// await using 與 temporary directory 分別擁有 socket/Task drain 及 RED probe 檔 cleanup，沒有跨 request retention。
    /// </summary>
    [Fact]
    public async Task Callback_failure_does_not_echo_upstream_body()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        await using var server = ScriptedLoopbackServer.Start((HttpStatusCode.BadRequest, ResponseBodyMarker));
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session, server.BaseUri);

        var result = await controller.AdfsCallback("synthetic-code", OAuthState, null, null);

        ResultContainsAny(result, ResponseBodyMarker, "bodyPreview").Should().BeFalse();
    }

    /// <summary>
    /// 驗證 JSON 解析例外只映射成固定 <c>upstream-error</c> category，不把 exception type/message/inner detail 回顯。
    /// 非法 response 由 bounded loopback server 提供，沒有外部網路與不確定 timeout；server、Session state、暫存目錄與所有 stream
    /// 都有唯一 cleanup owner。helper 只回傳分類是否精確匹配的布林值，故 assertion 失敗不會輸出 raw exception message。
    /// </summary>
    [Fact]
    public async Task Callback_exception_uses_fixed_error_category()
    {
        using var workingDirectory = TemporaryWorkingDirectory.Enter();
        await using var server = ScriptedLoopbackServer.Start((HttpStatusCode.OK, "not-json"));
        var session = CreateSessionWithOAuthState();
        var controller = CreateController(session, server.BaseUri);

        var result = await controller.AdfsCallback("synthetic-code", OAuthState, null, null);

        HasFixedErrorCategory(result, "upstream-error").Should().BeTrue();
    }

    /// <summary>
    /// 建立帶有一次性 OAuth state 的記憶體 Session。seed 是測試 arrange，不計入 Production Set 次數；dictionary 是 fake 的
    /// 唯一 owner，值在存取時複製以避免 caller 共享 mutable byte[]。helper 不建立 timer、distributed-session connection、
    /// cancellation registration 或 background cleanup，所有狀態在 test scope 結束後即可回收。
    /// </summary>
    private static RecordingSession CreateSessionWithOAuthState(DateTimeOffset? issuedAt = null)
    {
        var session = new RecordingSession(SessionMarker);
        session.SeedOAuthState(OAuthState, issuedAt ?? DateTimeOffset.UtcNow);
        return session;
    }

    /// <summary>
    /// 建立單一 request scope 的 DiagnosticsController 與 DefaultHttpContext。Configuration 只含 synthetic deployment marker；
    /// 若有 loopback URI，token 與 WhoAmI 都固定指向該測試 server，絕不連正式端點。Session 與 controller 由呼叫端唯一擁有，
    /// 沒有 DI singleton、shared cache 或背景工作；request scheme/host 固定，避免 callback URI 依賴 process 環境。
    /// </summary>
    private static DiagnosticsController CreateController(RecordingSession session, Uri? loopbackBaseUri = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["DynamicsAccess:Embedded:AuthorityUri"] = loopbackBaseUri?.AbsoluteUri ?? AuthorityMarker,
            ["DynamicsAccess:Embedded:ResourceUri"] = ResourceMarker,
            ["DynamicsAccess:Embedded:ClientId"] = ClientMarker,
            ["DynamicsAccess:Embedded:RedirectUri"] = RedirectMarker,
            ["DynamicsAccess:Embedded:OrganizationWebApiBaseUri"] = loopbackBaseUri is null
                ? "https://organization-marker.invalid/api/data/v8.2/"
                : new Uri(loopbackBaseUri, "api/data/v8.2/").AbsoluteUri
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var httpContext = new DefaultHttpContext
        {
            Session = session,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "synthetic-diagnostic-user") },
                "synthetic-authentication"))
        };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("request-marker.invalid");

        return new DiagnosticsController(configuration, new TestHttpClientFactory())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    /// <summary>
    /// 測試用 factory 每次建立由該 HttpClient wrapper 唯一擁有的 handler；controller action 的 using 會同步釋放
    /// wrapper 與 handler，因此 loopback socket 不會跨測試保留。Production 則由 Startup 的 named client 共用
    /// Host-owned pool；此 fake 不保存 request、token、Session、timer、background task 或 mutable default header。
    /// </summary>
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        /// <summary>建立有界、安全且只屬於目前測試 callback 的 client。</summary>
        public HttpClient CreateClient(string name)
        {
            name.Should().Be("adfs-diagnostics");
            return new HttpClient(
                new SocketsHttpHandler
                {
                    UseCookies = false,
                    AllowAutoRedirect = false,
                    UseProxy = false,
                    AutomaticDecompression = DecompressionMethods.None,
                    MaxConnectionsPerServer = 1
                },
                disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }
    }

    /// <summary>
    /// 驗證 Session fake 觀察到 exactly-once Get 與 Remove，且 Remove 發生在讀取 expected state 之後並確實清除資料。
    /// helper 只斷言整數與布林，不格式化 state bytes、Session ID 或 response payload；沒有 I/O、等待或資源 ownership。
    /// 這個順序讓 callback 在所有 terminal path fail closed，並防止分散式 Session retention 與 replay window。
    /// </summary>
    private static void AssertOAuthStateReadAndRemoved(RecordingSession session)
    {
        session.StateReadCount.Should().Be(1);
        session.StateRemoveCount.Should().Be(1);
        session.StateWasReadBeforeRemove.Should().BeTrue();
        session.ContainsOAuthState.Should().BeFalse();
    }

    /// <summary>
    /// 驗證 controller response 同時包含 private 與 no-store cache directive。helper 只讀 response header 的短命字串並轉成
    /// 兩個布林 assertion，不輸出完整 header 或 payload；它不配置 cache、stream、timer 或 background task，也沒有 Dispose 責任。
    /// 兩個 directive 缺一即 fail closed，避免瀏覽器或中介共享 diagnostic response。
    /// </summary>
    private static void AssertPrivateNoStore(Controller controller)
    {
        var cacheControl = controller.Response.Headers.CacheControl.ToString();
        cacheControl.Contains("private", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    /// <summary>
    /// 將 JsonResult 的 in-memory value 序列化後只回傳是否含任一 synthetic marker；caller 的 assertion 因此只看到布林值，
    /// 不會在失敗輸出中回顯 URL、client identifier、Session ID、body 或 exception detail。序列化結果有界於診斷 response，
    /// 不寫檔、不進 cache，方法完成後即可回收；非 JsonResult 直接視為 contract violation 並回傳 true。
    /// </summary>
    private static bool ResultContainsAny(IActionResult result, params string[] markers)
    {
        if (result is not JsonResult jsonResult)
        {
            return true;
        }

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        return markers.Any(marker => serialized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判斷 callback replay 是否得到固定 invalid-state category，不將實際 dictionary、state 或其他 response 欄位交給 assertion。
    /// helper 是純記憶體、同步且無配置的 fail-closed check；非 JsonResult、非 dictionary 或分類不符都回傳 false，
    /// 不建立 cache、背景工作或 cleanup 責任。
    /// </summary>
    private static bool IsInvalidStateResult(IActionResult result)
        => result is JsonResult { Value: IDictionary<string, object?> values } &&
           values.TryGetValue("error", out var error) &&
           string.Equals(error as string, "Invalid or missing OAuth state.", StringComparison.Ordinal);

    /// <summary>
    /// 判斷 exception response 是否只使用呼叫端指定的固定 error category。helper 不讀 inner exception、不序列化整份 response、
    /// 不記錄實際 error value；所有非預期形狀都 fail closed 為 false。它沒有 I/O、取消、並行或 Dispose 責任，避免測試工具
    /// 本身擴大敏感資料生命週期。
    /// </summary>
    private static bool HasFixedErrorCategory(IActionResult result, string expectedCategory)
        => result is JsonResult { Value: IDictionary<string, object?> values } &&
           values.TryGetValue("error", out var error) &&
           string.Equals(error as string, expectedCategory, StringComparison.Ordinal);

    /// <summary>
    /// 記錄 OAuth state read/set/remove 順序的記憶體 Session fake。Dictionary 是唯一 owner；所有 byte[] 在 Set/TryGetValue
    /// 邊界複製，避免 Production 與 test 共用 mutable buffer。fake 不模擬 distributed store、timer、expiry 或背景清理，
    /// 因而不會掩蓋 callback 的 deterministic read-and-remove 責任；資料量固定且每個 test 建立獨立 instance。
    /// </summary>
    private sealed class RecordingSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        private int _lastStateOperation;

        /// <summary>
        /// 建立單一 test request 擁有的 Session；identifier 只用 synthetic marker，永不寫入 log/assertion，也不參與 cache key。
        /// Constructor 不建立外部連線、timer 或 cancellation registration。
        /// </summary>
        public RecordingSession(string id)
        {
            Id = id;
        }

        /// <summary>
        /// 測試 Session 永遠可用，避免可用性分支干擾 OAuth cleanup；此值沒有 mutable state 或資源 ownership。
        /// </summary>
        public bool IsAvailable => true;

        /// <summary>
        /// 取得 synthetic Session identifier；Production response 不得讀出並回顯此值。
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 取得目前 key 的 snapshot enumeration；dictionary 只由同一測試執行緒存取，不提供跨執行緒 mutation。
        /// </summary>
        public IEnumerable<string> Keys => _values.Keys;

        /// <summary>
        /// 取得 Production 對 OAuth state key 的 Set 次數；arrange seed 不計入，用來證明 preview 零配置。
        /// </summary>
        public int StateSetCount { get; private set; }

        /// <summary>
        /// 取得 Production 對 OAuth state key 的讀取次數，驗證 callback 在 cleanup 前取得 expected state。
        /// </summary>
        public int StateReadCount { get; private set; }

        /// <summary>
        /// 取得 Production 對 OAuth state key 的移除次數，驗證每個 terminal callback exactly-once cleanup。
        /// </summary>
        public int StateRemoveCount { get; private set; }

        /// <summary>
        /// 指示最後一次 Remove 前是否先完成 state read；只保存布林，不保留讀出的 state 值。
        /// </summary>
        public bool StateWasReadBeforeRemove { get; private set; }

        /// <summary>
        /// 指示 Session dictionary 是否仍持有 OAuth state；getter 不增加 read counter，避免 assertion 改變被測行為。
        /// </summary>
        public bool ContainsOAuthState => _values.ContainsKey(OAuthStateKey);

        /// <summary>
        /// 由 test arrange 直接放入一次性 state，不計入 Production Set。字串轉成 UTF-8 bytes 後由 dictionary 唯一擁有，
        /// 沒有外部 stream、共享 buffer 或 Dispose 責任。
        /// </summary>
        public void SeedOAuthState(string state, DateTimeOffset issuedAt)
        {
            _values[OAuthStateKey] = Encoding.UTF8.GetBytes(state);
            _values[OAuthStateIssuedAtKey] = Encoding.UTF8.GetBytes(
                issuedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 清除所有 in-memory values；沒有外部 store、timer 或 callback，完成後所有 byte[] 可回收。
        /// </summary>
        public void Clear()
            => _values.Clear();

        /// <summary>
        /// 模擬同步完成的 Session commit；尊重已取消 token 並且不啟動背景 flush，避免測試掩蓋取消語意。
        /// </summary>
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;

        /// <summary>
        /// 模擬同步完成的 Session load；尊重已取消 token，且不建立 distributed-store connection 或長生命週期 Task。
        /// </summary>
        public Task LoadAsync(CancellationToken cancellationToken = default)
            => cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;

        /// <summary>
        /// 移除指定 key；OAuth state 路徑會記錄 read-before-remove 順序與 exactly-once 計數，並立即釋放 dictionary ownership。
        /// </summary>
        public void Remove(string key)
        {
            if (string.Equals(key, OAuthStateKey, StringComparison.Ordinal))
            {
                StateRemoveCount++;
                StateWasReadBeforeRemove = _lastStateOperation == 1;
                _lastStateOperation = 2;
            }

            _values.Remove(key);
        }

        /// <summary>
        /// 複製並保存 Session value，避免 caller 在 Set 後修改同一 buffer；OAuth state 的 Production Set 會被計數。
        /// </summary>
        public void Set(string key, byte[] value)
        {
            if (string.Equals(key, OAuthStateKey, StringComparison.Ordinal))
            {
                StateSetCount++;
                _lastStateOperation = 3;
            }

            _values[key] = value.ToArray();
        }

        /// <summary>
        /// 讀取 Session value 時回傳 byte[] 複本，避免 controller 取得 dictionary 內部 mutable buffer；OAuth state read 會被計數。
        /// 找不到時回傳空陣列且不配置外部資源，沒有 stream、handle 或 cleanup responsibility。
        /// </summary>
        public bool TryGetValue(string key, out byte[] value)
        {
            if (string.Equals(key, OAuthStateKey, StringComparison.Ordinal))
            {
                StateReadCount++;
                _lastStateOperation = 1;
            }

            if (_values.TryGetValue(key, out var stored))
            {
                value = stored.ToArray();
                return true;
            }

            value = Array.Empty<byte>();
            return false;
        }
    }

    /// <summary>
    /// 暫時切換 process current directory，隔離目前 RED Production 的 probe/token file writer。
    /// static gate 是此 process-wide mutable state 的唯一併發 owner；constructor 取得 gate、建立唯一空目錄並記住原路徑，
    /// Dispose 必須先還原再 recursive delete，最後才釋放 gate。scope 不含 token/Session/URL，且沒有 timer 或背景工作。
    /// </summary>
    private sealed class TemporaryWorkingDirectory : IDisposable
    {
        private static readonly SemaphoreSlim CurrentDirectoryGate = new(1, 1);
        private readonly string _previousDirectory;
        private readonly string _temporaryDirectory;
        private int _disposed;

        /// <summary>
        /// 在已取得 process-wide gate 後建立隔離工作目錄並切換 current directory。若建立或切換失敗，會釋放 gate 並向上傳遞，
        /// 不留下半初始化 owner；路徑使用隨機名稱，不含任何 request、Session 或 credential 資訊。
        /// </summary>
        private TemporaryWorkingDirectory()
        {
            CurrentDirectoryGate.Wait();
            try
            {
                _previousDirectory = Directory.GetCurrentDirectory();
                _temporaryDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "churchreport-adfs-security-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_temporaryDirectory);
                Directory.SetCurrentDirectory(_temporaryDirectory);
            }
            catch
            {
                CurrentDirectoryGate.Release();
                throw;
            }
        }

        /// <summary>
        /// 取得新的隔離 scope；呼叫端是唯一 Dispose owner，必須以 using 包住會觸發 controller 檔案輸出的完整 request。
        /// </summary>
        public static TemporaryWorkingDirectory Enter()
            => new();

        /// <summary>
        /// exactly-once 還原原工作目錄、recursive 清除 RED writer 產物並釋放 process-wide gate。
        /// cleanup 在 finally 釋放 gate，避免刪除失敗造成其他測試永久阻塞；scope 不等待背景工作，因 controller request 已先完成。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                Directory.SetCurrentDirectory(_previousDirectory);
                if (Directory.Exists(_temporaryDirectory))
                {
                    Directory.Delete(_temporaryDirectory, recursive: true);
                }
            }
            finally
            {
                CurrentDirectoryGate.Release();
            }
        }
    }

    /// <summary>
    /// 以 bounded raw TCP 實作的 loopback HTTP server，讓 controller behavioral tests 不連外並能控制 response 路徑。
    /// server instance 是 listener、accept worker、request stream 與 cancellation source 的唯一 owner；每個 response 一次使用，
    /// request header/body 有硬上限，buffer 歸還前清零。DisposeAsync 取消 accept、停止 listener、等待 worker，防止 socket/Task 洩漏。
    /// </summary>
    private sealed class ScriptedLoopbackServer : IAsyncDisposable
    {
        private const int MaxRequestBytes = 64 * 1024;
        private readonly TcpListener _listener;
        private readonly (HttpStatusCode StatusCode, string Body)[] _responses;
        private readonly CancellationTokenSource _disposeCts = new();
        private readonly Task _worker;
        private int _disposeStarted;

        /// <summary>
        /// 啟動綁定 IPv4 loopback 與 ephemeral port 的 listener，並建立唯一受管 worker 處理固定 response sequence。
        /// Constructor 不接受外部 host、不使用 proxy、不保存 Session/token key；worker Task 被欄位持有並在 DisposeAsync await，
        /// 因此沒有 fire-and-forget 生命週期。response 數量固定，避免無界 queue 與記憶體成長。
        /// </summary>
        private ScriptedLoopbackServer((HttpStatusCode StatusCode, string Body)[] responses)
        {
            _responses = responses;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/", UriKind.Absolute);
            _worker = RunAsync(_disposeCts.Token);
        }

        /// <summary>
        /// 取得只指向本機 ephemeral listener 的 base URI；值僅用於 synthetic configuration，不輸出到 assertion 或 log。
        /// </summary>
        public Uri BaseUri { get; }

        /// <summary>
        /// 建立具有固定 response sequence 的 server。至少一個 response 才能啟動；呼叫端是唯一 DisposeAsync owner，
        /// 必須在 controller request 結束後等待清理，確保 listener、stream、buffer 與 worker 回到基準。
        /// </summary>
        public static ScriptedLoopbackServer Start(
            params (HttpStatusCode StatusCode, string Body)[] responses)
        {
            ArgumentNullException.ThrowIfNull(responses);
            if (responses.Length == 0)
            {
                throw new ArgumentException("Loopback server 至少需要一個 response。", nameof(responses));
            }

            return new ScriptedLoopbackServer(responses);
        }

        /// <summary>
        /// 依序接受固定數量的 connection、完整 drain bounded request，再送出一次性 response 並關閉 client。
        /// cancellation 只由 server Dispose owner 發出；每個 TcpClient/NetworkStream 都在 using scope 清理，任何失敗都由 worker Task
        /// 保留並在 DisposeAsync 觀察，不吞掉非取消例外。沒有 shared queue、timer 或跨 request buffer。
        /// </summary>
        private async Task RunAsync(CancellationToken cancellationToken)
        {
            foreach (var response in _responses)
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                await using var stream = client.GetStream();
                await DrainRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(stream, response.StatusCode, response.Body, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 讀取 HTTP header 與宣告的 request body，總量超過 <see cref="MaxRequestBytes"/> 即 fail closed。
        /// 租用 buffer 在 finally 清零並歸還 ArrayPool；方法不解析或記錄 form 值，不保留 client identifier/code/resource，
        /// cancellation 直接終止讀取並由外層 Dispose 關閉 stream，避免半開 connection 與未回收記憶體。
        /// </summary>
        private static async Task DrainRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                using var received = new MemoryStream(capacity: 4096);
                var headerEnd = -1;
                while (headerEnd < 0)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new IOException("Loopback request 在 header 完成前結束。");
                    }

                    received.Write(buffer, 0, read);
                    if (received.Length > MaxRequestBytes)
                    {
                        throw new IOException("Loopback request 超過測試上限。");
                    }

                    headerEnd = FindHeaderEnd(received.GetBuffer().AsSpan(0, checked((int)received.Length)));
                }

                var requestBytes = received.GetBuffer();
                var headerText = Encoding.ASCII.GetString(requestBytes, 0, headerEnd);
                var contentLength = ParseContentLength(headerText);
                var bodyBytesRead = checked((int)received.Length) - headerEnd - 4;
                if (contentLength < 0 || headerEnd + 4 + contentLength > MaxRequestBytes)
                {
                    throw new IOException("Loopback request body 長度不合法。");
                }

                while (bodyBytesRead < contentLength)
                {
                    var remaining = Math.Min(buffer.Length, contentLength - bodyBytesRead);
                    var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new IOException("Loopback request body 提前結束。");
                    }

                    bodyBytesRead += read;
                }

                CryptographicOperations.ZeroMemory(
                    received.GetBuffer().AsSpan(0, checked((int)received.Length)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// 尋找 HTTP header 的 CRLFCRLF 結尾；純 span 掃描不配置字串、不保留 request bytes，也沒有並行或 cleanup 責任。
        /// 找不到時回傳 -1，讓 caller 繼續在總 byte 上限內讀取。
        /// </summary>
        private static int FindHeaderEnd(ReadOnlySpan<byte> bytes)
        {
            for (var index = 0; index <= bytes.Length - 4; index++)
            {
                if (bytes[index] == (byte)'\r' &&
                    bytes[index + 1] == (byte)'\n' &&
                    bytes[index + 2] == (byte)'\r' &&
                    bytes[index + 3] == (byte)'\n')
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// 從 bounded header 解析唯一 Content-Length；缺少時視為零，重複或無效值 fail closed。
        /// 方法只保留短命 header lines，不記錄 request body、URL 或 credential，且不建立 stream、timer 或 cache。
        /// </summary>
        private static int ParseContentLength(string headerText)
        {
            var lengths = headerText
                .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line[(line.IndexOf(':') + 1)..].Trim())
                .ToArray();
            if (lengths.Length == 0)
            {
                return 0;
            }

            if (lengths.Length != 1 || !int.TryParse(lengths[0], out var contentLength))
            {
                return -1;
            }

            return contentLength;
        }

        /// <summary>
        /// 寫出固定 status、JSON content type、明確 Content-Length 與 Connection: close 的 bounded response。
        /// response byte[] 在 finally 清零；NetworkStream 由外層 request scope Dispose。方法不啟動 flush background task，
        /// cancellation 會中止 write，server Dispose 隨後關閉 socket並等待 worker，避免殘留 handle。
        /// </summary>
        private static async Task WriteResponseAsync(
            NetworkStream stream,
            HttpStatusCode statusCode,
            string body,
            CancellationToken cancellationToken)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)statusCode} {GetReasonPhrase(statusCode)}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n\r\n");
            try
            {
                await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bodyBytes);
                CryptographicOperations.ZeroMemory(headerBytes);
            }
        }

        /// <summary>
        /// 將測試使用的有限 HTTP status 映射到固定 reason phrase；未知值使用中性文字，不回顯 request 或 upstream detail。
        /// 純函式無配置、並行、I/O 或 Dispose 責任。
        /// </summary>
        private static string GetReasonPhrase(HttpStatusCode statusCode)
            => statusCode switch
            {
                HttpStatusCode.OK => "OK",
                HttpStatusCode.BadRequest => "Bad Request",
                _ => "Synthetic"
            };

        /// <summary>
        /// exactly-once 停止 listener、取消仍在等待的 accept/read/write，並 await worker terminal state。
        /// 只吞掉由本 Dispose owner 造成的 cancellation/socket closure；其他 worker fault 會向上傳遞，避免假裝 cleanup 成功。
        /// 最後 Dispose CTS，使 listener、Task、socket、stream 與 cancellation registration 都有明確終點。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            {
                return;
            }

            _disposeCts.Cancel();
            _listener.Stop();
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
            }
            catch (SocketException) when (_disposeCts.IsCancellationRequested)
            {
            }
            finally
            {
                _disposeCts.Dispose();
            }
        }
    }
}
#endif
