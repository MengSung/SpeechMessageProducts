using ChurchReport.Models;
using ChurchReport.Tools;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using ToolUtilityNameSpace;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 教會系統控制器基礎類別
    /// 提供共用的功能和錯誤處理機制
    /// </summary>
    public abstract class BaseChurchController : Controller, IDisposable
    {
        #region 常數定義

        /// <summary>
        /// 追蹤層級總數 (1-5, 數字越小越重要)
        /// </summary>
        protected const int TOTAL_LEVEL = 1;

        /// <summary>
        /// 最高層級追蹤 (最容易被看到的大範圍部分)
        /// </summary>
        protected const int LEVEL_1 = 1;

        /// <summary>
        /// 次高層級追蹤
        /// </summary>
        protected const int LEVEL_2 = 2;

        /// <summary>
        /// 中等層級追蹤
        /// </summary>
        protected const int LEVEL_3 = 3;

        /// <summary>
        /// 次低層級追蹤
        /// </summary>
        protected const int LEVEL_4 = 4;

        /// <summary>
        /// 最低層級追蹤 (最不會被看到的細節部分)
        /// </summary>
        protected const int LEVEL_5 = 5;

        /// <summary>
        /// LINE 錯誤通知接收者 ID
        /// </summary>
        protected const string LINE_ERROR_RECEIVER_ID = "U7638e4ed509708a3573ba6d69970583d";

        #endregion

        #region 受保護欄位

        /// <summary>
        /// 工具類別實例 (用於 CRM 操作)
        /// </summary>
        protected readonly ToolUtilityClass ToolUtility;

        /// <summary>
        /// 記憶體內資料上下文 (儲存 Session 資料)
        /// </summary>
        protected readonly InMemoryDataContextSmallGroup InMemoryContext;

        /// <summary>
        /// 金流服務介面
        /// </summary>
        protected readonly IPayment PaymentService;

        #endregion

        #region 建構函式

        /// <summary>
        /// 初始化基礎控制器
        /// </summary>
        /// <param name="httpContextAccessor">HTTP 上下文存取器</param>
        /// <param name="memoryCache">記憶體快取</param>
        /// <param name="paymentService">金流服務</param>
        protected BaseChurchController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService)
        {
            // 初始化 CRM 連線工具
            ToolUtility = new ToolUtilityClass("DYNAMICS365-9.0");

            // 初始化記憶體資料上下文
            InMemoryContext = new InMemoryDataContextSmallGroup(
                httpContextAccessor, memoryCache, paymentService);

            // 儲存金流服務參考
            PaymentService = paymentService;
        }

        #endregion

        #region 錯誤處理

        /// <summary>
        /// 統一錯誤處理方法
        /// 記錄錯誤日誌並發送 LINE 通知
        /// </summary>
        /// <param name="exception">例外物件</param>
        /// <param name="methodName">發生錯誤的方法名稱</param>
        /// <returns>錯誤頁面或 JSON 結果</returns>
        protected IActionResult HandleError(Exception exception, string methodName)
        {
            // 組合錯誤訊息
            string errorMessage = $"錯誤訊息 : FullName = {GetType().FullName}, " +
                                $"Method = {methodName}, " +
                                $"Time = {DateTime.Now}, " +
                                $"Description = {exception}";

            // 寫入追蹤日誌 (加入 null 檢查)
            try
            {
                ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1, errorMessage);
            }
            catch (Exception traceEx)
            {
                // 追蹤失敗不影響錯誤處理流程
                System.Diagnostics.Debug.WriteLine($"TraceByLevel 失敗: {traceEx.Message}");
            }

            // 發送 LINE 通知
            SendLineErrorNotification(errorMessage);

            // 判斷是否為 AJAX 請求 (加入 null 檢查)
            bool isAjaxRequest = false;
            try
            {
                isAjaxRequest = Request?.Headers != null && 
                               Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            }
            catch
            {
                // 無法判斷請求類型，預設為非 AJAX
                isAjaxRequest = false;
            }

            if (isAjaxRequest)
            {
                // AJAX 請求返回 JSON
                return Json(new
                {
                    status = "error",
                    message = exception.Message,
                    timestamp = DateTime.Now
                });
            }
            else
            {
                // 一般請求導向錯誤頁面
                return RedirectToAction("DisplayErrorView", "Home", new { ErrorMessage = exception.Message });
            }
        }

        /// <summary>
        /// 發送 LINE 錯誤通知
        /// </summary>
        /// <param name="errorMessage">錯誤訊息內容</param>
        protected void SendLineErrorNotification(string errorMessage)
        {
            try
            {
                var lineProcessor = new LineMessagingProcessorClass();
                lineProcessor.SendMessage(LINE_ERROR_RECEIVER_ID, $"聖谷行道會: 錯誤 => {errorMessage}");
            }
            catch (Exception ex)
            {
                // LINE 通知失敗不影響主要流程
                try
                {
                    ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1,
                        $"LINE 通知發送失敗: {ex.Message}");
                }
                catch
                {
                    // 如果連追蹤都失敗，使用 Debug 輸出
                    System.Diagnostics.Debug.WriteLine($"LINE 通知發送失敗且追蹤失敗: {ex.Message}");
                }
            }
        }

        #endregion

        #region ViewBag 設定輔助方法

        /// <summary>
        /// 設定多小組版面參數
        /// 用於控制導覽選單和頁面顯示
        /// </summary>
        protected void SetMultiGroupLayoutParameter()
        {
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            bool integrateFlag = IsIntegrateDataLoaded();

            // 決定多小組索引類型
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
                ViewBag.MultiGroupIndex = "IntegrateView";
            }

            // 設定會計權限
            ViewBag.IsAOfficeWorker = InMemoryContext.QpayManager.m_QpayModel.IsAOfficeWorker
                ? "是的" : "否";
        }

        /// <summary>
        /// 檢查整合資料是否已載入
        /// </summary>
        /// <returns>True 表示已載入且有效</returns>
        protected bool IsIntegrateDataLoaded()
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
            return weeklyReport != null && weeklyReport.LoadFlag;
        }

        /// <summary>
        /// 設定基本 ViewBag 參數
        /// </summary>
        protected void SetupBasicViewBag()
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;

            // 設定繳費點名狀態
            SetupFeeDataListCount();
        }

        /// <summary>
        /// 設定繳費點名資料數量狀態
        /// </summary>
        protected void SetupFeeDataListCount()
        {
            bool hasFeeData = InMemoryContext.FeeList.FeeDataList != null &&
                            InMemoryContext.FeeList.FeeDataList.Count > 0;

            ViewBag.FeeDataListCount = hasFeeData ? "繳費與點名已有資料" : "繳費與點名尚無資料";
        }

        #endregion

        #region 資源釋放

        /// <summary>
        /// 釋放資源
        /// </summary>
        public new void Dispose()
        {
            // 釋放工具類別資源
            ToolUtility?.Dispose();

            // 呼叫基礎類別的 Dispose
            base.Dispose();
        }

        #endregion
    }
}
