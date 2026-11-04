using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using ChurchReport.Controllers;
using ChurchReport.Filters;
using ChurchReport.Middleware;
using ChurchReport.Models;
using DevExtreme.AspNet.Data.ResponseModel;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using ToolUtilityNameSpace.Factory;
using Xunit;
using Xunit.Abstractions;

namespace ChurchReport.MemberInfo.Tests.Triage;

/// <summary>
/// 唯讀分流探測：借用既有模型測試的無連線 Factory scope，直接呼叫真實控制器與授權元件。
/// 候選資料完全合成，沒有個資；此檔僅由暫存 MSBuild 匯入加入測試，不修改產品或測試專案。
/// 每個探測的 controller、Session 與 Factory 都在方法結束時釋放，不啟動網站或外部服務。
/// </summary>
[Collection("LegacyToolUtilityFactory")]
public sealed class SmallGroupBlankTriageProbes(ITestOutputHelper output)
{
    /// <summary>
    /// 以兩筆名單預覽列及空的歷史圖表模擬成員存在、尚無本週點名的 API 邊界資料。
    /// 只驗證控制器收到該候選後的回應；不將注入候選誤認為已驗證真實 CRM 查詢。
    /// </summary>
    [Fact]
    public void MembersWithoutAttendance_ReturnGridRowsAndEmptyChart()
    {
        using var factory = OpenExistingOfflineFactoryScope();
        var manager = MakeManager(_ => Report(includeMembers: true));
        using var controller = MakeController(manager, ajax: true);
        var grid = Assert.IsType<LoadResult>(controller.LoadIntegrate("synthetic-list", Options()));
        var chart = Assert.IsType<LoadResult>(controller.GetChartDataList("synthetic-list", Options()));
        Assert.Equal(2, grid.data.Cast<object>().Count());
        Assert.Empty(chart.data.Cast<object>());
        Assert.Equal(2, grid.totalCount);
        Assert.Equal(0, chart.totalCount);
        output.WriteLine("grid: data[2], totalCount=2; chart: data[], totalCount=0");
    }

    /// <summary>
    /// 另一種「無點名」情境：週報已存在，假服務有名單成員但回傳零筆出席紀錄。
    /// 呼叫真實下載器，證明它不會退回名單查詢；再呼叫真實 API 確認為正常空集合。
    /// 假服務只處理記憶體 QueryExpression，絕無 CRM、LINE 或外部傳輸能力。
    /// </summary>
    [Fact]
    public void ExistingWeeklyReportWithoutAttendance_DoesNotFallBackToContactList()
    {
        using var factory = OpenExistingOfflineFactoryScope();
        var service = DispatchProxy.Create<IOrganizationService, ProbeAttendanceService>();
        using var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        ToolUtilityFactory.SetAmbientService(new AmbientGatewayOrganizationService(
            () => provider, provider.GetRequiredService<IServiceScopeFactory>()));
        var loader = new ChurchReport.WebServiceConnector.DownloadIntegrateData();
        typeof(ChurchReport.WebServiceConnector.DownloadIntegrateData)
            .GetField("m_LoginType", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(loader, "小組長");
        var report = Report(includeMembers: false);
        loader.GetAllMemeberDataList(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), ref report);
        Assert.Empty(report.m_SmallGroupDataList.m_AllMemeberData.Members);
        var proxy = (ProbeAttendanceService)(object)service;
        Assert.Equal(1, proxy.AttendanceReads);
        Assert.Equal(0, proxy.ContactReads);
        var manager = MakeManager(_ => report);
        using var controller = MakeController(manager, ajax: true);
        var result = Assert.IsType<LoadResult>(controller.LoadIntegrate("synthetic-list", Options()));
        Assert.Empty(result.data.Cast<object>());
        Assert.Equal(0, result.totalCount);
        output.WriteLine("existing weekly report, zero attendance: grid data[], totalCount=0; contact fallback reads=0");
    }

