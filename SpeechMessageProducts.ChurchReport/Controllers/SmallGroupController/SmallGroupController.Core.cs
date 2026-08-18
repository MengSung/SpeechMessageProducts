// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.Core.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupController
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：ChurchReport.Models、ChurchReport.Services、ChurchReport.Tools、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory、System、ToolUtilityNameSpace.ConnectionOperations
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Services;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - 核心定義
    /// 負責處理小組週報、整合視圖、多組週報等相關功能
    ///
    /// 快取策略（混合式）：
    /// - 資料層：使用 IMemoryCache 快取 CRM 查詢結果（15分鐘TTL）
    /// - HTTP 層：不快取回應（NoStore），確保每次都從最新記憶體取得資料
    /// - 清理機制：日期變更時清除相關快取，確保資料一致性
    /// </summary>
    public partial class SmallGroupController : BaseChurchController
    {
        #region 快取設定常數

        // 快取鍵前綴
        private const string CACHE_KEY_PREFIX = "SmallGroup_";
        private const string CACHE_KEY_MULTI_CHART = CACHE_KEY_PREFIX + "MultiChart_";
        private const string CACHE_KEY_MULTI_GRID = CACHE_KEY_PREFIX + "MultiGrid_";
        private const string CACHE_KEY_INTEGRATE = CACHE_KEY_PREFIX + "Integrate_";

        // 快取過期時間（分鐘）
        private const int CACHE_DURATION_MINUTES = 15;

        // 快取優先順序
        private static readonly CacheItemPriority CACHE_PRIORITY = CacheItemPriority.Normal;

        #endregion

        #region 建構函式與欄位

        /// <summary>
        /// 記憶體快取服務（用於混合式快取策略）
        /// </summary>
        private readonly IMemoryCache _memoryCache;

        /// <summary>
        /// 小組快取管理服務（透過 DI 注入）
        /// </summary>
        private readonly ChurchReport.Services.Caching.ISmallGroupCacheManager _cacheManager;

        /// <summary>
        /// LINE binding notification service. Controller decides the flow; service owns profile lookup, message composition, and workflow send.
        /// </summary>
        private readonly IChurchReportLineBindingNotificationService _lineBindingNotificationService;

        /// <summary>
        /// 建立背景工作專用 DI scope 的工廠。背景上傳可能在 HTTP request 結束後仍執行，
        /// 因此 scope 必須由背景工作自行擁有並在工作完成時釋放，避免沿用 request scope
        /// 的 ToolUtility、CRM 連線或其他可變狀態而造成跨 request 資源洩漏。
        /// </summary>
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// 建立小組管理控制器。
        /// HTTP request 的服務由基底控制器持有；背景上傳另以 scopeFactory 建立
        /// operation scope，確保 request 結束後仍執行的工作不會持有已釋放的
        /// ToolUtility 或 CRM 連線，且工作結束時資源一定歸還。
        /// </summary>
        /// <param name="httpContextAccessor">目前 HTTP request 的上下文存取器。</param>
        /// <param name="memoryCache">網站共用的記憶體快取。</param>
        /// <param name="toolUtilityProvider">目前 request scope 的 ToolUtility 提供者。</param>
        /// <param name="connectionPool">CRM 連線池，負責租約的取得與歸還。</param>
        /// <param name="inMemoryContext">目前 request 的小組資料上下文。</param>
        /// <param name="cacheManager">小組畫面快取管理服務。</param>
        /// <param name="lineBindingNotificationService">LINE 綁定通知工作流程服務。</param>
        /// <param name="scopeFactory">建立背景工作獨立 DI scope 的工廠。</param>
        public SmallGroupController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IInMemoryDataContext inMemoryContext,
            ChurchReport.Services.Caching.ISmallGroupCacheManager cacheManager,
            IChurchReportLineBindingNotificationService lineBindingNotificationService,
            IServiceScopeFactory scopeFactory)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool, inMemoryContext)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _lineBindingNotificationService = lineBindingNotificationService ?? throw new ArgumentNullException(nameof(lineBindingNotificationService));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        #endregion
    }
}
