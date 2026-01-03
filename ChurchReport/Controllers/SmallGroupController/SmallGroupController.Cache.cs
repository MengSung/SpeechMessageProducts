using Microsoft.Extensions.Caching.Memory;
using System;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - 快取管理
    /// </summary>
    public partial class SmallGroupController
    {
        #region 快取管理

        /// <summary>
        /// 清除多小組相關的所有快取
        /// </summary>
        private void ClearMultiGroupCache()
        {
            try
            {
                var chartCacheKey = $"{CACHE_KEY_MULTI_CHART}{InMemoryContext.ListManager.m_SelectDate:yyyyMMdd}";
                _memoryCache?.Remove(chartCacheKey);
                
                var gridCacheKey = $"{CACHE_KEY_MULTI_GRID}{InMemoryContext.ListManager.m_SelectDate:yyyyMMdd}";
                _memoryCache?.Remove(gridCacheKey);
                
                System.Diagnostics.Debug.WriteLine($"[ClearMultiGroupCache] 已清除快取鍵: {chartCacheKey}, {gridCacheKey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClearMultiGroupCache] 清除快取失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 清除整合視圖相關的快取
        /// </summary>
        private void ClearIntegrateCache(string listId = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(listId))
                {
                    var cacheKey = $"{CACHE_KEY_INTEGRATE}{listId}_{InMemoryContext.ListManager.m_SelectDate:yyyyMMdd}";
                    _memoryCache?.Remove(cacheKey);
                    System.Diagnostics.Debug.WriteLine($"[ClearIntegrateCache] 已清除快取鍵: {cacheKey}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClearIntegrateCache] 清除快取失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立快取選項
        /// </summary>
        private MemoryCacheEntryOptions CreateCacheOptions()
        {
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_DURATION_MINUTES),
                Priority = CACHE_PRIORITY,
                Size = 1
            };
        }

        #endregion
    }
}