    /// <summary>
    /// 注入一筆純數值圖表，確認正常序列化的欄位大小寫符合 Razor 的欄位設定；
    /// 不輸出任何列內容，也不把伺服器型別檢查宣稱為瀏覽器繪圖成功。
    /// </summary>
    [Fact]
    public void ChartWithSyntheticHistory_PreservesExpectedFieldNames()
    {
        using var factory = OpenExistingOfflineFactoryScope();
        var manager = MakeManager(_ =>
        {
            var report = Report(includeMembers: false);
            report.m_WeeklyReportChart.m_ChartDataList.Add(new ChartData
            {
                WeeklyReportEntityId = "synthetic-week",
                SundayDate = "2026/11/1",
                SundayNumber = 3,
                SmallNumber = 2
            });
            return report;
        });
        using var controller = MakeController(manager, ajax: true);
        var result = Assert.IsType<LoadResult>(controller.GetChartDataList("synthetic-list", Options()));
        var json = JsonConvert.SerializeObject(result);
        foreach (var key in new[] { "SundayDate", "SundayNumber", "SmallNumber", "WeeklyReportEntityId" })
            Assert.True(json.Contains("\"" + key + "\"", StringComparison.Ordinal));
        output.WriteLine("chart: data[1]; PascalCase chart fields preserved");
    }

    /// <summary>
    /// 用 loader timeout 或遺失可見小組 scope，探測兩支 API 的真實 HandleError 分支。
    /// 共用告警未掛載且 ToolUtility provider 為無操作替身，禁止建立 LINE/CRM 連線。
    /// 決勝斷言為錯誤 JSON 沒有 data，且 JsonResult 未明確設定 HTTP 錯誤狀態碼。
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ApiFailure_ReturnsErrorEnvelopeRatherThanLoadResult(bool chart, bool missingScope)
    {
        using var factory = OpenExistingOfflineFactoryScope();
        Assert.False(ExceptionReporting.IsActive);
        var manager = MakeManager(_ => throw new TimeoutException("synthetic-timeout"));
        if (missingScope)
            manager.m_MultiGroupList.m_WeeklyReportRecordListData.Clear();
        using var controller = MakeController(manager, ajax: true);
        if (missingScope)
            controller.HttpContext.Session.Clear();
        var result = chart
            ? controller.GetChartDataList("synthetic-list", Options())
            : controller.LoadIntegrate("synthetic-list", Options());
        var error = Assert.IsType<JsonResult>(result);
        Assert.Null(error.StatusCode);
        Assert.Equal(200, controller.Response.StatusCode);
        var value = error.Value!;
        Assert.Equal("error", value.GetType().GetProperty("status")!.GetValue(value));
        Assert.Null(value.GetType().GetProperty("data"));
        output.WriteLine("API={0}; cause={1}; JsonResult status=error, no data, explicit HTTP status unset",
            chart ? "chart" : "grid", missingScope ? "session/scope loss" : "loader timeout");
    }

