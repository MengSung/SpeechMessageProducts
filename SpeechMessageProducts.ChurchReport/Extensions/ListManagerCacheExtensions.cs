/*
 * 檔案整體說明
 *
 * 目的與定位：ListManager 快取擴充方法 Phase 2.3: 為 ListManager 提供快取功能的擴充方法 使用情境: - 快取多小組清單資料 (MultiGroupList) - 快取圖表資料 
 * (MultiGroupChartDataList) - 支援按帳號+日期的快取策略
 *
 * 主要功能與流程：
 * 本檔宣告 ListManagerCacheExtensions、CachedListManagerData。
 * 主要操作入口包含 SetupListManagerWithCache、SetupIntegrateDataWithCache、InvalidateCache、RestoreFromCache、CreateCacheData。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using ChurchReport.Models;
using ChurchReport.Services.Caching;

namespace ChurchReport.Extensions
{
    /// <summary>
    /// ListManager 快取擴充方法
    /// Phase 2.3: 為 ListManager 提供快取功能的擴充方法
    ///
    /// 使用情境:
    /// - 快取多小組清單資料 (MultiGroupList)
    /// - 快取圖表資料 (MultiGroupChartDataList)
    /// - 支援按帳號+日期的快取策略
    /// </summary>
    public static class ListManagerCacheExtensions
    {
        /// <summary>
        /// 使用快取設定 ListManager
        /// </summary>
        /// <param name="listManager">ListManager 實例</param>
        /// <param name="cacheService">快取服務</param>
        /// <param name="account">帳號</param>
        /// <param name="password">密碼</param>
        /// <param name="selectDate">選擇日期</param>
        /// <param name="forceRefresh">是否強制重新載入</param>
        public static void SetupListManagerWithCache(
            this ListManager listManager,
            ICacheService cacheService,
            string account,
            string password,
            DateTime selectDate,
            bool forceRefresh = false)
        {
            if (cacheService == null)
            {
                // 沒有快取服務時，使用原始方法
                listManager.SetupListManager(account, password, selectDate);
                return;
            }

            var cacheKey = CacheKeys.MultiGroupList(account, selectDate);

            // 強制重新載入時，先清除快取
            if (forceRefresh)
            {
                cacheService.Remove(cacheKey);
            }

            // 嘗試從快取取得
            if (!forceRefresh && cacheService.TryGet<CachedListManagerData>(cacheKey, out var cachedData))
            {
                // 從快取還原資料
                RestoreFromCache(listManager, cachedData, account, password, selectDate);
                System.Diagnostics.Debug.WriteLine($"[ListManager快取] 命中: {cacheKey}");
                return;
            }

            // 快取未命中，執行原始載入
            listManager.SetupListManager(account, password, selectDate);

            // 儲存到快取
            var dataToCache = CreateCacheData(listManager);
            cacheService.Set(
                cacheKey,
                dataToCache,
                CacheKeys.Expiration.MultiGroupList,
                TimeSpan.FromMinutes(15)); // 滑動過期 15 分鐘

            System.Diagnostics.Debug.WriteLine($"[ListManager快取] 建立: {cacheKey}");
        }

        /// <summary>
        /// 使用快取設定整合資料
        /// </summary>
        public static void SetupIntegrateDataWithCache(
            this ListManager listManager,
            ICacheService cacheService,
            string listEntityId,
            bool forceRefresh = false)
        {
            if (cacheService == null || string.IsNullOrEmpty(listEntityId))
            {
                listManager.SetupIntegrateData(listEntityId);
                return;
            }

            var cacheKey = CacheKeys.IntegrateData(listEntityId, listManager.m_SelectDate);

            // 強制重新載入時，先清除快取
            if (forceRefresh)
            {
                cacheService.Remove(cacheKey);
            }

            // 整合資料較為動態，快取時間較短
            // 目前先不快取整合資料，因為它包含可編輯的會員出席資料
            // 未來可考慮只快取唯讀部分
            listManager.SetupIntegrateData(listEntityId);
        }

        /// <summary>
        /// 清除 ListManager 相關快取
        /// </summary>
        public static void InvalidateCache(
            this ListManager listManager,
            ICacheService cacheService,
            string account = null)
        {
            if (cacheService == null) return;

            if (!string.IsNullOrEmpty(account))
            {
                // 清除特定帳號的快取
                cacheService.RemoveByPrefix($"{CacheKeys.MultiGroupListPrefix}{account}_");
            }
            else if (!string.IsNullOrEmpty(listManager.m_Account))
            {
                // 清除當前帳號的快取
                cacheService.RemoveByPrefix($"{CacheKeys.MultiGroupListPrefix}{listManager.m_Account}_");
            }

            // 清除整合資料快取
            if (!string.IsNullOrEmpty(listManager.ActiveListId))
            {
                cacheService.RemoveByPrefix($"{CacheKeys.IntegrateDataPrefix}{listManager.ActiveListId}_");
            }

            System.Diagnostics.Debug.WriteLine("[ListManager快取] 已清除相關快取");
        }

        #region 私有輔助方法

        /// <summary>
        /// 從快取還原資料
        /// </summary>
        private static void RestoreFromCache(
            ListManager listManager,
            CachedListManagerData cachedData,
            string account,
            string password,
            DateTime selectDate)
        {
            listManager.m_Account = account;
            listManager.m_Password = password;
            listManager.m_SelectDate = selectDate;
            listManager.LoginType = cachedData.LoginType;
            listManager.LoginFullName = cachedData.LoginFullName;
            listManager.UserType = cachedData.UserType;
            listManager.ActiveListId = cachedData.ActiveListId;
            listManager.m_MultiGroupList = cachedData.MultiGroupList;
            listManager.m_MultiGroupChartDataList = cachedData.MultiGroupChartDataList;
        }

        /// <summary>
        /// 建立快取資料
        /// </summary>
        private static CachedListManagerData CreateCacheData(ListManager listManager)
        {
            return new CachedListManagerData
            {
                LoginType = listManager.LoginType,
                LoginFullName = listManager.LoginFullName,
                UserType = listManager.UserType,
                ActiveListId = listManager.ActiveListId,
                MultiGroupList = listManager.m_MultiGroupList,
                MultiGroupChartDataList = listManager.m_MultiGroupChartDataList,
                CachedAt = DateTime.UtcNow
            };
        }

        #endregion
    }

    /// <summary>
    /// ListManager 快取資料結構
    /// </summary>
    public class CachedListManagerData
    {
        public string LoginType { get; set; }
        public string LoginFullName { get; set; }
        public string UserType { get; set; }
        public string ActiveListId { get; set; }
        public MultiGroupList MultiGroupList { get; set; }
        public MultiGroupChartDataList MultiGroupChartDataList { get; set; }
        public DateTime CachedAt { get; set; }
    }
}
