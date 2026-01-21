using ChurchReport.Models;
using ChurchReport.Tools;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 教會報表基底控制器
    /// 提供共用功能與錯誤處理邏輯
    /// </summary>
    public abstract class BaseChurchController : Controller, IDisposable
    {
        #region 常數定義

        protected const int TOTAL_LEVEL = 1;
        protected const int LEVEL_1 = 1;
        protected const int LEVEL_2 = 2;
        protected const int LEVEL_3 = 3;
        protected const int LEVEL_4 = 4;
        protected const int LEVEL_5 = 5;
        protected const string LINE_ERROR_RECEIVER_ID = "U7638e4ed509708a3573ba6d69970583d";

        #endregion

        #region 服務實例

        /// <summary>
        /// ToolUtility 提供者 (使用 Dependency Injection)
        /// </summary>
        protected readonly IToolUtilityProvider _toolUtilityProvider;

        /// <summary>
        /// CRM 連線池
        /// </summary>
        protected readonly ICrmConnectionPool _connectionPool;

        /// <summary>
        /// 工具類別實例 (透過 DI 取得 Singleton 實例)
        /// </summary>
        protected ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

        /// <summary>
        /// 記憶體資料上下文 (透過 DI 注入，避免靜態依賴)
        /// </summary>
        protected readonly IInMemoryDataContext InMemoryContext;

        /// <summary>
        /// 金流服務介面
        /// </summary>
        protected readonly IPayment PaymentService;

        #endregion

        #region 建構函式

        /// <summary>
        /// 初始化基底控制器
        /// </summary>
        /// <param name="httpContextAccessor">HTTP 上下文存取器</param>
        /// <param name="memoryCache">記憶體快取</param>
        /// <param name="paymentService">金流服務</param>
        /// <param name="toolUtilityProvider">ToolUtility 提供者 (透過 DI 注入)</param>
        /// <param name="connectionPool">CRM 連線池</param>
        /// <param name="inMemoryContext">記憶體資料上下文 (透過 DI 注入，可選參數以保持向後兼容)</param>
        protected BaseChurchController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IInMemoryDataContext inMemoryContext = null)
        {
            // 透過 DI 注入 ToolUtility 提供者
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));

            // ? 支援兩種方式：
            // 1. 新方式：透過 DI 注入記憶體資料上下文（推薦，避免靜態依賴）
            // 2. 舊方式：直接 new 實例（向後兼容，逐步淘汰）
            if (inMemoryContext != null)
            {
                // 使用 DI 注入的實例（推薦）
                InMemoryContext = inMemoryContext;
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] 使用 DI 注入的 InMemoryContext");
            }
            else
            {
                // 向後兼容：直接建立實例（將逐步淘汰）
                InMemoryContext = new InMemoryDataContextSmallGroup(
                    httpContextAccessor, memoryCache, paymentService, toolUtilityProvider);
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] ?? 使用向後兼容模式建立 InMemoryContext（請盡快更新為 DI 注入）");
            }

            // 存放金流服務參考
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
                lineProcessor.SendMessage(LINE_ERROR_RECEIVER_ID, $"好牧人: 錯誤 => {errorMessage}");
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

            // ? 修正：確保從 MultiGroupView 點擊小組後，保持 HybridView 模式
            // 讓「回報統計」和「小組回報」選項都顯示
            if (displayViewType == "MultiGroupView" && !integrateFlag)
            {
                // 純多小組模式（尚未點擊任何小組）
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
            }
            else if (displayViewType == "IntegrateView" && integrateFlag)
            {
                // 單一小組模式（只有一個小組）
                ViewBag.MultiGroupIndex = "IntegrateView";
            }
            else if (displayViewType == "MultiGroupView" && integrateFlag)
            {
                // ? 混合模式：有多個小組且已點擊其中一個
                // 此時應該顯示「回報統計」和「小組回報」兩個選項
                ViewBag.MultiGroupIndex = "HybridView";
            }
            else
            {
                // ? 預設為 IntegrateView 或 HybridView
                // 如果有載入整合資料，就使用 HybridView，確保選項不消失
                ViewBag.MultiGroupIndex = integrateFlag ? "HybridView" : "IntegrateView";
            }

            // 設定是否為行政同工
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

        #region Session 安全驗證

        /// <summary>
        /// 驗證當前 Session 是否合法
        /// 
        /// 設計模式：
        /// - Template Method Pattern: 提供通用驗證流程
        /// - Fail-Fast Principle: 發現問題立即返回 false
        /// 
        /// 驗證項目：
        /// 1. Session 是否存在
        /// 2. 用戶 ID 是否一致
        /// 3. Session 是否過期
        /// 
        /// 使用方式：
        /// 在 Controller Action 開始時呼叫：
        /// <code>
        /// if (!ValidateSession())
        /// {
        ///     return RedirectToAction("Login", "Authentication");
        /// }
        /// </code>
        /// </summary>
        /// <returns>true 表示 Session 合法，false 表示需要重新登入</returns>
        protected bool ValidateSession()
        {
            try
            {
                // ========================================
                // Step 1: 檢查 Session 是否存在
                // ========================================
                var sessionUserId = HttpContext.Session.GetString("_SessionUserId");
                if (string.IsNullOrEmpty(sessionUserId))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] ? Session 不存在或已過期");
                    return false;
                }

                // ========================================
                // Step 2: 檢查 Session 創建時間（防止過期 Session）
                // ========================================
                var sessionCreatedAt = HttpContext.Session.GetString("_SessionCreatedAt");
                if (!string.IsNullOrEmpty(sessionCreatedAt))
                {
                    if (DateTime.TryParse(sessionCreatedAt, out DateTime createdTime))
                    {
                        var sessionAge = DateTime.UtcNow - createdTime;
                        // Session 超過 8 小時視為過期（額外保護層）
                        if (sessionAge.TotalHours > 8)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ValidateSession] ?? Session 已過期 ({sessionAge.TotalHours:F2} 小時)");
                            return false;
                        }
                    }
                }

                // ========================================
                // Step 3: 驗證用戶身份一致性
                // ========================================
                // 檢查 InMemoryContext 中的用戶資料是否與 Session 一致
                var currentAccount = InMemoryContext?.ListManager?.m_Account;
                if (string.IsNullOrEmpty(currentAccount))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] ?? 用戶資料不存在於 InMemoryContext");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[ValidateSession] ? Session 驗證通過 - UserId: {sessionUserId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateSession] ? Session 驗證失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 強制重新生成 Session ID（用於安全升級）
        /// 
        /// 使用情境：
        /// - 權限變更後
        /// - 敏感操作前
        /// - 定期安全檢查
        /// 
        /// 注意：此方法會清除並重建 Session，但保留用戶資料
        /// </summary>
        protected void RegenerateSessionId()
        {
            try
            {
                // 暫存重要資料
                var userId = HttpContext.Session.GetString("_SessionUserId");
                var userAgent = HttpContext.Session.GetString("_SessionUserAgent");
                var realIp = HttpContext.Session.GetString("_SessionRealIp");

                // 清除舊 Session
                HttpContext.Session.Clear();

                // 強制生成新 Session ID
                HttpContext.Session.CommitAsync().GetAwaiter().GetResult();

                // 恢復資料（使用新的時間戳）
                if (!string.IsNullOrEmpty(userId))
                {
                    HttpContext.Session.SetString("_SessionUserId", userId);
                    HttpContext.Session.SetString("_SessionUserIdentifier", $"{userId}_{DateTime.UtcNow.Ticks}");
                    HttpContext.Session.SetString("_SessionCreatedAt", DateTime.UtcNow.ToString("O"));
                    HttpContext.Session.SetString("_SessionUserAgent", userAgent ?? "");
                    HttpContext.Session.SetString("_SessionRealIp", realIp ?? "");
                }

                System.Diagnostics.Debug.WriteLine("[RegenerateSessionId] ? Session ID 已重新生成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerateSessionId] ? 重新生成失敗: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 連接池操作

        /// <summary>
        /// 從連接池獲取 CRM 連接
        /// </summary>
        /// <returns>IOrganizationService 實例</returns>
        /// <exception cref="TimeoutException">連接池已滿且等待超時</exception>
        /// <exception cref="InvalidOperationException">連接池未正確初始化</exception>
        protected IOrganizationService GetConnection()
        {
            try
            {
                if (_connectionPool == null)
                {
                    throw new InvalidOperationException("連接池未初始化");
                }

                var connection = _connectionPool.AcquireConnection();
                
                if (connection == null)
                {
                    throw new InvalidOperationException("無法從連接池獲取有效連接");
                }

                return connection;
            }
            catch (TimeoutException)
            {
                // 連接池已滿，記錄日誌
                System.Diagnostics.Debug.WriteLine($"[GetConnection] 連接池已滿，等待超時");
                throw;
            }
            catch (Exception ex)
            {
                // 其他異常
                System.Diagnostics.Debug.WriteLine($"[GetConnection] 獲取連接失敗: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 歸還連接到連接池
        /// </summary>
        /// <param name="connection">要歸還的連接</param>
        protected void ReleaseConnection(IOrganizationService connection)
        {
            try
            {
                if (connection == null)
                {
                    // 連接為 null，不需要歸還
                    return;
                }

                if (_connectionPool == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ReleaseConnection] 連接池未初始化，無法歸還連接");
                    return;
                }

                _connectionPool.ReleaseConnection(connection);
            }
            catch (Exception ex)
            {
                // 歸還連接失敗不應該中斷業務邏輯
                System.Diagnostics.Debug.WriteLine($"[ReleaseConnection] 歸還連接失敗: {ex.Message}");
                
                // 記錄到追蹤日誌
                try
                {
                    ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1, 
                        $"歸還連接失敗: {ex.Message}");
                }
                catch
                {
                    // 追蹤失敗也不影響流程
                }
            }
        }

        /// <summary>
        /// 獲取連接池統計資訊
        /// 用於監控和除錯
        /// </summary>
        /// <returns>連接池統計資訊</returns>
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
                System.Diagnostics.Debug.WriteLine($"[GetConnectionPoolStats] 獲取統計資訊失敗: {ex.Message}");
                
                // 返回空統計資訊
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
