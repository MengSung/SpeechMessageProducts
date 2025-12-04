using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 優化的實體查詢服務
    /// ? Phase 3.3: 提供明確欄位查詢、批量查詢、解決 N+1 問題
    /// 
    /// 設計原則:
    /// 1. 明確欄位 - 避免 ColumnSet(true)
    /// 2. 批量查詢 - 避免 N+1 問題
    /// 3. 非同步化 - 提升效能
    /// 4. 效能監控 - 識別慢查詢
    /// </summary>
    public class EntityOptimizedQueryService : IDisposable
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;
        private bool _disposed = false;

        // ? 效能監控閾值
        private const int SLOW_QUERY_THRESHOLD_MS = 2000;  // 慢查詢閾值 2 秒
        private const int BATCH_SIZE_LIMIT = 500;          // 批量查詢上限

        public EntityOptimizedQueryService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        #region 單筆查詢 (明確欄位)

        /// <summary>
        /// 查詢單一實體 (指定欄位)
        /// ? Phase 3.3: 明確欄位查詢，避免取得不必要的資料
        /// </summary>
        /// <param name="entityName">實體名稱</param>
        /// <param name="entityId">實體 ID</param>
        /// <param name="columns">要查詢的欄位</param>
        /// <returns>實體</returns>
        public Entity RetrieveEntity(string entityName, Guid entityId, params string[] columns)
        {
            if (string.IsNullOrEmpty(entityName))
                throw new ArgumentException("entityName cannot be null or empty", nameof(entityName));
            
            if (entityId == Guid.Empty)
                throw new ArgumentException("entityId cannot be empty", nameof(entityId));

            var startTime = DateTime.UtcNow;

            try
            {
                var columnSet = columns?.Length > 0 
                    ? new ColumnSet(columns) 
                    : new ColumnSet(true);

                var entity = _organizationService.Retrieve(entityName, entityId, columnSet);

                LogQueryPerformance(entityName, "RetrieveEntity", startTime, 1);
                
                return entity;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, $"RetrieveEntity 失敗: {entityName}, Id={entityId}");
                throw;
            }
        }

        /// <summary>
        /// 非同步查詢單一實體 (指定欄位)
        /// ? Phase 3.3: 非同步 + 明確欄位
        /// </summary>
        public async Task<Entity> RetrieveEntityAsync(
            string entityName, 
            Guid entityId, 
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return await Task.Run(() => 
                RetrieveEntity(entityName, entityId, columns), 
                cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region 批量查詢 (解決 N+1 問題)

        /// <summary>
        /// 批量查詢實體 (使用 IN 條件)
        /// ? Phase 3.3: 解決 N+1 查詢問題，一次查詢多筆資料
        /// 
        /// 使用範例:
        /// var ids = new List<Guid> { id1, id2, id3 };
        /// var entities = RetrieveBatch("contact", ids, "fullname", "mobilephone");
        /// </summary>
        /// <param name="entityName">實體名稱</param>
        /// <param name="entityIds">實體 ID 列表</param>
        /// <param name="columns">要查詢的欄位</param>
        /// <returns>實體字典 (Key: EntityId, Value: Entity)</returns>
        public Dictionary<Guid, Entity> RetrieveBatch(
            string entityName, 
            List<Guid> entityIds, 
            params string[] columns)
        {
            if (string.IsNullOrEmpty(entityName))
                throw new ArgumentException("entityName cannot be null or empty", nameof(entityName));

            if (entityIds == null || entityIds.Count == 0)
                return new Dictionary<Guid, Entity>();

            // ? 限制批量大小
            if (entityIds.Count > BATCH_SIZE_LIMIT)
            {
                SafeLogWarning($"批量查詢超過限制: {entityIds.Count} > {BATCH_SIZE_LIMIT}，建議分批查詢");
            }

            var startTime = DateTime.UtcNow;

            try
            {
                // ? 使用 QueryExpression + IN 條件
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columns?.Length > 0 
                        ? new ColumnSet(columns) 
                        : new ColumnSet(true),
                    
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                // ? 加入 ID IN 條件
                query.Criteria.AddCondition(
                    $"{entityName}id",
                    ConditionOperator.In,
                    entityIds.Cast<object>().ToArray()
                );

                // ? 加入分頁資訊
                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                };

                var collection = _organizationService.RetrieveMultiple(query);

                LogQueryPerformance(entityName, "RetrieveBatch", startTime, collection.Entities.Count);

                // ? 建立字典映射
                return collection.Entities.ToDictionary(e => e.Id, e => e);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, $"RetrieveBatch 失敗: {entityName}, Count={entityIds.Count}");
                throw;
            }
        }

        /// <summary>
        /// 非同步批量查詢
        /// ? Phase 3.3: 非同步 + 批量查詢
        /// </summary>
        public async Task<Dictionary<Guid, Entity>> RetrieveBatchAsync(
            string entityName,
            List<Guid> entityIds,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return await Task.Run(() => 
                RetrieveBatch(entityName, entityIds, columns), 
                cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region 條件查詢 (明確欄位)

        /// <summary>
        /// 根據條件查詢實體 (指定欄位)
        /// ? Phase 3.3: 明確欄位 + 條件查詢
        /// </summary>
        /// <param name="entityName">實體名稱</param>
        /// <param name="filter">篩選條件</param>
        /// <param name="topCount">最多返回筆數</param>
        /// <param name="columns">要查詢的欄位</param>
        /// <returns>實體集合</returns>
        public EntityCollection RetrieveByCondition(
            string entityName,
            FilterExpression filter,
            int topCount = 1000,
            params string[] columns)
        {
            if (string.IsNullOrEmpty(entityName))
                throw new ArgumentException("entityName cannot be null or empty", nameof(entityName));

            var startTime = DateTime.UtcNow;

            try
            {
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columns?.Length > 0 
                        ? new ColumnSet(columns) 
                        : new ColumnSet(true),
                    
                    TopCount = topCount,
                    
                    Criteria = filter ?? new FilterExpression(LogicalOperator.And)
                };

                // ? 確保查詢活動記錄
                if (!HasStateCodeCondition(query.Criteria))
                {
                    query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                }

                var collection = _organizationService.RetrieveMultiple(query);

                LogQueryPerformance(entityName, "RetrieveByCondition", startTime, collection.Entities.Count);

                return collection;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, $"RetrieveByCondition 失敗: {entityName}");
                throw;
            }
        }

        /// <summary>
        /// 非同步條件查詢
        /// </summary>
        public async Task<EntityCollection> RetrieveByConditionAsync(
            string entityName,
            FilterExpression filter,
            int topCount = 1000,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return await Task.Run(() => 
                RetrieveByCondition(entityName, filter, topCount, columns), 
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 根據單一欄位值查詢 (指定欄位)
        /// ? Phase 3.3: 簡化的條件查詢
        /// </summary>
        public EntityCollection RetrieveByFieldValue(
            string entityName,
            string fieldName,
            object fieldValue,
            int topCount = 100,
            params string[] columns)
        {
            var filter = new FilterExpression(LogicalOperator.And);
            filter.AddCondition(fieldName, ConditionOperator.Equal, fieldValue);

            return RetrieveByCondition(entityName, filter, topCount, columns);
        }

        /// <summary>
        /// 非同步根據欄位值查詢
        /// </summary>
        public async Task<EntityCollection> RetrieveByFieldValueAsync(
            string entityName,
            string fieldName,
            object fieldValue,
            int topCount = 100,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return await Task.Run(() => 
                RetrieveByFieldValue(entityName, fieldName, fieldValue, topCount, columns), 
                cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region 分頁查詢

        /// <summary>
        /// 分頁查詢 (避免一次載入大量資料)
        /// ? Phase 3.3: 分頁查詢，降低記憶體使用
        /// </summary>
        public async Task<PagedResult<Entity>> RetrievePagedAsync(
            string entityName,
            FilterExpression filter = null,
            int pageSize = 100,
            string pagingCookie = null,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columns?.Length > 0 
                        ? new ColumnSet(columns) 
                        : new ColumnSet(true),

                    PageInfo = new PagingInfo
                    {
                        Count = pageSize,
                        PageNumber = string.IsNullOrEmpty(pagingCookie) ? 1 : 2,
                        PagingCookie = pagingCookie
                    }
                };

                if (filter != null)
                {
                    query.Criteria = filter;
                }

                var result = _organizationService.RetrieveMultiple(query);

                return new PagedResult<Entity>
                {
                    Entities = result.Entities.ToList(),
                    TotalCount = result.TotalRecordCount,
                    MoreRecords = result.MoreRecords,
                    PagingCookie = result.PagingCookie
                };
            }, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region 效能監控

        /// <summary>
        /// 記錄查詢效能
        /// ? Phase 3.3: 自動識別慢查詢
        /// </summary>
        private void LogQueryPerformance(string entityName, string method, DateTime startTime, int resultCount)
        {
            var duration = DateTime.UtcNow - startTime;
            var durationMs = duration.TotalMilliseconds;

            if (durationMs > SLOW_QUERY_THRESHOLD_MS)
            {
                // ? 慢查詢告警
                SafeLogWarning($"慢查詢偵測: {method} - {entityName}, 耗時: {durationMs:F0}ms, 結果數: {resultCount}");
            }
        }

        /// <summary>
        /// 檢查是否已有 statecode 條件
        /// </summary>
        private bool HasStateCodeCondition(FilterExpression filter)
        {
            if (filter == null) return false;

            return filter.Conditions.Any(c => 
                c.AttributeName?.Equals("statecode", StringComparison.OrdinalIgnoreCase) == true);
        }

        #endregion

        #region 日誌輔助方法

        private void SafeLogError(Exception ex, string message)
        {
            try
            {
                if (_logger == null) return;

                var loggerType = _logger.GetType();
                var logMethod = loggerType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Log" && m.GetParameters().Length == 5 && m.IsGenericMethod);

                if (logMethod != null)
                {
                    var genericMethod = logMethod.MakeGenericMethod(typeof(object));
                    var logLevelType = Type.GetType("Microsoft.Extensions.Logging.LogLevel, Microsoft.Extensions.Logging.Abstractions");
                    object errorLevel = null;
                    if (logLevelType != null)
                    {
                        errorLevel = Enum.Parse(logLevelType, "Error");
                    }

                    var eventIdType = Type.GetType("Microsoft.Extensions.Logging.EventId, Microsoft.Extensions.Logging.Abstractions");
                    object eventId = null;
                    if (eventIdType != null)
                    {
                        eventId = Activator.CreateInstance(eventIdType, 0, string.Empty);
                    }

                    Func<object, Exception, string> formatter = (s, e) => s?.ToString() ?? string.Empty;
                    var parameters = new object[] { errorLevel, eventId, message, ex, formatter };
                    genericMethod.Invoke(_logger, parameters);
                }
            }
            catch
            {
                // Swallow logging errors
            }
        }

        private void SafeLogWarning(string message)
        {
            try
            {
                if (_logger == null) return;

                var loggerType = _logger.GetType();
                var logMethod = loggerType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Log" && m.GetParameters().Length == 5 && m.IsGenericMethod);

                if (logMethod != null)
                {
                    var genericMethod = logMethod.MakeGenericMethod(typeof(object));
                    var logLevelType = Type.GetType("Microsoft.Extensions.Logging.LogLevel, Microsoft.Extensions.Logging.Abstractions");
                    object warningLevel = null;
                    if (logLevelType != null)
                    {
                        warningLevel = Enum.Parse(logLevelType, "Warning");
                    }

                    var eventIdType = Type.GetType("Microsoft.Extensions.Logging.EventId, Microsoft.Extensions.Logging.Abstractions");
                    object eventId = null;
                    if (eventIdType != null)
                    {
                        eventId = Activator.CreateInstance(eventIdType, 0, string.Empty);
                    }

                    Func<object, Exception, string> formatter = (s, e) => s?.ToString() ?? string.Empty;
                    var parameters = new object[] { warningLevel, eventId, message, null, formatter };
                    genericMethod.Invoke(_logger, parameters);
                }
            }
            catch
            {
                // Swallow logging errors
            }
        }

        #endregion

        #region Dispose Pattern

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // Dispose managed resources if any
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    /// <summary>
    /// 分頁查詢結果
    /// </summary>
    public class PagedResult<T>
    {
        public List<T> Entities { get; set; }
        public int TotalCount { get; set; }
        public bool MoreRecords { get; set; }
        public string PagingCookie { get; set; }
    }
}
