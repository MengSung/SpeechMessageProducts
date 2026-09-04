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
    /// 已修復的說明。
    ///
    /// 已修復的說明。
    /// 已修復的說明。
    /// 已修復的說明。
    /// 已修復的說明。
    /// 已修復的說明。
    /// 已修復的說明。
    ///
    /// 已修復的說明。
    /// 已修復的說明。
    /// 已修復的說明。
    /// 已修復的說明。
    ///
    /// 已修復的說明。
    /// public class MyController : BaseChurchController
    /// {
    ///     public MyController(...) : base(...) { }
    ///
    ///     public IActionResult MyAction()
    ///     {
    /// 已修復的說明。
    ///     }
    /// }
    /// </summary>
    public abstract class BaseChurchController : Controller, IDisposable
    {
        #region 常數

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// </summary>
        protected const int TOTAL_LEVEL = 1;
        protected const int LEVEL_1 = 1;
        protected const int LEVEL_2 = 2;
        protected const int LEVEL_3 = 3;
        protected const int LEVEL_4 = 4;
        protected const int LEVEL_5 = 5;


        /// <summary>
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        private const int USER_VALIDATION_CACHE_SECONDS = 30;

        #endregion

        #region 服務執行個體

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// </summary>
        protected readonly IToolUtilityProvider _toolUtilityProvider;

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// </summary>
        protected readonly ICrmConnectionPool _connectionPool;

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// - Value: (LastValidated, IsValid, PasswordHash)
        /// 已修復的說明。
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastValidated, bool IsValid, string PasswordHash)>
            _userValidationCache = new();

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected readonly IInMemoryDataContext InMemoryContext;

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected new HttpContext HttpContext
        {
            get
            {
                // 已修復的註解。
                var context = _httpContextAccessor?.HttpContext;

                // 已修復的註解。
                if (context == null)
                {
                    context = base.HttpContext;
                }

                // 已修復的註解。
                if (context == null)
                {
                    throw new InvalidOperationException(
                        "HttpContext is not available. Ensure the request is running inside an ASP.NET Core HTTP pipeline and IHttpContextAccessor is registered.");
                }

                return context;
            }
        }

        #endregion

        #region 撱箸??賢? (Constructor)

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected BaseChurchController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IInMemoryDataContext inMemoryContext = null)
        {
            // ========================================
            // 已修復的註解。
            // ========================================
            // 已修復的註解。
            // 已修復的註解。
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

            // 已修復的註解。
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));

            // 已修復的註解。
            // 已修復的註解。
            // 已修復的註解。
            if (inMemoryContext != null)
            {
                // 已修復的註解。
                InMemoryContext = inMemoryContext;
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] DI 容器未註冊 InMemoryContext。");
            }
            else
            {
                // 已修復的註解。
                // 已修復的註解。
                // 已修復的註解。
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

            // 已修復的註解。
        }

        #endregion

        #region 錯誤處理

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected IActionResult HandleError(Exception exception, string methodName)
        {
            // 瀏覽器只可見固定訊息；原始例外可能含 CRM 端點或內部型別，僅供伺服器端診斷。
            const string safeUserMessage = "系統暫時無法完成操作，請稍後再試。";

            // 已修復的註解。
            string errorMessage = $"錯誤資訊：FullName = {GetType().FullName}, " +
                                $"Method = {methodName}, " +
                                $"Time = {DateTime.Now}, " +
                                $"Description = {exception}";

            // 已修復的註解。
            try
            {
                ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1, errorMessage);
            }
            catch (Exception traceEx)
            {
                // 已修復的註解。
                System.Diagnostics.Debug.WriteLine($"TraceByLevel 憭望?: {traceEx.Message}");
            }

            // 已修復的註解。
            SendLineErrorNotification(errorMessage);

            // 已修復的註解。
            bool isAjaxRequest = false;
            try
            {
                isAjaxRequest = Request?.Headers != null &&
                               Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            }
            catch
            {
                // 已修復的註解。
                isAjaxRequest = false;
            }

            if (isAjaxRequest)
            {
                // 已修復的註解。
                return Json(new
                {
                    status = "error",
                    message = safeUserMessage,
                    timestamp = DateTime.Now
                });
            }
            else
            {
                // 已修復的註解。
                // 長錯誤訊息不可塞進 route（會 404 / 斷字）。改放 TempData。
                StoreSafeErrorMessage(safeUserMessage);
                return RedirectToAction("DisplayErrorView", "Home");
            }
        }

        /// <summary>
        /// 將已去識別化的錯誤訊息寫入目前 request 的 TempData。
        ///
        /// <para>
        /// TempData 是 MVC request scope 的可選基礎設施；相容性轉送、背景觸發或測試
        /// 可能沒有可用 provider。寫入失敗時必須保留原始錯誤處理結果，不能以第二個
        /// NullReferenceException 覆蓋前一個例外，也不得把原始例外內容放入任何共享狀態。
        /// </para>
        /// </summary>
        /// <param name="safeUserMessage">已驗證為安全、可呈現給使用者的固定訊息。</param>
        private void StoreSafeErrorMessage(string safeUserMessage)
        {
            try
            {
                TempData["ErrorMessage"] = safeUserMessage;
            }
            catch (Exception tempDataException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Unable to store the safe error message in TempData: {tempDataException.GetType().Name}");
            }
        }

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected void SendLineErrorNotification(string errorMessage)
        {
            try
            {
                ChurchReportLineAdminNotificationService.NotifyDefaultError("BaseChurchController", errorMessage);
            }
            catch (Exception ex)
            {
                // 已修復的註解。
                try
                {
                    ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1,
                        $"LINE 通知傳送失敗：{ex.Message}");
                }
                catch
                {
                    // 已修復的註解。
                    System.Diagnostics.Debug.WriteLine($"LINE 通知處理發生例外：{ex.Message}");
                }
            }
        }

        #endregion

        #region ViewBag 設定輔助方法

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected void SetMultiGroupLayoutParameter()
        {
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            bool integrateFlag = IsIntegrateDataLoaded();

            // 已修復的註解。
            // 已修復的註解。
            if (displayViewType == "MultiGroupView" && !integrateFlag)
            {
                // 已修復的註解。
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
            }
            else if (displayViewType == "IntegrateView" && integrateFlag)
            {
                // 已修復的註解。
                ViewBag.MultiGroupIndex = "IntegrateView";
            }
            else if (displayViewType == "MultiGroupView" && integrateFlag)
            {
                // 已修復的註解。
                // 已修復的註解。
                ViewBag.MultiGroupIndex = "HybridView";
            }
            else
            {
                // 已修復的註解。
                // 已修復的註解。
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
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected bool IsIntegrateDataLoaded()
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
            return weeklyReport != null && weeklyReport.LoadFlag;
        }

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected void SetupBasicViewBag()
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;

            // 已修復的註解。
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
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected void SetupFeeDataListCount()
        {
            bool hasFeeData = InMemoryContext.FeeList.FeeDataList != null &&
                            InMemoryContext.FeeList.FeeDataList.Count > 0;

            ViewBag.FeeDataListCount = hasFeeData ? "已載入收費資料" : "尚未載入收費資料";
        }

        #endregion

        #region Session 安全性驗證

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected virtual void EnsureCorrectUserData()
        {
            try
            {
                // ========================================
                // 已修復的註解。
                // ========================================
                var sessionId = HttpContext?.Session?.Id;
                if (string.IsNullOrEmpty(sessionId))
                {
                     return; // Session 狀態已驗證。
                }

                // ========================================
                // 已修復的註解。
                // ========================================
                var sessionPassword = HttpContext?.Session?.GetString("_LoginPassword");
                var listManagerPassword = InMemoryContext?.ListManager?.m_Password;

                // 已修復的註解。
                if (string.IsNullOrEmpty(sessionPassword) && string.IsNullOrEmpty(listManagerPassword))
                {
                    return;
                }

                // 已修復的註解。
                var currentPasswordHash = GetStableHash(sessionPassword ?? listManagerPassword ?? "");
                var cacheKey = $"{sessionId}_{currentPasswordHash}";

                // ========================================
                // 已修復的註解。
                // ========================================
                if (_userValidationCache.TryGetValue(cacheKey, out var cached))
                {
                    var cacheAge = (DateTime.UtcNow - cached.LastValidated).TotalSeconds;

                    // 已修復的註解。
                    if (cacheAge < USER_VALIDATION_CACHE_SECONDS &&
                        cached.IsValid &&
                        cached.PasswordHash == currentPasswordHash)
                    {
                        // 已修復的註解。
                        return;
                    }
                }

                // ========================================
                // 已修復的註解。
                // ========================================
                var sessionAccount = HttpContext?.Session?.GetString("_LoginAccount");

                // 已修復的註解。
                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword == listManagerPassword)
                {
                    // 已修復的註解。
                    _userValidationCache[cacheKey] = (DateTime.UtcNow, true, currentPasswordHash);

                    // 已修復的註解。
                    CleanupOldCacheForSession(sessionId, cacheKey);
                    return;
                }

                // ========================================
                // 已修復的註解。
                // ========================================
#if DEBUG
                 System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] 使用者資料不存在，已重新初始化 ListManager。");
#endif

                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword != listManagerPassword)
                {
                    // 已修復的註解。
                    InMemoryContext.ListManager.SetupListManager(
                        sessionAccount ?? "",
                        sessionPassword,
                        InMemoryContext.ListManager.m_SelectDate != default
                            ? InMemoryContext.ListManager.m_SelectDate
                            : DateTime.Now);

                    // 已修復的註解。
                    var newPasswordHash = GetStableHash(sessionPassword);
                    var newCacheKey = $"{sessionId}_{newPasswordHash}";
                    _userValidationCache[newCacheKey] = (DateTime.UtcNow, true, newPasswordHash);

                    // 已修復的註解。
                    CleanupOldCacheForSession(sessionId, newCacheKey);
                    return;
                }

                // ========================================
                // 已修復的註解。
                // ========================================
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
                         System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] Session 資料與 LINE ID 不一致，已重新初始化。");
