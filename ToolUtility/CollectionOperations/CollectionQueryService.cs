using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.CollectionOperations
{
    /// <summary>
    /// 集合查詢服務實作
    /// 遵循 LINUS 原則: 簡潔、高效、可測試
    /// 支援同步與非同步操作
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

        #region 同步方法 (保留向下相容)

        /// <summary>
        /// 根據欄位查詢實體集合 (同步)
        /// </summary>
        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
        {
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            return _organizationService.RetrieveMultiple(query);
        }

        /// <summary>
        /// 查詢週報資料 (同步)
        /// </summary>
        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
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

        #endregion

        #region 非同步方法 (新增)

        /// <summary>
        /// 根據欄位查詢實體集合 (非同步)
        /// </summary>
        public async Task<EntityCollection> RetrieveEntityCollectionByFieldAsync(
            string entityName, 
            string fieldName, 
            string fieldValue,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
                query.Attributes.AddRange(fieldName, "statecode");
                query.Values.AddRange(fieldValue, 0);
                
                return _organizationService.RetrieveMultiple(query);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 根據單一條件查詢實體集合 (非同步)
        /// </summary>
        public async Task<EntityCollection> RetrieveEntityCollectionByConditionAsync(
            string entityName,
            string fieldName,
            ConditionOperator conditionOperator,
            object value,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
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
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 根據多重條件查詢實體集合 (非同步)
        /// </summary>
        public async Task<EntityCollection> RetrieveEntityCollectionByConditionsAsync(
            string entityName,
            Dictionary<string, object> conditions,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
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
                
                // 預設只查詢啟用的記錄
                query.Criteria.Conditions.Add(
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0));
                
                return _organizationService.RetrieveMultiple(query);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 查詢週報資料 (非同步)
        /// </summary>
        public async Task<EntityCollection> QueryWeeklyReportBeforeTowMonthOfSundayAsync(
            DateTime aSunday, 
            Guid aListEntityId,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
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
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 分頁查詢實體集合 (非同步)
        /// </summary>
        public async Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(
            string entityName,
            FilterExpression filter = null,
            ColumnSet columnSet = null,
            int pageSize = 100,
            string pagingCookie = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
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
                    // 預設只查詢啟用的記錄
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
                    Entities = result.Entities.ToList(),
                    TotalCount = result.TotalRecordCount,
                    MoreRecords = result.MoreRecords,
                    PagingCookie = result.PagingCookie
                };
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 批量查詢實體 (使用 IN 條件，非同步)
        /// </summary>
        public async Task<EntityCollection> RetrieveBatchByIdsAsync(
            string entityName,
            string idFieldName,
            IEnumerable<Guid> ids,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columnSet ?? new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        FilterOperator = LogicalOperator.And,
                        Conditions =
                        {
                            new ConditionExpression(idFieldName, ConditionOperator.In, ids.ToArray()),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };
                
                return _organizationService.RetrieveMultiple(query);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 批次根據 ID 列表查詢實體（避免 N+1 查詢)
        /// ? Phase 3.1: 新增批次查詢，效能提升 10-100倍
        /// </summary>
        public async Task<Dictionary<Guid, Entity>> RetrieveBatchByIdsAsync(
            string entityName,
            List<Guid> entityIds,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default)
        {
            if (entityIds == null || entityIds.Count == 0)
                return new Dictionary<Guid, Entity>();

            var result = new Dictionary<Guid, Entity>();

            try
            {
                // ? 使用 IN 條件一次查詢所有 ID
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columnSet ?? new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                // 添加 ID 條件
                query.Criteria.AddCondition(
                    $"{entityName}id",
                    ConditionOperator.In,
                    entityIds.Cast<object>().ToArray()
                );

                // ? 添加分頁，防止一次查詢過多
                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                };

                // ? 非同步執行查詢
                var collection = await Task.Run(() =>
                    _organizationService.RetrieveMultiple(query),
                    cancellationToken).ConfigureAwait(false);

                // ? 建立字典映射
                foreach (var entity in collection.Entities)
                {
                    result[entity.Id] = entity;
                }

                return result;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, $"RetrieveBatchByIdsAsync 錯誤: {entityName}");
                throw;
            }
        }

        /// <summary>
        /// 批次根據欄位值查詢實體（使用 IN 條件）
        /// ? Phase 3.1: 批次查詢，避免循環查詢
        /// </summary>
        public async Task<EntityCollection> RetrieveBatchByFieldValuesAsync(
            string entityName,
            string fieldName,
            List<string> fieldValues,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default)
        {
            if (fieldValues == null || fieldValues.Count == 0)
                return new EntityCollection();

            try
            {
                // ? 使用 QueryExpression 配合 IN 條件
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = columnSet ?? new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                // ? 添加 IN 條件
                query.Criteria.AddCondition(
                    fieldName,
                    ConditionOperator.In,
                    fieldValues.Cast<object>().ToArray()
                );

                // 添加 statecode 條件（只查詢活動記錄）
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

                // ? 添加分頁
                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                };

                // ? 非同步執行
                var collection = await Task.Run(() =>
                    _organizationService.RetrieveMultiple(query),
                    cancellationToken).ConfigureAwait(false);

                return collection;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, $"RetrieveBatchByFieldValuesAsync 錯誤: {entityName}.{fieldName}");
                throw;
            }
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 建立週報查詢表達式
        /// 重構共用邏輯，避免程式碼重複
        /// </summary>
        private QueryExpression BuildWeeklyReportQuery(DateTime aSunday, Guid aListEntityId)
        {
            // 建立條件表達式
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

            // 建立篩選條件
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

            // 建立排序
            var orderByDate = new OrderExpression
            {
                AttributeName = "new_sunday_date",
                OrderType = OrderType.Ascending
            };

            // 建立查詢表達式
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
        /// 安全的錯誤日誌記錄
        /// </summary>
        private void SafeLogError(Exception ex, string format, params object[] args)
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
                    
                    object state = string.Format(format, args);
                    Func<object, Exception, string> formatter = (s, e) => s?.ToString() ?? string.Empty;
                    var parameters = new object[] { errorLevel, eventId, state, ex, formatter };
                    genericMethod.Invoke(_logger, parameters);
                }
            }
            catch
            {
                // swallow - 不讓日誌錯誤影響主要功能
            }
        }

        #endregion
    }
}
