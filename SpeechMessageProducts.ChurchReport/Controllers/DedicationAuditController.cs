// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/DedicationAuditController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class DedicationAuditController
// 主要成員：DedicationFeeAuditViewLine、DedicationFeeAuditViewWeb、SetupAuditViewBag、AuditQueryDedication、LoadDedicationFeeList、LoadSameNameList、ApproveDedication、RejectDedication、ExportDedicationReport、GetDedicationSummary
// 引用命名空間：ChurchReport.Models、ChurchReport.Tools、DevExtreme.AspNet.Data、DevExtreme.AspNet.Mvc、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory、System
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
using System;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 奉獻審核控制器
    /// 處理線上捐款的奉獻審核與查詢功能
    /// </summary>
    public class DedicationAuditController : BaseChurchController
    {
        #region 建構函式

        public DedicationAuditController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
        }

        #endregion

        #region 奉獻稽核主頁面

        /// <summary>
        /// 奉獻稽核頁面 (LINE 登入)
        /// 供行政人員查詢與稽核奉獻記錄
        /// </summary>
        [Route("/DedicationAudit/AuditViewLine")]
        public IActionResult DedicationFeeAuditViewLine()
        {
            try
            {
                SetupAuditViewBag(false);

                var manager = InMemoryContext.DonationPaymentManager;
                EnsureAuditFormModel(manager);
                var lineUserId = HttpContext.Session.GetString(DonationPaymentSessionKeys.LineUserId);
                if (!DedicationController.IsValidLineUserId(lineUserId)
                    || !string.Equals(lineUserId, InMemoryContext.LineBindingViewModel?.LineUserId, StringComparison.Ordinal))
                {
                    ClearAuditLineState(manager);
                    return View(EnsureAuditFormModel(manager));
                }

                return View(manager.SetDedicationFeeList(lineUserId));
            }
            catch (Exception e)
            {
                return HandleError(e, "DedicationFeeAuditViewLine");
            }
        }

        /// <summary>
        /// 奉獻稽核頁面 (網頁登入)
        /// 供行政人員查詢與稽核奉獻記錄
        /// </summary>
        [Route("/DedicationAudit/AuditViewWeb")]
        public IActionResult DedicationFeeAuditViewWeb()
        {
            try
            {
                SetupAuditViewBag(true);

                return View(BuildAuditWebFormModel());
            }
            catch (Exception e)
            {
                return HandleError(e, "DedicationFeeAuditViewWeb");
            }
        }

        /// <summary>
        /// 設定稽核頁面的 ViewBag
        /// </summary>
        /// <param name="isWebLogin">是否為網頁登入</param>
        private void SetupAuditViewBag(bool isWebLogin)
        {
            if (isWebLogin)
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
                // LINE 入口也可能在 legacy 狀態清理後先進入 ViewBag 設定；缺少表單模型時
                // 應安全視為非行政同工，不能讓導覽列初始化再次因 NullReferenceException 當機。
                ViewBag.IsAOfficeWorker = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel?.IsAOfficeWorker == true
                    ? "是的"
                    : "否";
            }
        }

        #endregion

        /// <summary>
        /// 建立網頁稽核頁所需的表單模型，並以伺服器端登入 contact 作為唯一身分來源。
        /// </summary>
        /// <remarks>
        /// <c>DonationPaymentManager</c> 現在是 request-scoped；它的 <c>m_Contact</c> 只在奉獻付款
        /// 流程內設定，從 Layout 點入稽核頁時可能仍為 null。直接把該欄位傳給
        /// <c>SetDedicationFeeList(Entity)</c> 會觸發 <c>ArgumentNullException(lineLoginContact)</c>，
        /// 也就是畫面看到的錯誤。這裡改用已由伺服器登入流程建立的
        /// <c>PersonalInfomationModel.m_LoginContact</c>；若登入 contact 尚未可用，回傳清空且具安全
        /// 預設值的表單，不讀取或顯示任何可能屬於前一個使用者的奉獻資料。
        /// </remarks>
        internal DonationPaymentFormModel BuildAuditWebFormModel()
        {
            var manager = InMemoryContext.DonationPaymentManager;
            var loginContact = InMemoryContext.PersonalInfomationModel?.m_LoginContact;

            if (loginContact != null)
            {
                return manager.SetDedicationFeeList(loginContact);
            }

            var model = EnsureAuditFormModel(manager);
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
            return model;
        }

        /// <summary>
        /// 取得目前 request 專屬且具完整預設值的稽核表單模型。
        /// </summary>
        /// <remarks>
        /// Grid/AJAX 可能在主頁 View 尚未完成或 legacy 流程清空 manager 後直接抵達，
        /// 因此所有讀取入口都必須先經過這個 helper。若狀態不存在，建立的新模型只由目前
        /// request-scoped manager 持有並立即回存；不放入程序級快取，也不複製其他 Session 的資料。
        /// </remarks>
        internal static DonationPaymentFormModel EnsureAuditFormModel(DonationPaymentManager manager)
        {
            var model = manager.m_DonationPaymentFormModel ?? new DonationPaymentFormModel();
            model.EnsureFormDefaults();
            model.DedicationFeeList ??= new System.Collections.Generic.List<DedicationFee>();
            model.SameNameList ??= new System.Collections.Generic.List<SameNameElement>();
            manager.m_DonationPaymentFormModel = model;
            return model;
        }

        /// <summary>
        /// 清除 LINE 稽核頁在驗證失敗時可能殘留的 contact 與奉獻資料。
        /// </summary>
        /// <param name="manager">目前 request 擁有的奉獻 manager。</param>
        /// <remarks>
        /// 稽核頁與付款頁共用 manager 型別，但不共用跨 request 的實例；此處仍必須清空目前
        /// request 的所有個資欄位，避免同一 Session 換 LINE 使用者後讀到前一位清單。
        /// </remarks>
        private static void ClearAuditLineState(DonationPaymentManager manager)
        {
            manager.m_Contact = null;
            manager.m_LoginContact = null;
            var model = EnsureAuditFormModel(manager);
            model.FullName = string.Empty;
            model.Mobile = string.Empty;
            model.DedicationNumber = string.Empty;
            model.NationId = string.Empty;
            model.LastSixDigit = string.Empty;
            model.DedicationFeeList.Clear();
            model.SameNameList.Clear();
            model.TotalAmount = 0;
        }

        #region 奉獻查詢

        /// <summary>
        /// 稽核查詢奉獻記錄
        /// 依據查詢條件篩選奉獻資料
        /// </summary>
        /// <param name="DonationPaymentFormModel">查詢條件(日期區間、奉獻者等)</param>
        [HttpPost]
        public async Task<IActionResult> AuditQueryDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                return await InMemoryContext.DonationPaymentManager.AuditQueryDedication(DonationPaymentFormModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "AuditQueryDedication");
            }
        }

        #endregion

        #region 奉獻資料載入

        /// <summary>
        /// 載入奉獻費清單
        /// 用於 DevExtreme DataGrid 顯示捐獻資料
        /// </summary>
        /// <param name="id">查詢ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩選)</param>
        [HttpGet]
        public object LoadDedicationFeeList(string id, DataSourceLoadOptions loadOptions)
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

                var tasks = EnsureAuditFormModel(InMemoryContext.DonationPaymentManager).DedicationFeeList;
                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadDedicationFeeList");
            }
        }

        /// <summary>
        /// 載入同名連絡人清單
        /// 當有多位同名連絡人時，用於選擇正確的捐獻者
        /// </summary>
        /// <param name="id">查詢參數</param>
        /// <param name="loadOptions">載入選項</param>
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

                var sameNameList = EnsureAuditFormModel(InMemoryContext.DonationPaymentManager).SameNameList;

                return DataSourceLoader.Load(sameNameList, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadSameNameList");
            }
        }

        #endregion

        #region 稽核操作

        /// <summary>
        /// 核准奉獻記錄
        /// </summary>
        /// <param name="key">奉獻記錄ID</param>
        [HttpPost]
        public async Task<IActionResult> ApproveDedication(string key)
        {
            try
            {
                // 實作核准邏輯
                // await InMemoryContext.DonationPaymentManager.ApproveDedication(key);

                return Json(new { status = "1", message = "奉獻記錄已核准" });
            }
            catch (Exception e)
            {
                return HandleError(e, "ApproveDedication");
            }
        }

        /// <summary>
        /// 退回奉獻記錄
        /// </summary>
        /// <param name="key">奉獻記錄ID</param>
        /// <param name="reason">退回原因</param>
        [HttpPost]
        public async Task<IActionResult> RejectDedication(string key, string reason)
        {
            try
            {
                // 實作退回邏輯
                // await InMemoryContext.DonationPaymentManager.RejectDedication(key, reason);

                return Json(new { status = "1", message = "奉獻記錄已退回" });
            }
            catch (Exception e)
            {
                return HandleError(e, "RejectDedication");
            }
        }

        /// <summary>
        /// 匯出奉獻報表
        /// 產生 Excel 或 PDF 格式的奉獻統計報表
        /// </summary>
        /// <param name="startDate">起始日期</param>
        /// <param name="endDate">結束日期</param>
        /// <param name="format">匯出格式 (Excel/PDF)</param>
        [HttpGet]
        public async Task<IActionResult> ExportDedicationReport(
            DateTime startDate,
            DateTime endDate,
            string format = "Excel")
        {
            try
            {
                // 實作匯出邏輯
                // var reportData = await InMemoryContext.DonationPaymentManager
                //     .GenerateDedicationReport(startDate, endDate);

                // 暫時返回成功訊息
                return Json(new
                {
                    status = "1",
                    message = $"正在產生 {startDate:yyyy/MM/dd} 至 {endDate:yyyy/MM/dd} 的奉獻報表"
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "ExportDedicationReport");
            }
        }

        #endregion

        #region 統計資訊

        /// <summary>
        /// 取得奉獻統計摘要
        /// 包含總金額、筆數、奉獻者人數等
        /// </summary>
        /// <param name="startDate">起始日期</param>
        /// <param name="endDate">結束日期</param>
        [HttpGet]
        public async Task<IActionResult> GetDedicationSummary(DateTime startDate, DateTime endDate)
        {
            try
            {
                // 實作統計邏輯
                var summary = new
                {
                    totalAmount = 0m,
                    totalCount = 0,
                    donorCount = 0,
                    averageAmount = 0m,
                    startDate = startDate.ToString("yyyy/MM/dd"),
                    endDate = endDate.ToString("yyyy/MM/dd")
                };

                return Json(new { status = "1", data = summary });
            }
            catch (Exception e)
            {
                return HandleError(e, "GetDedicationSummary");
            }
        }

        /// <summary>
        /// 取得奉獻趨勢圖資料
        /// 用於圖表顯示奉獻金額趨勢
        /// </summary>
        /// <param name="startDate">起始日期</param>
        /// <param name="endDate">結束日期</param>
        /// <param name="groupBy">分組方式 (日/週/月)</param>
        [HttpGet]
        public async Task<IActionResult> GetDedicationTrend(
            DateTime startDate,
            DateTime endDate,
            string groupBy = "月")
        {
            try
            {
                // 實作趨勢分析邏輯
                var trendData = new object[] { };

                return Json(new { status = "1", data = trendData });
            }
            catch (Exception e)
            {
                return HandleError(e, "GetDedicationTrend");
            }
        }

        /// <summary>
        /// 以 contact Id 取得該聯絡人的奉獻清單 (AJAX)
        /// </summary>
        /// <param name="id">contact Id (GUID)</param>
        [HttpGet]
        public IActionResult GetFeesByContactId(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                    return Json(new { status = "0", message = "missing id", DedicationFeeList = new object[] { } });

                var manager = InMemoryContext.DonationPaymentManager;
                EnsureAuditFormModel(manager);
                var feeList = manager.GetDedicationFeesByContactId(id);

                // GetDedicationFeesByContactId 內部已透過 SetDedicationFeeList 算好總金額
                var totalAmount = manager.m_DonationPaymentFormModel.TotalAmount;
                return Json(new { status = "1", DedicationFeeList = feeList, TotalAmount = totalAmount });
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"GetFeesByContactId 錯誤: {e.Message}");
                return Json(new { status = "0", message = e.Message, DedicationFeeList = new object[] { } });
            }
        }

        #endregion
    }
}