    /// <summary>
    /// 探測沒有 Session 身分也沒有驗證 Cookie 的請求；真正授權應在 action 之前短路。
    /// AJAX 得 401、一般導覽得到登入 redirect；此處不跟隨重新導向、不執行 action。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingAuthentication_IsRejectedBeforeAction(bool ajax)
    {
        var http = new DefaultHttpContext { Session = new ProbeSession() };
        if (ajax) http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await new GlobalAuthorizationFilter(configuration).OnAuthorizationAsync(context);
        if (ajax)
            Assert.Equal(401, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
        else
            Assert.Equal("Login", Assert.IsType<RedirectToActionResult>(context.Result).ActionName);
        output.WriteLine(ajax ? "unauthenticated AJAX: 401, action not executed" :
            "unauthenticated navigation: redirect to Authentication/Login, action not executed");
    }

    /// <summary>
    /// 注入非 LINE 的 User-Agent 不一致，驗證 SessionValidation 在 AJAX 授權過濾器之前
    /// 也可能回傳 302。Session 只含固定假值，Commit 為記憶體操作，下游 action 不可被執行。
    /// </summary>
    [Fact]
    public async Task SessionUserAgentMismatch_RedirectsAjaxToLogin()
    {
        var session = new ProbeSession();
        session.SetString("_SessionUserId", "synthetic-subject");
        session.SetString("_SessionUserAgent", "synthetic-agent-old");
        var http = new DefaultHttpContext { Session = session };
        http.Request.Path = "/SmallGroup/LoadIntegrate";
        http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        http.Request.Headers["User-Agent"] = "synthetic-agent-new";
        var called = false;
        var middleware = new SessionValidationMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, NullLogger<SessionValidationMiddleware>.Instance);
        await middleware.InvokeAsync(http);
        Assert.False(called);
        Assert.Equal(302, http.Response.StatusCode);
        Assert.Equal("/Login", http.Response.Headers.Location.ToString());
        Assert.Empty(session.Keys);
        output.WriteLine("Session User-Agent mismatch: AJAX 302 Location=/Login, session cleared, action not executed");
    }

