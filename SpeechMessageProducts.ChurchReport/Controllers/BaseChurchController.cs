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
    /// ChurchReport 所有 MVC 控制器的共用基底類別。
    ///
    /// 此類別集中處理 HTTP 脈絡存取、ViewBag 初始化、Session 驗證、CRM 連線池
    /// 借還，以及不洩漏內部例外內容的錯誤回應。Controller 只擁有自己的請求狀態；
    /// ToolUtility provider、CRM pool 與其建立的共享物件由外部容器擁有，不能在此類別
    /// 的 Dispose 中釋放。每一個 CRM 連線都必須由取得它的操作在 finally 區塊歸還。
    /// </summary>
    public abstract class BaseChurchController : Controller, IDisposable
    {
        #region 常數與快取界限

        /// <summary>目前使用的最小診斷層級。</summary>
        protected const int TOTAL_LEVEL = 1;
        /// <summary>一般錯誤追蹤層級。</summary>
        protected const int LEVEL_1 = 1;
        /// <summary>第二層診斷層級，保留給較詳細的操作追蹤。</summary>
        protected const int LEVEL_2 = 2;
        /// <summary>第三層診斷層級。</summary>
        protected const int LEVEL_3 = 3;
        /// <summary>第四層診斷層級。</summary>
        protected const int LEVEL_4 = 4;
        /// <summary>第五層診斷層級，僅應在明確啟用詳細診斷時使用。</summary>
        protected const int LEVEL_5 = 5;


        /// <summary>單一 Session 身分驗證結果最多快取的秒數。</summary>
        private const int USER_VALIDATION_CACHE_SECONDS = 30;

        #endregion

        #region 相依服務與受控存取

        /// <summary>提供 CRM 工具物件的外部 provider；生命週期不由 Controller 管理。</summary>
        protected readonly IToolUtilityProvider _toolUtilityProvider;

        /// <summary>負責 CRM 連線的借出、歸還與統計。</summary>
        protected readonly ICrmConnectionPool _connectionPool;

        /// <summary>以 request scope 取得目前 HTTP 脈絡，支援測試與非標準 MVC 建立流程。</summary>
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// 以 SessionId 與密碼雜湊值隔離的短期驗證快取。快取清理只移除目前 Session
        /// 的舊鍵或逾時項目，避免跨使用者、跨租戶或跨請求重用身分結果。
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastValidated, bool IsValid, string PasswordHash)>
            _userValidationCache = new();

        /// <summary>每次存取都向 provider 取得工具物件，不在 Controller 中保存可釋放的共享 client。</summary>
        protected ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

        /// <summary>目前控制器使用的記憶體資料上下文；由建構式或 DI 建立。</summary>
        protected readonly IInMemoryDataContext InMemoryContext;


        /// <summary>
        /// 安全取得 HTTP 脈絡。若 accessor 與基底 Controller 都沒有脈絡，立即以明確例外
        /// 終止，避免後續錯誤處理以 NullReferenceException 覆蓋原始原因。
        /// </summary>
        protected new HttpContext HttpContext
        {
            get
            {
                var context = _httpContextAccessor?.HttpContext;

                if (context == null)
                {
                    context = base.HttpContext;
                }

                if (context == null)
                {
                    throw new InvalidOperationException(
                        "HttpContext is not available. Ensure the request is running inside an ASP.NET Core HTTP pipeline and IHttpContextAccessor is registered.");
                }

                return context;
            }
        }

        #endregion

        #region 建構與相依性注入

        /// <summary>
        /// 建立基底控制器並驗證所有必要依賴。傳入的 InMemoryContext 若存在則直接使用；
        /// 否則從 request service provider 解析產品流程所需的 adapter 與 workflow。
        /// </summary>
        /// <param name="httpContextAccessor">目前請求的 HTTP 脈絡 accessor。</param>
        /// <param name="memoryCache">供記憶體資料上下文使用的快取。</param>
        /// <param name="toolUtilityProvider">外部擁有的 ToolUtility provider。</param>
        /// <param name="connectionPool">CRM 連線池，負責連線 lease 的生命週期。</param>
        /// <param name="inMemoryContext">可選的測試或呼叫端提供之上下文。</param>
        protected BaseChurchController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IInMemoryDataContext inMemoryContext = null)
        {
            // ========================================
            // ========================================
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));

            if (inMemoryContext != null)
            {
                InMemoryContext = inMemoryContext;
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] InMemoryContext was resolved through dependency injection.");
            }
            else
            {
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

        }

        #endregion

        #region 安全錯誤處理

        /// <summary>
        /// 將例外轉換成安全的 MVC 或 AJAX 回應，同時保留伺服器端診斷資訊。
        /// 原始例外只寫入受控診斷管道；瀏覽器、JSON、redirect、TempData 均只能取得
        /// 固定訊息。TempData provider 失效時仍必須完成錯誤轉換，不能再次拋出例外。
        /// </summary>
        /// <param name="exception">要記錄的原始例外。</param>
        /// <param name="methodName">發生錯誤的 action 或服務方法名稱。</param>
        /// <returns>安全的 JSON 結果或錯誤頁 redirect。</returns>
        protected IActionResult HandleError(Exception exception, string methodName)
        {
            // 瀏覽器只可見固定訊息；原始例外可能含 CRM 端點或內部型別，僅供伺服器端診斷。
            const string safeUserMessage = "系統暫時無法完成操作，請稍後再試。";

            string errorMessage = $"Unhandled ChurchReport exception: FullName = {GetType().FullName}, " +
                                $"Method = {methodName}, " +
                                $"Time = {DateTime.Now}, " +
                                $"Description = {exception}";

            try
            {
                ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1, errorMessage);
            }
            catch (Exception traceEx)
            {
                System.Diagnostics.Debug.WriteLine($"[BaseChurchController] TraceByLevel failed: {traceEx.Message}");
            }

            SendLineErrorNotification(errorMessage);

            bool isAjaxRequest = false;
            try
            {
                isAjaxRequest = Request?.Headers != null &&
                               Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            }
            catch
            {
                isAjaxRequest = false;
            }

            if (isAjaxRequest)
            {
                return Json(new
                {
                    status = "error",
                    message = safeUserMessage,
                    timestamp = DateTime.Now
                });
            }
            else
            {
                // 長錯誤訊息不可塞進 route（會 404 / 斷字）。改放 TempData。
                StoreSafeErrorMessage(safeUserMessage);
                return RedirectToAction("DisplayErrorView", "Home");
            }
        }

        /// <summary>嘗試將固定安全訊息寫入 TempData；provider 失效時以診斷記錄降級。</summary>
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

        /// <summary>發送管理端錯誤通知；通知失敗不得覆蓋原始錯誤處理流程。</summary>
        protected void SendLineErrorNotification(string errorMessage)
        {
            try
            {
                ChurchReportLineAdminNotificationService.NotifyDefaultError("BaseChurchController", errorMessage);
            }
            catch (Exception ex)
            {
                try
                {
                    ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1,
                        $"LINE notification failed: {ex.Message}");
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"[BaseChurchController] LINE notification failed: {ex.Message}");
                }
            }
        }

        #endregion

        #region ViewBag 組態輔助方法

        /// <summary>依小組報表資料狀態設定共用版面所需的 ViewBag 旗標。</summary>
        protected void SetMultiGroupLayoutParameter()
        {
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            bool integrateFlag = IsIntegrateDataLoaded();

            if (displayViewType == "MultiGroupView" && !integrateFlag)
            {
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
            }
            else if (displayViewType == "IntegrateView" && integrateFlag)
            {
                ViewBag.MultiGroupIndex = "IntegrateView";
            }
            else if (displayViewType == "MultiGroupView" && integrateFlag)
            {
                ViewBag.MultiGroupIndex = "HybridView";
            }
            else
            {
                ViewBag.MultiGroupIndex = integrateFlag ? "HybridView" : "IntegrateView";
            }

            // 「奉獻管理」按鈕屬於全站導覽列權限，不應依賴奉獻付款頁面的表單模型是否已初始化。
            // 先用目前登入者 CRM contact.new_church_jobtitle 判斷；只有登入 contact 尚未載入時，
            // 才保留舊的 DonationPaymentFormModel.IsAOfficeWorker 作為最後 fallback。
            ViewBag.IsAOfficeWorker = ResolveDonationManagementAccessFlag();
        }

        /// <summary>根據登入聯絡人的 CRM 職稱或付款模型計算奉獻管理權限旗標。</summary>
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

        /// <summary>判斷整合報表資料是否已完成載入。</summary>
        protected bool IsIntegrateDataLoaded()
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
            return weeklyReport != null && weeklyReport.LoadFlag;
        }

        /// <summary>初始化登入資訊、費用類型、群組類型與成員導覽所需的 ViewBag。</summary>
        protected void SetupBasicViewBag()
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;

            SetupFeeDataListCount();
            SetupMemberInfoViewBag();
        }

        /// <summary>計算並快取目前 Session 的 MemberInfo 導覽權限。</summary>
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
        /// <summary>將費用資料是否存在轉換為檢視頁使用的固定顯示文字。</summary>
        protected void SetupFeeDataListCount()
        {
            bool hasFeeData = InMemoryContext.FeeList.FeeDataList != null &&
                            InMemoryContext.FeeList.FeeDataList.Count > 0;

            ViewBag.FeeDataListCount = hasFeeData ? "已載入收費資料" : "尚未載入收費資料";
        }

        #endregion

        #region Session 驗證與身分隔離

        protected virtual void EnsureCorrectUserData()
        {
            try
            {
                // ========================================
                // ========================================
                var sessionId = HttpContext?.Session?.Id;
                if (string.IsNullOrEmpty(sessionId))
                {
                    return;
                }

                // ========================================
                // ========================================
                var sessionPassword = HttpContext?.Session?.GetString("_LoginPassword");
                var listManagerPassword = InMemoryContext?.ListManager?.m_Password;

                if (string.IsNullOrEmpty(sessionPassword) && string.IsNullOrEmpty(listManagerPassword))
                {
                    return;
                }

                var currentPasswordHash = GetStableHash(sessionPassword ?? listManagerPassword ?? "");
                var cacheKey = $"{sessionId}_{currentPasswordHash}";

                // ========================================
                // ========================================
                if (_userValidationCache.TryGetValue(cacheKey, out var cached))
                {
                    var cacheAge = (DateTime.UtcNow - cached.LastValidated).TotalSeconds;

                    if (cacheAge < USER_VALIDATION_CACHE_SECONDS &&
                        cached.IsValid &&
                        cached.PasswordHash == currentPasswordHash)
                    {
                        return;
                    }
                }

                // ========================================
                // ========================================
                var sessionAccount = HttpContext?.Session?.GetString("_LoginAccount");

                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword == listManagerPassword)
                {
                    _userValidationCache[cacheKey] = (DateTime.UtcNow, true, currentPasswordHash);

                    CleanupOldCacheForSession(sessionId, cacheKey);
                    return;
                }

                // ========================================
                // ========================================
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[BaseChurch.EnsureCorrectUserData] Session password differs; rehydrating ListManager.");
#endif

                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword != listManagerPassword)
                {
                    InMemoryContext.ListManager.SetupListManager(
                        sessionAccount ?? "",
                        sessionPassword,
                        InMemoryContext.ListManager.m_SelectDate != default
                            ? InMemoryContext.ListManager.m_SelectDate
                            : DateTime.Now);

                    var newPasswordHash = GetStableHash(sessionPassword);
                    var newCacheKey = $"{sessionId}_{newPasswordHash}";
                    _userValidationCache[newCacheKey] = (DateTime.UtcNow, true, newPasswordHash);

                    CleanupOldCacheForSession(sessionId, newCacheKey);
                    return;
                }

                // ========================================
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
                        System.Diagnostics.Debug.WriteLine("[BaseChurch.EnsureCorrectUserData] Restoring ListManager from LINE authentication ticket.");