#endif

                        InMemoryContext.ListManager.SetupListManager(
                            "LineIdLogin",
                            passwordKey,
                            InMemoryContext.ListManager.m_SelectDate != default
                                ? InMemoryContext.ListManager.m_SelectDate
                                : DateTime.Now);

                        HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
                        HttpContext?.Session?.SetString("_LoginPassword", passwordKey);

                        // 已修復的註解。
                        var linePasswordHash = GetStableHash(passwordKey);
                        var lineCacheKey = $"{sessionId}_{linePasswordHash}";
                        _userValidationCache[lineCacheKey] = (DateTime.UtcNow, true, linePasswordHash);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                 System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] 驗證發生例外：{ex.Message}");
#endif
            }
        }

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        private static string GetStableHash(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "EMPTY";

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
                var hash = Convert.ToBase64String(bytes);
                return hash.Length > 8 ? hash.Substring(0, 8) : hash;
            }
        }

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        private static void CleanupOldCacheForSession(string sessionId, string currentCacheKey)
        {
            try
            {
                var keysToRemove = new System.Collections.Generic.List<string>();
                var now = DateTime.UtcNow;

                foreach (var kvp in _userValidationCache)
                {
                    // 已修復的註解。
                    if (kvp.Key.StartsWith(sessionId + "_") && kvp.Key != currentCacheKey)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                    // 已修復的註解。
                    else if ((now - kvp.Value.LastValidated).TotalMinutes > 5)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                // 已修復的註解。
                foreach (var key in keysToRemove)
                {
                    _userValidationCache.TryRemove(key, out _);
                }

#if DEBUG
                if (keysToRemove.Count > 0)
                {
                     System.Diagnostics.Debug.WriteLine($"[CleanupOldCache] 已移除 {keysToRemove.Count} 筆過期快取（Session={sessionId.Substring(0, Math.Min(8, sessionId.Length))}...）。");
                }
#endif
            }
            catch
            {
                // 已修復的註解。
            }
        }


        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// if (!ValidateSession())
        /// {
        ///     return RedirectToAction("Login", "Authentication");
        /// }
        /// </summary>
        protected bool ValidateSession()
        {
            try
            {
                // ========================================
                // 已修復的註解。
                // ========================================
                var sessionUserId = HttpContext.Session.GetString("_SessionUserId");
                if (string.IsNullOrEmpty(sessionUserId))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] Session user id is missing.");
                    return false;
                }

                // ========================================
                // 已修復的註解。
                // ========================================
                var sessionCreatedAt = HttpContext.Session.GetString("_SessionCreatedAt");
                if (!string.IsNullOrEmpty(sessionCreatedAt))
                {
                    if (DateTime.TryParse(sessionCreatedAt, out DateTime createdTime))
                    {
                        var sessionAge = DateTime.UtcNow - createdTime;
                        // 已修復的註解。
                        if (sessionAge.TotalHours > 8)
                        {
                             System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 已過期（{sessionAge.TotalHours:F2} 小時）。");
                            return false;
                        }
                    }
                }

                // ========================================
                // 已修復的註解。
                // ========================================
                // 已修復的註解。
                var currentAccount = InMemoryContext?.ListManager?.m_Account;
                if (string.IsNullOrEmpty(currentAccount))
                {
                 System.Diagnostics.Debug.WriteLine("[ValidateSession] 找不到目前的 InMemoryContext。");
                    return false;
                }

                 System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 驗證完成 - UserId: {sessionUserId}");
                return true;
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 驗證發生例外：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected void RegenerateSessionId()
        {
            try
            {
                // 已修復的註解。
                var userId = HttpContext.Session.GetString("_SessionUserId");
                var userAgent = HttpContext.Session.GetString("_SessionUserAgent");
                var realIp = HttpContext.Session.GetString("_SessionRealIp");

                // 已修復的註解。
                HttpContext.Session.Clear();

                // 已修復的註解。
                HttpContext.Session.CommitAsync().GetAwaiter().GetResult();

                // 已修復的註解。
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
                 System.Diagnostics.Debug.WriteLine($"[RegenerateSessionId] 重新建立 Session 識別碼時發生例外：{ex.Message}");
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

        #endregion

        #region 連線集區操作

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// using (var connection = GetConnection())
        /// {
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected IOrganizationService GetConnection()
        {
            try
            {
                if (_connectionPool == null)
                {
                    throw new InvalidOperationException("CRM connection pool is not initialized.");
                }

                var connection = _connectionPool.AcquireConnection();

                if (connection == null)
                {
                    throw new InvalidOperationException("CRM connection pool returned a null connection.");
                }

#if DEBUG
                if (ChurchReport.Diagnostics.Profiling.ProfilingSwitch.Enabled)
                {
                    var httpAccessor = HttpContext?.RequestServices?
                        .GetService(typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor))
                        as Microsoft.AspNetCore.Http.IHttpContextAccessor;
                    if (httpAccessor != null
                        && connection is not ChurchReport.Diagnostics.Profiling.TimedOrganizationService)
                    {
                        connection = new ChurchReport.Diagnostics.Profiling.TimedOrganizationService(connection, httpAccessor);
                    }
                }
#endif

                return connection;
            }
            catch (TimeoutException)
            {
                // 已修復的註解。
                 System.Diagnostics.Debug.WriteLine($"[GetConnection] 等候 CRM 連線逾時。");
                throw;
            }
            catch (Exception ex)
            {
                // 已修復的註解。
                 System.Diagnostics.Debug.WriteLine($"[GetConnection] 取得 CRM 連線時發生例外：{ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        protected void ReleaseConnection(IOrganizationService connection)
        {
            try
            {
                if (connection == null)
                {
                    // 已修復的註解。
                    return;
                }

                if (_connectionPool == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ReleaseConnection] CRM connection pool is not initialized.");
                    return;
                }

#if DEBUG
                if (connection is ChurchReport.Diagnostics.Profiling.TimedOrganizationService timedConnection)
                {
                    connection = timedConnection.Inner;
                }
#endif

                _connectionPool.ReleaseConnection(connection);
            }
            catch (Exception ex)
            {
                // 已修復的註解。
                 System.Diagnostics.Debug.WriteLine($"[ReleaseConnection] 歸還 CRM 連線時發生例外：{ex.Message}");

                // 已修復的註解。
                try
                {
                    ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1,
                        $"歸還 CRM 連線時發生例外：{ex.Message}");
                }
                catch
                {
                    // 已修復的註解。
                }
            }
        }

        /// <summary>
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
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
                 System.Diagnostics.Debug.WriteLine($"[GetConnectionPoolStats] 取得連線集區統計時發生例外：{ex.Message}");

                // 已修復的註解。
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
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        /// 已修復的說明。
        ///
        /// 已修復的說明。
        /// 已修復的說明。
        /// </summary>
        public new void Dispose()
        {
            // 已修復的註解。
            // Controller 僅結束自己的 MVC 狀態；ToolUtility 屬 Provider/Factory，禁止在此釋放。
            // 各 CRM lease 必須由借用方法的 finally 歸還，避免跨請求保留連線或 session 狀態。
            base.Dispose();
        }

        #endregion
    }
}