    /// <summary>
    /// 只讀取 repo 的兩份 JSON，使用正式 Configuration provider 比較安全白名單節點。
    /// 不讀 User Secrets 或程序憑證，不輸出帳密/token/收件人；字串只顯示相同與否，
    /// URL 只顯示是否符合已核定測試主機，不能把此快照等同執行中 Host 的完整有效設定。
    /// </summary>
    [Fact]
    public void RepositoryJsonOverlay_ReportsOnlySafeConfigurationDifferences()
    {
        var root = Environment.GetEnvironmentVariable("SM_TRIAGE_REPO")!;
        Assert.True(File.Exists(Path.Combine(root, "SpeechMessageProducts.sln")));
        var directory = Path.Combine(root, "SpeechMessageProducts.ChurchReport");
        using var baseline = (ConfigurationRoot)new ConfigurationBuilder().SetBasePath(directory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false).Build();
        using var development = (ConfigurationRoot)new ConfigurationBuilder().SetBasePath(directory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false).Build();
        var prefixes = new[] { "CrmConnection:", "DynamicsAccess:", "Dataverse:Pool:",
            "LineMessaging:DefaultOrganization", "ExceptionNotifications:", "Security:" };
        foreach (var pair in development.AsEnumerable().Where(p => p.Value != null)
            .Where(p => prefixes.Any(prefix => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var changed = !string.Equals(baseline[pair.Key], pair.Value, StringComparison.Ordinal);
            if (new[] { "Password", "Username", "Domain", "Token", "Secret", "Key", "ConnectionString" }
                .Any(fragment => pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                output.WriteLine("{0}: {1}; value withheld", pair.Key, changed ? "overridden" : "inherited");
                continue;
            }
            var safeValue = bool.TryParse(pair.Value, out var flag) ? flag.ToString()
                : int.TryParse(pair.Value, out var number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "<string withheld>";
            if (pair.Key is "Dataverse:Pool:AcquireTimeout" or "Dataverse:Pool:IdleTimeout" or "Dataverse:Pool:HealthInterval" &&
                TimeSpan.TryParse(pair.Value, System.Globalization.CultureInfo.InvariantCulture, out var duration))
                safeValue = duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
            if (Uri.TryCreate(pair.Value, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "https" || uri.Scheme == "http"))
                safeValue = "https=" + (uri.Scheme == "https") + "; testHost=" +
                    string.Equals(uri.Host, "goodshepherdback.speechmessage.com.tw", StringComparison.OrdinalIgnoreCase) +
                    "; OrganizationServicePath=" +
                    string.Equals(uri.AbsolutePath, "/XRMServices/2011/Organization.svc", StringComparison.Ordinal);
            output.WriteLine("{0}: {1}; {2}", pair.Key, changed ? "overridden" : "inherited", safeValue);
        }
        Assert.True(development["CrmConnection:Organization"] == "goodshepherdback");
        Assert.True(development["LineMessaging:DefaultOrganization"] == "DevTest");
        foreach (var channel in development.GetSection("LineMessaging").GetChildren()
            .Where(c => c.Key != "DevTest" && c["ChannelAccessToken"] != null))
            Assert.True(string.IsNullOrEmpty(channel["ChannelAccessToken"]));
        output.WriteLine("test organization=true; default DevTest=true; baseline channel tokens masked=true");
        output.WriteLine("Host User Secrets and environment overrides not loaded in this probe");
        var http = new DefaultHttpContext { Session = new ProbeSession() };
        http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var authorization = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        new GlobalAuthorizationFilter(development).OnAuthorizationAsync(authorization).GetAwaiter().GetResult();
        output.WriteLine("repository JSON unauthenticated AJAX blocked by global authorization={0}",
            authorization.Result != null);
    }

    /// <summary>
    /// 借用既有、已驗證不建立外部連線的 Factory scope；反射僅用於研究，不修改私有實作。
    /// scope 的 Dispose 會清除 Factory static 綁定，避免合成設定留到下一個探測。
    /// </summary>
    private static IDisposable OpenExistingOfflineFactoryScope()
    {
        var type = typeof(Models.ListManagerIntegratePublicationTests)
            .GetNestedType("LegacyToolUtilityFactoryScope", BindingFlags.NonPublic)!;
        return (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
    }

    /// <summary>建立完全合成的可見小組與區域 loader，所有資料只屬本次探測。</summary>
    private static ListManager MakeManager(Func<string, ListSmallGroupWeeklyReport> loader) => new(loader)
    {
        m_Account = "synthetic-account",
        m_Password = "synthetic-credential",
        LoginType = "小組長",
        m_SelectDate = new DateTime(2026, 11, 3),
        m_MultiGroupList = new MultiGroupList
        {
            m_WeeklyReportRecordListData = [new() { ListEntityId = "synthetic-list", WeeklyReportEntityId = "" }]
        }
    };

    /// <summary>建立空圖表及可選的名單預覽列；不建立姓名、電話或 LINE ID 測試資料。</summary>
    private static ListSmallGroupWeeklyReport Report(bool includeMembers) => new()
    {
        LoadFlag = true,
        ListEntityId = "synthetic-list",
        m_SmallGroupDataList = new SmallGroupDataList
        {
            m_SmallGroupData = new SmallGroupData
            {
                Members = includeMembers ? [new() { PresentRecordId = "0" }, new() { PresentRecordId = "1" }] : []
            }
        },
        m_WeeklyReportChart = new ChartDataList { m_ChartDataList = [] }
    };

    /// <summary>
    /// 略過控制器建構式的 CRM/DI 圖，只注入真實 action 需要的記憶體 context。
    /// provider 永遠回傳 null，HandleError 的 Trace 不會間接觸發服務建立；不啟動真實 Host。
    /// </summary>
    private static SmallGroupController MakeController(ListManager manager, bool ajax)
    {
        var context = DispatchProxy.Create<IInMemoryDataContext, ProbeContextProxy>();
        ((ProbeContextProxy)(object)context).Manager = manager;
        var controller = (SmallGroupController)RuntimeHelpers.GetUninitializedObject(typeof(SmallGroupController));
        typeof(BaseChurchController).GetField("InMemoryContext", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(controller, context);
        typeof(BaseChurchController).GetField("_toolUtilityProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(controller, DispatchProxy.Create<IToolUtilityProvider, ProbeNullToolProvider>());
        var session = new ProbeSession();
        session.SetString("_LoginPassword", "synthetic-credential");
        var http = new DefaultHttpContext
        {
            Session = session,
            User = new ClaimsPrincipal(new ClaimsIdentity("synthetic-cookie"))
        };
        if (ajax) http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    /// <summary>與 Grid 一樣要求總數，避免因未要求 totalCount 而誤讀預設值。</summary>
    private static DataSourceLoadOptions Options() => new() { RequireTotalCount = true };
}

/// <summary>只允許 ListManager 讀取；其他依賴視為研究設定錯誤，禁止默默建立實際服務。</summary>
public class ProbeContextProxy : DispatchProxy
{
    /// <summary>本次方法獨占的合成模型；不放入 static 或跨測試快取。</summary>
    public ListManager Manager { get; set; } = null!;
    /// <summary>僅轉送小組 manager 讀取，不具備任何外部 I/O 能力。</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod!.Name == "get_ListManager" ? Manager : throw new NotSupportedException("Unexpected probe dependency.");
}

/// <summary>診斷 Trace 的無操作邊界；任何外部服務建構均不可由這個替身發生。</summary>
public class ProbeNullToolProvider : DispatchProxy
{
    /// <summary>只允許取得空的工具參考，讓控制器既有 null-safe Trace 不執行 I/O。</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod!.Name == "GetToolUtility" ? null : throw new NotSupportedException("Unexpected provider dependency.");
}

/// <summary>方法自有的記憶體 Session；沒有磁碟、網路、timer、訂閱或跨測試共享資料。</summary>
internal sealed class ProbeSession : ISession
{
    private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);
    /// <summary>研究期間始終可用，不觸發任何遠端 store。</summary>
    public bool IsAvailable => true;
    /// <summary>每個探測唯一且不輸出的 Session ID，避免碰撞程序內的驗證快取。</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");
    /// <summary>當次 Session 的鍵；不含真實個資或機密。</summary>
    public IEnumerable<string> Keys => values.Keys;
    /// <summary>清空本次 Session 的所有短命資料。</summary>
    public void Clear() => values.Clear();
    /// <summary>無外部 store，提交立即完成，不保存取消權杖。</summary>
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <summary>無外部 store，載入立即完成，不保存取消權杖。</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <summary>移除指定鍵的假資料。</summary>
    public void Remove(string key) => values.Remove(key);
    /// <summary>複製輸入，避免可變陣列與呼叫端共享。</summary>
    public void Set(string key, byte[] value) => values[key] = value.ToArray();
    /// <summary>回傳複本，使探測外部無法改寫 Session 擁有的資料。</summary>
    public bool TryGetValue(string key, out byte[] value)
    {
        var found = values.TryGetValue(key, out var stored);
        value = found ? stored!.ToArray() : [];
        return found;
    }
}

/// <summary>
/// 只在記憶體保有兩筆合成 contact；出席紀錄查詢固定為零筆。
/// 非查詢操作直接失敗，禁止測試意外執行寫入或透過其他服務補資料。
/// </summary>
public class ProbeAttendanceService : DispatchProxy
{
    /// <summary>目前方法實際查詢出席紀錄的次數。</summary>
    public int AttendanceReads { get; private set; }
    /// <summary>目前方法實際查詢名單成員的次數，用於辨識是否有 fallback。</summary>
    public int ContactReads { get; private set; }
    /// <summary>辨識查詢目標後回傳純合成集合，永遠不建立外部連線。</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name != "RetrieveMultiple" || args![0] is not QueryExpression query)
            throw new NotSupportedException("Unexpected offline service operation.");
        if (query.EntityName == "new_present_record")
        {
            AttendanceReads++;
            return new EntityCollection();
        }
        if (query.EntityName is "contact" or "listmember")
        {
            ContactReads++;
            return new EntityCollection([
                new Entity(query.EntityName, Guid.NewGuid()),
                new Entity(query.EntityName, Guid.NewGuid())
            ]);
        }
        throw new NotSupportedException("Unexpected offline query.");
    }
}
