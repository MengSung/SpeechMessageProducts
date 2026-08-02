// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Security/AdfsDiagnosticSecurityTests.cs
// 目的：鎖定 ChurchReport DEBUG 診斷僅保留 index、Session 與效能資訊，並證明舊 ADFS／
//       direct Web API 診斷路由、OAuth token exchange 與網路 probe 已完全退休。
// ============================================================================

using System.Security.Claims;
using ChurchReport.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

/// <summary>
/// 以 Release-safe source contract 驗證條件編譯中的 <c>DiagnosticsController</c> 不再保留舊 ADFS 或 direct Web API
/// 實作。每個測試只讀目前 worktree 的單一 UTF-8 source file；不建立網路連線、Session store、timer、背景工作、
/// token、credential 或跨測試 cache。repository root 探索必須同時找到 Production 與 Test sentinel，否則 fail closed，
/// 避免誤讀另一個 checkout 後產生假綠燈。
/// </summary>
public sealed class AdfsDiagnosticSourceContractTests
{
    /// <summary>
    /// 驗證 controller source 不宣告 <c>adfs-authorize</c>、<c>adfs-callback</c> 或對應 action。這個掃描保護
    /// ASP.NET route surface，即使 Release 組態不會編譯 DEBUG controller 仍可執行；它只比較固定字串，不解析、記錄或
    /// 輸出任何實際 endpoint、authorization code、Session state 或使用者識別。
    /// </summary>
    [Fact]
    public void Controller_source_does_not_expose_legacy_adfs_routes()
    {
        var source = ReadControllerSource();
        var forbiddenFragments = new[]
        {
            "[HttpGet(\"adfs-authorize\")]",
            "[HttpGet(\"adfs-callback\")]",
            "AdfsAuthorize(",
            "AdfsCallback("
        };

        forbiddenFragments.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// 驗證 controller 不再讀取 Embedded Dynamics 設定、不交換 OAuth token、不建立 bearer request，也不直接呼叫
    /// HTTP identity probe。allow-none 清單同時覆蓋設定、HTTP、token parser、OAuth state 與 bounded-body helper；
    /// source 只在單一 test scope 暫留，完成後可由 GC 回收，沒有檔案 writer、網路 probe 或敏感輸出。
    /// </summary>
    [Fact]
    public void Controller_source_has_no_embedded_oauth_or_direct_webapi_implementation()
    {
        var source = ReadControllerSource();
        var forbiddenFragments = new[]
        {
            "DynamicsAccess:Embedded",
            "IConfiguration",
            "IHttpClientFactory",
            "System.Net.Http",
            "HttpClient",
            "HttpRequestMessage",
            "FormUrlEncodedContent",
            "AuthenticationHeaderValue",
            "\"Bearer\"",
            "/oauth2/authorize",
            "/oauth2/token",
            "WhoAmI",
            "access_token",
            "RandomNumberGenerator",
            "AdfsOAuthState",
            "ReadBoundedContentAsync",
            "ParseAccessToken",
            "DiagnosticUpstreamException"
        };

        forbiddenFragments.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// 驗證 DEBUG index 明確把舊 ADFS 診斷標示為不可用且已退休，避免操作員把已刪除路由誤認為 Gateway 或
    /// Phase 4 相容性入口。source assertion 不要求或輸出任何部署值，且不建立 controller、Session、HTTP client 或
    /// response；行為層另有 DEBUG test 驗證實際 JSON 與 private/no-store header。
    /// </summary>
    [Fact]
    public void Controller_source_reports_legacy_adfs_diagnostic_as_retired_and_unavailable()
    {
        var source = ReadControllerSource();

        source.Contains("[\"adfsAuthorizeAvailable\"] = false", StringComparison.Ordinal)
            .Should().BeTrue();
        source.Contains("[\"adfsDiagnosticStatus\"] = \"retired\"", StringComparison.Ordinal)
            .Should().BeTrue();
    }

    /// <summary>
    /// 驗證已退休的 ADFS 診斷不再留下命名 HttpClient 或 handler pool 註冊；否則即使路由已移除，
    /// Host 仍會建立無消費者的網路資源與舊方向設定面。測試只讀取工作樹來源，不建立 client、
    /// socket、timer、subscription 或背景工作，因此沒有跨測試或跨 Session 的可變狀態。
    /// </summary>
    [Fact]
    public void Retired_adfs_diagnostic_has_no_orphaned_http_client_configuration()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sources =
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                "SpeechMessageProducts.ChurchReport",
                "Startup.cs")) +
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                "SpeechMessageProducts.ChurchReport",
                "Security",
                "DiagnosticsOperatorAuthorization.cs"));
        var forbiddenFragments = new[]
        {
            "DiagnosticsHttpClientName",
            "\"adfs-diagnostics\""
        };

        forbiddenFragments.Should().OnlyContain(fragment =>
            !sources.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// 驗證診斷授權仍由部署端操作員清單與登入 cookie 內伺服器簽發的聯絡人識別 claim 共同決定。空清單、未驗證
    /// 身分、缺少或重複 <see cref="ClaimTypes.NameIdentifier"/>、以及清單外識別都必須 fail closed；helper 不可讀取
    /// Session、建立 cache、timer、subscription 或保留 request principal。
    /// </summary>
    [Fact]
    public void Diagnostics_operator_authorization_uses_server_issued_contact_claim_and_fails_closed()
    {
        var policyType = typeof(global::ChurchReport.Startup).Assembly.GetType(
            "ChurchReport.Security.DiagnosticsOperatorAuthorization");
        policyType.Should().NotBeNull("診斷端點必須保留獨立且可測試的 fail-closed 操作員政策");

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
    /// 讀取目前 worktree 的 controller source。<see cref="File.ReadAllText(string)"/> 自行關閉檔案 handle；方法不接受
    /// caller-controlled 路徑、不保存 static cache，也不把實際 repository path 放入 assertion，避免跨 workspace trust boundary。
    /// </summary>
    private static string ReadControllerSource()
        => File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "DiagnosticsController.cs"));

    /// <summary>
    /// 從目前 test output 向上尋找同時包含 Test project 與 Production controller 的唯一 worktree root。每一層只建立短命
    /// <see cref="DirectoryInfo"/>，不持有 watcher、timer、directory handle 或背景工作；找不到完整 sentinel 時 fail closed。
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
/// 直接執行 DEBUG-only <see cref="DiagnosticsController"/>，驗證保留的 index、Session 與效能 action 只輸出有界、
/// 非敏感資訊並套用 private/no-store。每個 test 建立獨立記憶體 Session 與 <see cref="DefaultHttpContext"/>；controller
/// 以零參數 constructor 建立，不注入設定、HTTP client 或其他資源 owner，因此測試不會建立 socket、handler、timer、
/// subscription 或背景工作。
/// </summary>
public sealed class AdfsDiagnosticBehaviorTests
{
    private const string SessionMarker = "diagnostic-session-marker";

