// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/DedicationController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class DedicationController
// 主要成員：DonationPaymentView、IsWebLogin、RestoreWebLoginDonationPaymentModel、RestoreWebLoginDonationPaymentModelFromSession、TryRestoreDonationDonorIdentity、SetupDonationPaymentViewBag、SaveDonationPaymentDedication、LoadCreditCardList、DeleteCreditCard、LoadDedicationBookingList、DeleteDedicationBooking
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
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
                    // LINE LIFF 已在 SetupUserLineId 完成 ID Token 驗證並寫入 Session；
                    // 此頁只接受同一個 server-bound id，不能因 route segment 改變就替其他人查詢資料。
                    var boundLineUserId = TryGetVerifiedLineUserId();
                    if (!string.Equals(boundLineUserId, LineId, StringComparison.Ordinal))
                    {
                        ClearLineDonationState(InMemoryContext.DonationPaymentManager);
                    }
                    else if (!InMemoryContext.DonationPaymentManager.TrySetDonationPaymentModelForLineUser(LineId))
                    {
                        ClearLineDonationState(InMemoryContext.DonationPaymentManager);
                    }
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
        /// 在奉獻交易 POST 內重新建立奉獻者身分。
        ///
        /// 根因說明：
        /// DonationPaymentManager 由 scoped 的 IInMemoryDataContext 每個 request 重新建立，
        /// GET 奉獻頁時設定的 m_Contact 不會延續到這個 POST。若不在此重新解析身分，
        /// m_Contact 會是 null 並一路傳到 SetFeeParameter 的 aContact.Id，造成 NullReferenceException。
        ///
        /// 身分只從伺服器端 Session 還原（已驗證的 LINE user id，或網頁登入保存的 contact id），
        /// 絕不接受表單或 route 傳入的身分值，維持既有的 Session 隔離保證。
        /// </summary>
        /// <returns>成功取得奉獻者 contact 時為 true。</returns>
        private bool TryRestoreDonationDonorIdentity()
        {
            var manager = InMemoryContext.DonationPaymentManager;
            if (manager.m_Contact != null)
            {
                return true;
            }

            var verifiedLineUserId = TryGetVerifiedLineUserId();
            if (!string.IsNullOrEmpty(verifiedLineUserId)
                && manager.TryRestoreDonorIdentityForLineUser(verifiedLineUserId))
            {
                return true;
            }

            return RestoreWebLoginDonationPaymentModelFromSession(manager) && manager.m_Contact != null;
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
                if (!TryRestoreDonationDonorIdentity())
                {
                    return Json(new { status = "2", message = "登入狀態已失效，請重新登入或從 LINE 重新進入奉獻頁面後再送出。" });
                }

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

                // LINE 清單的日期查詢透過 AJAX POST 後會保存於目前 Session；
                // 必須在重新建立清單模型前還原，才能保留使用者選取的跨年度區間。
                // 此操作只讀寫目前 request 的 Session，不建立快取、背景工作或其他資源。
                RestoreDedicationQueryDatesFromSession();
                return View(BuildDedicationFeeLineFormModel());
            }
            catch (Exception e)
            {
                return HandleError(e, "DedicationFeeView");
            }
        }

        /// <summary>
        /// 建立 LINE 登入的奉獻收費清單模型。
        /// </summary>
        /// <remarks>
        /// LINE user id 只在 <see cref="DonationPaymentSessionKeys.LineUserId"/> 存有由伺服器驗證成功的值，
        /// 且與目前 Session 的 LineBindingViewModel 完全一致時才可使用。route、query 或 form 參數不是
        /// 身分來源；失敗時立即清除 manager 與 Session 的奉獻狀態，回傳隔離的空白模型，避免跨 LINE 使用者
        /// 顯示前一位使用者的姓名、奉獻編號或收費清單。清理只操作目前 request/session owner，沒有背景工作、
        /// cache entry、timer、subscription 或其他需要延後釋放的資源。
        /// </remarks>
        internal DonationPaymentFormModel BuildDedicationFeeLineFormModel()
        {
            var manager = InMemoryContext.DonationPaymentManager;
            EnsureLineDonationFormModel(manager);
            var lineUserId = TryGetVerifiedLineUserId();
            if (lineUserId == null)
            {
                ClearLineDonationState(manager);
                return EnsureLineDonationFormModel(manager);
            }

            try
            {
                var model = manager.SetDedicationFeeList(lineUserId);
                if (manager.m_Contact == null || manager.m_LoginContact == null)
                {
                    ClearLineDonationState(manager);
                    return EnsureLineDonationFormModel(manager);
                }

                return model;
            }
            catch
            {
                // CRM/查詢失敗時先清理所有身分參照，再把例外交給 action 的統一錯誤處理；
                // 清理順序保證後續 request 不會取得半完成的舊模型。
                ClearLineDonationState(manager);
                throw;
            }
        }

        /// <summary>
        /// 取得由 LINE 登入流程建立且與目前 Session 綁定的 user id。
        /// </summary>
        /// <returns>驗證成功的 LINE user id；任何缺失、不一致或 Session 無法讀取時回傳 null。</returns>
        private string TryGetVerifiedLineUserId()
        {
            try
            {
                var sessionLineUserId = HttpContext.Session.GetString(DonationPaymentSessionKeys.LineUserId);
                var bindingLineUserId = InMemoryContext.LineBindingViewModel?.LineUserId;
                if (!IsValidLineUserId(sessionLineUserId)
                    || !string.Equals(sessionLineUserId, bindingLineUserId, StringComparison.Ordinal))
                {
                    return null;
                }

                return sessionLineUserId;
            }
            catch
            {
                // Session middleware 尚未建立或已失效時採 fail-closed，絕不退回 route/client 值。
                return null;
            }
        }

        /// <summary>
        /// 驗證 LINE user id 的固定格式，避免任意字串觸發 CRM 查詢或污染 Session 狀態。
        /// </summary>
        internal static bool IsValidLineUserId(string lineUserId)
        {
            if (string.IsNullOrWhiteSpace(lineUserId) || lineUserId.Length != 33 || lineUserId[0] != 'U')
            {
                return false;
            }

            for (var index = 1; index < lineUserId.Length; index++)
            {
                if (!char.IsLetterOrDigit(lineUserId[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 確保 LINE 收費清單在沒有有效登入者時仍有可序列化的空白表單。
        /// </summary>
        private static DonationPaymentFormModel EnsureLineDonationFormModel(DonationPaymentManager manager)
        {
            var model = manager.m_DonationPaymentFormModel ?? new DonationPaymentFormModel();
            model.EnsureFormDefaults();
            manager.m_DonationPaymentFormModel = model;
            return model;
        }

        /// <summary>
        /// 清除目前 manager 與 LINE Session 的奉獻身分狀態。
        /// </summary>
        /// <param name="manager">本次 request 擁有的付款 manager。</param>
        /// <remarks>
        /// 所有個資、清單與 contact 參照在同一個同步區段內清除；Session key 也立即移除，
        /// 因此不會把失敗登入或前一位使用者狀態留給下一次導覽。
        /// </remarks>
        private void ClearLineDonationState(DonationPaymentManager manager)
        {
            manager.m_Contact = null;
            manager.m_LoginContact = null;

            var model = manager.m_DonationPaymentFormModel ?? new DonationPaymentFormModel();
            model.EnsureFormDefaults();
            model.FullName = string.Empty;
            model.Mobile = string.Empty;
            model.DedicationNumber = string.Empty;
            model.NationId = string.Empty;
            model.LastSixDigit = string.Empty;
            model.DedicationFeeList.Clear();
            model.SameNameList.Clear();
            model.TotalAmount = 0;
            manager.m_DonationPaymentFormModel = model;

            // LineBindingViewModel 也可能被 Session cache 保留；清除其 user/profile 欄位，
            // 避免失敗登入後下一次頁面仍顯示前一位 LINE 使用者的暱稱或識別資訊。
            var lineBinding = InMemoryContext.LineBindingViewModel;
            if (lineBinding != null)
            {
                lineBinding.LineUserId = string.Empty;
                lineBinding.DisplayId = string.Empty;
                lineBinding.DisplayName = string.Empty;
                lineBinding.UserDisplayName = string.Empty;
                lineBinding.FullName = string.Empty;
                lineBinding.OtherName = string.Empty;
                lineBinding.Mobile = string.Empty;
                lineBinding.PictureUrl = string.Empty;
                lineBinding.StatusMessage = string.Empty;
                lineBinding.GroupId = string.Empty;
                lineBinding.RoomId = string.Empty;
                lineBinding.ViewType = string.Empty;
            }

            try
            {
                HttpContext.Session.Remove(DonationPaymentSessionKeys.LineUserId);
                HttpContext.Session.Remove(DonationPaymentSessionKeys.WebLoginContactId);
            }
            catch
            {
                // Session 不可用時 manager 清理仍已完成；不可用的 Session 不得阻止 fail-closed。
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

                // Layout 導覽可直接進入本頁，這時 request-scoped manager 尚未必有 m_Contact。
                // 先嘗試用既有網頁登入恢復流程補齊 contact，再由 builder 在缺少 contact 時回傳
                // 隔離的空白模型；不可把 null 傳入 FillFromContact，否則會拋出
                // ArgumentNullException(lineLoginContact) 並將使用者導向錯誤頁。
                RestoreWebLoginDonationPaymentModel();
                return View(BuildDedicationFeeWebFormModel());
            }
            catch (Exception e)
            {
                return HandleError(e, "DedicationFeeViewWeb");
            }
        }

        /// <summary>
        /// 建立網頁登入的奉獻收費清單模型，並確保 contact 缺失時不會讓頁面當機。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 正常路徑會使用伺服器登入模型、驗證 Cookie claim 或已驗證的 Session contact id 查詢 CRM；
        /// 只有確定取得非 null contact 才呼叫 <c>SetDedicationFeeList(Entity)</c>。
        /// </para>
        /// <para>
        /// 若登入狀態尚未完成、Session 已失效或 CRM 暫時沒有 contact，回傳的新模型會清除
        /// 姓名、奉獻編號、個資與奉獻清單，避免把任何前一個身分的資料留在目前回應中。
        /// 模型只由目前 request 的 manager 持有，不寫入程序級快取，也不建立背景工作或外部資源。
        /// </para>
        /// </remarks>
        internal DonationPaymentFormModel BuildDedicationFeeWebFormModel()
        {
            var manager = InMemoryContext.DonationPaymentManager;
            EnsureLineDonationFormModel(manager);
            var contact = ResolveAuthenticatedWebDonationContact();

            if (contact != null)
            {
                // SetDedicationFeeList 只負責組裝畫面清單，不會改寫 manager 的 m_Contact。
                // 這裡明確同步目前已驗證的 contact，避免同一個長生命週期狀態容器在登出/換人
                // 後仍保留舊身分，讓後續付款或更新流程誤用前一位使用者的 CRM entity。
                manager.m_Contact = contact;
                manager.m_LoginContact = contact;
                return manager.SetDedicationFeeList(contact);
            }

            var model = manager.m_DonationPaymentFormModel ?? new DonationPaymentFormModel();
            model.EnsureFormDefaults();
            model.FullName = string.Empty;
            model.Mobile = string.Empty;
            model.DedicationNumber = string.Empty;
            model.NationId = string.Empty;
            model.LastSixDigit = string.Empty;
            model.DedicationFeeList ??= new System.Collections.Generic.List<DedicationFee>();
            model.DedicationFeeList.Clear();
            model.SameNameList ??= new System.Collections.Generic.List<SameNameElement>();
            model.SameNameList.Clear();
            model.TotalAmount = 0;
            // 找不到目前登入者時連 manager 內的 contact 參照也一併清除，
            // 避免下一個流程誤用前一個身分的 CRM entity。
            manager.m_Contact = null;
            manager.m_LoginContact = null;
            manager.m_DonationPaymentFormModel = model;
            return model;
        }

        /// <summary>
        /// 依伺服器建立的登入狀態取得目前網頁使用者的奉獻 contact。
        /// </summary>
        /// <remarks>
        /// 取得順序是 request-local 的登入模型、驗證 Cookie 的 contact-id claim，最後才是奉獻登入流程
        /// 專用的 Session contact id；三者都必須先通過 GUID 解析與 CRM 讀取，絕不接受 query/form 的 contact id。
        /// 找不到或讀取失敗時回傳 null，由呼叫端清除模型並安全呈現空白清單，避免 null 例外與跨使用者資料殘留。
        /// </remarks>
        private Entity ResolveAuthenticatedWebDonationContact()
        {
            try
            {
                var httpContext = HttpContext;
                var contactIdText = httpContext.User?.FindFirst(ChurchReport.Security.LoginClaimsFactory.ContactIdClaim)?.Value;
                if (Guid.TryParse(contactIdText, out var contactId))
                {
                    var contact = ToolUtility.RetrieveEntity("contact", contactId);
                    if (contact != null)
                    {
                        if (InMemoryContext.PersonalInfomationModel != null)
                        {
                            InMemoryContext.PersonalInfomationModel.m_LoginContact = contact;
                        }
                        return contact;
                    }
                }

                // 奉獻專用網頁登入目前以 Session 保存 contact id；只有 GUID 可解析且 CRM
                // 查得到實體時才接受，絕不採用 query/form 的 client-controllable 值。
                contactIdText = httpContext.Session.GetString(DonationPaymentSessionKeys.WebLoginContactId);
                if (Guid.TryParse(contactIdText, out contactId))
                {
                    var contact = ToolUtility.RetrieveEntity("contact", contactId);
                    if (contact != null)
                    {
                        if (InMemoryContext.PersonalInfomationModel != null)
                        {
                            InMemoryContext.PersonalInfomationModel.m_LoginContact = contact;
                        }
                        return contact;
                    }
                }

                // 舊版一般登入流程可能已建立 request/session-scoped PersonalInfomationModel，
                // 但若沒有現行 Cookie claim 或奉獻登入 Session，就不能把它當成權限來源。
                // 只有在 ASP.NET 已確認目前請求為 authenticated 時，才允許此相容 fallback；
                // 否則回傳 null 並清空表單，避免跨使用者殘留資料被重新帶出。
                if (httpContext.User?.Identity?.IsAuthenticated == true)
                {
                    return InMemoryContext.PersonalInfomationModel?.m_LoginContact;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DedicationController] 網頁奉獻 contact 恢復失敗：{ex.Message}");
                return null;
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
            string IdToken,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsValidLineUserId(UserLineId))
                {
                    ClearLineDonationState(InMemoryContext.DonationPaymentManager);
                    return Json(new { status = "0", message = "LINE 使用者識別無效" });
                }

                if (!await VerifyLineIdTokenAsync(IdToken, UserLineId, cancellationToken).ConfigureAwait(false))
                {
                    ClearLineDonationState(InMemoryContext.DonationPaymentManager);
                    return Json(new { status = "0", message = "LINE 身分驗證失敗，請重新登入" });
                }

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

                // ToolUtility 目前是同步 CRM API；Task.Run 只會額外佔用 ThreadPool，且取消時無法停止
                // 底層同步呼叫。這裡在同一個 request 內直接查詢，並在查詢前後檢查取消狀態，避免產生
                // 未被 await、無法取消或延後寫入使用者狀態的背景工作。
                var loginContact = ToolUtility.RetrieveContactByLineId(UserLineId);
                cancellationToken.ThrowIfCancellationRequested();
                if (loginContact == null)
                {
                    ClearLineDonationState(InMemoryContext.DonationPaymentManager);
                    return Json(new { status = "0", message = "找不到 LINE 對應的奉獻者資料" });
                }

                InMemoryContext.DonationPaymentManager.m_Contact = loginContact;
                InMemoryContext.DonationPaymentManager.m_LoginContact = loginContact;
                InMemoryContext.DonationPaymentManager.SetDonationPaymentModel(loginContact);
                HttpContext.Session.SetString(DonationPaymentSessionKeys.LineUserId, UserLineId);

                return Json(new { status = "1" });
            }
            catch (OperationCanceledException)
            {
                ClearLineDonationState(InMemoryContext.DonationPaymentManager);
                return Json(new { status = "0", message = "操作已取消" });
            }
            catch (Exception e)
            {
                ClearLineDonationState(InMemoryContext.DonationPaymentManager);
                return HandleError(e, "SetupUserLineId");
            }
        }

        /// <summary>
        /// 向 LINE 驗證 LIFF ID Token，並確認 token 內的 subject 就是本次送出的 user id。
        /// </summary>
        /// <param name="idToken">LIFF SDK 取得的短命 ID Token；不寫入 Session、cache 或 log。</param>
        /// <param name="expectedUserId">本次 request 的 LINE user id。</param>
        /// <param name="cancellationToken">請求取消訊號。</param>
        /// <returns>token 由 LINE 驗證成功且 subject/audience/issuer/期限均符合時為 true。</returns>
        /// <remarks>
        /// LINE user id 本身是 client-controlled，不能單獨當作身份憑證。驗證使用 IHttpClientFactory
        /// 提供的共用 client；token 只放在單一 HttpRequestMessage，避免任何可達的 DefaultRequestHeaders
        /// 帶著前一位使用者憑證。request、content 與 response 都 deterministic dispose，取消時不建立
        /// 未受控的背景工作。
        /// </remarks>
        private async Task<bool> VerifyLineIdTokenAsync(
            string idToken,
            string expectedUserId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(idToken) || !IsValidLineUserId(expectedUserId))
            {
                return false;
            }

            var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
            var channelId = configuration?["LineLogin:ChannelId"];
            var factory = HttpContext.RequestServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
            if (string.IsNullOrWhiteSpace(channelId) || factory == null)
            {
                return false;
            }

            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id_token", idToken),
                new KeyValuePair<string, string>("client_id", channelId)
            });
            using var response = await factory.CreateClient("LineLoginApi")
                .PostAsync("oauth2/v2.1/verify", content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var verifiedToken = System.Text.Json.JsonSerializer.Deserialize<LineIdTokenVerificationResponse>(body);
            if (verifiedToken == null
                || !string.Equals(verifiedToken.Issuer, "https://access.line.me", StringComparison.Ordinal)
                || !string.Equals(verifiedToken.Subject, expectedUserId, StringComparison.Ordinal)
                || !string.Equals(verifiedToken.Audience, channelId, StringComparison.Ordinal))
            {
                return false;
            }

            return verifiedToken.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        /// <summary>LINE ID Token 驗證 API 回應；只保存驗證所需欄位。</summary>
        private sealed class LineIdTokenVerificationResponse
        {
            /// <summary>Token issuer。</summary>
            [System.Text.Json.Serialization.JsonPropertyName("iss")]
            public string Issuer { get; set; }

            /// <summary>LINE user id。</summary>
            [System.Text.Json.Serialization.JsonPropertyName("sub")]
            public string Subject { get; set; }

            /// <summary>Token audience/channel id。</summary>
            [System.Text.Json.Serialization.JsonPropertyName("aud")]
            public string Audience { get; set; }

            /// <summary>Unix seconds 過期時間。</summary>
            [System.Text.Json.Serialization.JsonPropertyName("exp")]
            public long ExpiresAt { get; set; }
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
