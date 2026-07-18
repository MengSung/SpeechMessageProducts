// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/QueryOperations/PresentRecordQueryService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class PresentRecordQueryService
// 主要成員：QueryPresentRecordByContactIdAndSunday、QueryPresentRecordSortBySunday、QueryPresentRecordSortBySundayFetchXml、QueryPresentRecordInWeeklyReportByContactId、QueryEntityListByDate、QueryWeeklyReportBySunday、QueryWeeklyReportBeforeTwoMonthOfSunday、QueryListByContactId、SafeLogError
// 引用命名空間：System、System.Linq、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、Microsoft.Xrm.Sdk.Messages
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 個人聚會與靈修記錄查詢服務
    /// </summary>
    public class PresentRecordQueryService : IPresentRecordQueryService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public PresentRecordQueryService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 搜尋主日日期是最近N週的靈修單
        /// </summary>
        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int weekPeriod)
        {
            try
            {
                var query = new QueryExpression
                {
                    EntityName = "new_present_record",
                    ColumnSet = new ColumnSet("new_sunday_present_this_week", "new_group_present_this_week")
                };

                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition("new_list_new_present_record", ConditionOperator.Equal, listEntityId);
                filter.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactId);
                filter.AddCondition("new_sunday_date", ConditionOperator.LastXWeeks, weekPeriod);
                query.Criteria = filter;

                query.AddOrder("new_sunday_date", OrderType.Descending);

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryPresentRecordByContactIdAndSunday 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據主日日期排序查詢出席記錄
        /// </summary>
        public EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
        {
            try
            {
                var query = new QueryExpression
                {
                    EntityName = childEntityName,
                    ColumnSet = new ColumnSet(true)
                };

                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition(associationName, ConditionOperator.Equal, parentEntityId);
                filter.AddCondition("statecode", ConditionOperator.Equal, 0);
                filter.AddCondition("new_sunday_date", ConditionOperator.GreaterEqual, DateTime.UtcNow.AddDays(-32));
                query.Criteria = filter;

                query.AddOrder("new_sunday_date", OrderType.Ascending);

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryPresentRecordSortBySunday 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 使用 FetchXML 查詢最近N週的出席記錄
        /// </summary>
        public EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId)
        {
            try
            {
                var lastWeeksString = $"'{lastWeeks}'";
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
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                          <condition attribute='new_sunday_date' operator='last-x-weeks' value={lastWeeksString} />
                        </filter>
                      </entity>
                    </fetch>";

                var fetchRequest = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                var response = (RetrieveMultipleResponse)_organizationService.Execute(fetchRequest);
                return response.EntityCollection;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryPresentRecordSortBySundayFetchXml 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據週報和聯絡人ID查詢出席記錄
        /// </summary>
        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId)
        {
            try
            {
                var query = new QueryExpression
                {
                    EntityName = "new_present_record",
                    ColumnSet = new ColumnSet(true)
                };

                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition("new_group_present_weekly_report_prese", ConditionOperator.Equal, weeklyReportEntityId);
                filter.AddCondition("statecode", ConditionOperator.Equal, 0);
                filter.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactId);
                query.Criteria = filter;

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryPresentRecordInWeeklyReportByContactId 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據日期範圍查詢實體清單
        /// 修復：new_class_end_date 是父實體的欄位，需要使用 LinkEntity 關聯
        /// </summary>
        public EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName)
        {
            try
            {
                var query = new QueryExpression
                {
                    EntityName = childEntityName,
                    ColumnSet = new ColumnSet(true)
                };

                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition(associationName, ConditionOperator.Equal, parentEntityId);
                filter.AddCondition("statecode", ConditionOperator.Equal, 0);
                query.Criteria = filter;

                // ? 修復：new_class_end_date 是父實體 (new_disciple_lessons) 的欄位
                // 需要透過 LinkEntity 關聯到父實體，並在父實體上添加日期過濾條件
                if (parentEntityName == "new_disciple_lessons")
                {
                    var linkEntity = new LinkEntity
                    {
                        LinkFromEntityName = childEntityName,
                        LinkToEntityName = parentEntityName,
                        LinkFromAttributeName = associationName,
                        LinkToAttributeName = parentEntityIdName,
                        JoinOperator = JoinOperator.Inner
                    };

                    // 在父實體 (課程) 上添加日期過濾：課程結束日期在最近7天內或之後
                    var linkFilter = new FilterExpression(LogicalOperator.And);
                    linkFilter.AddCondition("new_class_end_date", ConditionOperator.OnOrAfter, DateTime.UtcNow.AddDays(-7));
                    linkEntity.LinkCriteria = linkFilter;

                    query.LinkEntities.Add(linkEntity);
                }

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryEntityListByDate 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 查詢週報(根據主日日期)
        /// </summary>
        public EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId)
        {
            try
            {
                var query = new QueryExpression
                {
                    EntityName = "new_group_present_weekly_report",
                    ColumnSet = new ColumnSet("new_small_group_member_number", "new_sunday_present_number",
                        "new_sunday_present_rate", "new_small_group_number", "new_small_group_rate",
                        "new_memo", "new_weekly_report_status")
                };

                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, listEntityId);
                filter.AddCondition("statecode", ConditionOperator.Equal, 0);
                filter.AddCondition("new_sunday_date", ConditionOperator.On, sunday);
                query.Criteria = filter;

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
        /// 查詢週報(主日日期前兩個月)
        /// </summary>
        public EntityCollection QueryWeeklyReportBeforeTwoMonthOfSunday(DateTime sunday, Guid listEntityId)
        {
            try
            {
                var query = new QueryExpression
                {
                    EntityName = "new_group_present_weekly_report",
                    ColumnSet = new ColumnSet(true)
                };

                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, listEntityId);
                filter.AddCondition("statecode", ConditionOperator.Equal, 0);
                filter.AddCondition("new_sunday_date", ConditionOperator.OnOrAfter, sunday.AddMonths(-2));
                filter.AddCondition("new_sunday_date", ConditionOperator.OnOrBefore, sunday);
                query.Criteria = filter;

                query.AddOrder("new_sunday_date", OrderType.Ascending);

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryWeeklyReportBeforeTwoMonthOfSunday 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據聯絡人ID查詢名單
        /// </summary>
        public EntityCollection QueryListByContactId(Guid contactId, string associationName)
        {
            try
            {
                var query = new QueryExpression
                {
                    EntityName = "list",
                    ColumnSet = new ColumnSet(
                        "listid",
                        "listname",
                        "purpose",
                        "new_app_named",
                        "new_contact_family_leader_list",
                        "new_contact_race_leager_list",
                        "new_contact_list_arealeader",
                        "new_contact_list_vice_family_leader",
                        "new_contact_co_race_leager_list",
                        "new_contact_list_co_arealeader",
                        "new_familyhead_list",
                        "new_happy_start_date",
                        "new_happy_end_date",
                        "statuscode",
                        "statecode"),
                    PageInfo = new PagingInfo
                    {
                        Count = 5000,
                        PageNumber = 1
                    }
                };

                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition(associationName, ConditionOperator.Equal, contactId);
                filter.AddCondition("statecode", ConditionOperator.Equal, 0);
                filter.AddCondition("new_app_named", ConditionOperator.Equal, true);
                query.Criteria = filter;

                query.AddOrder("listname", OrderType.Ascending);

                return _organizationService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryListByContactId 發生錯誤");
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
