using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// N:1 和 N:N 關聯查詢服務
    /// </summary>
    public class RelationshipQueryService : IRelationshipQueryService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public RelationshipQueryService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 查詢 N:1 關聯的集合
        /// </summary>
        public EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
        {
            try
            {
                var condition = new ConditionExpression(parentEntityIdName, ConditionOperator.Equal, parentEntityId);
                var stateCondition = new ConditionExpression("statecode", ConditionOperator.Equal, 0);

                var filter = new FilterExpression(LogicalOperator.And);
                filter.Conditions.Add(condition);
                filter.Conditions.Add(stateCondition);

                var link = new LinkEntity
                {
                    LinkCriteria = filter,
                    LinkFromEntityName = childEntityName,
                    LinkFromAttributeName = associationName,
                    LinkToAttributeName = parentEntityIdName,
                    LinkToEntityName = parentEntityName
                };

                var query = new QueryExpression
                {
                    EntityName = childEntityName,
                    ColumnSet = new ColumnSet(true)
                };
                query.LinkEntities.Add(link);

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "RetrieveManyToOneRelationship 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 查詢 N:1 關聯的集合(根據名稱排序)
        /// </summary>
        public EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
        {
            try
            {
                var condition = new ConditionExpression(parentEntityIdName, ConditionOperator.Equal, parentEntityId);
                var stateCondition = new ConditionExpression("statecode", ConditionOperator.Equal, 0);

                var filter = new FilterExpression(LogicalOperator.And);
                filter.Conditions.Add(condition);
                filter.Conditions.Add(stateCondition);

                var link = new LinkEntity
                {
                    LinkCriteria = filter,
                    LinkFromEntityName = childEntityName,
                    LinkFromAttributeName = associationName,
                    LinkToAttributeName = parentEntityIdName,
                    LinkToEntityName = parentEntityName
                };

                var query = new QueryExpression
                {
                    EntityName = childEntityName,
                    ColumnSet = new ColumnSet(true)
                };
                query.LinkEntities.Add(link);
                query.AddOrder("listname", OrderType.Ascending);

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryListsAndOrderedByListName 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 查詢 N:1 關聯(使用 LinkEntity 取得關聯資料)
        /// </summary>
        public EntityCollection RetrieveManyToOneWithLinkEntity()
        {
            try
            {
                var query = new QueryExpression("contact");
                var columnNames = new[] { "fullname", "address1_city" };
                query.ColumnSet = new ColumnSet(columnNames);

                var colsAccount = new[] { "accountnumber" };
                var linkEntityAccount = new LinkEntity
                {
                    LinkFromEntityName = "contact",
                    LinkFromAttributeName = "parentcustomerid",
                    LinkToEntityName = "account",
                    LinkToAttributeName = "accountid",
                    JoinOperator = JoinOperator.Inner,
                    Columns = new ColumnSet(colsAccount),
                    EntityAlias = "aliasAccount"
                };

                query.LinkEntities.Add(linkEntityAccount);

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "RetrieveManyToOneWithLinkEntity 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 查詢週報(根據主日日期和N:1關聯)
        /// </summary>
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName,
            string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            try
            {
                var condition = new ConditionExpression(parentEntityIdName, ConditionOperator.Equal, parentEntityId);
                var stateCondition = new ConditionExpression("statecode", ConditionOperator.Equal, 0);
                var dateCondition = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, sunday.ToString());

                var filter = new FilterExpression(LogicalOperator.And);
                filter.Conditions.Add(condition);
                filter.Conditions.Add(stateCondition);
                filter.Conditions.Add(dateCondition);

                var link = new LinkEntity
                {
                    LinkCriteria = filter,
                    LinkFromEntityName = childEntityName,
                    LinkFromAttributeName = associationName,
                    LinkToAttributeName = parentEntityIdName,
                    LinkToEntityName = parentEntityName
                };

                var query = new QueryExpression
                {
                    EntityName = childEntityName,
                    ColumnSet = new ColumnSet(true)
                };
                query.LinkEntities.Add(link);
                query.AddOrder("new_sunday_date", OrderType.Descending);

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryWeeklyReportBySunday 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 查詢 N:N (ManyToMany) 的集合
        /// </summary>
        public EntityCollection QueryManyToMany(string conditionAttributeName, string entityNameToSearch,
            string linkFromEntityName, string linkFromAttributeName, string linkToEntityName,
            string linkToAttributeName, string attributeName, Guid entityIdValue)
        {
            try
            {
                var condition = new ConditionExpression(conditionAttributeName, ConditionOperator.Equal, true);
                var stateCondition = new ConditionExpression("statecode", ConditionOperator.Equal, 0);

                var filter = new FilterExpression(LogicalOperator.And);
                filter.Conditions.Add(condition);
                filter.Conditions.Add(stateCondition);

                var query = new QueryExpression
                {
                    Criteria = filter,
                    EntityName = entityNameToSearch,
                    LinkEntities =
                    {
                        new LinkEntity
                        {
                            LinkFromEntityName = linkFromEntityName,
                            LinkFromAttributeName = linkFromAttributeName,
                            LinkToEntityName = linkToEntityName,
                            LinkToAttributeName = linkToAttributeName,
                            LinkCriteria = new FilterExpression
                            {
                                FilterOperator = LogicalOperator.And,
                                Conditions =
                                {
                                    new ConditionExpression
                                    {
                                        AttributeName = attributeName,
                                        Operator = ConditionOperator.Equal,
                                        Values = { entityIdValue }
                                    }
                                }
                            }
                        }
                    }
                };

                query.ColumnSet.AllColumns = true;

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryManyToMany 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 連絡人相關的各類名單 (N:N查詢)
        /// </summary>
        public EntityCollection QueryListOfContactManyToMany(Guid contactId)
        {
            try
            {
                var condition = new ConditionExpression("new_app_named", ConditionOperator.Equal, true);
                var stateCondition = new ConditionExpression("statecode", ConditionOperator.Equal, 0);

                var filter = new FilterExpression(LogicalOperator.And);
                filter.Conditions.Add(condition);
                filter.Conditions.Add(stateCondition);

                var query = new QueryExpression
                {
                    Criteria = filter,
                    EntityName = "list",
                    LinkEntities =
                    {
                        new LinkEntity
                        {
                            LinkFromEntityName = "list",
                            LinkFromAttributeName = "listid",
                            LinkToEntityName = "listmember",
                            LinkToAttributeName = "listid",
                            LinkCriteria = new FilterExpression
                            {
                                FilterOperator = LogicalOperator.And,
                                Conditions =
                                {
                                    new ConditionExpression
                                    {
                                        AttributeName = "entityid",
                                        Operator = ConditionOperator.Equal,
                                        Values = { contactId }
                                    }
                                }
                            }
                        }
                    }
                };

                query.ColumnSet.AllColumns = true;

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryListOfContactManyToMany 發生錯誤");
                throw;
            }
        }

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
                // swallow
            }
        }
    }
}
