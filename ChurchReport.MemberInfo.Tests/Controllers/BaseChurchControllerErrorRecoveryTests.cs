// ============================================================================
// ChurchReport 錯誤復原與共享資源生命週期回歸測試
//
// 本檔案保護三個不可退讓的契約：錯誤處理不得因缺少 HTTP/TempData 基礎設施
// 而覆蓋原始例外、瀏覽器不得取得內部例外文字，以及單一 Controller 的結束
// 不得釋放由 Provider/Factory 持有的跨請求 ToolUtility 執行個體。
//
// 測試中的 ToolUtilityClass 使用未初始化物件，只觀察 Dispose 狀態；它不建立
// CRM 連線、不保存憑證，也不會啟動背景工作。所有假物件都由測試方法持有，
// 沒有 static 快取、計時器、連線或跨測試可變狀態，因此不會造成 Session 或
// Memory Leakage。
// ============================================================================

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ChurchReport.Controllers;
using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 驗證 Controller 錯誤回復與共享 CRM 工具的擁有權邊界。
///
/// <para>
/// 這些測試模擬兩種故障注入：沒有 MVC 管線所需的 TempData，以及 TempData 載入
/// 本身失敗。決定性斷言是原始例外文字不會進入瀏覽器回應，且 Controller Dispose
/// 後共享 ToolUtility 不會變為已釋放。這可防止原始 CRM 例外被第二個例外掩蓋，並
/// 防止後續請求使用已釋放的 client。
/// </para>
/// </summary>
public sealed class BaseChurchControllerErrorRecoveryTests
{
    /// <summary>
    /// 保護非 AJAX 錯誤分支：即使控制器沒有 TempData，也必須保留可導向錯誤頁的
    /// 結果，而不是以第二個 NullReferenceException 覆蓋第一個 CRM 例外。
    /// </summary>
    [Fact]
    public void HandleError_WhenTempDataIsUnavailable_ReturnsErrorRedirectWithoutThrowing()
    {
        var controller = CreateUninitializedController();

        var action = () => controller.InvokeHandleError(
            new ObjectDisposedException("crm-client", "internal CRM connection detail"),
            "ProcessLogin");

        var result = action.Should().NotThrow().Subject;
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("DisplayErrorView");
        redirect.ControllerName.Should().Be("Home");
    }

    /// <summary>
    /// 保護 AJAX 錯誤分支：伺服器可以記錄原始例外，但傳回瀏覽器的 JSON 只能有
    /// 固定的安全訊息，不能包含 CRM、連線或例外的內部描述。
    /// </summary>
    [Fact]
    public void HandleError_WhenAjaxRequest_DoesNotExposeTheOriginalExceptionMessage()
    {
        var controller = CreateUninitializedController();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = controller.InvokeHandleError(
            new InvalidOperationException("internal CRM connection detail"),
            "ProcessLogin");

        var json = result.Should().BeOfType<JsonResult>().Subject;
        var payload = JsonSerializer.Serialize(json.Value);
        using var document = JsonDocument.Parse(payload);
        var userMessage = document.RootElement.GetProperty("message").GetString();

        payload.Should().NotContain("internal CRM connection detail");
        userMessage.Should().Be("系統暫時無法完成操作，請稍後再試。");
    }

    /// <summary>
    /// 保護 Factory 擁有的共享工具：Controller 的生命週期只屬於當前 HTTP 請求，
    /// 因此不得釋放由 Provider 提供、會被下一個請求重用的 ToolUtility 實例。
    /// </summary>
    [Fact]
    public void Dispose_DoesNotDisposeProviderOwnedToolUtility()
    {
        var sharedToolUtility = (ToolUtilityClass)RuntimeHelpers.GetUninitializedObject(
            typeof(ToolUtilityClass));
        var controller = new TestableBaseChurchController(
            new FixedToolUtilityProvider(sharedToolUtility));

        controller.Dispose();

        GetDisposedFlag(sharedToolUtility).Should().BeFalse();
    }

