using Microsoft.Extensions.Caching.Memory;
using System;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - 快取管理
    /// 使用 ISmallGroupCacheManager 進行快取操作
    /// </summary>
    public partial class SmallGroupController
    {
        #region 快取管理

        /// <summary>
        /// 清除多小組相關的所有快取
        /// 委派給 ISmallGroupCacheManager 處理
        /// </summary>
        private void ClearMultiGroupCache()
        {
            try
            {
                var selectedDate = InMemoryContext.ListManager.m_SelectDate;
                var account = InMemoryContext.ListManager.m_Account;
                
                _cacheManager.ClearMultiGroupCache(selectedDate, account);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClearMultiGroupCache] 清除快取失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 清除整合視圖相關的快取
        /// 委派給 ISmallGroupCacheManager 處理
        /// </summary>
        /// <param name="listId">清單 ID</param>
        private void ClearIntegrateCache(string listId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(listId))
                {
                    System.Diagnostics.Debug.WriteLine("[ClearIntegrateCache] listId 為空，跳過清除");
                    return;
                }

                var selectedDate = InMemoryContext.ListManager.m_SelectDate;
                _cacheManager.ClearIntegrateCache(listId, selectedDate);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClearIntegrateCache] 清除快取失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立快取選項
        /// 委派給 ISmallGroupCacheManager 處理
        /// </summary>
        private MemoryCacheEntryOptions CreateCacheOptions()
        {
            // 使用 CacheManager 提供的標準快取選項
            if (_cacheManager is ChurchReport.Services.Caching.SmallGroupCacheManager concreteManager)
            {
                return concreteManager.CreateCacheOptions();
            }

            // 備用方案：直接建立
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_DURATION_MINUTES),
                Priority = CACHE_PRIORITY,
                Size = 1
            };
        }

        /// <summary>
        /// 取得快取統計資訊
        /// 用於監控和除錯
        /// </summary>
        /// <returns>快取統計資訊</returns>
        private ChurchReport.Services.Caching.SmallGroupCacheStatistics GetCacheStatistics()
        {
            return _cacheManager.GetCacheStatistics();
        }

        #endregion
    }
}
