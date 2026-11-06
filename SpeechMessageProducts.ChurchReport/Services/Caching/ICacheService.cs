/*
 * 檔案整體說明
 *
 * 目的與定位：快取服務介面 Phase 2.2: 提供統一的快取操作介面
 *
 * 主要功能與流程：
 * 本檔宣告 ICacheService、CacheStatistics。
 * 資料／選項欄位包含 ICacheService、CacheStatistics、ItemCount、HitCount、MissCount、HitRatio、TrackedKeyCount，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs、
 * SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs、
 * SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Services.Caching
{
    /// <summary>
    /// 快取服務介面
    /// Phase 2.2: 提供統一的快取操作介面
    /// </summary>
    public interface ICacheService
    {
        #region 同步方法

        /// <summary>
        /// 取得或建立快取資料（同步）
        /// </summary>
        /// <typeparam name="T">資料類型</typeparam>
        /// <param name="key">快取鍵</param>
        /// <param name="factory">資料工廠方法</param>
        /// <param name="absoluteExpiration">絕對過期時間</param>
        /// <param name="slidingExpiration">滑動過期時間</param>
        /// <returns>快取資料</returns>
        T GetOrCreate<T>(
            string key,
            Func<T> factory,
            TimeSpan? absoluteExpiration = null,
            TimeSpan? slidingExpiration = null);

        /// <summary>
        /// 嘗試從快取取得資料
        /// </summary>
        bool TryGet<T>(string key, out T value);

        /// <summary>
        /// 設定快取資料
        /// </summary>
        void Set<T>(
            string key,
            T value,
            TimeSpan? absoluteExpiration = null,
            TimeSpan? slidingExpiration = null);

        #endregion

        #region 非同步方法

        /// <summary>
        /// 取得或建立快取資料（非同步）
        /// </summary>
        Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? absoluteExpiration = null,
            TimeSpan? slidingExpiration = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 非同步設定快取資料
        /// </summary>
        Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? absoluteExpiration = null,
            TimeSpan? slidingExpiration = null,
            CancellationToken cancellationToken = default);

        #endregion

        #region 快取管理

        /// <summary>
        /// 移除指定鍵的快取
        /// </summary>
        void Remove(string key);

        /// <summary>
        /// 移除符合前綴的所有快取
        /// </summary>
        void RemoveByPrefix(string prefix);

        /// <summary>
        /// 移除多個快取鍵
        /// </summary>
        void RemoveMultiple(params string[] keys);

        /// <summary>
        /// 檢查快取是否存在
        /// </summary>
        bool Exists(string key);

        /// <summary>
        /// 取得所有已追蹤的快取鍵
        /// </summary>
        IEnumerable<string> GetTrackedKeys();

        /// <summary>
        /// 取得快取統計資訊
        /// </summary>
        CacheStatistics GetStatistics();

        #endregion
    }

    /// <summary>
    /// 快取統計資訊
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>快取項目數量</summary>
        public int ItemCount { get; set; }

        /// <summary>快取命中次數</summary>
        public long HitCount { get; set; }

        /// <summary>快取未命中次數</summary>
        public long MissCount { get; set; }

        /// <summary>快取命中率</summary>
        public double HitRatio => HitCount + MissCount > 0
            ? (double)HitCount / (HitCount + MissCount)
            : 0;

        /// <summary>追蹤的快取鍵數量</summary>
        public int TrackedKeyCount { get; set; }
    }
}
