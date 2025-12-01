using Microsoft.Xrm.Sdk;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Caching
{
    /// <summary>
    /// CRM 快取服務介面
    /// ? Phase 3.2: 定義快取操作契約
    /// </summary>
    public interface ICrmCacheService
    {
        #region 泛型快取方法

        /// <summary>
        /// 獲取或創建快取項目（非同步）
        /// </summary>
        Task<T> GetOrCreateAsync<T>(
            string cacheKey,
            Func<Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 獲取或創建快取項目（同步）
        /// </summary>
        T GetOrCreate<T>(
            string cacheKey,
            Func<T> factory,
            TimeSpan? expiration = null);

        #endregion

        #region 實體快取

        /// <summary>
        /// 快取單一實體
        /// </summary>
        Task<Entity> GetOrCreateEntityAsync(
            string entityName,
            Guid entityId,
            Func<Task<Entity>> factory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 快取實體集合
        /// </summary>
        Task<EntityCollection> GetOrCreateEntityCollectionAsync(
            string cacheKey,
            Func<Task<EntityCollection>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);

        #endregion

        #region 查詢結果快取

        /// <summary>
        /// 快取查詢結果
        /// </summary>
        Task<EntityCollection> GetOrCreateQueryResultAsync(
            string queryKey,
            Func<Task<EntityCollection>> factory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 快取聯絡人查詢結果
        /// </summary>
        Task<Entity> GetOrCreateContactAsync(
            string identifier,
            string identifierType,
            Func<Task<Entity>> factory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 快取名單成員集合
        /// </summary>
        Task<EntityCollection> GetOrCreateListMembersAsync(
            Guid listId,
            Func<Task<EntityCollection>> factory,
            CancellationToken cancellationToken = default);

        #endregion

        #region 快取失效

        /// <summary>
        /// 移除單一快取項目
        /// </summary>
        void Remove(string cacheKey);

        /// <summary>
        /// 移除實體快取
        /// </summary>
        void RemoveEntity(string entityName, Guid entityId);

        /// <summary>
        /// 移除多個相關快取
        /// </summary>
        void RemoveMultiple(params string[] cacheKeys);

        /// <summary>
        /// 移除名單相關的所有快取
        /// </summary>
        void InvalidateListCache(Guid listId);

        /// <summary>
        /// 移除聯絡人相關的所有快取
        /// </summary>
        void InvalidateContactCache(string identifier, string identifierType);

        #endregion

        #region 輔助方法

        /// <summary>
        /// 建立查詢快取鍵
        /// </summary>
        string BuildQueryCacheKey(string entityName, params object[] parameters);

        #endregion
    }
}
