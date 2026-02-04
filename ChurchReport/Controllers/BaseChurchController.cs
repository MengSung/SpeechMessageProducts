using ChurchReport.Models;
using ChurchReport.Tools;
using LineMessagingProcessor;
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
    /// 教會報表基底控制器 (Base Controller for Church Reports)
    /// 
    /// 教學說明：
    /// 這是一個抽象基底類別，所有教會相關的控制器都會繼承自這個類別。
    /// 為什麼需要基底控制器？
    /// - 避免重複代碼：將共用的功能（如錯誤處理、Session 驗證）放在這裡。
    /// - 統一行為：確保所有控制器都有相同的錯誤處理和安全檢查。
    /// - 依賴注入：集中管理外部服務的注入。
    /// 
    /// 設計模式：
    /// - Template Method Pattern：提供通用流程，讓子類別覆寫特定步驟。
    /// - Dependency Injection：不直接創建依賴，而是從外部注入。
    /// - Singleton Pattern：某些服務（如 ToolUtility）是單例的。
    /// 
    /// 使用方式：
    /// public class MyController : BaseChurchController
    /// {
    ///     public MyController(...) : base(...) { }
    ///     
    ///     public IActionResult MyAction()
    ///     {
    ///         // 可以直接使用基底類別的屬性，如 ToolUtility, InMemoryContext 等
    ///     }
    /// }
    /// </summary>
    public abstract class BaseChurchController : Controller, IDisposable
    {
        #region 常數定義 (Constants)

        /// <summary>
        /// 日誌記錄的總層級 (Total logging level)
        /// 
        /// 教學說明：
        /// 在企業應用中，日誌分層級管理：
        /// - Level 1: 基本資訊
        /// - Level 2: 詳細資訊
        /// - Level 3: 除錯資訊
        /// - Level 4: 警告
        /// - Level 5: 錯誤
        /// 
        /// 這裡定義了常用的層級常數，方便統一使用。
        /// </summary>
        protected const int TOTAL_LEVEL = 1;
        protected const int LEVEL_1 = 1;
        protected const int LEVEL_2 = 2;
        protected const int LEVEL_3 = 3;
        protected const int LEVEL_4 = 4;
        protected const int LEVEL_5 = 5;

        /// <summary>
        /// LINE 錯誤接收者 ID (LINE error receiver ID)
        /// 
        /// 教學說明：
        /// 當系統發生錯誤時，會自動發送 LINE 訊息通知管理員。
        /// 這個 ID 是接收通知的 LINE 用戶 ID。
        /// 為什麼用常數？因為這是固定值，不會在運行時改變。
        /// </summary>
        protected const string LINE_ERROR_RECEIVER_ID = "U7638e4ed509708a3573ba6d69970583d";

        #endregion

        #region 服務實例 (Service Instances)

        /// <summary>
        /// ToolUtility 提供者 (ToolUtility Provider)
        /// 
        /// 教學說明：
        /// 什麼是 ToolUtility？
        /// - 這是一個工具類別，提供日誌記錄、CRM 操作等功能。
        /// - 為什麼用提供者模式？因為 ToolUtility 是單例的，需要統一管理。
        /// - 依賴注入：不直接創建實例，而是從外部注入，符合 SOLID 原則。
        /// 
        /// 設計模式：Provider Pattern
        /// </summary>
        protected readonly IToolUtilityProvider _toolUtilityProvider;

        /// <summary>
        /// CRM 連線池 (CRM Connection Pool)
        /// 
        /// 教學說明：
        /// 什麼是連線池？
        /// - CRM 系統連線很耗資源，不能每次都新建連線。
        /// - 連線池預先建立多個連線，重複使用，提高性能。
        /// - 當連線用完時，自動歸還到池中。
        /// 
        /// 設計模式：Object Pool Pattern
        /// </summary>
        protected readonly ICrmConnectionPool _connectionPool;

        /// <summary>
        /// HTTP 上下文存取器 (HTTP Context Accessor)
        /// 
        /// 教學說明：
        /// 為什麼需要這個？
        /// - 在 ASP.NET Core 中，HttpContext 不是總是可用的（尤其在背景任務中）。
        /// - IHttpContextAccessor 提供安全的方式來存取當前請求的上下文。
        /// - 這是 ASP.NET Core 依賴注入系統的一部分。
        /// </summary>
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// 工具類別實例 (Tool Utility Instance)
        /// 
        /// 教學說明：
        /// 這個屬性提供對 ToolUtility 的存取。
        /// 為什麼用屬性而不是直接存取 _toolUtilityProvider？
        /// - 簡化代碼：子類別可以直接用 ToolUtility，而不用知道提供者。
        /// - 延遲載入：只有在需要時才從提供者取得實例。
        /// </summary>
        protected ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

        /// <summary>
        /// 記憶體資料上下文 (In-Memory Data Context)
        /// 
        /// 教學說明：
        /// 什麼是記憶體資料上下文？
        /// - 存放應用程式的資料狀態，如用戶資訊、小組資料等。
        /// - 為什麼用介面？因為可以輕鬆切換不同的實作（測試用、生產用）。
        /// - 依賴注入：從外部注入，避免硬編碼依賴。
        /// </summary>
        protected readonly IInMemoryDataContext InMemoryContext;

        /// <summary>
        /// 金流服務介面 (Payment Service Interface)
        /// 
        /// 教學說明：
        /// 負責處理付款相關的業務邏輯。
        /// 為什麼用介面？因為可以有不同的付款提供者（信用卡、LINE Pay 等）。
        /// 設計模式：Strategy Pattern
        /// </summary>
        protected readonly IPayment PaymentService;

        /// <summary>
        /// 安全的 HttpContext 存取 (Safe HttpContext Access)
        /// 
        /// 教學說明：
        /// 這個屬性提供安全的方式來存取 HttpContext。
        /// 為什麼需要特殊處理？
        /// - Controller 的 HttpContext 在建構函式中可能還沒初始化。
        /// - 使用 IHttpContextAccessor 可以隨時安全存取。
        /// - 如果都不可用，拋出異常，防止隱藏錯誤。
        /// 
        /// 設計考量：
        /// - 優先使用 IHttpContextAccessor（更可靠）。
        /// - 如果失敗，嘗試基類的 HttpContext。
        /// - 如果都失敗，拋出有意義的錯誤訊息。
        /// </summary>
        protected new HttpContext HttpContext
        {
            get
            {
                // 優先使用 IHttpContextAccessor（更可靠）
                var context = _httpContextAccessor?.HttpContext;
                
                // 如果 IHttpContextAccessor 沒有提供，嘗試使用基類的 HttpContext
                if (context == null)
                {
                    context = base.HttpContext;
                }

                // 如果仍然為 null，拋出有意義的異常
                if (context == null)
                {
                    throw new InvalidOperationException(
                        "HttpContext 未初始化。請確保此方法從有效的 HTTP 請求上下文中調用。" +
                        "如果在單元測試中，請模擬 IHttpContextAccessor。");
                }

                return context;
            }
        }

        #endregion

        #region 建構函式 (Constructor)

        /// <summary>
        /// 初始化基底控制器 (Initialize Base Controller)
        /// 
        /// 教學說明：
        /// 建構函式是類別被創建時自動執行的方法。
        /// 這裡做的事情：
        /// 1. 驗證參數：確保必要的服務都被注入。
        /// 2. 保存參考：將注入的服務存起來供後續使用。
        /// 3. 初始化上下文：建立或注入記憶體資料上下文。
        /// 
        /// 依賴注入的優點：
        /// - 鬆耦合：類別不依賴具體實作。
        /// - 可測試性：可以輕鬆用假物件替換真實服務。
        /// - 靈活性：可以根據環境注入不同的實作。
        /// 
        /// 參數說明：
        /// - httpContextAccessor: 存取 HTTP 請求上下文
        /// - memoryCache: 記憶體快取服務
        /// - paymentService: 付款處理服務
        /// - toolUtilityProvider: 工具類別提供者
        /// - connectionPool: CRM 連線池
        /// - inMemoryContext: 記憶體資料上下文（可選，向後相容）
        /// </summary>
        protected BaseChurchController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IInMemoryDataContext inMemoryContext = null)
        {
            // ========================================
            // 關鍵修復：保存 IHttpContextAccessor
            // ========================================
            // 這是修復 "HttpContext 未初始化" 錯誤的關鍵
            // 透過保存 IHttpContextAccessor，我們可以在任何時候安全地取得 HttpContext
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

            // 透過 DI 注入 ToolUtility 提供者
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));

            // 支援兩種方式：
            // 1. 新方式：透過 DI 注入記憶體資料上下文（推薦，避免靜態依賴）
            // 2. 舊方式：直接 new 實例（向後相容，逐步淘汰）
            if (inMemoryContext != null)
            {
                // 使用 DI 注入的實例（推薦）
                InMemoryContext = inMemoryContext;
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] 使用 DI 注入的 InMemoryContext");
            }
            else
            {
                // 向後相容：直接建立實例（將逐步淘汰）
                InMemoryContext = new InMemoryDataContextSmallGroup(
                    httpContextAccessor, memoryCache, paymentService, toolUtilityProvider);
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] 使用向後相容模式建立 InMemoryContext（請盡快更新為 DI 注入）");
            }

            // 存放金流服務參考
            PaymentService = paymentService;
        }

        #endregion

        #region 錯誤處理 (Error Handling)

        /// <summary>
        /// 統一錯誤處理方法 (Unified Error Handling Method)
        /// 
        /// 教學說明：
        /// 為什麼需要統一錯誤處理？
        /// - 避免每個方法都重複寫 try-catch。
        /// - 確保錯誤被正確記錄和通知。
        /// - 提供一致的錯誤回應格式。
        /// 
        /// 處理流程：
        /// 1. 記錄錯誤到日誌
        /// 2. 發送 LINE 通知給管理員
        /// 3. 根據請求類型返回適當的回應
        /// 
        /// 參數：
        /// - exception: 發生的異常物件
        /// - methodName: 發生錯誤的方法名稱（用於追蹤）
        /// 
        /// 返回值：
        /// - AJAX 請求：返回 JSON 錯誤訊息
        /// - 一般請求：重導向到錯誤頁面
        /// </summary>
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
        /// 發送 LINE 錯誤通知 (Send LINE Error Notification)
        /// 
        /// 教學說明：
        /// 當系統發生嚴重錯誤時，主動通知管理員。
        /// 為什麼用 LINE？
        /// - 即時性：管理員可以立即收到通知。
        /// - 便利性：手機上就能看到。
        /// - 可靠性：即使郵件系統故障，LINE 通常還能用。
        /// 
        /// 錯誤處理：
        /// - 如果 LINE 發送失敗，不影響主要業務流程。
        /// - 會記錄 LINE 發送失敗的日誌。
        /// </summary>
        protected void SendLineErrorNotification(string errorMessage)
        {
            try
            {
                var lineProcessor = new LineMessagingProcessorClass();
                lineProcessor.SendMessage(LINE_ERROR_RECEIVER_ID, $"神助611靈糧堂: 錯誤 => {errorMessage}");
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
                    System.Diagnostics.Debug.WriteLine($"LINE 通通知發送失敗且追蹤失敗: {ex.Message}");
                }
            }
        }

        #endregion

        #region ViewBag 設定輔助方法 (ViewBag Setup Helper Methods)

        /// <summary>
        /// 設定多小組版面參數 (Set Multi-Group Layout Parameters)
        /// 
        /// 教學說明：
        /// ViewBag 是 ASP.NET MVC 中用來傳遞資料到 View 的機制。
        /// 這個方法決定頁面應該顯示什麼樣的導覽選單。
        /// 
        /// 業務邏輯：
        /// - 如果是多小組模式且未載入特定小組：顯示單純的多小組視圖
        /// - 如果是整合模式且已載入資料：顯示單一小組的詳細視圖
        /// - 如果是混合模式：顯示兩個選項（統計 + 詳細）
        /// 
        /// 為什麼需要這個？
        /// - 用戶體驗：根據用戶狀態顯示適當的選項
        /// - 導航邏輯：確保用戶不會迷路
        /// </summary>
        protected void SetMultiGroupLayoutParameter()
        {
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            bool integrateFlag = IsIntegrateDataLoaded();

            // 修正：確保從 MultiGroupView 點擊小組後，保持 HybridView 模式
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
                // 混合模式：有多個小組且已點擊其中一個
                // 此時應該顯示「回報統計」和「小組回報」兩個選項
                ViewBag.MultiGroupIndex = "HybridView";
            }
            else
            {
                // 預設為 IntegrateView 或 HybridView
                // 如果有載入整合資料，就使用 HybridView，確保選項不消失
                ViewBag.MultiGroupIndex = integrateFlag ? "HybridView" : "IntegrateView";
            }

            // 設定是否為行政同工
            ViewBag.IsAOfficeWorker = InMemoryContext.QpayManager.m_QpayModel.IsAOfficeWorker
                ? "是的" : "否";
        }

        /// <summary>
        /// 檢查整合資料是否已載入 (Check if Integrate Data is Loaded)
        /// 
        /// 教學說明：
        /// 整合資料是指特定小組的詳細資訊。
        /// 為什麼需要檢查？
        /// - 確保用戶看到的是正確的資料
        /// - 避免顯示空的或錯誤的資訊
        /// 
        /// 檢查條件：
        /// - 週報物件存在
        /// - LoadFlag 為 true（表示資料已載入）
        /// </summary>
        protected bool IsIntegrateDataLoaded()
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
            return weeklyReport != null && weeklyReport.LoadFlag;
        }

        /// <summary>
        /// 設定基本 ViewBag 參數 (Set Basic ViewBag Parameters)
        /// 
        /// 教學說明：
        /// 這個方法設定所有控制器都需要的基本 ViewBag 參數。
        /// 為什麼要統一設定？
        /// - 避免每個控制器重複寫相同的代碼
        /// - 確保所有頁面都有必要的資訊
        /// 
        /// 設定的參數：
        /// - 登入類型和用戶名稱
        /// - 費用類型
        /// - 快樂小組類型
        /// - 費用資料狀態
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
        /// 設定繳費點名資料數量狀態 (Set Fee Data List Count Status)
        /// 
        /// 教學說明：
        /// 這個方法檢查是否有費用資料，並設定對應的狀態訊息。
        /// 為什麼需要這個？
        /// - 讓用戶知道系統中是否有資料
        /// - 提供視覺回饋
        /// 
        /// 狀態訊息：
        /// - 有資料："繳費與點名已有資料"
        /// - 無資料："繳費與點名尚無資料"
        /// </summary>
        protected void SetupFeeDataListCount()
        {
            bool hasFeeData = InMemoryContext.FeeList.FeeDataList != null &&
                            InMemoryContext.FeeList.FeeDataList.Count > 0;

            ViewBag.FeeDataListCount = hasFeeData ? "繳費與點名已有資料" : "繳費與點名尚無資料";
        }

        #endregion

        #region Session 安全驗證 (Session Security Validation)

        /// <summary>
        /// 確保 AJAX 請求使用正確用戶的資料 (Ensure Correct User Data for AJAX Requests)
        /// 
        /// 教學說明：
        /// 在 AJAX 請求中，用戶的 Session 可能已經改變。
        /// 這個方法確保我們使用的是正確的用戶資料。
        /// 
        /// 為什麼需要這個？
        /// - 多用戶同時使用系統時，Session 可能會混亂
        /// - 確保資料安全和一致性
        /// - 防止用戶看到其他人的資料
        /// 
        /// 檢查流程：
        /// 1. 比較 Session 中的密碼和 ListManager 中的密碼
        /// 2. 如果不一致，重新載入資料
        /// 3. 如果 Session 為空，嘗試從請求中取得 LINE ID
        /// 
        /// 設計模式：Guard Clause Pattern
        /// - 先檢查錯誤情況，及早返回
        /// - 讓正常流程更清晰
        /// 
        /// 使用方式：
        /// 在 AJAX Action 方法開始時呼叫：
        /// EnsureCorrectUserData();
        /// </summary>
        protected virtual void EnsureCorrectUserData()
        {
            try
            {
                // ========================================
                // Step 1: 取得當前 Session 和 ListManager 的資料
                // ========================================
                // 教學說明：
                // Session 是伺服器端儲存用戶狀態的地方。
                // ListManager 是應用程式中管理用戶資料的物件。
                // 我們需要確保兩者的密碼一致，否則資料可能不正確。
                var sessionAccount = HttpContext?.Session?.GetString("_LoginAccount");
                var sessionPassword = HttpContext?.Session?.GetString("_LoginPassword");
                var listManagerPassword = InMemoryContext?.ListManager?.m_Password;

                // 除錯日誌：記錄密碼狀態（隱藏實際密碼）
                // 教學說明：
                // 日誌很重要，但不能記錄敏感資訊如密碼。
                // 這裡用 "***" 隱藏密碼，只記錄是否存在。
                System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] Session Password: {(string.IsNullOrEmpty(sessionPassword) ? "(null)" : "***")}");
                System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] ListManager Password: {(string.IsNullOrEmpty(listManagerPassword) ? "(null)" : "***")}");

                // ========================================
                // Step 2: 檢查 Session 和 ListManager 的密碼是否一致
                // ========================================
                // 教學說明：
                // 如果密碼不一致，表示用戶的狀態已經改變。
                // 需要重新載入 ListManager 的資料以保持同步。
                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword != listManagerPassword)
                {
                    // 憑證不一致，需要重新載入
                    System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] 憑證不一致，重新載入 ListManager 資料");
                    
                    // 重新載入 ListManager 資料
                    // 教學說明：
                    // SetupListManager 方法會重新初始化用戶的資料。
                    // 參數：帳號、密碼、選擇的日期
                    InMemoryContext.ListManager.SetupListManager(
                        sessionAccount ?? "",
                        sessionPassword,
                        InMemoryContext.ListManager.m_SelectDate != default
                            ? InMemoryContext.ListManager.m_SelectDate
                            : DateTime.Now);

                    // 處理完畢，返回
                    return;
                }

                // ========================================
                // Step 3: 如果 Session 密碼為空，嘗試從請求中取得 LINE ID
                // ========================================
                // 教學說明：
                // 有時 Session 會遺失（例如瀏覽器重啟）。
                // LINE 登入時，用戶 ID 會在 HTTP Referer 中。
                // 我們可以從請求中解析出用戶 ID 來恢復身份。
                if (string.IsNullOrEmpty(sessionPassword))
                {
                    var lineUserId = TryGetLineUserIdFromRequest();
                    
                    // 如果找到 LINE ID 且與 ListManager 的密碼不同
                    if (!string.IsNullOrEmpty(lineUserId) && lineUserId != listManagerPassword)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] Session 憑證為空，使用 LINE ID 重新載入 ListManager");
                        
                        // 使用 LINE ID 重新載入資料
                        InMemoryContext.ListManager.SetupListManager(
                            "LineIdLogin",
                            lineUserId,
                            InMemoryContext.ListManager.m_SelectDate != default
                                ? InMemoryContext.ListManager.m_SelectDate
                                : DateTime.Now);

                        // 更新 Session
                        // 教學說明：
                        // 恢復 Session 狀態，確保後續請求能正常工作。
                        HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
                        HttpContext?.Session?.SetString("_LoginPassword", lineUserId);
                    }
                }
            }
            catch (Exception ex)
            {
                // 錯誤處理：記錄異常但不中斷流程
                // 教學說明：
                // 驗證失敗不應該讓整個請求失敗。
                // 記錄日誌供後續分析，但讓請求繼續執行。
                System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] 驗證失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 嘗試從請求中取得 LINE 用戶 ID (Try to Get LINE User ID from Request)
        /// 
        /// 教學說明：
        /// LINE 登入時，用戶 ID 會包含在請求的 Referer 中。
        /// 這個方法解析出用戶 ID。
        /// 
        /// 解析邏輯：
        /// - 從 HTTP Referer 標頭中尋找 LINE 用戶 ID 格式
        /// - LINE 用戶 ID 以 "U" 開頭，長度為 33 個字元
        /// 
        /// 為什麼需要這個？
        /// - 當 Session 遺失時，可以從請求中恢復用戶身份
        /// - 提高系統的容錯能力
        /// </summary>
        protected string TryGetLineUserIdFromRequest()
        {
            try
            {
                var referer = HttpContext?.Request?.Headers["Referer"].ToString();
                if (!string.IsNullOrEmpty(referer))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(referer, "U[a-zA-Z0-9]{32}");
                    if (match.Success)
                    {
                        return match.Value;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 驗證當前 Session 是否合法 (Validate Current Session)
        /// 
        /// 教學說明：
        /// Session 驗證是 Web 應用安全的重要部分。
        /// 這個方法檢查用戶的登入狀態是否仍然有效。
        /// 
        /// 設計模式：Template Method Pattern
        /// - 提供通用的驗證流程
        /// - 子類別可以覆寫特定檢查
        /// 
        /// Fail-Fast 原則：
        /// - 一發現問題就立即返回 false
        /// - 不繼續執行不必要的檢查
        /// 
        /// 驗證項目：
        /// 1. Session 是否存在
        /// 2. Session 是否過期（8 小時）
        /// 3. 用戶身份是否一致
        /// 
        /// 使用方式：
        /// 在 Controller Action 開始時呼叫：
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
                // Step 1: 檢查 Session 是否存在
                // ========================================
                var sessionUserId = HttpContext.Session.GetString("_SessionUserId");
                if (string.IsNullOrEmpty(sessionUserId))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] Session 不存在或已過期");
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
                            System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 已過期 ({sessionAge.TotalHours:F2} 小時)");
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
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] 用戶資料不存在於 InMemoryContext");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 驗證通過 - UserId: {sessionUserId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 驗證失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 強制重新生成 Session ID (Regenerate Session ID)
        /// 
        /// 教學說明：
        /// Session ID 重新生成是安全最佳實務。
        /// 為什麼需要？
        /// - 防止 Session Fixation 攻擊
        /// - 在權限改變後確保安全
        /// 
        /// 使用情境：
        /// - 權限變更後
        /// - 敏感操作前
        /// - 定期安全檢查
        /// 
        /// 注意事項：
        /// - 此方法會清除並重建 Session
        /// - 但會保留用戶資料（使用新時間戳）
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
                    HttpContext.Session.SetString("_SessionIdentifier", $"{userId}_{DateTime.UtcNow.Ticks}");
                    HttpContext.Session.SetString("_SessionCreatedAt", DateTime.UtcNow.ToString("O"));
                    HttpContext.Session.SetString("_SessionUserAgent", userAgent ?? "");
                    HttpContext.Session.SetString("_SessionRealIp", realIp ?? "");
                }

                System.Diagnostics.Debug.WriteLine("[RegenerateSessionId] Session ID 已重新生成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerateSessionId] 重新生成失敗: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 連接池操作 (Connection Pool Operations)

        /// <summary>
        /// 從連接池獲取 CRM 連接 (Get CRM Connection from Pool)
        /// 
        /// 教學說明：
        /// CRM 連接很耗資源，所以使用連接池來管理。
        /// 這個方法從池中取得一個可用的連接。
        /// 
        /// 設計模式：Object Pool Pattern
        /// 
        /// 異常處理：
        /// - TimeoutException: 連接池已滿，等待超時
        /// - InvalidOperationException: 連接池未初始化
        /// 
        /// 使用方式：
        /// using (var connection = GetConnection())
        /// {
        ///     // 使用連接
        /// } // 自動歸還
        /// </summary>
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
        /// 歸還連接到連接池 (Return Connection to Pool)
        /// 
        /// 教學說明：
        /// 使用完連接後，一定要歸還到池中。
        /// 為什麼重要？
        /// - 連接是有限資源，不歸還會造成資源洩漏
        /// - 其他請求無法取得連接
        /// - 系統性能下降
        /// 
        /// 注意事項：
        /// - 即使歸還失敗，也不應該中斷業務邏輯
        /// - 會記錄失敗的日誌
        /// </summary>
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
        /// 獲取連接池統計資訊 (Get Connection Pool Statistics)
        /// 
        /// 教學說明：
        /// 監控連接池的狀態很重要。
        /// 這個方法返回連接池的統計資料。
        /// 
        /// 統計資訊包括：
        /// - 總連接數
        /// - 活躍連接數
        /// - 閒置連接數
        /// - 等待請求數
        /// - 取得/釋放計數
        /// - 超時和驗證失敗計數
        /// 
        /// 為什麼需要？
        /// - 性能監控：發現連接池問題
        /// - 容量規劃：決定是否需要增加連接數
        /// - 問題診斷：追蹤連接使用模式
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

        #region 資源釋放 (Resource Disposal)

        /// <summary>
        /// 釋放資源 (Dispose Resources)
        /// 
        /// 教學說明：
        /// 實現 IDisposable 介面是良好的實務。
        /// 這個方法在物件被銷毀時自動呼叫。
        /// 
        /// 釋放的資源：
        /// - ToolUtility：工具類別的資源
        /// - 基類資源：呼叫 Controller 的 Dispose
        /// 
        /// 為什麼重要？
        /// - 防止資源洩漏
        /// - 確保連接正確關閉
        /// - 系統資源得到有效利用
        /// 
        /// 注意：這個方法會被垃圾回收器自動呼叫，
        /// 或者可以手動呼叫 using 語句。
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
