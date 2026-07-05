// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Caching/SmallGroupCacheManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class SmallGroupCacheManager
// 主要成員：GetMultiGroupChartCacheKey、GetMultiGroupGridCacheKey、GetIntegrateCacheKey、ClearMultiGroupCache、ClearIntegrateCache、ClearSmallGroupCache、ClearAllSmallGroupCache、CacheExists、GetCacheStatistics、RemoveCacheKey
// 引用命名空間：Microsoft.Extensions.Caching.Memory、Microsoft.Extensions.Logging、System、System.Collections.Concurrent
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace ChurchReport.Services.Caching
{
    /// <summary>
    /// 小組快取管理實作
    /// 提供小組相關資料的快取管理功能
    /// 使用 IMemoryCache 進行快取儲存
    /// </summary>
    public class SmallGroupCacheManager : ISmallGroupCacheManager
    {
        #region 常數定義

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

        #region 私有欄位

        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<SmallGroupCacheManager> _logger;

        // 快取統計（線程安全）
        private readonly ConcurrentDictionary<string, DateTime> _cacheKeys = new();
        private long _hitCount = 0;
        private long _missCount = 0;
        private long _clearCount = 0;

        #endregion

        #region 建構函式

        /// <summary>
        /// 建構函式
        /// </summary>
        /// <param name="memoryCache">記憶體快取服務</param>
        /// <param name="logger">日誌服務</param>
        public SmallGroupCacheManager(
            IMemoryCache memoryCache,
            ILogger<SmallGroupCacheManager> logger)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region 快取鍵生成

        /// <summary>
        /// 取得多小組圖表快取鍵
        /// </summary>
        public string GetMultiGroupChartCacheKey(DateTime selectedDate, string account = null)
        {
            var accountSuffix = !string.IsNullOrEmpty(account) ? $"_{account}" : "";
            return $"{CACHE_KEY_MULTI_CHART}{selectedDate:yyyyMMdd}{accountSuffix}";
        }

        /// <summary>
        /// 取得多小組列表快取鍵
        /// </summary>
        public string GetMultiGroupGridCacheKey(DateTime selectedDate, string account = null)
        {
            var accountSuffix = !string.IsNullOrEmpty(account) ? $"_{account}" : "";
            return $"{CACHE_KEY_MULTI_GRID}{selectedDate:yyyyMMdd}{accountSuffix}";
        }

        /// <summary>
        /// 取得整合視圖快取鍵
        /// </summary>
        public string GetIntegrateCacheKey(string listId, DateTime selectedDate)
        {
            return $"{CACHE_KEY_INTEGRATE}{listId}_{selectedDate:yyyyMMdd}";
        }

        #endregion

        #region 快取清除

        /// <summary>
        /// 清除多小組相關的所有快取
        /// </summary>
        public void ClearMultiGroupCache(DateTime selectedDate, string account = null)
        {
            try
            {
                var chartCacheKey = GetMultiGroupChartCacheKey(selectedDate, account);
                var gridCacheKey = GetMultiGroupGridCacheKey(selectedDate, account);

                RemoveCacheKey(chartCacheKey);
                RemoveCacheKey(gridCacheKey);

                System.Threading.Interlocked.Increment(ref _clearCount);

                _logger.LogInformation(
                    "[ClearMultiGroupCache] 已清除快取鍵: {ChartKey}, {GridKey}",
                    chartCacheKey, gridCacheKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ClearMultiGroupCache] 清除快取失敗: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 清除整合視圖相關的快取
        /// </summary>
        public void ClearIntegrateCache(string listId, DateTime selectedDate)
        {
            try
            {
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.LogWarning("[ClearIntegrateCache] listId 為空，跳過清除");
                    return;
                }

                var cacheKey = GetIntegrateCacheKey(listId, selectedDate);
                RemoveCacheKey(cacheKey);

                System.Threading.Interlocked.Increment(ref _clearCount);

                _logger.LogInformation(
                    "[ClearIntegrateCache] 已清除快取鍵: {CacheKey}", cacheKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ClearIntegrateCache] 清除快取失敗: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 清除特定小組的所有快取
        /// </summary>
        public void ClearSmallGroupCache(string listId, DateTime selectedDate, string account = null)
        {
            ClearMultiGroupCache(selectedDate, account);
            ClearIntegrateCache(listId, selectedDate);

            _logger.LogInformation(
                "[ClearSmallGroupCache] 已清除小組 {ListId} 的所有快取", listId);
        }

        /// <summary>
        /// 清除所有小組相關的快取
        /// </summary>
        public void ClearAllSmallGroupCache()
        {
            try
            {
                var keysToRemove = System.Linq.Enumerable.ToArray(_cacheKeys.Keys);

                foreach (var key in keysToRemove)
                {
                    RemoveCacheKey(key);
                }

                System.Threading.Interlocked.Increment(ref _clearCount);

                _logger.LogInformation(
                    "[ClearAllSmallGroupCache] 已清除所有快取，共 {Count} 筆",
                    keysToRemove.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ClearAllSmallGroupCache] 清除所有快取失敗: {Message}", ex.Message);
            }
        }

        #endregion

        #region 快取檢查

        /// <summary>
        /// 檢查快取是否存在
        /// </summary>
        public bool CacheExists(string cacheKey)
        {
            try
            {
                var exists = _memoryCache.TryGetValue(cacheKey, out _);

                // 更新統計
                if (exists)
                {
                    System.Threading.Interlocked.Increment(ref _hitCount);
                }
                else
                {
                    System.Threading.Interlocked.Increment(ref _missCount);
                }

                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[CacheExists] 檢查快取失敗: {CacheKey}", cacheKey);
                return false;
            }
        }

        #endregion

        #region 快取統計

        /// <summary>
        /// 取得快取統計資訊
        /// </summary>
        public SmallGroupCacheStatistics GetCacheStatistics()
        {
            return new SmallGroupCacheStatistics
            {
                HitCount = _hitCount,
                MissCount = _missCount,
                TotalCacheCount = _cacheKeys.Count,
                ClearCount = _clearCount
            };
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 移除快取鍵
        /// </summary>
        private void RemoveCacheKey(string cacheKey)
        {
            _memoryCache.Remove(cacheKey);
            _cacheKeys.TryRemove(cacheKey, out _);
        }

        /// <summary>
        /// 建立快取選項
        /// </summary>
        public MemoryCacheEntryOptions CreateCacheOptions()
        {
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_DURATION_MINUTES),
                Priority = CACHE_PRIORITY,
                Size = 1,
                // 註冊清除回調
                PostEvictionCallbacks =
                {
                    new PostEvictionCallbackRegistration
                    {
                        EvictionCallback = (key, value, reason, state) =>
                        {
                            if (key is string cacheKey)
                            {
                                _cacheKeys.TryRemove(cacheKey, out _);
                                _logger.LogDebug(
                                    "[Cache Evicted] {CacheKey}, Reason: {Reason}",
                                    cacheKey, reason);
                            }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 註冊快取鍵（用於追蹤）
        /// </summary>
        public void RegisterCacheKey(string cacheKey)
        {
            _cacheKeys.TryAdd(cacheKey, DateTime.Now);
        }

        #endregion
    }
}