    /// <summary>
    /// 驗證 controller 仍要求具名操作員政策。MVC 授權在 action、Session 與任何資源配置之前執行，因此這是避免一般
    /// 已登入使用者讀取 DEBUG 診斷的第一道 fail-closed 邊界；測試只讀 attribute metadata，不執行網路或 Session I/O。
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
    /// 驗證 controller 不再注入設定或 HTTP factory。零參數建構式表示保留 action 沒有 token、endpoint、handler pool、
    /// request client 或其他長生命週期 runtime owner；若未來重新加入 dependency，本測試會在任何 action 執行前失敗。
    /// </summary>
    [Fact]
    public void Controller_constructor_has_no_service_dependencies()
    {
        var constructor = typeof(DiagnosticsController).GetConstructors().Single();

        constructor.GetParameters().Should().BeEmpty();
    }

    /// <summary>
    /// 驗證 controller 只暴露 index、Session 與效能三個 GET action。反射結果只包含固定 method/route 名稱，不觸發
    /// controller、Session 或 process 資源；任何舊 ADFS route 或新網路 probe action 都會使 allowlist assertion fail closed。
    /// </summary>
    [Fact]
    public void Controller_exposes_only_safe_non_network_diagnostic_actions()
    {
        var actions = typeof(DiagnosticsController)
            .GetMethods(System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .SelectMany(method => method
                .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true)
                .Cast<HttpGetAttribute>()
                .Select(attribute => $"{method.Name}:{attribute.Template ?? string.Empty}"));

        actions.Should().BeEquivalentTo(
            "Index:",
            "GetSessionInfo:session",
            "GetPerformanceInfo:performance");
    }

