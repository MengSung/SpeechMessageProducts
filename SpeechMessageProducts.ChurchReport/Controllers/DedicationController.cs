// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/DedicationController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class DedicationController
// 主要成員：DonationPaymentView、IsWebLogin、RestoreWebLoginDonationPaymentModel、RestoreWebLoginDonationPaymentModelFromSession、SetupDonationPaymentViewBag、SaveDonationPaymentDedication、LoadCreditCardList、DeleteCreditCard、LoadDedicationBookingList、DeleteDedicationBooking
// 引用命名空間：ChurchReport.Models、ChurchReport.Payments、ChurchReport.Tools、DevExtreme.AspNet.Data、DevExtreme.AspNet.Mvc、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 奉獻管理控制器
    /// 處理線上奉獻付款相關功能。
    /// 真正使用哪一家金流由共用 payment core 與 appsettings profile 決定，Controller 只負責 ChurchReport 的畫面、CRM 與 Session 流程。
    /// </summary>
    public class DedicationController : BaseChurchController
    {
        #region 私有欄位

        private readonly IConfiguration _configuration;

        #endregion

        #region 建構函式

        public DedicationController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IConfiguration configuration)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
            _configuration = configuration;
        }

        #endregion

        #region 奉獻主頁面 (Line 或網頁登入)

        /// <summary>
        /// 奉獻付款主頁面。
        ///
        /// `/Dedication/DonationPaymentView/{LineId}` 是新的中性入口；
        /// `/Dedication/QPayView/{LineId}` 僅保留給舊 LINE 連結與既有書籤使用，不代表此 action 只服務永豐 QPay。
        /// </summary>
        /// <param name="LineId">LINE 使用者 ID (若從 LINE 進入)</param>
        [Route("/Dedication/DonationPaymentView/{LineId}")]
        [Route("/Dedication/QPayView/{LineId}")]
        public async Task<IActionResult> DonationPaymentView(string LineId)
        {
            try
            {
                // 處理 LINE 登入
                if (!string.IsNullOrEmpty(LineId) && !IsWebLogin(LineId))
                {
                    HttpContext.Session.Remove(DonationPaymentSessionKeys.WebLoginContactId);

                    // SetupUserLineId 會查詢 LINE 使用者對應的 CRM 連絡人，並填入
                    // DonationPaymentManager.m_DonationPaymentFormModel 的奉獻類別、付款方式、奉獻編號等資料。
                    // 必須等待它完成後再產生 View，否則畫面會先 render 出空白下拉選單。
                    await SetupUserLineId(LineId, "", "", "");
                }
                else if (IsWebLogin(LineId))
                {
                    RestoreWebLoginDonationPaymentModel();
                }

                SetupDonationPaymentViewBag();

                DonationPaymentFormModel donationPaymentFormModel = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel ?? new DonationPaymentFormModel();
                // DonationPaymentManager.m_DonationPaymentFormModel 是長生命週期狀態，可能曾被舊流程或失敗的 CRM 查詢清空欄位。
                // 在 render 前統一補齊表單必要預設值，避免 DevExtreme 下拉選單顯示空白。
                donationPaymentFormModel.EnsureFormDefaults();
                InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel = donationPaymentFormModel;

                return View("~/Views/Dedication/DonationPaymentView.cshtml", donationPaymentFormModel);
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(DonationPaymentView));
            }
        }

        /// <summary>
        /// 判斷目前路由是否為官網網頁奉獻登入。
        ///
        /// LINE 入口會把 LINE user id 放在同一個 route segment，因此這裡必須精準比對
        /// 「網頁登入」，不能用空字串或其他寬鬆判斷，避免把 LINE 使用者誤當成網頁登入。
        /// </summary>
        private static bool IsWebLogin(string lineId)
        {
            return string.Equals(lineId, "網頁登入", StringComparison.Ordinal);
        }

        /// <summary>
        /// 在官網網頁登入流程中，必要時重新建立奉獻頁模型。
        ///
        /// 根因說明：
        /// DonationPaymentLoginController 在 AJAX POST 中已經呼叫 SetDonationPaymentModel(contact)，
        /// 但瀏覽器接著會 redirect 到奉獻頁，DevExtreme Grid 又會再送 AJAX 請求。
        /// 如果這些請求讀到不同的 DonationPaymentManager memory-cache key，畫面就會拿到空模型，
        /// 導致姓名、奉獻編號與信用卡清單一起消失。
        ///
        /// 修補策略：
        /// - 不建立假姓名或假奉獻編號。
        /// - 優先沿用目前 manager 已有的 m_Contact。
        /// - 若 manager 是空的，使用登入成功時存在 ASP.NET Session 的 CRM contact id 重新讀取 contact。
        /// - 最後才嘗試 PersonalInfomationModel 的登入 contact。
        /// </summary>
        private void RestoreWebLoginDonationPaymentModel()
        {
            var manager = InMemoryContext.DonationPaymentManager;
            var donationPaymentFormModel = manager.m_DonationPaymentFormModel ?? new DonationPaymentFormModel();

            if (!donationPaymentFormModel.NeedsDonorIdentityRestore())
            {
                manager.m_DonationPaymentFormModel = donationPaymentFormModel;
                return;
            }

            if (manager.m_Contact != null)
            {
                manager.SetDonationPaymentModel(manager.m_Contact);
                return;
            }

            if (RestoreWebLoginDonationPaymentModelFromSession(manager))
            {
                return;
            }

            var loginContact = InMemoryContext.PersonalInfomationModel?.m_LoginContact;
            if (loginContact != null)
            {
                manager.SetDonationPaymentModel(loginContact);
            }
        }

        /// <summary>
        /// 使用 Session 保存的 CRM contact id 重新初始化 DonationPaymentManager。
        ///
        /// 這個方法只在產品層 Controller 使用，因為它同時碰到 ASP.NET Session、CRM 與畫面模型；
        /// 這些責任都不屬於抽離後的 SpeechMessage.Payments 金流核心。
        /// </summary>
        private bool RestoreWebLoginDonationPaymentModelFromSession(DonationPaymentManager manager)
        {
            var contactIdText = HttpContext.Session.GetString(DonationPaymentSessionKeys.WebLoginContactId);
            if (string.IsNullOrWhiteSpace(contactIdText))
            {
                return false;
            }

            if (!Guid.TryParse(contactIdText, out var contactId))
            {
                HttpContext.Session.Remove(DonationPaymentSessionKeys.WebLoginContactId);
                return false;
            }

            try
            {
                Entity contact = ToolUtility.RetrieveEntity("contact", contactId);
                if (contact == null)
                {
                    return false;
                }

                manager.SetDonationPaymentModel(contact);
                InMemoryContext.PersonalInfomationModel.m_LoginContact = contact;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DedicationController] 網頁奉獻登入模型恢復失敗，ContactId={contactId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 設定奉獻頁面的 ViewBag
        /// </summary>
        private void SetupDonationPaymentViewBag()
        {
            if (InMemoryContext.DonationPaymentManager.LoginType == "網頁登入")
            {
                // 網頁登入 - 使用完整選單
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();
            }
            else
            {
                // LINE 登入 - 簡化選單
                ViewBag.LoginType = "小組長";
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = "行政同工";
                ViewBag.DedicationType = "奉獻管理";
                ViewBag.DedicationFlag = "奉獻";
                ViewBag.IsAOfficeWorker = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.IsAOfficeWorker ? "是的" : "否";
            }
        }

        #endregion

        #region 奉獻交易處理

        /// <summary>
        /// 儲存奉獻交易
        /// 建立奉獻記錄並導向金流頁面
        /// </summary>
        /// <param name="DonationPaymentFormModel">奉獻資料模型</param>
        [HttpPost]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [Route("/Dedication/SaveDonationPaymentDedication")]
        public async Task<IActionResult> SaveDonationPaymentDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                return await InMemoryContext.DonationPaymentManager.SaveDonationPaymentDedicationAsync(DonationPaymentFormModel);
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(SaveDonationPaymentDedication));
            }
        }

        #endregion

        #region 信用卡管理

        /// <summary>
        /// 載入使用者的信用卡清單
        /// </summary>
        /// <param name="id">使用者ID</param>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public object LoadCreditCardList(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ========================================
                // ✅ 使用基底控制器的統一驗證方法
                // ========================================
                // 教學說明：
                // 基底控制器已經提供了完整的驗證方法，包含：
                // - Session 和 ListManager 密碼一致性檢查
                // - LINE ID 恢復機制
                // - 安全的日誌記錄（隱藏敏感資訊）
                // 信用卡的 CCTOKEN 非常重要敏感，絕對不能使用CACHE

                EnsureCorrectUserData();
                RestoreWebLoginDonationPaymentModel();

                // ✅ 檢查金流提供商 - 高鉅金流不需要載入信用卡列表
                var payProvider = _configuration["PAY_PROVIDER"];
                if (payProvider == "高鉅金流")
                {
                    // 高鉅金流不支援信用卡記憶功能，返回空集合
                    return DataSourceLoader.Load(
                        System.Linq.Enumerable.Empty<object>().AsQueryable(),
                        loadOptions
                    );
                }

                // 永豐金流或台新金流 - 載入信用卡列表
                var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.CreditCardList
                    ?? System.Linq.Enumerable.Empty<object>();

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadCreditCardList");
            }
        }

        /// <summary>
        /// 刪除信用卡
        /// </summary>
        /// <param name="key">信用卡 Token</param>
        [HttpDelete]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public void DeleteCreditCard(string key)
        {
            try
            {
                var creditCard = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.CreditCardList
                    .First(a => a.CCToken == key);

                InMemoryContext.DonationPaymentManager.DeleteCreditCard(creditCard);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteCreditCard");
            }
        }

        #endregion

        #region 認獻管理

        /// <summary>
        /// 載入認獻清單
        /// </summary>
        /// <param name="id">使用者ID</param>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        public object LoadDedicationBookingList(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ========================================
                // ✅ 使用基底控制器的統一驗證方法
                // ========================================
                // 教學說明：
                // 基底控制器已經提供了完整的驗證方法，包含：
                // - Session 和 ListManager 密碼一致性檢查
                // - LINE ID 恢復機制
                // - 安全的日誌記錄（隱藏敏感資訊）
                EnsureCorrectUserData();
                RestoreWebLoginDonationPaymentModel();

                // ✅ 檢查金流提供商 - 高鉅金流不需要載入認獻清單（不支援定期定額功能）
                var payProvider = _configuration["PAY_PROVIDER"];
                if (payProvider == "高鉅金流")
                {
                    // 高鉅金流不支援定期定額扣款功能，返回空集合
                    return DataSourceLoader.Load(
                        System.Linq.Enumerable.Empty<object>().AsQueryable(),
                        loadOptions
                    );
                }

                // 永豐金流或台新金流 - 載入認獻清單
                var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.DedicationBookingList
                    ?? System.Linq.Enumerable.Empty<object>();

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadDedicationBookingList");
            }
        }

        /// <summary>
        /// 取消認獻
        /// </summary>
        /// <param name="key">認獻實體ID</param>
        [HttpDelete]
        public void DeleteDedicationBooking(string key)
        {
            try
            {
                var dedicationBooking = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.DedicationBookingList
                    .First(a => a.EntityId == key);

                InMemoryContext.DonationPaymentManager.DeleteDedicationBooking(dedicationBooking);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteDedicationBooking");
            }
        }

        #endregion

        #region 奉獻收費清單

        /// <summary>
        /// 奉獻收費清單頁面 (LINE 登入)
        /// </summary>
        [Route("/Dedication/DedicationFeeView")]
        public IActionResult DedicationFeeView()
        {
            try
            {
                SetupDedicationFeeViewBag(false);

                return View(InMemoryContext.DonationPaymentManager.SetDedicationFeeList(
                    InMemoryContext.LineBindingViewModel.LineUserId));
            }
            catch (Exception e)
            {
                return HandleError(e, "DedicationFeeView");
            }
        }

        /// <summary>
        /// 奉獻收費清單頁面 (網頁登入)
        /// </summary>
        [Route("/Dedication/DedicationFeeViewWeb")]
        public IActionResult DedicationFeeViewWeb()
        {
            try
            {
                SetupDedicationFeeViewBag(true);

                // 還原使用者選的查詢日期（SetupDedicationFeeViewBag 期間的 model 重新載入可能已把日期重設成今年），
                // 必須在 SetDedicationFeeList 之前還原，才能依「收費日期(new_pay_date)」正確過濾跨年度紀錄。
                RestoreDedicationQueryDatesFromSession();

                return View(InMemoryContext.DonationPaymentManager.SetDedicationFeeList(
                    InMemoryContext.DonationPaymentManager.m_Contact));
            }
            catch (Exception e)
            {
                return HandleError(e, "DedicationFeeViewWeb");
            }
        }

        /// <summary>
        /// 設定奉獻收費清單的 ViewBag
        /// </summary>
        /// <param name="isWebLogin">是否為網頁登入</param>
        private void SetupDedicationFeeViewBag(bool isWebLogin)
        {
            if (isWebLogin)
            {
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();
            }
            else
            {
                ViewBag.LoginType = "小組長";
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = "行政同工";
                ViewBag.DedicationType = "奉獻管理";
                ViewBag.IsAOfficeWorker = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.IsAOfficeWorker ? "是的" : "否";
            }
        }

        /// <summary>
        /// 從 Session 還原使用者選擇的奉獻查詢日期區間（若有）。
        /// 用於避免頁面重新載入時 SetDonationPaymentModel 把查詢日期重設成「今年 1/1 ~ 今天」，
        /// 確保奉獻收費清單依使用者選的收費日期(new_pay_date)區間查詢，能正確顯示跨年度紀錄。
        /// </summary>
        private void RestoreDedicationQueryDatesFromSession()
        {
            var savedStart = HttpContext.Session.GetString("DedicationFeeQueryStart");
            var savedEnd = HttpContext.Session.GetString("DedicationFeeQueryEnd");

            if (DateTime.TryParse(savedStart, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var queryStart))
            {
                InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.QueryStartDate = queryStart;
            }
            if (DateTime.TryParse(savedEnd, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var queryEnd))
            {
                InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.QueryEndDate = queryEnd;
            }
        }

        /// <summary>
        /// 更新奉獻收費清單查詢日期
        /// </summary>
        /// <param name="aDonationPaymentFormModel">查詢條件</param>
        [HttpPost]
        public async Task<IActionResult> UpdateDedicationFeeView(DonationPaymentFormModel aDonationPaymentFormModel)
        {
            try
            {
                InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.QueryStartDate = aDonationPaymentFormModel.QueryStartDate;
                InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.QueryEndDate = aDonationPaymentFormModel.QueryEndDate;

                // 將使用者選的查詢日期存進 Session：頁面重新載入時 SetDonationPaymentModel 會把日期重設成「今年 1/1 ~ 今天」，
                // 導致跨年度（例如 2025）的紀錄查不到。存進 Session 後，於 GET 重新還原，確保依使用者選的「收費日期」區間查詢。
                HttpContext.Session.SetString("DedicationFeeQueryStart", aDonationPaymentFormModel.QueryStartDate.ToString("o"));
                HttpContext.Session.SetString("DedicationFeeQueryEnd", aDonationPaymentFormModel.QueryEndDate.ToString("o"));

                return Json(new { status = "1", message = "成功更新查詢日期!" });
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateDedicationFeeView");
            }
        }

        #endregion

        #region 行政人員奉獻管理

        /// <summary>
        /// 行政人員手動輸入奉獻頁面 (LINE)
        /// </summary>
        [Route("/Dedication/KeyInDedicationFeeView")]
        public IActionResult KeyInDedicationFeeView()
        {
            try
            {
                SetupKeyInViewBag(false);

                return View(InMemoryContext.DonationPaymentManager.SetDedicationFeeList(
                    InMemoryContext.LineBindingViewModel.LineUserId));
            }
            catch (Exception e)
            {
                return HandleError(e, "KeyInDedicationFeeView");
            }
        }

        /// <summary>
        /// 行政人員手動輸入奉獻頁面 (網頁)
        /// </summary>
        [Route("/Dedication/KeyInDedicationFeeViewWeb")]
        public IActionResult KeyInDedicationFeeViewWeb()
        {
            try
            {
                SetupKeyInViewBag(true);

                return View(InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "KeyInDedicationFeeViewWeb");
            }
        }

        /// <summary>
        /// 設定手動輸入頁面的 ViewBag
        /// </summary>
        private void SetupKeyInViewBag(bool isWebLogin)
        {
            if (isWebLogin)
            {
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();
            }
            else
            {
                ViewBag.LoginType = "小組長";
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = "行政同工";
                ViewBag.DedicationType = "奉獻管理";
                ViewBag.IsAOfficeWorker = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.IsAOfficeWorker ? "是的" : "否";
            }
        }

        /// <summary>
        /// 儲存手動輸入的奉獻記錄
        /// </summary>
        /// <param name="DonationPaymentFormModel">奉獻資料</param>
        [HttpPost]
        public async Task<IActionResult> SaveKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                return await InMemoryContext.DonationPaymentManager.SaveKeyInDedication(
                    DonationPaymentFormModel,
                    InMemoryContext.AppointmentsListManager.m_LoginContact);
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveKeyInDedication");
            }
        }

        #endregion

        #region 同名同姓處理

        /// <summary>
        /// 載入同名同姓清單
        /// </summary>
        [HttpGet]
        public object LoadSameNameList(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ========================================
                // ✅ 使用基底控制器的統一驗證方法
                // ========================================
                // 教學說明：
                // 基底控制器已經提供了完整的驗證方法，包含：
                // - Session 和 ListManager 密碼一致性檢查
                // - LINE ID 恢復機制
                // - 安全的日誌記錄（隱藏敏感資訊）
                EnsureCorrectUserData();

                var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.SameNameList;
                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadSameNameList");
            }
        }

        /// <summary>
        /// 刪除同名同姓聯絡人
        /// </summary>
        /// <param name="key">聯絡人識別碼</param>
        [HttpDelete]
        public void DeleteSameNameContact(string key)
        {
            try
            {
                var sameNameContact = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.SameNameList
                    .First(a => a.SameNameElementId == key);

                InMemoryContext.DonationPaymentManager.DeleteSameNameContact(sameNameContact);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteSameNameContact");
            }
        }

        /// <summary>
        /// 建立新聯絡人
        /// </summary>
        /// <param name="FullName">姓名</param>
        [HttpPost]
        public async Task<IActionResult> CreateContact(string FullName)
        {
            try
            {
                return await InMemoryContext.DonationPaymentManager.CreateContact(FullName);
            }
            catch (Exception e)
            {
                return HandleError(e, "CreateContact");
            }
        }

        #endregion

        #region LINE 登入設定

        /// <summary>
        /// 設定 LINE 使用者 ID
        /// 用於 LINE LIFF 奉獻頁面
        /// ? 已改造為非同步模式
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SetupUserLineId(
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 設定 LINE 綁定資訊
                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = RoomId;
                InMemoryContext.LineBindingViewModel.GroupId = GroupId;
                InMemoryContext.LineBindingViewModel.ViewType = ViewType;

                // 設定顯示 ID
                if (!string.IsNullOrEmpty(GroupId))
                    InMemoryContext.LineBindingViewModel.DisplayId = GroupId;
                else if (!string.IsNullOrEmpty(RoomId))
                    InMemoryContext.LineBindingViewModel.DisplayId = RoomId;
                else
                    InMemoryContext.LineBindingViewModel.DisplayId = UserLineId;

                // 設定奉獻管理器
                InMemoryContext.DonationPaymentManager.LoginType = "Line線上登入";
                HttpContext.Session.Remove(DonationPaymentSessionKeys.WebLoginContactId);

                cancellationToken.ThrowIfCancellationRequested();
                var loginContact = ToolUtility.RetrieveContactByLineId(UserLineId);

                if (loginContact != null)
                {
                    InMemoryContext.DonationPaymentManager.SetDonationPaymentModel(loginContact);
                }

                await Task.CompletedTask.ConfigureAwait(false);
                return Json(new { status = "1" });
            }
            catch (OperationCanceledException)
            {
                return Json(new { status = "0", message = "操作已取消" });
            }
            catch (Exception e)
            {
                return HandleError(e, "SetupUserLineId");
            }
        }

        /// <summary>
        /// 奉獻 LINE 登入頁面
        /// </summary>
        [Route("/Home/DediationLineLoginView/{LineIdLoginViewPatameter?}")]
        [Route("/Dedication/DediationLineLoginView/{LineIdLoginViewPatameter?}")]
        [Route("/Dedication/DediationLineLoginView")]
        [Route("/DediationLineLoginView/{LineIdLoginViewPatameter?}")]
        [Route("/DediationLineLoginView")]
        public IActionResult DediationLineLoginView(string LineIdLoginViewPatameter)
        {
            try
            {
                // 若缺少必要參數，提供友善提示頁面
                if (string.IsNullOrWhiteSpace(LineIdLoginViewPatameter))
                {
                    return RedirectToAction("DisplayErrorView", "Home", new { ErrorMessage = "缺少 LIFF 參數，請從 LINE 入口開啟。" });
                }

                var images = new System.Collections.Generic.List<string>
                {
                    Url.Content("~/assets/images/church-001.jpg"),
                    Url.Content("~/assets/images/church-002.jpg")
                };

                InMemoryContext.LineBindingViewModel.Images = images;
                TempData["Proponent"] = LineIdLoginViewPatameter;
                ViewBag.LiffId = LineIdLoginViewPatameter; // 同步 ViewBag，確保 View 可靠讀取 LIFF ID

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "DediationLineLoginView");
            }
        }

        #endregion

        #region 付款結果頁面

        /// <summary>
        /// 付款錯誤頁面
        /// </summary>
        [HttpGet]
        public IActionResult PaymentError(
            string title,
            string message,
            string code,
            string details,
            string timestamp)
        {
            ViewBag.ErrorTitle = title;
            ViewBag.ErrorMessage = message;
            ViewBag.ErrorCode = code;
            ViewBag.ErrorDetails = details;
            ViewBag.Timestamp = timestamp;

            return View();
        }

        #endregion
    }
}
