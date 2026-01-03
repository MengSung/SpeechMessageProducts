using ChurchReport.Models;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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

        public SmallGroupController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        #endregion
    }
}
