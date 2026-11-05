// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/CollectionOperations/CollectionQueryService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CollectionQueryService
// 主要成員：RetrieveEntityCollectionByField、QueryWeeklyReportBeforeTowMonthOfSunday、RetrieveEntityCollectionByFieldAsync、RetrieveEntityCollectionByConditionAsync、RetrieveEntityCollectionByConditionsAsync、QueryWeeklyReportBeforeTowMonthOfSundayAsync、RetrievePagedEntitiesAsync、RetrieveBatchByIdsAsync、RetrieveBatchByFieldValuesAsync、RetrieveEntityCollectionByFieldCore
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Messages、Microsoft.Xrm.Sdk.Query、System、System.Collections.Generic、System.Linq、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.Diagnostics;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.CollectionOperations
{
    /// <summary>
    /// 集合查詢服務實作。
    /// 這個類別負責封裝常見的 CRM 集合查詢模式，
    /// 保留原本同步 API 的行為，同時提供可逐步導入的非同步包裝方法。
    /// 由於底層 CRM SDK 仍以同步 I/O 為主，這裡特別避免額外的 ThreadPool 排程成本。
    /// </summary>
    public class CollectionQueryService : ICollectionQueryService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public CollectionQueryService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        #region 同步公開 API

        /// <summary>
        /// 依指定欄位值查詢實體集合。
        /// 此方法沿用既有的同步呼叫模式，並固定加上啟用狀態條件，
        /// 以維持舊版程式碼對回傳結果的預期。
        /// </summary>
        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
        {
            return RetrieveEntityCollectionByFieldCore(entityName, fieldName, fieldValue);
        }

        /// <summary>
        /// 查詢指定主日往前兩個月內的週報資料。
        /// 這是原有同步入口，保留既有查詢條件與排序方式。
        /// </summary>
        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
        {
            return QueryWeeklyReportBeforeTowMonthOfSundayCore(aSunday, aListEntityId);
        }

        #endregion

        #region 非同步公開 API

        /// <summary>
        /// 依指定欄位值查詢實體集合的非同步包裝版本。
        /// </summary>
        public Task<EntityCollection> RetrieveEntityCollectionByFieldAsync(
            string entityName,
            string fieldName,
            string fieldValue,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                () => RetrieveEntityCollectionByFieldCore(entityName, fieldName, fieldValue),
                cancellationToken);
        }

        /// <summary>
        /// 依單一條件查詢實體集合。
        /// 額外固定加入 statecode = 0，避免誤取停用資料。
        /// </summary>
        public Task<EntityCollection> RetrieveEntityCollectionByConditionAsync(
            string entityName,
            string fieldName,
            ConditionOperator conditionOperator,
            object value,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        FilterOperator = LogicalOperator.And,
                        Conditions =
                        {
                            new ConditionExpression(fieldName, conditionOperator, value),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };

                return _organizationService.RetrieveMultiple(query);
            }, cancellationToken);
        }

        /// <summary>
        /// 依多組欄位條件查詢實體集合。
        /// 適合上層已完成條件整理，只想一次送進查詢層的情境。
        /// </summary>
        public Task<EntityCollection> RetrieveEntityCollectionByConditionsAsync(
            string entityName,
            Dictionary<string, object> conditions,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        FilterOperator = LogicalOperator.And
                    }
                };

                foreach (var condition in conditions)
                {
                    query.Criteria.Conditions.Add(
                        new ConditionExpression(condition.Key, ConditionOperator.Equal, condition.Value));
                }

                query.Criteria.Conditions.Add(
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0));

                return _organizationService.RetrieveMultiple(query);
            }, cancellationToken);
        }

        /// <summary>
        /// 查詢指定主日往前兩個月內的週報資料之非同步包裝版本。
        /// </summary>
        public Task<EntityCollection> QueryWeeklyReportBeforeTowMonthOfSundayAsync(
            DateTime aSunday,
            Guid aListEntityId,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                () => QueryWeeklyReportBeforeTowMonthOfSundayCore(aSunday, aListEntityId),
                cancellationToken);
        }

        /// <summary>
        /// 執行分頁查詢，避免一次把大量資料全部載入記憶體。
        /// 若未指定 filter，會自動限制為啟用中的資料。
        /// </summary>
        public Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(
            string entityName,
            FilterExpression filter = null,
            ColumnSet columnSet = null,
            int pageSize = 100,
            string pagingCookie = null,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columnSet ?? new ColumnSet(true),
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
                else
                {
                    query.Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    };
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

        /// <summary>
        /// 以 IN 條件批量查詢多筆實體。
        /// 這個版本維持與舊介面相容，回傳 EntityCollection。
        /// </summary>
        public Task<EntityCollection> RetrieveBatchByIdsAsync(
            string entityName,
            string idFieldName,
            IEnumerable<Guid> ids,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var boxedIds = ids.Cast<object>().ToArray();
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columnSet ?? new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        FilterOperator = LogicalOperator.And,
                        Conditions =
                        {
                            new ConditionExpression(idFieldName, ConditionOperator.In, boxedIds),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };

                return _organizationService.RetrieveMultiple(query);
            }, cancellationToken);
        }

        /// <summary>
        /// 批次依 ID 清單查詢資料，並回傳以實體 ID 為鍵的字典。
        /// 這可以有效避免上層用迴圈逐筆查詢造成的 N+1 問題。
        /// </summary>
        public Task<Dictionary<Guid, Entity>> RetrieveBatchByIdsAsync(
            string entityName,
            List<Guid> entityIds,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default)
        {
            if (entityIds == null || entityIds.Count == 0)
            {
                return Task.FromResult(new Dictionary<Guid, Entity>());
            }

            return ExecuteAsync(() =>
            {
                try
                {
                    var result = new Dictionary<Guid, Entity>(entityIds.Count);
                    var query = new QueryExpression(entityName)
                    {
                        ColumnSet = columnSet ?? new ColumnSet(true),
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

                    foreach (var entity in collection.Entities)
                    {
                        result[entity.Id] = entity;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    SafeLogError(ex, $"RetrieveBatchByIdsAsync failed: {entityName}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// 批次依欄位值集合查詢資料。
        /// 常用於上層已收斂出一批外部鍵值，需要一次回查對應 CRM 記錄的情境。
        /// </summary>
        public Task<EntityCollection> RetrieveBatchByFieldValuesAsync(
            string entityName,
            string fieldName,
            List<string> fieldValues,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default)
        {
            if (fieldValues == null || fieldValues.Count == 0)
            {
                return Task.FromResult(new EntityCollection());
            }

            return ExecuteAsync(() =>
            {
                try
                {
                    var query = new QueryExpression(entityName)
                    {
                        ColumnSet = columnSet ?? new ColumnSet(true),
                        Criteria = new FilterExpression(LogicalOperator.And)
                    };

                    query.Criteria.AddCondition(
                        fieldName,
                        ConditionOperator.In,
                        fieldValues.ToArray());

                    query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

                    query.PageInfo = new PagingInfo
                    {
                        Count = 5000,
                        PageNumber = 1
                    };

                    return _organizationService.RetrieveMultiple(query);
                }
                catch (Exception ex)
                {
                    SafeLogError(ex, $"RetrieveBatchByFieldValuesAsync failed: {entityName}.{fieldName}");
                    throw;
                }
            }, cancellationToken);
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 同步核心實作：依欄位值查詢啟用中的資料。
        /// </summary>
        private EntityCollection RetrieveEntityCollectionByFieldCore(string entityName, string fieldName, string fieldValue)
        {
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            return _organizationService.RetrieveMultiple(query);
        }

        /// <summary>
        /// 同步核心實作：查詢主日往前兩個月的週報資料。
        /// </summary>
        private EntityCollection QueryWeeklyReportBeforeTowMonthOfSundayCore(DateTime aSunday, Guid aListEntityId)
        {
            try
            {
                var query = BuildWeeklyReportQuery(aSunday, aListEntityId);
                var retrieve = new RetrieveMultipleRequest { Query = query };
                var request = (RetrieveMultipleResponse)_organizationService.Execute(retrieve);
                return request.EntityCollection;
            }
            catch (Exception e)
            {
                string errorString = $"ERROR: FullName={GetType().FullName}, Time={DateTime.Now}, Description={e}";
                throw new InvalidOperationException(errorString, e);
            }
        }

        /// <summary>
        /// 以最小成本把同步結果包裝成 Task。
        /// 這裡刻意不使用 Task.Run，避免把同步 CRM I/O 再包一層 ThreadPool 排程，
        /// 否則高併發時只會增加上下文切換與排程成本。
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

        /// <summary>
        /// 建立週報查詢條件與排序。
        /// 單獨抽出來可以避免同步與非同步路徑重複組查詢物件。
        /// </summary>
        private QueryExpression BuildWeeklyReportQuery(DateTime aSunday, Guid aListEntityId)
        {
            var listCondition = new ConditionExpression
            {
                AttributeName = "new_list_group_present_weekly_report",
                Operator = ConditionOperator.Equal,
                Values = { aListEntityId }
            };

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal,
                Values = { 0 }
            };

            var dateAfterCondition = new ConditionExpression
            {
                AttributeName = "new_sunday_date",
                Operator = ConditionOperator.OnOrAfter,
                Values = { aSunday.AddMonths(-2) }
            };

            var dateBeforeCondition = new ConditionExpression
            {
                AttributeName = "new_sunday_date",
                Operator = ConditionOperator.OnOrBefore,
                Values = { aSunday }
            };

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And,
                Conditions =
                {
                    listCondition,
                    stateCondition,
                    dateAfterCondition,
                    dateBeforeCondition
                }
            };

            var orderByDate = new OrderExpression
            {
                AttributeName = "new_sunday_date",
                OrderType = OrderType.Ascending
            };

            var query = new QueryExpression
            {
                EntityName = "new_group_present_weekly_report",
                ColumnSet = new ColumnSet(true),
                Criteria = filter,
                Orders = { orderByDate }
            };

            return query;
        }

        /// <summary>
        /// 安全記錄錯誤，不讓日誌系統反向影響主流程。
        /// </summary>
        private void SafeLogError(Exception ex, string format, params object[] args)
        {
            try
            {
                var message = args == null || args.Length == 0
                    ? format
                    : string.Format(format, args);

                LoggerInvoker.LogError(_logger, ex, message);
            }
            catch
            {
                // 日誌只能輔助診斷，不能讓它變成新的故障來源。
            }
        }

        #endregion
    }
}
