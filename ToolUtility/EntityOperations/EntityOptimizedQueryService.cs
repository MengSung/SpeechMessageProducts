using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 優化版實體查詢服務。
    /// 設計重點如下：
    /// 1. 優先支援明確欄位查詢，降低不必要欄位傳輸成本。
    /// 2. 提供批量查詢，避免上層落入 N+1 查詢模式。
    /// 3. 非同步 API 採用輕量包裝，不額外製造 ThreadPool 壓力。
    /// 4. 內建慢查詢觀測能力，方便在高流量環境快速定位瓶頸。
    /// </summary>
    public class EntityOptimizedQueryService : IDisposable
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;
        private bool _disposed;

        private const int SLOW_QUERY_THRESHOLD_MS = 2000;
        private const int BATCH_SIZE_LIMIT = 500;

        /// <summary>
        /// 建立查詢服務實例。
        /// </summary>
        public EntityOptimizedQueryService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        #region 單筆查詢

        /// <summary>
        /// 依實體名稱與實體 ID 取得單筆資料。
        /// 若未指定欄位，會沿用舊行為抓取全部欄位。
        /// </summary>
        public Entity RetrieveEntity(string entityName, Guid entityId, params string[] columns)
        {
            if (string.IsNullOrEmpty(entityName))
                throw new ArgumentException("entityName cannot be null or empty", nameof(entityName));

            if (entityId == Guid.Empty)
                throw new ArgumentException("entityId cannot be empty", nameof(entityId));

            var startTimestamp = Stopwatch.GetTimestamp();

            try
            {
                var columnSet = columns?.Length > 0
                    ? new ColumnSet(columns)
                    : new ColumnSet(true);

                var entity = _organizationService.Retrieve(entityName, entityId, columnSet);

                LogQueryPerformance(entityName, "RetrieveEntity", startTimestamp, 1);
                return entity;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, $"RetrieveEntity failed: {entityName}, Id={entityId}");
                throw;
            }
        }

        /// <summary>
        /// 單筆查詢的非同步包裝版本。
        /// </summary>
        public Task<Entity> RetrieveEntityAsync(
            string entityName,
            Guid entityId,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return ExecuteAsync(
                () => RetrieveEntity(entityName, entityId, columns),
                cancellationToken);
        }

        #endregion

        #region 批量查詢

        /// <summary>
        /// 依多個實體 ID 一次批量查詢資料。
        /// 回傳字典可讓上層以 O(1) 方式快速比對結果，減少後續查找成本。
        /// </summary>
        public Dictionary<Guid, Entity> RetrieveBatch(
            string entityName,
            List<Guid> entityIds,
            params string[] columns)
        {
            if (string.IsNullOrEmpty(entityName))
                throw new ArgumentException("entityName cannot be null or empty", nameof(entityName));

            if (entityIds == null || entityIds.Count == 0)
                return new Dictionary<Guid, Entity>();

            if (entityIds.Count > BATCH_SIZE_LIMIT)
            {
                SafeLogWarning($"Batch query exceeds recommended size: {entityIds.Count} > {BATCH_SIZE_LIMIT}. Consider chunking requests.");
            }

            var startTimestamp = Stopwatch.GetTimestamp();

            try
            {
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columns?.Length > 0
                        ? new ColumnSet(columns)
                        : new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                query.Criteria.AddCondition(
                    $"{entityName}id",
                    ConditionOperator.In,
                    entityIds.Cast<object>().ToArray());

                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                };

                var collection = _organizationService.RetrieveMultiple(query);

                LogQueryPerformance(entityName, "RetrieveBatch", startTimestamp, collection.Entities.Count);

                var result = new Dictionary<Guid, Entity>(collection.Entities.Count);
                foreach (var entity in collection.Entities)
                {
                    result[entity.Id] = entity;
                }

                return result;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, $"RetrieveBatch failed: {entityName}, Count={entityIds.Count}");
                throw;
            }
        }

        /// <summary>
        /// 批量查詢的非同步包裝版本。
        /// </summary>
        public Task<Dictionary<Guid, Entity>> RetrieveBatchAsync(
            string entityName,
            List<Guid> entityIds,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return ExecuteAsync(
                () => RetrieveBatch(entityName, entityIds, columns),
                cancellationToken);
        }

        #endregion

        #region 條件查詢

        /// <summary>
        /// 依指定條件查詢實體集合。
        /// 若呼叫端沒有自行加入 statecode 條件，這裡會自動補上啟用狀態限制。
        /// </summary>
        public EntityCollection RetrieveByCondition(
            string entityName,
            FilterExpression filter,
            int topCount = 1000,
            params string[] columns)
        {
            if (string.IsNullOrEmpty(entityName))
                throw new ArgumentException("entityName cannot be null or empty", nameof(entityName));

            var startTimestamp = Stopwatch.GetTimestamp();

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

                if (!HasStateCodeCondition(query.Criteria))
                {
                    query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                }

                var collection = _organizationService.RetrieveMultiple(query);

                LogQueryPerformance(entityName, "RetrieveByCondition", startTimestamp, collection.Entities.Count);
                return collection;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, $"RetrieveByCondition failed: {entityName}");
                throw;
            }
        }

        /// <summary>
        /// 條件查詢的非同步包裝版本。
        /// </summary>
        public Task<EntityCollection> RetrieveByConditionAsync(
            string entityName,
            FilterExpression filter,
            int topCount = 1000,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return ExecuteAsync(
                () => RetrieveByCondition(entityName, filter, topCount, columns),
                cancellationToken);
        }

        /// <summary>
        /// 依單一欄位值查詢資料。
        /// 適合簡單的等值查詢情境。
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
        /// 單一欄位值查詢的非同步包裝版本。
        /// </summary>
        public Task<EntityCollection> RetrieveByFieldValueAsync(
            string entityName,
            string fieldName,
            object fieldValue,
            int topCount = 100,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return ExecuteAsync(
                () => RetrieveByFieldValue(entityName, fieldName, fieldValue, topCount, columns),
                cancellationToken);
        }

        #endregion

        #region 分頁查詢

        /// <summary>
        /// 以分頁方式查詢資料，避免單次載入過多結果。
        /// </summary>
        public Task<PagedResult<Entity>> RetrievePagedAsync(
            string entityName,
            FilterExpression filter = null,
            int pageSize = 100,
            string pagingCookie = null,
            CancellationToken cancellationToken = default,
            params string[] columns)
        {
            return ExecuteAsync(() =>
            {
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
                    Entities = new List<Entity>(result.Entities),
                    TotalCount = result.TotalRecordCount,
                    MoreRecords = result.MoreRecords,
                    PagingCookie = result.PagingCookie
                };
            }, cancellationToken);
        }

        #endregion

        #region 效能監控

        /// <summary>
        /// 記錄查詢耗時，超過閾值時輸出警告。
        /// </summary>
        private void LogQueryPerformance(string entityName, string method, long startTimestamp, int resultCount)
        {
            var durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (durationMs > SLOW_QUERY_THRESHOLD_MS)
            {
                SafeLogWarning($"Slow query detected: {method} - {entityName}, Duration: {durationMs:F0}ms, ResultCount: {resultCount}");
            }
        }

        /// <summary>
        /// 檢查呼叫端是否已自行加入 statecode 條件。
        /// </summary>
        private bool HasStateCodeCondition(FilterExpression filter)
        {
            if (filter == null)
            {
                return false;
            }

            return filter.Conditions.Any(c =>
                c.AttributeName?.Equals("statecode", StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// 以最小成本包裝同步查詢結果為 Task。
        /// 不使用 Task.Run，避免在同步 CRM I/O 路徑上增加多餘的排程成本。
        /// </summary>
        private static Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            try
            {
                return Task.FromResult(operation());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        #endregion

        #region 日誌輔助方法

        /// <summary>
        /// 安全記錄錯誤，不讓記錄失敗反向影響主要流程。
        /// </summary>
        private void SafeLogError(Exception ex, string message)
        {
            try
            {
                LoggerInvoker.LogError(_logger, ex, message);
            }
            catch
            {
                // 日誌錯誤只吞掉，不干擾查詢主流程。
            }
        }

        /// <summary>
        /// 安全記錄警告訊息。
        /// </summary>
        private void SafeLogWarning(string message)
        {
            try
            {
                LoggerInvoker.LogWarning(_logger, message);
            }
            catch
            {
                // 日誌錯誤只吞掉，不干擾查詢主流程。
            }
        }

        #endregion

        #region 資源釋放

        /// <summary>
        /// 標準 Dispose 實作入口。
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // 目前沒有額外受控資源需要主動釋放，保留擴充點。
            }

            _disposed = true;
        }

        /// <summary>
        /// 釋放物件並抑制終結器。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    /// <summary>
    /// 分頁查詢結果模型。
    /// </summary>
    public class PagedResult<T>
    {
        /// <summary>
        /// 當前頁面回傳的資料集合。
        /// </summary>
        public List<T> Entities { get; set; }

        /// <summary>
        /// 資料來源回報的總筆數。
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 是否仍有後續頁面資料。
        /// </summary>
        public bool MoreRecords { get; set; }

        /// <summary>
        /// CRM 分頁查詢使用的 paging cookie。
        /// </summary>
        public string PagingCookie { get; set; }
    }
}
