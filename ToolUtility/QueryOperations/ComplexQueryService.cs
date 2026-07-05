// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/QueryOperations/ComplexQueryService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ComplexQueryService
// 主要成員：RetrieveManyToOneCollection、QueryBloodReportByContactId、QueryPresentRecordByContactIdAndSunday、RetrieveManyToOneRelationship、QueryPresentRecordSortBySunday、QueryPresentRecordSortBySundayFetchXml、QueryPresentRecordInWeeklyReportByContactId、QueryEntityListByDate、QueryWeeklyReportBySunday、QueryWeeklyReportBeforeTwoMonthsOfSunday
// 引用命名空間：System、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、Microsoft.Xrm.Sdk.Messages、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 複雜查詢服務實作 - 處理 Many-to-One、週報、出席記錄等複雜查詢
    /// </summary>
    public class ComplexQueryService : IComplexQueryService
    {
        private readonly object _logger;
        private readonly ICrmClient _crmClient;

        public ComplexQueryService(object logger, ICrmClient crmClient)
        {
            _logger = logger;
            _crmClient = crmClient ?? throw new ArgumentNullException(nameof(crmClient));
        }

        #region Many-to-One 關係查詢
        public EntityCollection RetrieveManyToOneCollection()
        {
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

            return _crmClient.RetrieveMultiple(query);
        }

        public Entity QueryBloodReportByContactId(Guid contactId)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            if (response.EntityCollection.TotalRecordCount > 0)
            {
                return response.EntityCollection.Entities[0];
            }
            return null;
        }

        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int monthPeriod)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }

        public EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }

        public EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }

        public EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId)
        {
            string lastWeeksString = $"'{lastWeeks}'";
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_sunday_date' />
                        <attribute name='new_groupleader_present_record' />
                        <attribute name='new_followup_ways' />
                        <attribute name='new_follow_up' />
                        <attribute name='new_conclusion_choise' />
                        <attribute name='new_next_step' />
                        <attribute name='new_explanation' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype ='contact' value={contactId} />
                          <condition attribute='new_sunday_date' operator='last-x-weeks' value={lastWeeksString} />
                        </filter>
                      </entity>
                    </fetch>";

            var request = new RetrieveMultipleRequest
            {
                Query = new FetchExpression(fetchXml)
            };

            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);
            return response.EntityCollection;
        }

        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }

        public EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }
        #endregion

        #region 週報查詢
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
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
            dateTimeCondition.Values.Add(sunday.ToString());

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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }

        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }

        public EntityCollection QueryWeeklyReportBeforeTwoMonthsOfSunday(DateTime sunday, Guid listEntityId)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }
        #endregion

        #region 名單查詢
        public EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }

        public EntityCollection QueryListByContactId(Guid contactId, string associationName)
        {
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

            var request = new RetrieveMultipleRequest { Query = query };
            var response = (RetrieveMultipleResponse)_crmClient.Execute(request);

            return response.EntityCollection;
        }
        #endregion

        #region 成員名單查詢
        public Entity RetrieveContactCollectionByLineId(string lineId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_lineid", "statecode");
            query.Values.AddRange(lineId, 0);

            var retrieved = _crmClient.RetrieveMultiple(query);

            if (retrieved.Entities.Count > 0)
            {
                return retrieved.Entities[0];
            }
            return null;
        }

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(Guid listId)
        {
            var query = new QueryByAttribute("listmember");
            query.AddAttributeValue("listid", listId);
            query.ColumnSet = new ColumnSet(true);

            return _crmClient.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveDynamicMemberListDynamics365(Guid listId)
        {
            var cols = new ColumnSet(new string[] { "query" });
            var entity = _crmClient.Retrieve("list", listId, cols);
            var dynamicQuery = entity.Attributes["query"].ToString();

            return _crmClient.RetrieveMultiple(new FetchExpression(dynamicQuery));
        }
        #endregion

        private string GetAttributeValue(Entity entity, string attributeName)
        {
            if (entity.Contains(attributeName))
            {
                var attr = entity[attributeName];
                if (attr is AliasedValue aliased)
                {
                    return aliased.Value?.ToString() ?? string.Empty;
                }
                return attr?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }
    }
}
