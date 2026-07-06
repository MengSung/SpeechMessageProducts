// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.Cache.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupController
// 主要成員：ClearMultiGroupCache、ClearIntegrateCache、CreateCacheOptions、GetCacheStatistics
// 引用命名空間：Microsoft.Extensions.Caching.Memory、System
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
