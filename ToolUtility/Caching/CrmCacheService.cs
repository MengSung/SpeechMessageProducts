using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Caching
{
    /// <summary>
    /// CRM 快取服務
    /// ? Phase 3.2: 實現多層次快取策略
    /// 使用 Cache-Aside Pattern
    /// 預期效果: 減少 70% 重複查詢
    /// </summary>
    public class CrmCacheService : ICrmCacheService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly object _logger;
        
        // 快取過期時間設定
        private static readonly TimeSpan StaticDataExpiration = TimeSpan.FromMinutes(30);   // 靜態資料（名單、組織）
        private static readonly TimeSpan UserDataExpiration = TimeSpan.FromMinutes(10);     // 用戶資料
        private static readonly TimeSpan QueryResultExpiration = TimeSpan.FromMinutes(5);   // 查詢結果

        public CrmCacheService(IMemoryCache memoryCache, object logger = null)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger;
        }

        #region 泛型快取方法

        /// <summary>
        /// 獲取或創建快取項目（非同步）
        /// ? Cache-Aside Pattern 實現
        /// </summary>
        public async Task<T> GetOrCreateAsync<T>(
            string cacheKey,
            Func<Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            // 嘗試從快取獲取
            if (_memoryCache.TryGetValue(cacheKey, out T cachedValue))
            {
                SafeLog($"快取命中: {cacheKey}");
                return cachedValue;
            }

            // 快取未命中，執行工廠方法
            SafeLog($"快取未命中: {cacheKey}，執行查詢");
            var value = await factory().ConfigureAwait(false);

            // 設定快取選項
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? QueryResultExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(2),  // 滑動過期
                Priority = CacheItemPriority.Normal
            };

            // 存入快取
            _memoryCache.Set(cacheKey, value, cacheOptions);
            SafeLog($"已快取: {cacheKey}，過期時間: {expiration?.TotalMinutes ?? QueryResultExpiration.TotalMinutes} 分鐘");

            return value;
        }

        /// <summary>
        /// 獲取或創建快取項目（同步）
        /// </summary>
        public T GetOrCreate<T>(
            string cacheKey,
            Func<T> factory,
            TimeSpan? expiration = null)
        {
            if (_memoryCache.TryGetValue(cacheKey, out T cachedValue))
            {
                SafeLog($"快取命中: {cacheKey}");
                return cachedValue;
            }

            SafeLog($"快取未命中: {cacheKey}，執行查詢");
            var value = factory();

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? QueryResultExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(2),
                Priority = CacheItemPriority.Normal
            };

            _memoryCache.Set(cacheKey, value, cacheOptions);
            return value;
        }

        #endregion

        #region 實體快取

        /// <summary>
        /// 快取單一實體
        /// ? 用於常用的靜態資料
        /// </summary>
        public async Task<Entity> GetOrCreateEntityAsync(
            string entityName,
            Guid entityId,
            Func<Task<Entity>> factory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = BuildEntityCacheKey(entityName, entityId);
            return await GetOrCreateAsync(cacheKey, factory, StaticDataExpiration, cancellationToken);
        }

        /// <summary>
        /// 快取實體集合
        /// ? 用於名單、組織等集合
        /// </summary>
        public async Task<EntityCollection> GetOrCreateEntityCollectionAsync(
            string cacheKey,
            Func<Task<EntityCollection>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            return await GetOrCreateAsync(cacheKey, factory, expiration ?? StaticDataExpiration, cancellationToken);
        }

        #endregion

        #region 查詢結果快取

        /// <summary>
        /// 快取查詢結果
        /// ? 用於頻繁的查詢操作
        /// </summary>
        public async Task<EntityCollection> GetOrCreateQueryResultAsync(
            string queryKey,
            Func<Task<EntityCollection>> factory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"Query:{queryKey}";
            return await GetOrCreateAsync(cacheKey, factory, QueryResultExpiration, cancellationToken);
        }

        /// <summary>
        /// 快取聯絡人查詢結果
        /// ? 用於根據 Line ID 或帳號查詢聯絡人
        /// </summary>
        public async Task<Entity> GetOrCreateContactAsync(
            string identifier,
            string identifierType,
            Func<Task<Entity>> factory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"Contact:{identifierType}:{identifier}";
            return await GetOrCreateAsync(cacheKey, factory, UserDataExpiration, cancellationToken);
        }

        /// <summary>
        /// 快取名單成員集合
        /// ? 用於名單成員查詢
        /// </summary>
        public async Task<EntityCollection> GetOrCreateListMembersAsync(
            Guid listId,
            Func<Task<EntityCollection>> factory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"ListMembers:{listId}";
            return await GetOrCreateAsync(cacheKey, factory, StaticDataExpiration, cancellationToken);
        }

        #endregion

        #region 快取失效

        /// <summary>
        /// 移除單一快取項目
        /// </summary>
        public void Remove(string cacheKey)
        {
            _memoryCache.Remove(cacheKey);
            SafeLog($"已移除快取: {cacheKey}");
        }

        /// <summary>
        /// 移除實體快取
        /// ? 當實體更新或刪除時調用
        /// </summary>
        public void RemoveEntity(string entityName, Guid entityId)
        {
            var cacheKey = BuildEntityCacheKey(entityName, entityId);
            Remove(cacheKey);
        }

        /// <summary>
        /// 移除多個相關快取
        /// ? 批量失效機制
        /// </summary>
        public void RemoveMultiple(params string[] cacheKeys)
        {
            foreach (var key in cacheKeys)
            {
                Remove(key);
            }
        }

        /// <summary>
        /// 移除名單相關的所有快取
        /// ? 當名單成員變更時調用
        /// </summary>
        public void InvalidateListCache(Guid listId)
        {
            // 移除名單成員快取
            Remove($"ListMembers:{listId}");
            
            // 移除相關的查詢結果快取
            // 注意：這裡可能需要實現更複雜的快取鍵追蹤機制
            SafeLog($"已失效名單快取: {listId}");
        }

        /// <summary>
        /// 移除聯絡人相關的所有快取
        /// ? 當聯絡人更新時調用
        /// </summary>
        public void InvalidateContactCache(string identifier, string identifierType)
        {
            Remove($"Contact:{identifierType}:{identifier}");
        }

        #endregion

        #region 輔助方法

        /// <summary>
        /// 建立實體快取鍵
        /// </summary>
        private string BuildEntityCacheKey(string entityName, Guid entityId)
        {
            return $"Entity:{entityName}:{entityId}";
        }

        /// <summary>
        /// 建立查詢快取鍵
        /// ? 根據查詢參數生成唯一鍵
        /// </summary>
        public string BuildQueryCacheKey(string entityName, params object[] parameters)
        {
            var paramString = string.Join("_", parameters);
            return $"Query:{entityName}:{paramString.GetHashCode()}";
        }

        /// <summary>
        /// 安全的日誌記錄
        /// </summary>
        private void SafeLog(string message)
        {
            try
            {
                if (_logger == null) return;
                
                var loggerType = _logger.GetType();
                var logMethod = loggerType.GetMethod("LogInformation", new[] { typeof(string) });
                
                if (logMethod != null)
                {
                    logMethod.Invoke(_logger, new object[] { $"[CrmCacheService] {message}" });
                }
            }
            catch
            {
                // swallow
            }
        }

        #endregion
    }
}
