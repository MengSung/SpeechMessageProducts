// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/SessionLifecycle/AuthenticationSessionResourceDrainTests.cs
// 測試責任：驗證 ChurchReport 登出與重新登入共用的 Session resource drain/clear 順序。
// 信任邊界：只使用合成 Session ID 與 opaque scope，不建立真實使用者、Credential、Token、LINE 或 CRM 連線。
// 失敗策略：scope 受污染或 coordinator 無法驗證時必須在 Session.Clear 前 fail closed，保留 owner 可追蹤性。
// 生命週期：每個 coordinator 與 MemoryCache 都由測試方法確定性 Dispose，不留下 callback、entry 或 request lease。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using ChurchReport.Controllers;
using ChurchReport.Models;
using ChurchReport.Services.Caching;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.SessionLifecycle;

/// <summary>
/// 驗證 AuthenticationController 兩條身份重設路徑共用的原子順序 helper。
/// Controller 不直接 Dispose Manager；它只先要求 singleton coordinator 撤銷舊 generation，再清 Session。
/// 舊 request 是否仍可完成與最後 lease cleanup 已由 coordinator 狀態機測試覆蓋，本檔聚焦 HTTP 身份邊界順序。
/// </summary>
[Collection(SessionLifecycleCollection.Name)]
public sealed class AuthenticationSessionResourceDrainTests
{
    /// <summary>
    /// Session 內若存在不符合 43 字元 Base64Url 契約的 scope，coordinator 必須先拒絕；
    /// ClearCount 維持零證明 Controller 沒有先清掉 Session 再留下無法定位的舊 resource generation。
    /// </summary>
    [Fact]
    public void Invalid_scope_fails_closed_before_session_clear()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<DonationPaymentManager>(cache);
        var session = new RecordingSession("synthetic-invalid-scope-session");
        session.SetString("_DonationPaymentResourceScopeId", "invalid-session-derived-key");
        var helper = GetDrainAndClearHelper();

        Action action = () => helper.Invoke(null, new object[] { session, coordinator });