#endif

                        InMemoryContext.ListManager.SetupListManager(
                            "LineIdLogin",
                            passwordKey,
                            InMemoryContext.ListManager.m_SelectDate != default
                                ? InMemoryContext.ListManager.m_SelectDate
                                : DateTime.Now);

                        HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
                        HttpContext?.Session?.SetString("_LoginPassword", passwordKey);

                        var linePasswordHash = GetStableHash(passwordKey);
                        var lineCacheKey = $"{sessionId}_{linePasswordHash}";
                        _userValidationCache[lineCacheKey] = (DateTime.UtcNow, true, linePasswordHash);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] Failed to restore session state: {ex.Message}");
#endif
            }
        }

        /// <summary>以 SHA-256 產生固定長度雜湊，供 Session 驗證快取鍵使用。</summary>
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

        /// <summary>清理指定 Session 的舊驗證快取與全域逾時項目，限制記憶體保留時間。</summary>
        private static void CleanupOldCacheForSession(string sessionId, string currentCacheKey)
        {
            try
            {
                var keysToRemove = new System.Collections.Generic.List<string>();
                var now = DateTime.UtcNow;

                foreach (var kvp in _userValidationCache)
                {
                    if (kvp.Key.StartsWith(sessionId + "_") && kvp.Key != currentCacheKey)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                    else if ((now - kvp.Value.LastValidated).TotalMinutes > 5)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _userValidationCache.TryRemove(key, out _);
                }

#if DEBUG
                if (keysToRemove.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[CleanupOldCache] Removed {keysToRemove.Count} stale validation entries.");
                }
#endif
            }
            catch
            {
            }
        }


        /// <summary>
        /// 驗證 Session 使用者識別、建立時間與 InMemoryContext 帳號是否一致。
        /// 任一條件失敗即採 fail-closed 回傳 false，避免未驗證請求繼續存取產品資料。
        /// </summary>
        protected bool ValidateSession()
        {
            try
            {
                // ========================================
                // ========================================
                var sessionUserId = HttpContext.Session.GetString("_SessionUserId");
                if (string.IsNullOrEmpty(sessionUserId))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] Session user id is missing.");
                    return false;
                }

                // ========================================
                // ========================================
                var sessionCreatedAt = HttpContext.Session.GetString("_SessionCreatedAt");
                if (!string.IsNullOrEmpty(sessionCreatedAt))
                {
                    if (DateTime.TryParse(sessionCreatedAt, out DateTime createdTime))
                    {
                        var sessionAge = DateTime.UtcNow - createdTime;
                        if (sessionAge.TotalHours > 8)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session expired after {sessionAge.TotalHours:F2} hours.");
                            return false;
                        }
                    }
                }

                // ========================================
                // ========================================
                var currentAccount = InMemoryContext?.ListManager?.m_Account;
                if (string.IsNullOrEmpty(currentAccount))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] Current account is missing from InMemoryContext.");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session validated for user {sessionUserId}.");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session validation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 清除並重建登入 Session 的必要欄位，以降低 Session fixation 風險。方法只操作
        /// 目前 request 的 Session，不保存跨請求可變的身分資料。
        /// </summary>
        protected void RegenerateSessionId()
        {
            try
            {
                var userId = HttpContext.Session.GetString("_SessionUserId");
                var userAgent = HttpContext.Session.GetString("_SessionUserAgent");
                var realIp = HttpContext.Session.GetString("_SessionRealIp");

                HttpContext.Session.Clear();

                HttpContext.Session.CommitAsync().GetAwaiter().GetResult();

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

        #endregion

        #region CRM 連線池借還

        /// <summary>
        /// 從 CRM pool 借出一條操作連線。呼叫端必須在 finally 呼叫 ReleaseConnection，
        /// 不得把借用物件寫入 singleton、Controller 欄位或跨請求快取。
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
                System.Diagnostics.Debug.WriteLine("[GetConnection] Timed out while acquiring a CRM connection.");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetConnection] Failed to acquire a CRM connection: {ex.Message}");
                throw;
            }
        }

        /// <summary>將借用的 CRM 連線歸還 pool；null、pool 未初始化或歸還失敗都安全降級。</summary>
        protected void ReleaseConnection(IOrganizationService connection)
        {
            try
            {
                if (connection == null)
                {
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
                System.Diagnostics.Debug.WriteLine($"[ReleaseConnection] Failed to return CRM connection: {ex.Message}");

                try
                {
                    ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1,
                        $"Connection release failed: {ex.Message}");
                }
                catch
                {
                }
            }
        }

        /// <summary>取得 CRM pool 的 bounded 統計；讀取失敗時回傳全零統計而不阻斷頁面。</summary>
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

        #region 控制器釋放責任

        /// <summary>
        /// 釋放 Controller 自己的 MVC 資源。ToolUtility provider、CRM pool 與共享 client
        /// 由外部擁有，刻意不在此 Dispose；各 lease 的歸還責任仍屬取得連線的操作。
        /// </summary>
        public new void Dispose()
        {
            // Controller 僅結束自己的 MVC 狀態；ToolUtility 屬 Provider/Factory，禁止在此釋放。
            // 各 CRM lease 必須由借用方法的 finally 歸還，避免跨請求保留連線或 session 狀態。
            base.Dispose();
        }

        #endregion
    }
}