    /// <summary>
    /// 保護錯誤頁的最後一道防線：TempData provider 載入失敗時，錯誤頁必須改用固定
    /// 訊息完成 View 結果，絕不能再拋出例外或反射輸入的錯誤文字。
    /// </summary>
    [Fact]
    public void DisplayErrorView_WhenTempDataProviderFails_UsesSafeFallbackMessage()
    {
        var controller = new HomeController(
            new HttpContextAccessor(),
            null!,
            new FixedToolUtilityProvider(null!),
            new NoOpCrmConnectionPool(),
            new NullInMemoryDataContext())
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new ThrowingTempDataProvider())
        };

        var action = () => controller.DisplayErrorView("internal CRM connection detail");

        var result = action.Should().NotThrow().Subject;
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewData["ErrorMessage"].Should().Be("系統暫時無法完成操作，請稍後再試。");
    }

    /// <summary>
    /// 保護既有的 LIFF 入口提示：路由只能傳遞受控錯誤代碼，錯誤頁必須由自己的
    /// 對照表產生使用者訊息，不能把 URL 中任意的 ErrorMessage 原樣回顯。
    /// </summary>
    [Fact]
    public void DisplayErrorView_WhenRecognizedErrorCodeIsProvided_UsesMappedSafeMessage()
    {
        var controller = new HomeController(
            new HttpContextAccessor(),
            null!,
            new FixedToolUtilityProvider(null!),
            new NoOpCrmConnectionPool(),
            new NullInMemoryDataContext());

        var method = typeof(HomeController).GetMethod(
            nameof(HomeController.DisplayErrorView),
            new[] { typeof(string), typeof(string) });
        method.Should().NotBeNull("錯誤頁必須明確接受受控 ErrorCode，而非重用 ErrorMessage");

        var result = method!.Invoke(
            controller,
            new object?[] { "internal CRM connection detail", "missing-liff-parameter" });

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewData["ErrorMessage"].Should().Be("缺少 LINE 啟動參數，請從 LINE 入口重新開啟。");
    }

    /// <summary>
    /// 建立未經建構式初始化的受測 Controller，刻意不提供 HTTP/TempData 依賴，
    /// 重現錯誤處理器在背景、手動轉送或測試環境中缺少完整 MVC 管線的情境。
    /// </summary>
    private static TestableBaseChurchController CreateUninitializedController()
    {
        return (TestableBaseChurchController)RuntimeHelpers.GetUninitializedObject(
            typeof(TestableBaseChurchController));
    }

    /// <summary>
    /// 讀取 ToolUtility 的私有釋放旗標，僅用來驗證生命週期契約；不會建立或觸碰
    /// 任何真實 CRM 連線。欄位遺失代表實作契約已改變，測試應立即失敗而非默默略過。
    /// </summary>
    private static bool GetDisposedFlag(ToolUtilityClass toolUtility)
    {
        var field = typeof(ToolUtilityClass).GetField(
            "_disposed",
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull("釋放狀態是共享資源生命週期的受保護契約");
        return field!.GetValue(toolUtility).Should().BeOfType<bool>().Subject;
    }

    /// <summary>
    /// 只公開受保護的 HandleError 以進行黑箱測試。正式程式仍保留受保護可見性，
    /// 因此不會擴大 Controller 的產品 API 面積。
    /// </summary>
    private sealed class TestableBaseChurchController : BaseChurchController
    {
        /// <summary>
        /// 建立具備最小 DI 依賴的測試控制器。所有相依物件都是無副作用假物件，
        /// 不持有跨測試狀態，並且不會建立 CRM、快取或 Session 資源。
        /// </summary>
        public TestableBaseChurchController(IToolUtilityProvider toolUtilityProvider)
            : base(
                new HttpContextAccessor(),
                null!,
                toolUtilityProvider,
                new NoOpCrmConnectionPool(),
                new NullInMemoryDataContext())
        {
        }

        /// <summary>
        /// 執行基底控制器的錯誤處理流程，讓測試驗證實際回傳的 IActionResult，
        /// 而不是複製正式程式碼中的條件判斷。
        /// </summary>
        public IActionResult InvokeHandleError(Exception exception, string methodName)
        {
            return HandleError(exception, methodName);
        }
    }

    /// <summary>
    /// 固定回傳指定 ToolUtility 的 Provider。它不擁有該物件，因此不實作 Dispose；
    /// 擁有權維持在測試方法，正好模擬正式 Factory/Provider 的共享實例契約。
    /// </summary>
    private sealed class FixedToolUtilityProvider : IToolUtilityProvider
    {
        private readonly ToolUtilityClass _toolUtility;

        /// <summary>
        /// 建立不轉移擁有權的 Provider；呼叫端僅能使用，不得釋放傳入的實例。
        /// </summary>
        public FixedToolUtilityProvider(ToolUtilityClass toolUtility)
        {
            _toolUtility = toolUtility;
        }

        /// <summary>
        /// 取得固定共享實例。測試刻意不建立新實例，以捕捉錯誤的 Controller Dispose。
        /// </summary>
        public ToolUtilityClass GetToolUtility()
        {
            return _toolUtility;
        }
    }

    /// <summary>
    /// 不配置 CRM client 的空連線池。測試不呼叫借用操作；若正式程式意外要求借用，
    /// 立即失敗以避免測試暗中連至外部 CRM 或留下未歸還資源。
    /// </summary>
    private sealed class NoOpCrmConnectionPool : ICrmConnectionPool
    {
        /// <summary>禁止在此錯誤處理測試中借用 CRM 連線。</summary>
        public IOrganizationService AcquireConnection() => throw new NotSupportedException();

        /// <summary>禁止在未借用情況下歸還 CRM 連線。</summary>
        public void ReleaseConnection(IOrganizationService service) => throw new NotSupportedException();

        /// <summary>回傳空統計資訊，不建立背景計時器或共享集合。</summary>
        public ConnectionPoolStats GetStats() => new();

        /// <summary>禁止健康檢查，以確保測試不觸碰外部服務。</summary>
        public bool ValidateConnection(IOrganizationService service) => throw new NotSupportedException();

        /// <summary>沒有資源可釋放；方法存在是為了滿足介面契約。</summary>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 只滿足 BaseChurchController 建構式的資料上下文契約。所有屬性皆為空，因為本檔
    /// 測試的行為在進入任何使用者資料或 CRM 查詢之前就已完成。
    /// </summary>
    private sealed class NullInMemoryDataContext : IInMemoryDataContext
    {
        public ListManager ListManager => null!;
        public SmallGroupDataList SmallGroupDataList => null!;
        public WeeklyReportData WeeklyReportData => null!;
        public NewPersonModel NewPersonModel => null!;
        public PersonalInfomationModel PersonalInfomationModel => null!;
        public HappyGroupDataManager HappyGroupDataManager => null!;
        public ListManagementDataManager ListManagementDataManager => null!;
        public EquipmentDataManager EquipmentDataManager => null!;
        public FeeList FeeList => null!;
        public LineBindingViewModel LineBindingViewModel => null!;
        public AppointmentsListManager AppointmentsListManager => null!;
        public DonationPaymentManager DonationPaymentManager => null!;
        public PollManager PollManager => null!;
        public ToolUtilityClass ToolUtilityClass => null!;

        /// <summary>禁止資料初始化，因為測試只驗證錯誤處理與 Dispose 邊界。</summary>
        public void SetupSmallGroupData(string fullName, string account, string password, DateTime selectDate, bool displayDateFlag)
        {
            throw new NotSupportedException();
        }

        /// <summary>禁止持久化；空資料上下文沒有要提交的跨請求狀態。</summary>
        public void SaveChanges()
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 故意在首次讀取 TempData 時失敗的 provider，用來驗證錯誤頁的防禦性讀取。
    /// 它不配置任何資料、檔案或 Session，因此失敗後也沒有資源需要清理。
    /// </summary>
    private sealed class ThrowingTempDataProvider : ITempDataProvider
    {
        /// <summary>注入 deterministic 載入失敗，重現 TempData 基礎設施不可用情況。</summary>
        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            throw new InvalidOperationException("TempData provider is unavailable");
        }

        /// <summary>此測試不儲存 TempData；若被呼叫即代表受測流程超出預期範圍。</summary>
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            throw new NotSupportedException();
        }
    }
}