        action.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentException>();
        session.ClearCount.Should().Be(0);
        session.TryGetValue("_DonationPaymentResourceScopeId", out _).Should().BeTrue();
    }

    /// <summary>
    /// 合法 scope 即使當下沒有 active Manager，也必須先從 Session 移除，再且僅執行一次 Clear。
    /// 這保證重新登入後無法再次使用上一個身份世代的 opaque scope；下一次 Manager 存取只能建立新 scope。
    /// </summary>
    [Fact]
    public void Valid_scope_is_removed_before_session_is_cleared_once()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<DonationPaymentManager>(cache);
        var session = new RecordingSession("synthetic-valid-scope-session");
        session.SetString("_DonationPaymentResourceScopeId", "K" + new string('A', 42));
        var helper = GetDrainAndClearHelper();

        helper.Invoke(null, new object[] { session, coordinator });

        session.ClearCount.Should().Be(1);
        session.ScopeWasRemovedBeforeClear.Should().BeTrue();
        session.Keys.Should().BeEmpty();
    }

    /// <summary>
    /// 直接執行 production <see cref="AuthenticationController.Logout"/> action，而不是只反射共用 helper。
    /// 測試先建立一個仍有 active lease 的 Donation manager；Logout 必須先撤銷 scope、再 Clear Session、再呼叫
    /// authentication sign-out。Action 完成時 in-flight manager 尚不可 Dispose，最後 lease 歸還後才回到零基準線。
    /// 若未來有人從 Logout 移除 drain 呼叫而保留 helper，本測試會因 scope 仍存在、manager 未進入 Draining 而失敗。
    /// </summary>
    [Fact]
    public async Task Logout_action_drains_active_generation_before_session_clear_and_signout()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<DonationPaymentManager>(cache);
        var session = new RecordingSession("synthetic-logout-action-session");
        var scope = coordinator.GetOrCreateResourceScopeId(session);
        var manager = CreateUninitializedDonationPaymentManager();
        var lease = coordinator.Acquire(scope, () => manager, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        var authentication = new RecordingAuthenticationService();
        using var requestServices = new ServiceCollection()
            .AddSingleton(coordinator)
            .AddSingleton<IAuthenticationService>(authentication)
            .AddSingleton<IUrlHelperFactory, StaticUrlHelperFactory>()
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            Session = session,
            RequestServices = requestServices
        };
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var controller = CreateUninitializedController(httpContext);

        var result = await controller.Logout();

        if (result is JsonResult errorResult)
        {
            var errorMessage = errorResult.Value?.GetType().GetProperty("message")?.GetValue(errorResult.Value);
            throw new InvalidOperationException($"Logout action returned its error path: {errorMessage}");
        }

        result.Should().BeOfType<RedirectToActionResult>();
        authentication.SignOutCount.Should().Be(1);
        session.ClearCount.Should().Be(1);
        session.ScopeWasRemovedBeforeClear.Should().BeTrue();
        ReadDisposeState(manager).Should().Be(0, "in-flight logout request 的 lease 尚未歸還");

        lease.Dispose();

        ReadDisposeState(manager).Should().Be(1);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 直接執行 production 私有 re-login 初始化方法，並故意傳入缺少 view model 的輸入，讓流程在 drain/clear 之後失敗。
    /// 主要 assertion 不是後續登入成功，而是即使後段初始化丟例外，舊 scope 已在第一次 Session.Clear 前撤銷，
    /// 舊 manager 仍受 lease 保護且最後能確定性清理。若實際 re-login 呼叫點被移除，helper 測試可能仍綠，
    /// 但此測試會觀察到 scope 未移除與 generation 仍 live。
    /// </summary>
    [Fact]
    public async Task Relogin_initialization_invokes_shared_drain_before_later_initialization_failure()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<DonationPaymentManager>(cache);
        var session = new RecordingSession("synthetic-relogin-action-session");
        var scope = coordinator.GetOrCreateResourceScopeId(session);
        var manager = CreateUninitializedDonationPaymentManager();
        var lease = coordinator.Acquire(scope, () => manager, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        using var requestServices = new ServiceCollection()
            .AddSingleton(coordinator)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            Session = session,
            RequestServices = requestServices
        };
        var controller = CreateUninitializedController(httpContext);
        var initialize = typeof(AuthenticationController).GetMethod(
            "InitializeUserSessionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AuthenticationController 缺少 re-login Session 初始化方法。");

        Func<Task> invoke = async () =>
        {
            var task = initialize.Invoke(controller, new object?[] { null, null }) as Task
                       ?? throw new InvalidOperationException("re-login Session 初始化方法未回傳 Task。");
            await task;
        };

        await invoke.Should().ThrowAsync<Exception>();
        session.ClearCount.Should().Be(1);
        session.ScopeWasRemovedBeforeClear.Should().BeTrue();
        ReadDisposeState(manager).Should().Be(0, "後段登入失敗不得提前終止既有 request lease");

        lease.Dispose();

        ReadDisposeState(manager).Should().Be(1);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 取得 production private static helper。以反射保留 Controller 的最小公開表面，同時讓測試直接驗證真正排序邏輯；
    /// 若後續有人把登出與重新登入拆成兩份清理程式，這個 helper 消失會讓測試在建置／查找階段立即失敗。
    /// </summary>
    private static MethodInfo GetDrainAndClearHelper()
    {
        return typeof(AuthenticationController).GetMethod(
                   "DrainDonationSessionResourcesAndClearSession",
                   BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "AuthenticationController 缺少共用 Session resource drain/clear helper。");
    }

    /// <summary>
    /// 建立不執行 legacy controller/base 建構式的真實 AuthenticationController instance，並只注入測試擁有的 HttpContext。
    /// Logout 與 re-login drain 路徑只需要 ControllerContext；略過 CRM pool、ToolUtility 與網站環境可確保測試不讀設定、
    /// 不建立連線或 credential。此 helper 不修改 production 可見性，也不新增測試專用建構式。
    /// </summary>
    private static AuthenticationController CreateUninitializedController(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var controller = (AuthenticationController)RuntimeHelpers.GetUninitializedObject(
            typeof(AuthenticationController));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    /// <summary>
    /// 建立不執行 LINE／CRM 建構式的真實 DonationPaymentManager；coordinator 仍透過 production IDisposable 映射清理它。
    /// 所有 owned 欄位維持 null，Dispose 的 null-safe cleanup 與 terminal sentinel 仍會執行，不產生外部副作用。
    /// </summary>
    private static DonationPaymentManager CreateUninitializedDonationPaymentManager()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var temporaryDirectory = Directory.CreateTempSubdirectory("churchreport-auth-session-drain-");
        var configurationPath = Path.Combine(temporaryDirectory.FullName, "appsettings.json");

        try
        {
            // DonationPaymentManager 的 legacy static ConfigurationBuilder 強制讀取 working-directory appsettings.json。
            // 測試只提供 UTF-8 without BOM 的空 JSON，不含 endpoint、credential、token 或真實環境值；collection
            // 禁止平行執行，避免 process-global current directory 切換污染其他 lifecycle 測試。
            File.WriteAllText(
                configurationPath,
                "{}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Directory.SetCurrentDirectory(temporaryDirectory.FullName);
            RuntimeHelpers.RunClassConstructor(typeof(DonationPaymentManager).TypeHandle);
            return (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(
                typeof(DonationPaymentManager));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            if (File.Exists(configurationPath))
            {
                File.Delete(configurationPath);
            }

            temporaryDirectory.Delete();
        }
    }

    /// <summary>
    /// 讀取 manager 的 production terminal sentinel，避免測試只相信 coordinator 計數而漏掉實際 manager cleanup。
    /// </summary>
    private static int ReadDisposeState(DonationPaymentManager manager)
    {
        var field = typeof(DonationPaymentManager).GetField(
            "_disposeState",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DonationPaymentManager 缺少 Dispose terminal sentinel。");
        return (int)(field.GetValue(manager) ?? 0);
    }

    /// <summary>
    /// 可記錄 Clear 順序的合成 Session。內部 dictionary 只保存測試建立的 opaque scope；
    /// 所有 byte[] 讀寫都複製，避免測試本身共享 mutable buffer 而掩蓋生命週期或競爭問題。
    /// </summary>
    private sealed class RecordingSession : ISession
    {
        private const string ResourceScopeKey = "_DonationPaymentResourceScopeId";
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public RecordingSession(string id)
        {
            Id = id;
        }

        public bool IsAvailable => true;

        public string Id { get; }

        public IEnumerable<string> Keys => _values.Keys;

        public int ClearCount { get; private set; }

        public bool ScopeWasRemovedBeforeClear { get; private set; }

        public void Clear()
        {
            ClearCount++;
            ScopeWasRemovedBeforeClear = !_values.ContainsKey(ResourceScopeKey);
            _values.Clear();
        }

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
    /// 只記錄 SignOut 次數的伺服器端 authentication 測試替身。其餘方法回傳中性結果且不建立 cookie、claims cache、
    /// timer 或外部身份連線；所有 cancellation token 都在入口尊重，避免測試掩蓋 action 的取消語意。
    /// </summary>
    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        private int _signOutCount;

        public int SignOutCount => Volatile.Read(ref _signOutCount);

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Task.CompletedTask;
        }

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Task.CompletedTask;
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(principal);
            return Task.CompletedTask;
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            ArgumentNullException.ThrowIfNull(context);
            Interlocked.Increment(ref _signOutCount);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 提供不依賴 EndpointRouting/action-descriptor 圖的固定 URL helper，讓直接 controller action 測試可建立 RedirectToActionResult。
    /// 它只回傳合成 local path，不讀 Host、Forwarded headers、route values 或使用者輸入，避免測試把 routing 主機缺件誤判為
    /// Logout 生命週期失敗。Factory 不持有 HttpContext 或背景資源，ServiceProvider 結束時不需要額外 cleanup。
    /// </summary>
    private sealed class StaticUrlHelperFactory : IUrlHelperFactory
    {
        public IUrlHelper GetUrlHelper(ActionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return new StaticUrlHelper(context);
        }
    }

    /// <summary>
    /// Logout 測試使用的最小 local URL 實作。所有 action/route/link 都回傳同一個不可識別的相對路徑，
    /// 不進行 DNS、HTTP、route table 或外部編碼工作；測試只驗證 redirect result 型別，不驗證 MVC routing 本身。
    /// </summary>
    private sealed class StaticUrlHelper : IUrlHelper
    {
        public StaticUrlHelper(ActionContext actionContext)
        {
            ActionContext = actionContext ?? throw new ArgumentNullException(nameof(actionContext));
        }

        public ActionContext ActionContext { get; }

        public string? Action(UrlActionContext actionContext) => "/Authentication/Login";

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url[0] == '/';

        public string? Link(string? routeName, object? values) => "/Authentication/Login";

        public string? RouteUrl(UrlRouteContext routeContext) => "/Authentication/Login";
    }
}
