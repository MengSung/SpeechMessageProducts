// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Security/SensitiveDiagnosticOutputSecurityTests.cs
// 目的：以 Release-safe source contract 防止 Token、Session ID、OAuth 上游內容與使用者識別資料進入診斷輸出。
// ============================================================================

using FluentAssertions;
using System.Globalization;
using System.Runtime.CompilerServices;
using ChurchReport.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

/// <summary>
/// 驗證 ChurchReport controller 不會把 bearer token 片段、Session identifier、未驗證的 OAuth error description、
/// CRM update payload 或 LINE profile identifier 寫入 Debug／Trace／redirect error。這些資料跨越 request trust boundary
/// 後可能被 IDE output、集中式 log、瀏覽器 history 或其他 Session 讀取，因此屬於 release blocker。
///
/// 測試只讀兩個目前 worktree 內的 UTF-8 source file，沒有建立 controller、Session、HTTP client、token、file watcher、
/// timer 或 background task；每次 File.ReadAllText 都立即關閉 handle，短命 source string 不進 static cache，完成後可由 GC 回收。
/// </summary>
public sealed class SensitiveDiagnosticOutputSecurityTests
{
    /// <summary>
    /// 掃描已知敏感輸出模式。清單使用精確且穩定的 source fragments，讓 Production 移除不安全 log 後測試轉綠，
    /// 未來若重新加入 token substring、Session ID、raw update values、LINE user identifier 或上游 error description，
    /// 會在 Debug 與 Release test 都 fail closed。斷言只輸出違規數量，不把任何實際 token、Session 或 payload 顯示於結果。
    /// </summary>
    [Fact]
    public void Controllers_do_not_emit_token_session_payload_or_identity_values()
    {
        var repositoryRoot = FindRepositoryRoot();
        var authenticationSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "AuthenticationController",
            "AuthenticationController.LineLoginOAuth.cs"));
        var smallGroupSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "SmallGroupController",
            "SmallGroupController.Crud.cs"));

        var forbiddenAuthenticationFragments = new[]
        {
            "Debug.WriteLine($\"",
            "access_token.Substring",
            "用戶 ID:",
            "用戶名稱:",
            "LINE OAuth 錯誤:",
            "LINE 登入失敗: {error_description}",
            "public string refresh_token",
            "new HttpClient()",
            "ReadAsStringAsync",
            "sessionState != state",
            "HandleError(e, \"LineLoginStart\")",
            "HandleError(e, \"LineCallback\")"
        };
        var forbiddenSmallGroupFragments = new[]
        {
            "Session ID:",
            "Key: {key",
            "Values: {values"
        };

        var violationCount = forbiddenAuthenticationFragments.Count(fragment =>
                                 authenticationSource.Contains(fragment, StringComparison.Ordinal)) +
                             forbiddenSmallGroupFragments.Count(fragment =>
                                 smallGroupSource.Contains(fragment, StringComparison.Ordinal));

        violationCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 LINE OAuth 的跨層資源與安全契約。Controller action 是 Session state、callback URL、
    /// bearer token 與上游 HTTP wrapper 的唯一 request-local owner；連線池則只能由 DI host 的
    /// <c>IHttpClientFactory</c> 擁有。這個 source contract 防止後續重構重新引入每次要求建立 handler、
    /// 無界限 body 讀取、可重播 state，或將 callback URL 從已消耗的 Session 再次取回。
    ///
    /// 回應內容必須在 <c>ResponseHeadersRead</c> 模式下，以固定上限讀入租用緩衝區；租用陣列與
    /// action-local body 都必須在 finally 清零。caller cancellation 由 <c>RequestAborted</c> 向下傳遞，
    /// timeout 則由具名 client 管理，故 action 結束後不留下背景工作、socket owner 或敏感 body。
    /// </summary>
    [Fact]
    public void Line_oauth_uses_factory_owned_bounded_http_and_one_time_state()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "AuthenticationController",
            "AuthenticationController.LineLoginOAuth.cs"));

        var requiredFragments = new[]
        {
            "IHttpClientFactory",
            "CreateClient(\"line-login-oauth\")",
            "HttpCompletionOption.ResponseHeadersRead",
            "MaxLineOAuthResponseBytes",
            "ArrayPool<byte>.Shared.Rent",
            "CryptographicOperations.ZeroMemory",
            "HttpContext.RequestAborted",
            "FixedTimeEquals(sessionState, state)",
            "IsFreshLineLoginState(stateIssuedAtTicks)",
            "ExchangeCodeForToken(code, callbackUrl)"
        };

        requiredFragments.Should().OnlyContain(fragment =>
            source.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// 驗證 LINE OAuth callback 在檢查 upstream error 或其他 early return 之前，已先讀取並移除 state、issued-at、
    /// callback URL 與 nonce。source-order contract 不執行外部 OAuth，也不建立 Session／socket；它保護所有 terminal
    /// callback 都只有一次 consumer，避免錯誤 callback、CSRF mismatch 或例外把驗證材料留給下一個 request 重播。
    /// 搜尋只比較固定 index，不把 state、code 或設定值輸出到 assertion。
    /// </summary>
    [Fact]
    public void Line_callback_consumes_oauth_session_material_before_early_returns()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "AuthenticationController",
            "AuthenticationController.LineLoginOAuth.cs"));
        var callbackStart = source.IndexOf(
            "public async Task<IActionResult> LineCallback",
            StringComparison.Ordinal);
        var stateRead = source.IndexOf(
            "HttpContext.Session.GetString(\"_LineLoginState\")",
            callbackStart,
            StringComparison.Ordinal);
        var stateRemove = source.IndexOf(
            "HttpContext.Session.Remove(\"_LineLoginState\")",
            callbackStart,
            StringComparison.Ordinal);
        var issuedAtRemove = source.IndexOf(
            "HttpContext.Session.Remove(LineLoginStateIssuedAtSessionKey)",
            callbackStart,
            StringComparison.Ordinal);
        var callbackUrlRemove = source.IndexOf(
            "HttpContext.Session.Remove(LineLoginCallbackUrlSessionKey)",
            callbackStart,
            StringComparison.Ordinal);
        var nonceRemove = source.IndexOf(
            "HttpContext.Session.Remove(\"_LineLoginNonce\")",
            callbackStart,
            StringComparison.Ordinal);
        var errorBranch = source.IndexOf(
            "if (!string.IsNullOrEmpty(error))",
            callbackStart,
            StringComparison.Ordinal);

        callbackStart.Should().BeGreaterThanOrEqualTo(0);
        stateRead.Should().BeGreaterThan(callbackStart);
        stateRemove.Should().BeGreaterThan(stateRead).And.BeLessThan(errorBranch);
        issuedAtRemove.Should().BeGreaterThan(stateRead).And.BeLessThan(errorBranch);
        callbackUrlRemove.Should().BeGreaterThan(stateRead).And.BeLessThan(errorBranch);
        nonceRemove.Should().BeGreaterThan(stateRead).And.BeLessThan(errorBranch);
    }

    /// <summary>
    /// 使用同一個真實 controller action 與同一個 Session 連續送入相同 code/state。第一次 callback 會先消費所有
    /// OAuth Session material，之後因測試 DI 未提供 LINE 上游設定而在任何外部 HTTP 前安全失敗；第二次 replay
    /// 必須因 state 已不存在而回到 State 錯誤，不能再次進入 token exchange。Uninitialized controller 只避免啟動
    /// legacy CRM 依賴，request scope、Session read/remove 與 action 分支皆為 production 程式；測試沒有 socket、timer、
    /// background task、token、LINE user ID 或跨案例 mutable owner。
    /// </summary>
    [Fact]
    public async Task Line_callback_replay_with_same_session_is_rejected_after_first_consumption()
    {
        const string state = "synthetic-line-oauth-state";
        var session = new ReplaySession("synthetic-line-replay-session");
        session.SetString("_LineLoginState", state);
        session.SetString(
            "_LineLoginStateIssuedAtUtcTicks",
            DateTimeOffset.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        session.SetString("_LineLoginCallbackUrl", "https://callback.invalid/Authentication/LineCallback");
        session.SetString("_LineLoginNonce", "synthetic-line-oauth-nonce");
        using var services = new ServiceCollection().BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            Session = session,
            RequestServices = services
        };
        var controller = (AuthenticationController)RuntimeHelpers.GetUninitializedObject(
            typeof(AuthenticationController));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.Url = new ReplayUrlHelper();

        var first = await controller.LineCallback("synthetic-code", state, null!, null!);
        var replay = await controller.LineCallback("synthetic-code", state, null!, null!);

        ReadRedirectError(first).Should().NotContain("State");
        ReadRedirectError(replay).Should().Contain("State");
        session.Keys.Should().NotContain(new[]
        {
            "_LineLoginState",
            "_LineLoginStateIssuedAtUtcTicks",
            "_LineLoginCallbackUrl",
            "_LineLoginNonce"
        });
    }

    /// <summary>讀取固定 Login redirect 的 error category；不序列化或保存其他 route/session 資料。</summary>
    private static string ReadRedirectError(IActionResult result)
    {
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.RouteValues.Should().NotBeNull();
        return redirect.RouteValues!["error"].Should().BeOfType<string>().Subject;
    }

    /// <summary>
    /// 單一測試 request 擁有的 in-memory Session fake；每次 Set 都複製 byte[]，Clear/Remove 立即釋放 dictionary reference，
    /// 不建立 timer、subscription、distributed connection 或 background cleanup，也不把 Session ID 寫入輸出。
    /// </summary>
    private sealed class ReplaySession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public ReplaySession(string id) => Id = id;

        public bool IsAvailable => true;

        public string Id { get; }

        public IEnumerable<string> Keys => _values.Keys;

        public void Clear() => _values.Clear();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _values.Remove(key);

        public void Set(string key, byte[] value) => _values[key] = value.ToArray();

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

    /// <summary>
    /// 只供 ControllerBase 建立 RedirectToActionResult 的無狀態 URL helper；固定回傳本機 Login path，不解析 request host、
    /// 不建立 route cache、service scope、timer 或 background work，也不接觸 callback/token/Session material。
    /// </summary>
    private sealed class ReplayUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();

        public string? Action(UrlActionContext actionContext) => "/Login";

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => true;

        public string? Link(string? routeName, object? values) => "/Login";

        public string? RouteUrl(UrlRouteContext routeContext) => "/Login";
    }

    /// <summary>
    /// 尋找目前測試 checkout 的 repository root；只走訪父目錄並檢查固定 sentinel，不接受 caller path、不建立 watcher，
    /// 也不保存 DirectoryInfo、handle、Session 或其他跨測試狀態。
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
                    "AuthenticationController",
                    "AuthenticationController.LineLoginOAuth.cs")) &&
                File.Exists(Path.Combine(
                    current.FullName,
                    "SpeechMessageProducts.ChurchReport",
                    "Controllers",
                    "SmallGroupController",
                    "SmallGroupController.Crud.cs")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到目前 ChurchReport worktree root。");
    }
}