    /// <summary>
    /// 驗證 index 明確回報舊 ADFS 診斷 unavailable/retired，並保留 Session 可用性與 private/no-store。response 不得
    /// 包含 endpoint、client identifier、token 或網路狀態；測試只比對固定 dictionary shape，沒有外部 I/O 或 cleanup owner。
    /// </summary>
    [Fact]
    public void Index_reports_legacy_adfs_diagnostic_as_retired_and_is_private_no_store()
    {
        var controller = CreateController(new TestSession(SessionMarker));

        var result = controller.Index();

        var values = RequireJsonValues(result);
        values["ok"].Should().Be(true);
        values["stage"].Should().Be("diagnostics");
        values["adfsAuthorizeAvailable"].Should().Be(false);
        values["adfsDiagnosticStatus"].Should().Be("retired");
        values["sessionAvailable"].Should().Be(true);
        values.Keys.Should().BeEquivalentTo(
            "ok",
            "stage",
            "adfsAuthorizeAvailable",
            "adfsDiagnosticStatus",
            "sessionAvailable");
        AssertPrivateNoStore(controller);
    }

    /// <summary>
    /// 驗證 Session action 只回傳可用性，不回顯 <see cref="ISession.Id"/>，並強制 private/no-store。記憶體 Session 是
    /// 唯一資料 owner，沒有 distributed store、timer、subscription 或 background flush；assertion 不輸出實際 identifier。
    /// </summary>
    [Fact]
    public void Session_response_is_private_no_store_and_does_not_echo_identifier()
    {
        var controller = CreateController(new TestSession(SessionMarker));

        var result = controller.GetSessionInfo();

        var values = RequireJsonValues(result);
        values["ok"].Should().Be(true);
        values["stage"].Should().Be("session");
        values["available"].Should().Be(true);
        values.Keys.Should().BeEquivalentTo("ok", "stage", "available");
        ResultContains(result, SessionMarker).Should().BeFalse();
        AssertPrivateNoStore(controller);
    }

    /// <summary>
    /// 驗證效能 action 只保留 working set、private memory 與 thread count 三個 process-level 指標，且 response
    /// private/no-store。Production action 是 <see cref="System.Diagnostics.Process"/> handle 的唯一 owner；測試不保留
    /// process object、不輸出 user、command line、Session 或 endpoint，也不建立輪詢 timer 或背景 profiler。
    /// </summary>
    [Fact]
    public void Performance_response_is_private_no_store_and_has_only_bounded_metrics()
    {
        var controller = CreateController(new TestSession(SessionMarker));

        var result = controller.GetPerformanceInfo();

        var values = RequireJsonValues(result);
        values["ok"].Should().Be(true);
        values["stage"].Should().Be("performance");
        values["workingSetMb"].Should().BeOfType<long>();
        values["privateMemoryMb"].Should().BeOfType<long>();
        values["threadCount"].Should().BeOfType<int>();
        values.Keys.Should().BeEquivalentTo(
            "ok",
            "stage",
            "workingSetMb",
            "privateMemoryMb",
            "threadCount");
        ResultContains(result, SessionMarker).Should().BeFalse();
        AssertPrivateNoStore(controller);
    }

