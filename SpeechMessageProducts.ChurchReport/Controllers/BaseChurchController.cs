// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/BaseChurchController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class BaseChurchController
// 主要成員：HandleError、SendLineErrorNotification、SetMultiGroupLayoutParameter、ResolveDonationManagementAccessFlag、IsIntegrateDataLoaded、SetupBasicViewBag、SetupMemberInfoViewBag、SetupFeeDataListCount、EnsureCorrectUserData、GetStableHash
// 引用命名空間：ChurchReport.Models、ChurchReport.Payments、ChurchReport.Services.Donation、ChurchReport.Services.MemberInfo、ChurchReport.Tools、ChurchReport.Services、LineMessagingProcessor.Workflows、Microsoft.AspNetCore.Http
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Services.Donation;
using ChurchReport.Services.MemberInfo;
using ChurchReport.Tools;
using ChurchReport.Services;
using LineMessagingProcessor.Workflows;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// ChurchReport 所有業務控制器的共同基底類別。
    /// </summary>
    /// <remarks>
    /// <para><b>這個類別解決什麼問題</b></para>
    /// 本系統的每一支控制器都需要同一組前置條件：
    /// 取得 Dataverse/CRM 操作進入點、取得該次請求的記憶體資料內容、
    /// 把登入者身分與頁面狀態填進 ViewBag、以及在例外發生時用一致的方式回應。
    /// 把這些共通行為集中在基底類別，讓各控制器只專注於自己的業務邏輯。
    ///
    /// <para><b>提供的四類能力</b></para>
    /// <list type="number">
    /// <item>相依服務存取：<see cref="ToolUtility"/>、<see cref="InMemoryContext"/>、連線集區統計。</item>
    /// <item>錯誤處理：<see cref="HandleError"/> 統一記錄、通知並依請求型態回應。</item>
    /// <item>ViewBag 組裝：<see cref="SetupBasicViewBag"/> 等方法填入 Layout 需要的導覽狀態。</item>
    /// <item>Session 一致性：<see cref="EnsureCorrectUserData"/> 確保記憶體狀態與 Session 身分相符。</item>
    /// </list>
    ///
    /// <para><b>執行緒與生命週期</b></para>
    /// 本類別是 per-request 物件，由 MVC 為每次請求建立與釋放。
    /// 唯一的跨請求狀態是 <c>_userValidationCache</c>（靜態驗證快取），
    /// 它以 Session Id 與密碼雜湊組合為鍵，設計上不可能被其他 Session 讀到，
    /// 詳見該欄位與 <c>CleanupOldCacheForSession</c> 的說明。
    ///
    /// <para><b>典型用法</b></para>
    /// <code>
    /// public class MyController : BaseChurchController
    /// {
    ///     public MyController(...) : base(...) { }
    ///
    ///     public IActionResult MyAction()
    ///     {
    ///         try { EnsureCorrectUserData(); SetupBasicViewBag(); return View(); }
    ///         catch (Exception ex) { return HandleError(ex, nameof(MyAction)); }
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public abstract class BaseChurchController : Controller, IDisposable
    {
        #region 常數與快取界限

        /// <summary>
        /// Trace 分級輸出用的層級常數。
        /// </summary>
        /// <remarks>
        /// 搭配 <c>ToolUtilityClass.TraceByLevel(總層級, 目前層級, 訊息)</c> 使用：
        /// 第一個參數是這次呼叫允許輸出到第幾層，第二個參數是本則訊息所屬層級，
        /// 只有當「訊息層級 &lt;= 總層級」時才會實際寫出。
        ///
        /// <c>TOTAL_LEVEL</c> 目前設為 1，代表全站預設只輸出最重要的第 1 層訊息；
        /// <c>LEVEL_1</c> 保留給錯誤與例外，數字越大代表越細節、越不重要的診斷資訊。
        /// 把層級寫成具名常數而非魔術數字，是為了讓呼叫端一眼看出訊息的重要性。
        /// </remarks>
        protected const int TOTAL_LEVEL = 1;
        protected const int LEVEL_1 = 1;
        protected const int LEVEL_2 = 2;
        protected const int LEVEL_3 = 3;
        protected const int LEVEL_4 = 4;
        protected const int LEVEL_5 = 5;


        /// <summary>
        /// 使用者身分驗證結果的快取有效秒數。
        /// </summary>
        /// <remarks>
        /// <see cref="EnsureCorrectUserData"/> 每次請求都要確認「Session 中的密碼」與
        /// 「ListManager 目前持有的密碼」是否一致。這個比對本身不貴，
        /// 但比對不符時要重新載入 ListManager，成本很高。
        /// 因此把「已確認一致」的結果快取 30 秒，避免同一位使用者在連續操作時反覆檢查。
        ///
        /// 30 秒是刻意取的短值：夠短，使用者切換身分後最多 30 秒就會被重新驗證；
        /// 夠長，足以涵蓋一個頁面連帶發出的多個 AJAX 請求。
        /// </remarks>
        private const int USER_VALIDATION_CACHE_SECONDS = 30;

        #endregion

        #region 相依服務與受控存取

        /// <summary>
        /// ToolUtility 的取得管道，由 DI 注入。
        /// </summary>
        /// <remarks>
        /// 刻意注入 provider 而不是直接注入 <c>ToolUtilityClass</c>，原因是生命週期：
        /// <c>ToolUtilityClass</c> 由 <c>ToolUtilityFactory</c> 以程序級單例形式管理，
        /// 如果在建構式就把實例抓進欄位，控制器會在整個請求期間釘住那個參考，
        /// 後續若單例被替換（例如設定重載）就會用到過期的物件。
        /// 透過 provider 在每次存取時才取得，可讓控制器永遠拿到當下正確的實例。
        /// </remarks>
        protected readonly IToolUtilityProvider _toolUtilityProvider;

        /// <summary>
        /// Dataverse/CRM 連線集區，由 DI 注入。
        /// </summary>
        /// <remarks>
        /// 本欄位只用於讀取集區統計（<see cref="GetConnectionPoolStats"/>），
        /// 供診斷頁面與健康檢查顯示目前的連線使用狀況。
        ///
        /// 實際的 CRM 操作「不」經由這個欄位，而是走 <see cref="ToolUtility"/>；
        /// 連線的取得與歸還由組合根註冊的 gateway 在 request scope 內自動處理，
        /// 控制器不應該、也不需要自己向集區借還連線。
        /// </remarks>
        protected readonly ICrmConnectionPool _connectionPool;

        /// <summary>
        /// HttpContext 的存取器，由 DI 注入。
        /// </summary>
        /// <remarks>
        /// 基底類別 <c>Controller.HttpContext</c> 在控制器建構期間尚未被 MVC 指派，
        /// 此時讀取會得到 null。本欄位讓建構式與建構期呼叫的方法也能安全取得目前請求。
        /// 實際取用請透過下方覆寫的 <see cref="HttpContext"/> 屬性，它已處理好兩種來源的優先順序。
        /// </remarks>
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// 使用者身分驗證結果的程序級快取。
        /// </summary>
        /// <remarks>
        /// <para><b>結構</b></para>
        /// <list type="bullet">
        /// <item>Key：<c>"{SessionId}_{密碼的 SHA256 前 8 碼}"</c></item>
        /// <item>Value：<c>(LastValidated 最後驗證時間, IsValid 是否通過, PasswordHash 當時的密碼雜湊)</c></item>
        /// </list>
        ///
        /// <para><b>為什麼鍵要包含密碼雜湊</b></para>
        /// 只用 SessionId 當鍵會有風險：使用者在同一個 Session 內換了身分（例如重新登入成別人）時，
        /// 舊的「已驗證」結果會被誤用。把密碼雜湊放進鍵，等於讓不同身分自然落在不同的鍵上，
        /// 換身分後必定查不到舊項目，一定會重新驗證。
        ///
        /// <para><b>為什麼這不是跨使用者洩漏</b></para>
        /// 鍵同時綁定 Session 與密碼，任何一位使用者都無法查到另一位的項目；
        /// 而且讀取端還會再檢查一次有效期與密碼雜湊是否相符（見 <see cref="EnsureCorrectUserData"/>）。
        /// 快取內只存「是否通過」的布林值與雜湊，不存密碼原文、姓名或任何個資。
        ///
        /// <para><b>記憶體上界</b></para>
        /// 由 <c>CleanupOldCacheForSession</c> 以「30 秒節流 + 4096 筆硬上界」雙重機制回收，
        /// 不會隨著線上人數無限成長。
        /// </remarks>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastValidated, bool IsValid, string PasswordHash)>
            _userValidationCache = new();

        /// <summary>
        /// 取得 Dataverse/CRM 與各項後端服務的統一操作進入點。
        /// </summary>
        /// <remarks>
        /// 這是一個「每次存取都重新取得」的屬性，不是快取欄位，理由見 <c>_toolUtilityProvider</c> 的說明。
        /// 呼叫成本很低（provider 內部回傳既有單例），可以放心在方法內多次使用。
        ///
        /// ⚠️ 絕對不要對這個屬性回傳的物件呼叫 Dispose。它是程序級單例，
        /// 由短命的控制器釋放長命的單例會讓後續所有請求失敗，詳見 <see cref="Dispose"/> 的說明。
        /// </remarks>
        protected ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

        /// <summary>
        /// 本次請求的記憶體資料內容，聚合各個資料管理器。
        /// </summary>
        /// <remarks>
        /// 透過它可取得 <c>ListManager</c>（登入身分與名單）、<c>FeeList</c>（收費資料）、
        /// <c>PersonalInfomationModel</c>（個人資料）、<c>HappyGroupDataManager</c>、
        /// <c>DonationPaymentManager</c>（奉獻付款狀態）等子管理器。
        ///
        /// 宣告型別刻意使用介面 <c>IInMemoryDataContext</c> 而非具體類別，
        /// 讓單元測試可以替換成假的實作，不必連上真的 CRM。
        /// 實例來源見建構式：優先使用 DI 注入，未注入時才自行組裝。
        /// </remarks>
        protected readonly IInMemoryDataContext InMemoryContext;

        /// <summary>
        /// 取得目前請求的 <see cref="Microsoft.AspNetCore.Http.HttpContext"/>，覆寫基底類別的同名屬性。
        /// </summary>
        /// <remarks>
        /// <para><b>為什麼要覆寫</b></para>
        /// MVC 是在控制器「建構完成之後」才指派 <c>ControllerBase.HttpContext</c>，
        /// 所以在建構式或建構期間呼叫的方法裡讀取基底屬性會拿到 null，
        /// 造成難以追查的 NullReferenceException。
        ///
        /// <para><b>取得順序</b></para>
        /// <list type="number">
        /// <item>先問 <c>IHttpContextAccessor</c>：它在整個請求生命週期都有效，包含建構期。</item>
        /// <item>再退回基底類別的屬性：涵蓋 accessor 未註冊的情境。</item>
        /// <item>兩者皆無時明確擲出例外：與其讓 null 流到更深處才爆炸，不如在此點出真正的原因。</item>
        /// </list>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// 不在 ASP.NET Core 請求管線中執行，或 <c>IHttpContextAccessor</c> 未註冊時擲出。
        /// </exception>
        protected new HttpContext HttpContext
        {
            get
            {
                // 第一順位：IHttpContextAccessor。它在控制器建構期間就已可用，
                var context = _httpContextAccessor?.HttpContext;

                // 第二順位：基底類別屬性。涵蓋 IHttpContextAccessor 未註冊的情境。
                if (context == null)
                {
                    context = base.HttpContext;
                }

                // 兩個來源都沒有，代表根本不在 HTTP 請求管線中執行。
                if (context == null)
                {
                    throw new InvalidOperationException(
                        "HttpContext is not available. Ensure the request is running inside an ASP.NET Core HTTP pipeline and IHttpContextAccessor is registered.");
                }

                return context;
            }
        }

        #endregion

        #region 建構式 (Constructor)

        /// <summary>
        /// 建立控制器並準備好所有共用相依項。
        /// </summary>
        /// <remarks>
        /// <para><b>必要相依項</b></para>
        /// 前四個參數都是必要的，任一為 null 立即擲出 <see cref="ArgumentNullException"/>。
        /// 這是刻意的 fail-fast：缺少相依項的控制器不可能正確運作，
        /// 與其讓它建立成功、之後在某個 action 中以 NullReferenceException 失敗，
        /// 不如在建構當下就明確指出是哪一個相依項沒有註冊。
        ///
        /// <para><b>inMemoryContext 為何是選用的</b></para>
        /// 正常情況下由 DI 注入 request-scoped 的實例。保留自行組裝的分支是為了相容
        /// 尚未改為建構式注入的舊有控制器，避免一次性大規模改寫。
        /// 自行組裝時會從當前請求的服務容器取得付款與 LINE 工作流程，
        /// 因此仍然是 request 範圍的物件，不會跨請求共用狀態。
        /// </remarks>
        /// <param name="httpContextAccessor">HTTP 請求存取器，必要。</param>
        /// <param name="memoryCache">記憶體快取，僅在需要自行組裝 InMemoryContext 時使用。</param>
        /// <param name="toolUtilityProvider">ToolUtility 取得管道，必要。</param>
        /// <param name="connectionPool">CRM 連線集區，必要（供統計用）。</param>
        /// <param name="inMemoryContext">記憶體資料內容；未提供時由本建構式自行組裝。</param>
        protected BaseChurchController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IInMemoryDataContext inMemoryContext = null)
        {
            // ========================================
            // 步驟 1：驗證並保存必要相依項
            // ========================================
            // 全部採 fail-fast。缺少任何一項都代表 DI 註冊有誤，
            // 在此擲出例外比讓它在某個 action 內以 NullReferenceException 失敗容易除錯得多。
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

            // ToolUtility 與連線集區同為必要項，理由同上。
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));

            // ========================================
            // 步驟 2：取得記憶體資料內容
            // ========================================
            if (inMemoryContext != null)
            {
                // 正常路徑：DI 已提供 request-scoped 實例，直接採用。
                InMemoryContext = inMemoryContext;
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] InMemoryContext was resolved through dependency injection.");
            }
            else
            {
                // 相容路徑：呼叫端尚未改為建構式注入，於此自行組裝。
                // 三個工作流程都從「當前請求」的服務容器取得，因此組出來的物件
                // 仍然只屬於這一次請求，不會把任何狀態帶到別的請求。
                var donationPaymentCreateGatewayAdapter =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(IDonationPaymentCreateGatewayAdapter))
                        as IDonationPaymentCreateGatewayAdapter;
                var lineNotificationWorkflow =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(ILineNotificationWorkflow))
                        as ILineNotificationWorkflow;
                var lineReplyWorkflow =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(ILineReplyWorkflow))
                        as ILineReplyWorkflow;
                InMemoryContext = new InMemoryDataContextSmallGroup(
                    httpContextAccessor, memoryCache, toolUtilityProvider, donationPaymentCreateGatewayAdapter, lineNotificationWorkflow, lineReplyWorkflow);
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] Created InMemoryContext from DI services.");
            }

            // 至此所有共用相依項都已就緒，衍生控制器可以安全使用。
        }

        #endregion

        #region 錯誤處理

        /// <summary>
        /// 統一處理控制器動作中未預期的例外，並回傳合適的結果。
        /// </summary>
        /// <remarks>
        /// <para><b>處理順序</b></para>
        /// <list type="number">
        /// <item>組出含型別、方法名、時間與完整例外內容的訊息。</item>
        /// <item>寫入 Trace（第 1 層，最高重要性）。</item>
        /// <item>發送 LINE 通知給管理者。</item>
        /// <item>依請求型態回應：AJAX 回 JSON，一般導覽轉向錯誤頁。</item>
        /// </list>
        ///
        /// <para><b>為什麼每一步都包在 try/catch 裡</b></para>
        /// 這個方法本身就是在處理錯誤，如果記錄或通知的過程再擲出例外，
        /// 使用者會看到一個與原始問題完全無關的錯誤畫面，真正的原因反而被掩蓋。
        /// 因此每個附屬動作都獨立保護，確保無論如何都能走到最後回傳結果那一步。
        ///
        /// <para><b>為什麼要區分 AJAX</b></para>
        /// 對 AJAX 請求回傳 302 轉向，前端收到的會是一整頁錯誤頁的 HTML，
        /// 既無法解析也無法顯示有意義的訊息。回傳結構化 JSON 才能讓前端正確處理。
        /// </remarks>
        /// <param name="exception">攔截到的例外。</param>
        /// <param name="methodName">發生例外的方法名稱，建議以 <c>nameof</c> 傳入。</param>
        /// <returns>AJAX 請求回傳 JSON；其餘回傳轉向錯誤頁的結果。</returns>
        protected IActionResult HandleError(Exception exception, string methodName)
        {
            // 組出診斷訊息。包含型別全名與方法名，才能在多個控制器共用本方法時定位來源。
            string errorMessage = $"Unhandled ChurchReport exception: FullName = {GetType().FullName}, " +
                                $"Method = {methodName}, " +
                                $"Time = {DateTime.Now}, " +
                                $"Description = {exception}";

            // 步驟 1：寫入 Trace。即使失敗也不能中斷錯誤處理流程。
            try
            {
                ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1, errorMessage);
            }
            catch (Exception traceEx)
            {
                // Trace 本身失敗時退回 Debug 輸出，至少在開發環境仍看得到。
                System.Diagnostics.Debug.WriteLine($"TraceByLevel 失敗: {traceEx.Message}");
            }

            // 步驟 2：通知管理者。方法內部已自行處理例外，此處不需再包一層。
            SendLineErrorNotification(errorMessage);

            // 步驟 3：判斷是否為 AJAX 請求，決定回應格式。
            bool isAjaxRequest = false;
            try
            {
                isAjaxRequest = Request?.Headers != null &&
                               Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            }
            catch
            {
                // 讀取標頭失敗時保守地當成一般請求，讓使用者至少看得到錯誤頁。
                isAjaxRequest = false;
            }

            if (isAjaxRequest)
            {
                // AJAX：回傳結構化 JSON，讓前端能解析並顯示訊息。
                return Json(new
                {
                    status = "error",
                    message = exception.Message,
                    timestamp = DateTime.Now
                });
            }
            else
            {
                // 一般導覽：轉向統一的錯誤頁。
                return RedirectToAction("DisplayErrorView", "Home", new { ErrorMessage = exception.Message });
            }
        }

        /// <summary>
        /// 把錯誤訊息透過 LINE 通知管理者。
        /// </summary>
        /// <remarks>
        /// 系統的多數操作牽涉外部 CRM 與金流，錯誤往往需要人工介入，
        /// 因此除了寫入 Trace 之外，額外以 LINE 即時推播讓管理者能第一時間知道。
        ///
        /// <para><b>三層保護</b></para>
        /// 通知失敗（例如 LINE API 無法連線）時，先嘗試寫入 Trace；
        /// 若連 Trace 也失敗，最後退回 Debug 輸出。
        /// 無論如何都不會把例外往外拋，因為通知失敗不應該讓原本的錯誤處理流程再次中斷。
        /// </remarks>
        /// <param name="errorMessage">要送出的錯誤訊息內容。</param>
        protected void SendLineErrorNotification(string errorMessage)
        {
            try
            {
                ChurchReportLineAdminNotificationService.NotifyDefaultError("BaseChurchController", errorMessage);
            }
            catch (Exception ex)
            {
                // 第二層：通知失敗，改記錄到 Trace。
                try
                {
                    ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1,
                        $"LINE notification failed: {ex.Message}");
                }
                catch
                {
                // 第三層：連 Trace 都失敗，退回 Debug 輸出，確保不再往外拋例外。
                    System.Diagnostics.Debug.WriteLine($"LINE notification failed: {ex.Message}");
                }
            }
        }

        #endregion

        #region ViewBag 設定輔助方法

        /// <summary>
        /// 依目前的檢視型態與資料載入狀態，決定 Layout 要呈現哪一種小組檢視。
        /// </summary>
        /// <remarks>
        /// <para><b>兩個輸入</b></para>
        /// <list type="bullet">
        /// <item><c>displayViewType</c>：使用者選擇的檢視（MultiGroupView 或 IntegrateView）。</item>
        /// <item><c>integrateFlag</c>：整合週報資料是否已經載入完成。</item>
        /// </list>
        ///
        /// 兩者交叉後決定 <c>ViewBag.MultiGroupIndex</c>，Layout 據此挑選 partial view。
        /// 會有這個組合邏輯，是因為「使用者想看什麼」與「目前有什麼資料可看」不一定一致，
        /// 必須在兩者之間挑出一個實際可以正確渲染的檢視。
        ///
        /// 本方法最後也會設定「奉獻管理」導覽權限旗標，
        /// 詳見 <see cref="ResolveDonationManagementAccessFlag"/>。
        /// </remarks>
        protected void SetMultiGroupLayoutParameter()
        {
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            bool integrateFlag = IsIntegrateDataLoaded();

            // 以下四個分支涵蓋「使用者選擇」與「資料狀態」的四種組合。
            // 每個分支都要挑出一個確定能正確渲染的檢視，不能只看使用者的選擇。
            if (displayViewType == "MultiGroupView" && !integrateFlag)
            {
                // 想看多組、但整合資料未載入：只呈現單一多組檢視。
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
            }
            else if (displayViewType == "IntegrateView" && integrateFlag)
            {
                // 想看整合、且整合資料已備妥：直接使用整合檢視。
                ViewBag.MultiGroupIndex = "IntegrateView";
            }
            else if (displayViewType == "MultiGroupView" && integrateFlag)
            {
                // 想看多組、而整合資料也已載入：兩種資料都有，
                // 使用混合檢視同時呈現，避免讓已載入的資料被浪費。
                ViewBag.MultiGroupIndex = "HybridView";
            }
            else
            {
                // 其餘組合（想看整合但資料未載入）：
                // 依整合旗標決定退回混合或整合檢視，確保一定有可渲染的目標。
                ViewBag.MultiGroupIndex = integrateFlag ? "HybridView" : "IntegrateView";
            }

            // 「奉獻管理」按鈕屬於全站導覽列權限，不應依賴奉獻付款頁面的表單模型是否已初始化。
            // 先用目前登入者 CRM contact.new_church_jobtitle 判斷；只有登入 contact 尚未載入時，
            // 才保留舊的 DonationPaymentFormModel.IsAOfficeWorker 作為最後 fallback。
            ViewBag.IsAOfficeWorker = ResolveDonationManagementAccessFlag();
        }

        /// <summary>
        /// 解析 Layout 是否要顯示「奉獻管理／奉獻稽核」導覽入口。
        ///
        /// 根因說明：
        /// _Layout.cshtml 每一頁都會渲染，但 DonationPaymentManager.m_DonationPaymentFormModel
        /// 只會在奉獻付款流程初始化後才具有完整狀態。若使用者剛登入或停留在回報統計等非奉獻頁，
        /// 直接讀表單模型會得到預設 false，導致原本有權限的會計同工看不到「奉獻管理」按鈕。
        ///
        /// 正確資料來源是登入者的 CRM contact 職稱；奉獻付款表單狀態只能作為舊流程相容 fallback。
        /// </summary>
        private string ResolveDonationManagementAccessFlag()
        {
            try
            {
                var personalModel = InMemoryContext?.PersonalInfomationModel;
                if (personalModel != null && personalModel.m_LoginContact == null)
                {
                    try
                    {
                        personalModel.SetPersonalInfomationViewModel();
                    }
                    catch
                    {
                        // 某些入口頁可能尚未能載入登入 contact；不要讓導覽列渲染失敗，改走 fallback。
                    }
                }

                var loginContact = personalModel?.m_LoginContact;
                if (loginContact != null)
                {
                    var toolUtility = ToolUtility;
                    var jobTitle = toolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? string.Empty;
                    return DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle) ? "是的" : "否";
                }
            }
            catch
            {
                // 導覽列權限判斷不應中斷頁面輸出；下方 fallback 會維持舊流程可用。
            }

            return InMemoryContext?.DonationPaymentManager?.m_DonationPaymentFormModel?.IsAOfficeWorker == true
                ? "是的"
                : "否";
        }

        /// <summary>
        /// 判斷整合週報資料是否已經載入完成。
        /// </summary>
        /// <remarks>
        /// 判斷依據是 <c>ListManager.m_ListSmallGroupWeeklyReport</c> 的 <c>LoadFlag</c>。
        /// 必須同時檢查物件本身不為 null 與旗標為 true：
        /// 物件為 null 代表根本還沒開始載入，旗標為 false 代表正在載入或載入失敗，
        /// 兩種情況都不能當作「資料已可使用」。
        ///
        /// 此結果會影響 Layout 選擇哪一種小組檢視，見 <see cref="SetMultiGroupLayoutParameter"/>。
        /// </remarks>
        /// <returns>整合週報資料已備妥時回傳 true。</returns>
        protected bool IsIntegrateDataLoaded()
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
            return weeklyReport != null && weeklyReport.LoadFlag;
        }

        /// <summary>
        /// 填入所有頁面共用的 ViewBag 項目。
        /// </summary>
        /// <remarks>
        /// <para><b>填入的項目</b></para>
        /// <list type="bullet">
        /// <item><c>LoginType</c>：登入方式（一般帳號或 LINE）。</item>
        /// <item><c>LoginFullName</c>：登入者姓名，顯示於導覽列。</item>
        /// <item><c>FeeType</c>：目前的收費類別。</item>
        /// <item><c>HappyType</c>：幸福小組類別。</item>
        /// </list>
        ///
        /// 接著再委派 <see cref="SetupFeeDataListCount"/> 與 <see cref="SetupMemberInfoViewBag"/>
        /// 補上收費資料狀態與會員資料存取權限。
        ///
        /// 各控制器的 action 在回傳 View 之前應呼叫本方法，否則 Layout 會因缺少這些值而顯示不完整。
        /// </remarks>
        protected void SetupBasicViewBag()
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;

            // 委派給專責方法，讓各自的判斷邏輯保持獨立、易於單獨測試。
            SetupFeeDataListCount();
            SetupMemberInfoViewBag();
        }

        /// <summary>
        /// Setup member-info navigation access flag.
        /// </summary>
        protected void SetupMemberInfoViewBag()
        {
            try
            {
                var cached = HttpContext?.Session?.GetString("_MemberInfoAccess");
                if (!string.IsNullOrEmpty(cached))
                {
                    ViewBag.MemberInfoAccess = cached;
                    return;
                }

                var personalModel = InMemoryContext?.PersonalInfomationModel;
                if (personalModel != null && personalModel.m_LoginContact == null)
                {
                    try
                    {
                        personalModel.SetPersonalInfomationViewModel();
                    }
                    catch
                    {
                        // Login contact may not be ready on some entry requests. Do not cache a negative result.
                    }
                }

                var loginContact = personalModel?.m_LoginContact;
                if (loginContact == null)
                {
                    ViewBag.MemberInfoAccess = null;
                    return;
                }

                var toolUtility = ToolUtility;
                var jobTitle = toolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? string.Empty;
                var loginType = InMemoryContext?.ListManager?.LoginType ?? string.Empty;
                var access = MemberInfoAccessResolver.Resolve(jobTitle, loginType);

                if (!string.IsNullOrEmpty(access))
                {
                    HttpContext?.Session?.SetString("_MemberInfoAccess", access);
                }

                ViewBag.MemberInfoAccess = access;
            }
            catch
            {
                ViewBag.MemberInfoAccess = null;
            }
        }
        /// <summary>
        /// 設定收費資料的載入狀態文字，供 Layout 顯示提示。
        /// </summary>
        /// <remarks>
        /// 刻意只回報「有或沒有」而不回報實際筆數。
        /// 使用者在這個位置需要知道的只是「現在的操作有沒有資料基礎」，
        /// 精確筆數屬於報表頁面的內容，放在導覽提示只會造成干擾。
        ///
        /// 判斷同時檢查集合不為 null 與筆數大於 0：
        /// null 代表尚未查詢，空集合代表查詢過但沒有符合的資料，
        /// 在這個提示的語境下兩者都應顯示為「尚未載入」。
        /// </remarks>
        protected void SetupFeeDataListCount()
        {
            bool hasFeeData = InMemoryContext.FeeList.FeeDataList != null &&
                            InMemoryContext.FeeList.FeeDataList.Count > 0;

            ViewBag.FeeDataListCount = hasFeeData ? "已載入收費資料" : "尚未載入收費資料";
        }

        #endregion

        #region Session 安全性驗證

        /// <summary>
        /// 確保記憶體中的使用者資料與目前 Session 的身分一致。
        /// </summary>
        /// <remarks>
        /// <para><b>要解決的問題</b></para>
        /// <c>ListManager</c> 持有登入者的帳號、密碼與名單資料。
        /// 如果 Session 中的身分已經改變（重新登入、切換帳號、LINE 驗證票證恢復），
        /// 而 ListManager 還停留在舊身分，使用者就會看到上一個身分的資料。
        /// 本方法在每次需要使用者資料的 action 之前呼叫，用來消除這種不一致。
        ///
        /// <para><b>五個步驟</b></para>
        /// <list type="number">
        /// <item>取得 Session Id；沒有 Session 就不做任何事。</item>
        /// <item>查驗證快取；30 秒內已確認一致就直接返回。</item>
        /// <item>比對兩邊密碼；一致就記入快取並返回。</item>
        /// <item>密碼不一致：以 Session 的密碼重新載入 ListManager。</item>
        /// <item>Session 沒有密碼：嘗試從 LINE 驗證票證恢復身分。</item>
        /// </list>
        ///
        /// <para><b>為什麼整段包在 try/catch 裡</b></para>
        /// 本方法是輔助性質的一致性維護，不是授權檢查。
        /// 真正的授權由 <c>GlobalAuthorizationFilter</c> 與 <c>SessionValidationMiddleware</c> 負責。
        /// 因此這裡發生例外時應保持沉默、讓頁面繼續渲染，而不是讓整個請求失敗。
        /// </remarks>
        protected virtual void EnsureCorrectUserData()
        {
            try
            {
                // ========================================
                // 步驟 1：取得 Session Id 作為快取鍵的基礎
                // ========================================
                var sessionId = HttpContext?.Session?.Id;
                if (string.IsNullOrEmpty(sessionId))
                {
                    return; // Session is unavailable; leave the current request state unchanged.
                }

                // ========================================
                // 步驟 2：取出兩邊的密碼準備比對
                // ========================================
                // sessionPassword 是本次請求的權威身分來源；
                // listManagerPassword 是記憶體中目前生效的身分。
                var sessionPassword = HttpContext?.Session?.GetString("_LoginPassword");
                var listManagerPassword = InMemoryContext?.ListManager?.m_Password;

                // 兩邊都沒有密碼代表使用者根本尚未登入，沒有需要同步的狀態。
                if (string.IsNullOrEmpty(sessionPassword) && string.IsNullOrEmpty(listManagerPassword))
                {
                    return;
                }

                // 以密碼雜湊而非密碼原文組鍵，確保快取內不出現任何憑證明文。
                var currentPasswordHash = GetStableHash(sessionPassword ?? listManagerPassword ?? "");
                var cacheKey = $"{sessionId}_{currentPasswordHash}";

                // ========================================
                // 步驟 3：查詢驗證快取，命中就省下後續所有比對
                // ========================================
                if (_userValidationCache.TryGetValue(cacheKey, out var cached))
                {
                    var cacheAge = (DateTime.UtcNow - cached.LastValidated).TotalSeconds;

                    // 三個條件必須同時成立才採信快取：未過期、當時通過、且密碼雜湊仍相符。
                    if (cacheAge < USER_VALIDATION_CACHE_SECONDS &&
                        cached.IsValid &&
                        cached.PasswordHash == currentPasswordHash)
                    {
                        // 快取有效，記憶體狀態確定與 Session 一致，直接返回。
                        return;
                    }
                }

                // ========================================
                // 步驟 4：快取未命中，實際比對兩邊的密碼
                // ========================================
                var sessionAccount = HttpContext?.Session?.GetString("_LoginAccount");

                // 情況 A：兩邊都有密碼且完全相同，代表狀態本來就一致。
                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword == listManagerPassword)
                {
                    // 記錄本次驗證結果，讓接下來 30 秒內的請求都能走快取捷徑。
                    _userValidationCache[cacheKey] = (DateTime.UtcNow, true, currentPasswordHash);

                    // 順便清掉這個 Session 底下屬於舊密碼的項目。
                    CleanupOldCacheForSession(sessionId, cacheKey);
                    return;
                }

                // ========================================
                // 情況 B：兩邊密碼不同，以 Session 為準重新載入
                // ========================================
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] Session password differs; rehydrating ListManager.");
#endif

                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword != listManagerPassword)
                {
                    // Session 是身分的權威來源，因此用它的密碼重新初始化 ListManager。
                    InMemoryContext.ListManager.SetupListManager(
                        sessionAccount ?? "",
                        sessionPassword,
                        InMemoryContext.ListManager.m_SelectDate != default
                            ? InMemoryContext.ListManager.m_SelectDate
                            : DateTime.Now);

                    // 身分已變更，必須用新密碼的雜湊重算快取鍵，不能沿用舊鍵。
                    var newPasswordHash = GetStableHash(sessionPassword);
                    var newCacheKey = $"{sessionId}_{newPasswordHash}";
                    _userValidationCache[newCacheKey] = (DateTime.UtcNow, true, newPasswordHash);

                    // 清掉同一 Session 底下屬於舊身分的快取項目。
                    CleanupOldCacheForSession(sessionId, newCacheKey);
                    return;
                }

                // ========================================
                // 情況 C：Session 沒有密碼，嘗試從 LINE 驗證票證恢復
                // ========================================
                // Session 可能已逾時被清空，但 Cookie 驗證票證仍然有效。
                // 此時可從票證的 Claims 取回身分，避免使用者被迫重新登入。
                if (string.IsNullOrEmpty(sessionPassword))
                {
                    var principal = HttpContext?.User;
                    var loginType = principal?.FindFirst(ChurchReport.Security.LoginClaimsFactory.LoginTypeClaim)?.Value;
                    var passwordKey = principal?.FindFirst(ChurchReport.Security.LoginClaimsFactory.PasswordKeyClaim)?.Value;

                    if (principal?.Identity?.IsAuthenticated == true &&
                        loginType == "LINE" &&
                        !string.IsNullOrEmpty(passwordKey) &&
                        passwordKey != listManagerPassword)
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] Restoring ListManager from LINE authentication ticket.");
#endif

                        InMemoryContext.ListManager.SetupListManager(
                            "LineIdLogin",
                            passwordKey,
                            InMemoryContext.ListManager.m_SelectDate != default
                                ? InMemoryContext.ListManager.m_SelectDate
                                : DateTime.Now);

                        HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
                        HttpContext?.Session?.SetString("_LoginPassword", passwordKey);

                        // 同樣記入驗證快取，讓後續請求不必重複這段恢復流程。
                        var linePasswordHash = GetStableHash(passwordKey);
                        var lineCacheKey = $"{sessionId}_{linePasswordHash}";
                        _userValidationCache[lineCacheKey] = (DateTime.UtcNow, true, linePasswordHash);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] Session operation failed: {ex.Message}");
