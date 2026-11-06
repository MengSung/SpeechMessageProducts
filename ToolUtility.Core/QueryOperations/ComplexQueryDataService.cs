#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using System.Globalization;
using System.Threading;
using ToolUtilityNameSpace.DataOperations;
using System.Xml.Linq;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 十六個舊複雜查詢的純資料核心，借入最小Read／Execute邊界，不建立host連線或保存logger。
    /// host先驗完整subject／organization／教會scope；輸入ID及查詢格式正確不構成授權。
    /// 每頁最多5000列，維持舊原生metadata及不同方法的條件CLR型別，不按同名資料去重或回部分頁。
    /// 無cache／timer／queue／背景工作；Dispose只清借用executor並拒新入口，已入場I/O由host先drain再關連線。
    /// </summary>
    public class ComplexQueryDataService : IComplexQueryService, IDisposable
    {
        private ICrmQueryExecutor? _crmClient;
        private int _disposed;

        /// <summary>借入原授權scope的最小查詢executor；logger只保留來源相容參數，不保存或反射。</summary>
        /// <param name="logger">選用舊參數，不成為request或credential owner。</param>
        /// <param name="crmClient">只提供讀取／Execute的借用邊界；host擁有真正連線與timeout。</param>
        public ComplexQueryDataService(object? logger, ICrmQueryExecutor crmClient)
        {
            _crmClient = crmClient ?? throw new ArgumentNullException(nameof(crmClient));
        }

        #region Many-to-One 關係查詢
        /// <summary>保留舊RetrieveManyToOneCollection查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection RetrieveManyToOneCollection()
        {
            var client = GetExecutor();
            
            Guid acctId = new Guid("B2071325-B861-E011-9E82-001D60789032");
            var condition = new ConditionExpression
            {
                AttributeName = "regardingobjectid",
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(acctId.ToString());

            var query = new QueryExpression
            {
                ColumnSet = new ColumnSet("subject"),
                EntityName = "task"
            };
            query.Criteria.AddCondition(condition);

            return Page(client.RetrieveMultiple(query), query);
        }

        /// <summary>保留舊QueryBloodReportByContactId查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public Entity? QueryBloodReportByContactId(Guid contactId)
        {
            var client = GetExecutor();
            RequireId(contactId);
            var contactCondition = new ConditionExpression
            {
                AttributeName = "new_blood_contact_relation",
                Operator = ConditionOperator.Equal
            };
            contactCondition.Values.Add(contactId.ToString());

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.AddCondition(contactCondition);

            var orderByDate = new OrderExpression
            {
                AttributeName = "createdon",
                OrderType = OrderType.Descending
            };

            var query = new QueryExpression
            {
                EntityName = "new_blood_report"
            };
            query.ColumnSet.AllColumns = true;
            query.Criteria = filter;
            query.Orders.Add(orderByDate);

            var candidate = ExecutePage(client, query);

            if (candidate.TotalRecordCount > 0)
            {
                if (candidate.Entities.Count == 0) throw new InvalidOperationException("The SDK returned inconsistent count metadata.");
                return candidate.Entities[0];
            }
            return null;
        }

        /// <summary>保留舊QueryPresentRecordByContactIdAndSunday查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int monthPeriod)
        {
            var client = GetExecutor();
            RequireId(listEntityId);
            RequireId(contactId);
            RequireWeeks(monthPeriod);
            var weeklyReportCondition = new ConditionExpression
            {
                AttributeName = "new_list_new_present_record",
                Operator = ConditionOperator.Equal
            };
            weeklyReportCondition.Values.Add(listEntityId.ToString());

            var contactCondition = new ConditionExpression
            {
                AttributeName = "new_contact_new_present_record",
                Operator = ConditionOperator.Equal
            };
            contactCondition.Values.Add(contactId.ToString());

            var dateTimeCondition = new ConditionExpression
            {
                AttributeName = "new_sunday_date",
                Operator = ConditionOperator.LastXWeeks
            };
            dateTimeCondition.Values.Add(monthPeriod);

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.AddCondition(weeklyReportCondition);
            filter.AddCondition(contactCondition);
            filter.AddCondition(dateTimeCondition);

            var orderByDate = new OrderExpression
            {
                AttributeName = "new_sunday_date",
                OrderType = OrderType.Descending
            };

            var query = new QueryExpression
            {
                EntityName = "new_present_record",
                ColumnSet = new ColumnSet("new_sunday_present_this_week", "new_group_present_this_week"),
                Criteria = filter
            };
            query.Orders.Add(orderByDate);

            var candidate = ExecutePage(client, query);

            return candidate;
        }

        /// <summary>保留舊RetrieveManyToOneRelationship查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            var client = GetExecutor();
            RequireParent(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            var condition = new ConditionExpression
            {
                AttributeName = parentEntityIdName,
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(parentEntityId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
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
                EntityName = childEntityName
            };
            query.ColumnSet.AllColumns = true;
            query.LinkEntities.Add(link);

            var candidate = ExecutePage(client, query);

            return candidate;
        }

        /// <summary>保留舊QueryPresentRecordSortBySunday查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            var client = GetExecutor();
            RequireParent(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            var condition = new ConditionExpression
            {
                AttributeName = associationName,
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(parentEntityId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var endDateCondition = new ConditionExpression("new_sunday_date", ConditionOperator.GreaterEqual, DateTime.UtcNow.AddDays(-32));

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.Conditions.Add(condition);
            filter.Conditions.Add(stateCondition);
            filter.Conditions.Add(endDateCondition);

            var orderBySunday = new OrderExpression
            {
                AttributeName = "new_sunday_date",
                OrderType = OrderType.Ascending
            };

            var query = new QueryExpression
            {
                EntityName = childEntityName,
                Criteria = filter
            };
            query.ColumnSet.AllColumns = true;
            query.Orders.Add(orderBySunday);

            var candidate = ExecutePage(client, query);

            return candidate;
        }

        /// <summary>保留舊QueryPresentRecordSortBySundayFetchXml查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId)
        {
            var client = GetExecutor();
            
            RequireWeeks(lastWeeks);
            var fields = new[] { "new_present_recordid", "new_name", "createdon", "new_sunday_date",
                "new_groupleader_present_record", "new_followup_ways", "new_follow_up", "new_conclusion_choise", "new_next_step", "new_explanation" };
            var entity = SdkDataQuery.Entity("new_present_record", fields, "new_name");
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Lookup("new_contact_new_present_record", contactName, "contact", contactId),
                SdkDataQuery.Condition("new_sunday_date", "last-x-weeks", lastWeeks.ToString(CultureInfo.InvariantCulture))));
            var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", "false"), entity);
            return ExecutePage(client, new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting)));
        }

        /// <summary>保留舊QueryPresentRecordInWeeklyReportByContactId查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId)
        {
            var client = GetExecutor();
            RequireId(contactId);
            RequireId(weeklyReportEntityId);
            var condition = new ConditionExpression
            {
                AttributeName = "new_group_present_weekly_report_prese",
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(weeklyReportEntityId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var contactCondition = new ConditionExpression
            {
                AttributeName = "new_contact_new_present_record",
                Operator = ConditionOperator.Equal
            };
            contactCondition.Values.Add(contactId);

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.Conditions.Add(condition);
            filter.Conditions.Add(stateCondition);
            filter.Conditions.Add(contactCondition);

            var query = new QueryExpression
            {
                EntityName = "new_present_record",
                Criteria = filter
            };
            query.ColumnSet.AllColumns = true;

            var candidate = ExecutePage(client, query);

            return candidate;
        }

        /// <summary>保留舊QueryEntityListByDate查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            var client = GetExecutor();
            RequireParent(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            var condition = new ConditionExpression
            {
                AttributeName = associationName,
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(parentEntityId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var dateTimeAfterCondition = new ConditionExpression
            {
                AttributeName = "new_class_end_date",
                Operator = ConditionOperator.OnOrAfter
            };
            dateTimeAfterCondition.Values.Add(DateTime.UtcNow.AddDays(-7));

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.Conditions.Add(condition);
            filter.Conditions.Add(stateCondition);
            filter.Conditions.Add(dateTimeAfterCondition);

            var query = new QueryExpression
            {
                EntityName = childEntityName,
                Criteria = filter
            };
            query.ColumnSet.AllColumns = true;

            var candidate = ExecutePage(client, query);

            return candidate;
        }
        #endregion

        #region 週報查詢
        /// <summary>保留舊QueryWeeklyReportBySunday查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            var client = GetExecutor();
            RequireParent(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            var condition = new ConditionExpression
            {
                AttributeName = parentEntityIdName,
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(parentEntityId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var dateTimeCondition = new ConditionExpression
            {
                AttributeName = "new_sunday_date",
                Operator = ConditionOperator.Equal
            };
            dateTimeCondition.Values.Add(sunday.ToString(CultureInfo.CurrentCulture));

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.Conditions.Add(condition);
            filter.Conditions.Add(stateCondition);
            filter.Conditions.Add(dateTimeCondition);

            var link = new LinkEntity
            {
                LinkCriteria = filter,
                LinkFromEntityName = childEntityName,
                LinkFromAttributeName = associationName,
                LinkToAttributeName = parentEntityIdName,
                LinkToEntityName = parentEntityName
            };

            var orderByDate = new OrderExpression
            {
                AttributeName = "new_sunday_date",
                OrderType = OrderType.Descending
            };

            var query = new QueryExpression
            {
                EntityName = childEntityName
            };
            query.ColumnSet.AllColumns = true;
            query.LinkEntities.Add(link);
            query.Orders.Add(orderByDate);

            var candidate = ExecutePage(client, query);

            return candidate;
        }

        /// <summary>保留舊QueryWeeklyReportBySunday查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId)
        {
            var client = GetExecutor();
            RequireId(listEntityId);
            var condition = new ConditionExpression
            {
                AttributeName = "new_list_group_present_weekly_report",
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(listEntityId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var dateTimeCondition = new ConditionExpression
            {
                AttributeName = "new_sunday_date",
                Operator = ConditionOperator.On
            };
            dateTimeCondition.Values.Add(sunday);

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.Conditions.Add(condition);
            filter.Conditions.Add(stateCondition);
            filter.Conditions.Add(dateTimeCondition);

            var orderByDate = new OrderExpression
            {
                AttributeName = "new_sunday_date",
                OrderType = OrderType.Descending
            };

            var query = new QueryExpression
            {
                EntityName = "new_group_present_weekly_report",
                ColumnSet = new ColumnSet("new_small_group_member_number", "new_sunday_present_number", "new_sunday_present_rate", "new_small_group_number", "new_small_group_rate", "new_memo", "new_weekly_report_status"),
                Criteria = filter
            };
            query.Orders.Add(orderByDate);

            var candidate = ExecutePage(client, query);

            return candidate;
        }

        /// <summary>保留舊QueryWeeklyReportBeforeTwoMonthsOfSunday查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryWeeklyReportBeforeTwoMonthsOfSunday(DateTime sunday, Guid listEntityId)
        {
            var client = GetExecutor();
            RequireId(listEntityId);
            var condition = new ConditionExpression
            {
                AttributeName = "new_list_group_present_weekly_report",
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(listEntityId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var dateTimeAfterCondition = new ConditionExpression
            {
                AttributeName = "new_sunday_date",
                Operator = ConditionOperator.OnOrAfter
            };
            dateTimeAfterCondition.Values.Add(sunday.AddMonths(-2));

            var dateTimeBeforeCondition = new ConditionExpression
            {
                AttributeName = "new_sunday_date",
                Operator = ConditionOperator.OnOrBefore
            };
            dateTimeBeforeCondition.Values.Add(sunday);

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.Conditions.Add(condition);
            filter.Conditions.Add(stateCondition);
            filter.Conditions.Add(dateTimeAfterCondition);
            filter.Conditions.Add(dateTimeBeforeCondition);

            var orderByDate = new OrderExpression
            {
                AttributeName = "new_sunday_date",
                OrderType = OrderType.Ascending
            };

            var query = new QueryExpression
            {
                EntityName = "new_group_present_weekly_report",
                Criteria = filter
            };
            query.ColumnSet.AllColumns = true;
            query.Orders.Add(orderByDate);

            var candidate = ExecutePage(client, query);

            return candidate;
        }
        #endregion

        #region 名單查詢
        /// <summary>保留舊QueryListsAndOrderedByListName查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            var client = GetExecutor();
            RequireParent(parentEntityName, parentEntityIdName, parentEntityId, associationName, childEntityName);
            var condition = new ConditionExpression
            {
                AttributeName = parentEntityIdName,
                Operator = ConditionOperator.Equal
            };
            condition.Values.Add(parentEntityId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
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

            var orderBySerial = new OrderExpression
            {
                AttributeName = "listname",
                OrderType = OrderType.Ascending
            };

            var query = new QueryExpression
            {
                EntityName = childEntityName
            };
            query.ColumnSet.AllColumns = true;
            query.LinkEntities.Add(link);
            query.Orders.Add(orderBySerial);

            var candidate = ExecutePage(client, query);

            return candidate;
        }

        /// <summary>保留舊QueryListByContactId查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection QueryListByContactId(Guid contactId, string associationName)
        {
            var client = GetExecutor();
            RequireId(contactId);
            SdkDataQuery.RequireLogicalName(associationName, nameof(associationName));
            var contactCondition = new ConditionExpression
            {
                AttributeName = associationName,
                Operator = ConditionOperator.Equal
            };
            contactCondition.Values.Add(contactId);

            var stateCondition = new ConditionExpression
            {
                AttributeName = "statecode",
                Operator = ConditionOperator.Equal
            };
            stateCondition.Values.Add(0);

            var appCondition = new ConditionExpression
            {
                AttributeName = "new_app_named",
                Operator = ConditionOperator.Equal
            };
            appCondition.Values.Add(true);

            var filter = new FilterExpression
            {
                FilterOperator = LogicalOperator.And
            };
            filter.Conditions.Add(contactCondition);
            filter.Conditions.Add(stateCondition);
            filter.Conditions.Add(appCondition);

            var orderBySerial = new OrderExpression
            {
                AttributeName = "listname",
                OrderType = OrderType.Ascending
            };

            var query = new QueryExpression
            {
                EntityName = "list",
                Criteria = filter
            };
            query.ColumnSet.AllColumns = true;
            query.Orders.Add(orderBySerial);

            var candidate = ExecutePage(client, query);

            return candidate;
        }
        #endregion

        #region 成員名單查詢
        /// <summary>保留舊RetrieveContactCollectionByLineId查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public Entity? RetrieveContactCollectionByLineId(string lineId)
        {
            var client = GetExecutor();
            if (string.IsNullOrWhiteSpace(lineId)) throw new ArgumentException("A binding value is required.", nameof(lineId));
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_lineid", "statecode");
            query.Values.AddRange(lineId, 0);

            var retrieved = Page(client.RetrieveMultiple(query), query);

            if (retrieved.Entities.Count > 0)
            {
                return retrieved.Entities[0];
            }
            return null;
        }

        /// <summary>保留舊RetrieveMemberListCollectionByListIdDynamics365查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(Guid listId)
        {
            var client = GetExecutor();
            RequireId(listId);
            var query = new QueryByAttribute("listmember");
            query.AddAttributeValue("listid", listId);
            query.ColumnSet = new ColumnSet(true);

            return Page(client.RetrieveMultiple(query), query);
        }

        /// <summary>保留舊RetrieveDynamicMemberListDynamics365查詢投影、條件CLR型別與SDK入口；完整頁正式ID驗證後才發布，故障原樣傳host。</summary>
        public EntityCollection RetrieveDynamicMemberListDynamics365(Guid listId)
        {
            var client = GetExecutor();
            RequireId(listId);
            var cols = new ColumnSet("query");
            var entity = client.Retrieve("list", listId, cols);
            if (entity == null || entity.Id != listId || entity.LogicalName != "list")
                throw new InvalidOperationException("The SDK returned a mismatched list identity.");
            if (!entity.Attributes.TryGetValue("query", out var stored) || stored is not string dynamicQuery)
                throw new InvalidOperationException("The dynamic list has no valid query.");
            SdkDataQuery.ExpectedFetchEntity(dynamicQuery, nameof(listId));
            var query = new FetchExpression(dynamicQuery);
            return Page(client.RetrieveMultiple(query), query);
        }
        #endregion

        /// <summary>先取得局部借用參照；Dispose停止新入口，已開始同步工作由host排空，不處置借入連線。</summary>
        private ICrmQueryExecutor GetExecutor()
        {
            var client = Volatile.Read(ref _crmClient);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ComplexQueryDataService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return client ?? throw new ObjectDisposedException(nameof(ComplexQueryDataService));
        }

        /// <summary>保留舊Execute與RetrieveMultiple差異；型別及完整頁驗證後才發布，metadata原樣。</summary>
        private static EntityCollection ExecutePage(ICrmQueryExecutor client, QueryBase query)
        {
            if (client.Execute(new RetrieveMultipleRequest { Query = query }) is not RetrieveMultipleResponse response)
                throw new InvalidOperationException("The SDK returned an invalid query response.");
            return Page(response.EntityCollection, query);
        }

        /// <summary>整頁最多5000列，先驗預算再配ID集合；原query值型別、根表及分頁資訊不改。</summary>
        private static EntityCollection Page(EntityCollection? candidate, QueryBase query)
        {
            var table = query is QueryExpression expression ? expression.EntityName
                : query is QueryByAttribute attributes ? attributes.EntityName
                : SdkDataQuery.ExpectedFetchEntity(((FetchExpression)query).Query, nameof(query));
            return SdkDataQuery.ValidatePage(candidate, table, 5000);
        }

        /// <summary>動態結構名稱及非空GUID先驗證；不把格式正確作為host授權的替代。</summary>
        private static void RequireParent(string parent, string field, string id, string association, string child)
        {
            SdkDataQuery.RequireLogicalName(parent, nameof(parent));
            SdkDataQuery.RequireLogicalName(field, nameof(field));
            SdkDataQuery.RequireLogicalName(association, nameof(association));
            SdkDataQuery.RequireLogicalName(child, nameof(child));
            if (!Guid.TryParse(id, out var parsed)) throw new ArgumentException("A database identifier is required.", nameof(id));
            RequireId(parsed);
        }

        /// <summary>零GUID不能代表正式記錄；失敗訊息不回顯ID。</summary>
        private static void RequireId(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(id));
        }

        /// <summary>週數必須正值，不以移除條件擴大查詢。</summary>
        private static void RequireWeeks(int weeks)
        {
            if (weeks < 1) throw new ArgumentOutOfRangeException(nameof(weeks), "A positive week period is required.");
        }

        /// <summary>冪等清借用client；不保留logger，不Dispose外部連線，已取得局部參照的同步工作可drain。</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Exchange(ref _crmClient, null);
        }

        /// <summary>停止新工作並清除唯一強參照；無背景Task、cache、timer或終結器。</summary>
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}