    /// <summary>
    /// 建立單一 test request scope 的零 dependency controller。<see cref="DefaultHttpContext"/>、Session 與 controller
    /// 只由 caller 的 test scope 擁有；helper 不解析設定、不建立 HTTP client、socket、timer、static cache 或 background work，
    /// 因此 request 結束後沒有額外 Dispose 或 drain owner。
    /// </summary>
    private static DiagnosticsController CreateController(TestSession session)
    {
        var controller = new DiagnosticsController();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Session = session,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, "synthetic-diagnostic-operator") },
                    "synthetic-cookie"))
            }
        };

        return controller;
    }

    /// <summary>
    /// 取得固定 JSON dictionary；任何非 <see cref="JsonResult"/> 或不同 shape 都立即 fail closed。helper 不序列化整份
    /// response、不寫 log、不建立 cache，也不延長 controller、Session 或 process 資源生命週期。
    /// </summary>
    private static IDictionary<string, object?> RequireJsonValues(IActionResult result)
        => result.Should().BeOfType<JsonResult>().Subject.Value
            .Should().BeAssignableTo<IDictionary<string, object?>>().Subject;

    /// <summary>
    /// 驗證 response 同時包含 private 與 no-store directive。helper 只讀短命 header 字串並轉成布林 assertion；不配置
    /// response cache、stream、timer 或 background task，且不輸出可能含部署資料的完整 response。
    /// </summary>
    private static void AssertPrivateNoStore(Controller controller)
    {
        var cacheControl = controller.Response.Headers.CacheControl.ToString();
        cacheControl.Contains("private", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    /// <summary>
    /// 將有界 JSON value 序列化後只回傳是否含 synthetic marker；caller assertion 因此只看到布林，不會在失敗輸出中
    /// 回顯 Session identifier。結果不寫檔、不進 cache，方法完成後即可回收。
    /// </summary>
    private static bool ResultContains(IActionResult result, string marker)
    {
        if (result is not JsonResult jsonResult)
        {
            return true;
        }

        var serialized = System.Text.Json.JsonSerializer.Serialize(jsonResult.Value);
        return serialized.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 提供固定、有界、單一 test scope 擁有的記憶體 Session。Dictionary 是唯一 owner，Set/TryGetValue 皆複製 byte[]，
    /// 避免 caller 共享 mutable buffer；沒有 distributed connection、timer、expiry callback、subscription 或背景清理，
    /// 因此不會把 Session 或 identity 狀態帶到另一個測試。
    /// </summary>
    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        /// <summary>
        /// 建立 synthetic Session；identifier 只用於負向回顯 assertion，不進 log、cache key 或 Production correlation。
        /// </summary>
        public TestSession(string id)
        {
            Id = id;
        }

        /// <summary>測試 Session 固定可用，不建立外部資源。</summary>
        public bool IsAvailable => true;

        /// <summary>取得 synthetic identifier；Production response 不得讀取或回顯。</summary>
        public string Id { get; }

        /// <summary>取得目前記憶體 key snapshot；只由單一 test thread 存取。</summary>
        public IEnumerable<string> Keys => _values.Keys;

        /// <summary>清除所有 test-owned byte[] reference，使資料可立即回收。</summary>
        public void Clear()
            => _values.Clear();

        /// <summary>模擬同步完成的 commit，取消時回傳已取消 Task，不啟動背景 flush。</summary>
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;

        /// <summary>模擬同步完成的 load，取消時 fail closed，且不建立 distributed-store connection。</summary>
        public Task LoadAsync(CancellationToken cancellationToken = default)
            => cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;

        /// <summary>移除指定 test-owned value，沒有 callback 或額外 cleanup owner。</summary>
        public void Remove(string key)
            => _values.Remove(key);

        /// <summary>複製並保存 value，避免呼叫端在 Set 後修改同一 mutable buffer。</summary>
        public void Set(string key, byte[] value)
            => _values[key] = value.ToArray();

        /// <summary>找到 value 時回傳 byte[] 複本；找不到時回傳共用空陣列且不配置外部資源。</summary>
        public bool TryGetValue(string key, out byte[] value)
        {
            if (_values.TryGetValue(key, out var stored))
            {
                value = stored.ToArray();
                return true;
            }

            value = Array.Empty<byte>();
            return false;
        }
    }
}
#endif