#endif
            }
        }

        /// <summary>
        /// 計算字串的穩定短雜湊，用於組成驗證快取的鍵。
        /// </summary>
        /// <remarks>
        /// <para><b>為什麼要雜湊</b></para>
        /// 快取鍵需要能區分不同的密碼，但絕對不能包含密碼原文。
        /// 雜湊讓鍵具備區分能力，同時保證即使快取內容被傾印也無法還原出密碼。
        ///
        /// <para><b>為什麼只取 8 個字元</b></para>
        /// 完整的 Base64 SHA256 有 44 個字元，作為字典鍵過長。
        /// 取前 8 個字元（48 位元）的碰撞機率極低，而且鍵中還包含 Session Id，
        /// 即使雜湊碰撞也必須同時發生在同一個 Session 內才可能造成影響。
        ///
        /// <para><b>穩定性</b></para>
        /// 相同輸入必定得到相同輸出，且與行程、機器、時間都無關。
        /// 這是快取能跨請求運作的前提，因此輸出格式不可更動。
        /// </remarks>
        /// <param name="input">要雜湊的字串，通常是登入密碼。</param>
        /// <returns>Base64 編碼的前 8 個字元；輸入為空時回傳固定字串 <c>"EMPTY"</c>。</returns>
        private static string GetStableHash(string input)
        {
            // 空字串以固定字面值代表，避免對空輸入做一次無意義的雜湊運算。
            // 這個回傳值同時也是快取鍵的一部分，必須保持穩定不可更動。
            if (string.IsNullOrEmpty(input))
                return "EMPTY";

            // ================================================================
            // ✅ 效能最佳化：以 SHA256.HashData 取代 SHA256.Create()
            // ================================================================
            // 【原本的成本】
            // 原本的寫法是 using (var sha256 = SHA256.Create())。每一次呼叫都會：
            //   1. 配置一個 SHA256 實作物件（在 Windows 上是 SHA256Cng 之類的包裝）
            //   2. 向作業系統開啟一個 CNG 演算法提供者控制代碼
            //   3. 雜湊區區數十個位元組
            //   4. 立刻 Dispose，把上述資源全部丟掉
            // 本方法位於 EnsureCorrectUserData 內，也就是每一個「已登入」請求的必經路徑，
            // 等於每個請求都付一次「建立加密提供者 → 用一次 → 銷毀」的代價。
            //
            // 【改用的 API】
            // SHA256.HashData 是 .NET 5 起提供的靜態一次性 API。它內部使用共用的實作，
            // 不配置任何需要 Dispose 的物件，也不會反覆開關 CNG 控制代碼。
            //
            // 【相容性保證】
            // 雜湊演算法、輸入位元組、Base64 編碼與「取前 8 個字元」的截斷方式全部保持不變，
            // 因此產生的字串與修改前完全相同。既有的快取鍵格式與碰撞特性都不受影響，
            // 執行中的行程即使新舊程式碼混用也不會有鍵不一致的問題。
            // ================================================================

            // 先算出 UTF-8 編碼所需的位元組數，才能決定要走堆疊還是租用緩衝區。
            var byteCount = System.Text.Encoding.UTF8.GetByteCount(input);

            // rented 保持為 null 代表這次走的是堆疊路徑，finally 就不需要歸還任何東西。
            byte[] rented = null;

            try
            {
                // 密碼長度在實務上遠小於 256 位元組，絕大多數呼叫會走 stackalloc 這條路，
                // 完全不產生任何堆積配置。超長輸入則向 ArrayPool 租用，避免產生大型物件。
                Span<byte> inputBytes = byteCount <= 256
                    ? stackalloc byte[256].Slice(0, byteCount)
                    : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount)).AsSpan(0, byteCount);

                // 直接把字串編碼進上面準備好的緩衝區，省掉 Encoding.GetBytes(string) 會產生的中間陣列。
                System.Text.Encoding.UTF8.GetBytes(input, inputBytes);

                // SHA256 的輸出長度固定為 32 位元組，可以安全地宣告在堆疊上。
                Span<byte> hashBytes = stackalloc byte[32];
                System.Security.Cryptography.SHA256.HashData(inputBytes, hashBytes);

                // 轉成 Base64 後截斷為 8 個字元。這裡刻意沿用原本的做法，
                // 因為快取鍵的格式必須維持不變（見上方相容性保證）。
                var hash = Convert.ToBase64String(hashBytes);
                return hash.Length > 8 ? hash.Substring(0, 8) : hash;
            }
            finally
            {
                // 只有走租用路徑時才需要歸還。這裡不清除內容：
                // 緩衝區裡是密碼的 UTF-8 位元組，下一位租用者可能讀到殘留資料，
                // 因此以 clearArray: true 歸還，確保不把憑證留在共用池中。
                if (rented != null)
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rented, clearArray: true);
                }
            }
        }

        /// <summary>
        /// 清除驗證快取中已失效的項目：屬於舊密碼的，以及過期的。
        /// </summary>
        /// <remarks>
        /// 這是 <c>_userValidationCache</c> 的唯一回收機制，負責讓它的大小保持有界。
        /// 實作與節流策略見下方方法本體的詳細說明。
        /// </remarks>
        /// <summary>
        /// ✅ 效能／記憶體：上次執行全表清掃的 UTC 時間戳（ticks），以 Interlocked 原子讀寫。
        /// 使用 long 而非 DateTime，是因為 Interlocked 只能對整數型別做原子操作。
        /// </summary>
        private static long _lastValidationCacheSweepTicks;

        /// <summary>
        /// 兩次全表清掃之間的最短間隔。距離上次清掃不足此間隔的呼叫會直接跳過。
        /// </summary>
        private static readonly long ValidationCacheSweepIntervalTicks = TimeSpan.FromSeconds(30).Ticks;

        /// <summary>
        /// 記憶體硬上界：不論距離上次清掃多久，一旦快取筆數達到此值就立即清掃。
        /// 這確保時間節流不會讓字典無限成長。
        /// </summary>
        private const int ValidationCacheForceSweepCount = 4096;

        private static void CleanupOldCacheForSession(string sessionId, string currentCacheKey)
        {
            // ================================================================
            // ✅ 效能最佳化：節流全表清掃
            // ================================================================
            // 【原本的成本】
            // 原本每一個「已登入且通過密碼比對」的請求都會執行到這裡，
            // 而這個方法會無條件走訪整個 _userValidationCache，
            // 並且無條件配置一個 List<string>（即使最後一筆都不需要移除）。
            // 也就是說單一請求的成本是 O(目前線上 Session 數)。
            // 在 1000 個並行 Session 的情況下，每個請求都要做 1000 次字典走訪，
            // 整體呈現 O(並行數平方) 的行為，是明顯的擴充性瓶頸。
            //
            // 【為什麼節流不會造成安全問題】
            // 這個清掃純粹是「記憶體衛生」，不是安全機制。
            // 快取的讀取路徑（EnsureCorrectUserData 的 Step 2）本身就有兩道檢查：
            //   1. cacheAge < USER_VALIDATION_CACHE_SECONDS —— 過期項目一律不採信
            //   2. cached.PasswordHash == currentPasswordHash —— 密碼不同一律不採信
            // 而快取鍵本身是 "{sessionId}_{passwordHash}"，
            // 屬於其他 Session 或其他密碼的項目根本不可能被查到。
            // 因此「晚幾秒才把舊項目刪掉」不會讓任何使用者看到別人的資料，
            // 也不會延長任何憑證的有效期。
            //
            // 【記憶體仍然有上界】
            // 除了 30 秒的時間節流之外，另外設有筆數硬上界
            // ValidationCacheForceSweepCount，一旦超過就立刻清掃，
            // 所以字典不會因為節流而無限成長。
            // ================================================================

            var nowTicks = DateTime.UtcNow.Ticks;

            // Interlocked.Read 用於 64 位元欄位的原子讀取，
            // 避免在 32 位元執行環境上讀到只更新一半的「撕裂值」。
            var lastSweep = Interlocked.Read(ref _lastValidationCacheSweepTicks);

            // 兩個觸發條件任一成立就執行清掃：時間到了，或筆數已達硬上界。
            var due = (nowTicks - lastSweep) >= ValidationCacheSweepIntervalTicks
                      || _userValidationCache.Count >= ValidationCacheForceSweepCount;

            if (!due)
            {
                return;
            }

            // 用 CompareExchange 搶下這一輪清掃的執行權。
            // 只有成功把時間戳從 lastSweep 換成 nowTicks 的那一條執行緒會繼續往下做，
            // 其餘同時抵達的執行緒直接返回，避免多條執行緒同時做全表走訪。
            if (Interlocked.CompareExchange(ref _lastValidationCacheSweepTicks, nowTicks, lastSweep) != lastSweep)
            {
                return;
            }

            try
            {
                // 延遲配置：只有真的找到要移除的鍵時才建立清單。
                // 穩定狀態下絕大多數清掃都不會找到任何項目，此時完全零配置。
                System.Collections.Generic.List<string> keysToRemove = null;
                var now = DateTime.UtcNow;

                // 先把前綴字串算好，避免在迴圈裡每一圈都做一次字串串接。
                var sessionPrefix = sessionId + "_";

                foreach (var kvp in _userValidationCache)
                {
                    // 檢查 1：同一個 Session、但不是目前這組快取鍵的項目。
                    // 這代表同一位使用者換過密碼，舊的雜湊鍵已經不會再被查詢，可以回收。
                    // 使用 StringComparison.Ordinal 是最快的比較方式，
                    // 而且 Session Id 與 Base64 雜湊都是 ASCII，不需要文化相關的比較規則。
                    if (kvp.Key.StartsWith(sessionPrefix, StringComparison.Ordinal) && kvp.Key != currentCacheKey)
                    {
                        (keysToRemove ??= new System.Collections.Generic.List<string>()).Add(kvp.Key);
                    }
                    // 檢查 2：任何超過 5 分鐘沒有更新的項目一律回收。
                    // 快取本身的有效期只有 USER_VALIDATION_CACHE_SECONDS（30 秒），
                    // 所以超過 5 分鐘的項目必定早已失效，留著只是佔用記憶體。
                    else if ((now - kvp.Value.LastValidated).TotalMinutes > 5)
                    {
                        (keysToRemove ??= new System.Collections.Generic.List<string>()).Add(kvp.Key);
                    }
                }

                // 沒有任何項目需要移除時直接返回，連清單都沒有配置過。
                if (keysToRemove == null)
                {
                    return;
                }

                // 走訪期間不可直接修改集合，因此在收集完成後才統一移除。
                // TryRemove 失敗代表別的執行緒已經先移除，屬於正常情況，可以忽略。
                foreach (var key in keysToRemove)
                {
                    _userValidationCache.TryRemove(key, out _);
                }

#if DEBUG
                if (keysToRemove.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[CleanupOldCache] 已清除 {keysToRemove.Count} 筆過期或屬於舊密碼的驗證快取項目 (Session={sessionId.Substring(0, Math.Min(8, sessionId.Length))}...)");
                }
#endif
            }
            catch
            {
                // 清理失敗不可影響主要流程。
                // 這裡只是記憶體衛生工作，任何例外都不應該讓一個已經通過驗證的請求失敗；
                // 下一輪節流到期時自然會再嘗試一次，因此吞掉例外是安全且刻意的。
            }
        }


        /// <summary>
        /// 驗證目前 Session 是否仍然有效且可信。
        /// </summary>
        /// <remarks>
        /// <para><b>三道檢查</b></para>
        /// <list type="number">
        /// <item>Session 中必須有使用者識別碼，否則視為未登入。</item>
        /// <item>Session 建立至今不得超過 8 小時，避免長期閒置的 Session 被重複利用。</item>
        /// <item>記憶體中的 ListManager 必須有帳號，確保後端狀態確實已初始化。</item>
        /// </list>
        ///
        /// <para><b>與其他防護的分工</b></para>
        /// 本方法是給控制器主動呼叫的輔助檢查，回傳布林值讓呼叫端自行決定如何處理。
        /// 全站強制性的授權由 <c>GlobalAuthorizationFilter</c> 負責，
        /// Session 劫持偵測由 <c>SessionValidationMiddleware</c> 負責，兩者都不需要控制器介入。
        ///
        /// <para><b>失敗時一律回傳 false</b></para>
        /// 任何例外都當作驗證失敗處理。安全性檢查在無法確定結果時，
        /// 必須選擇最保守的結果，絕不能因為讀取失敗就放行。
        ///
        /// <para><b>用法</b></para>
        /// <code>
        /// if (!ValidateSession())
        /// {
        ///     return RedirectToAction("Login", "Authentication");
        /// }
        /// </code>
        /// </remarks>
        /// <returns>三道檢查全數通過時回傳 true，否則回傳 false。</returns>
        protected bool ValidateSession()
        {
            try
            {
                // ========================================
                // 檢查 1：Session 中是否有使用者識別碼
                // ========================================
                var sessionUserId = HttpContext.Session.GetString("_SessionUserId");
                if (string.IsNullOrEmpty(sessionUserId))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] Session user id is missing.");
                    return false;
                }

                // ========================================
                // 檢查 2：Session 是否超過 8 小時的絕對存活上限
                // ========================================
                // 這是獨立於閒置逾時的硬上限，用來防止 Session 被無限期續用。
                var sessionCreatedAt = HttpContext.Session.GetString("_SessionCreatedAt");
                if (!string.IsNullOrEmpty(sessionCreatedAt))
                {
                    if (DateTime.TryParse(sessionCreatedAt, out DateTime createdTime))
                    {
                        var sessionAge = DateTime.UtcNow - createdTime;
                        // 超過上限一律視為失效，不論期間是否有活動。
                        if (sessionAge.TotalHours > 8)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session expired after {sessionAge.TotalHours:F2} hours.");
                            return false;
                        }
                    }
                }

                // ========================================
                // 檢查 3：記憶體狀態是否確實已初始化
                // ========================================
                // Session 有效但 ListManager 沒有帳號，代表後端狀態遺失（例如應用程式重啟），
                // 此時即使 Session 本身沒過期，也無法提供正確的資料。
                var currentAccount = InMemoryContext?.ListManager?.m_Account;
                if (string.IsNullOrEmpty(currentAccount))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] Current account is missing from InMemoryContext.");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session validated for user: {sessionUserId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session validation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 在身分變更後清空並重建 Session 內容，降低 Session Fixation 風險。
        /// </summary>
        /// <remarks>
        /// <para><b>實際行為與命名的差異</b></para>
        /// ⚠️ 方法名稱沿用傳統 ASP.NET 的說法，但 ASP.NET Core「不會」在此更換 Session Id。
        /// 本方法真正做的是：保留必要欄位、清空 Session、提交、再把欄位寫回去，
        /// 也就是「重建內容」而非「重新產生識別碼」。
        ///
        /// <para><b>為什麼這樣仍然有效</b></para>
        /// 在目前架構下，使用者身分的權威來源是 Cookie 驗證票證而不是 Session Id。
        /// 登入時重新簽發票證即可讓舊票證失效，因此清空 Session 內容足以切斷與舊身分的關聯。
        ///
        /// <para><b>保留的欄位</b></para>
        /// 使用者識別碼、User-Agent 與真實 IP 會被保留並重新寫入，
        /// 因為 <c>SessionValidationMiddleware</c> 需要它們來偵測 Session 劫持。
        /// 同時會更新 <c>_SessionIdentifier</c> 與 <c>_SessionCreatedAt</c>，重新起算存活時間。
        ///
        /// <para><b>失敗時會往外拋</b></para>
        /// 與本類別其他方法不同，這裡的例外不吞掉。
        /// Session 重建失敗代表安全狀態不確定，必須讓呼叫端知道並中止流程。
        /// </remarks>
        /// <exception cref="Exception">Session 重建過程中的任何失敗都會原樣往外拋。</exception>
        protected void RegenerateSessionId()
        {
            try
            {
                // 先把重建後仍需要的欄位讀出來暫存，Clear 之後就取不到了。
                var userId = HttpContext.Session.GetString("_SessionUserId");
                var userAgent = HttpContext.Session.GetString("_SessionUserAgent");
                var realIp = HttpContext.Session.GetString("_SessionRealIp");

                // 清空 Session 的所有內容，切斷與前一個身分的關聯。
                HttpContext.Session.Clear();

                // 必須立即提交，確保清除動作即刻生效而不是等到請求結束。
                HttpContext.Session.CommitAsync().GetAwaiter().GetResult();

                // 把安全驗證需要的欄位寫回，並重新起算 Session 的建立時間。
                if (!string.IsNullOrEmpty(userId))
                {
                    HttpContext.Session.SetString("_SessionUserId", userId);
                    HttpContext.Session.SetString("_SessionIdentifier", $"{userId}_{DateTime.UtcNow.Ticks}");
                    HttpContext.Session.SetString("_SessionCreatedAt", DateTime.UtcNow.ToString("O"));
                    HttpContext.Session.SetString("_SessionUserAgent", userAgent ?? "");
                    HttpContext.Session.SetString("_SessionRealIp", realIp ?? "");
                }

                System.Diagnostics.Debug.WriteLine("[RegenerateSessionId] Session data cleared. ASP.NET Core does not rotate the Session ID here; identity is bound to the auth ticket.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerateSessionId] Session regeneration failed: {ex.Message}");
                throw;
            }
        }

        protected async System.Threading.Tasks.Task IssueAuthTicketAsync(string contactId, string account, string passwordKey, string loginType)
        {
            try
            {
                var principal = ChurchReport.Security.LoginClaimsFactory.Build(contactId, account, passwordKey, loginType);
                await HttpContext.SignInAsync(
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                    principal);

                System.Diagnostics.Debug.WriteLine($"[IssueAuthTicket] Issued auth ticket. loginType={loginType}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IssueAuthTicket] Failed to issue auth ticket: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得 Dataverse/CRM 連線集區的目前統計數據。
        /// </summary>
        /// <remarks>
        /// <para><b>用途</b></para>
        /// 供診斷頁面與健康檢查顯示連線使用狀況，例如目前有多少連線在使用中、
        /// 多少請求正在排隊等待、累計發生過多少次逾時與驗證失敗。
        /// 連線洩漏最典型的徵兆就是「使用中連線只增不減」，可從這裡看出來。
        ///
        /// <para><b>為什麼失敗時回傳全零而不是 null</b></para>
        /// 本方法只服務診斷用途，呼叫端通常直接把數值渲染到頁面上。
        /// 回傳 null 會迫使每個呼叫端都要處理空值，稍有疏漏就讓診斷頁自己壞掉；
        /// 回傳全零的物件則能保證頁面永遠渲染得出來。
        ///
        /// 全零同時也是一個明確的訊號：正常運作時累計計數不可能為零，
        /// 看到一整排零就代表集區尚未初始化或讀取統計時發生了錯誤。
        /// </remarks>
        /// <returns>集區統計數據；集區未注入或讀取失敗時回傳各欄位皆為 0 的物件。</returns>
        protected ConnectionPoolStats GetConnectionPoolStats()
        {
            try
            {
                if (_connectionPool == null)
                {
                    return new ConnectionPoolStats
                    {
                        TotalConnections = 0,
                        ActiveConnections = 0,
                        IdleConnections = 0,
                        WaitingRequests = 0,
                        TotalAcquireCount = 0,
                        TotalReleaseCount = 0,
                        TimeoutCount = 0,
                        ValidationFailureCount = 0
                    };
                }

                return _connectionPool.GetStats();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetConnectionPoolStats] Failed to read pool statistics: {ex.Message}");

                // 讀取失敗時回傳全零物件，讓診斷頁面仍能正常渲染（理由見 remarks）。
                return new ConnectionPoolStats
                {
                    TotalConnections = 0,
                    ActiveConnections = 0,
                    IdleConnections = 0,
                    WaitingRequests = 0,
                    TotalAcquireCount = 0,
                    TotalReleaseCount = 0,
                    TimeoutCount = 0,
                    ValidationFailureCount = 0
                };
            }
        }

        #endregion

        #region 資源釋放

        /// <summary>
        /// 釋放本 Controller 自身的資源。本方法由 ASP.NET Core 於每個 request 結束時呼叫。
        /// </summary>
        /// <remarks>
        /// 重要：本方法「刻意不」釋放 <c>ToolUtility</c>，請勿還原該呼叫。
        ///
        /// 生命週期不相符是唯一理由：<c>ToolUtility</c> 取自
        /// <c>IToolUtilityProvider.GetToolUtility()</c>，其實作 <c>ToolUtilityFactory.GetInstance()</c>
        /// 回傳的是「程序級單一實例」（static 欄位，double-check locking 建立），
        /// 存活期等同整個 Worker Process；而 Controller 是 per-request 物件，存活期僅一次請求。
        /// 由短命物件去釋放長命物件，等同「一個請求結束時把整個程序共用的資源關掉」，
        /// 之後所有請求都會操作到已釋放的物件。
        ///
        /// 這個錯誤在歷史版本中確實存在過（曾有一行在此呼叫 ToolUtility.Dispose）。
        /// 它長期未造成可見故障，只因 <c>ToolUtilityClass.Dispose</c> 內的
        /// 連線關閉呼叫，
        /// 在 <c>OnPremiseClient</c> 尚未實作 <see cref="IDisposable"/> 之前是無作用的空操作。
        /// 一旦該型別補上確定性釋放，這個既有的生命週期錯誤立即顯現為
        /// <see cref="ObjectDisposedException"/>（ServiceChannel 已關閉），實測會使登入流程失敗。
        ///
        /// 釋放責任歸屬：程序級單例於程序結束時回收，不由任何 request 範圍或 operation 範圍物件負責。
        /// 本 Controller 自身的 request 範圍資源（含注入的 IOrganizationService 租約），
        /// 由基底類別與 DI 容器在 request scope 結束時確定性釋放，無須於此重複處理。
        /// </remarks>
        public new void Dispose()
        {
            // 不得在此釋放 ToolUtility —— 它是程序級單例，理由見上方 remarks。
            base.Dispose();
        }

        #endregion
    }
}
