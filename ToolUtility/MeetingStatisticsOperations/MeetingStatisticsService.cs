using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    /// <summary>
    /// 個人聚會與靈修記錄服務實作
    /// </summary>
    public class MeetingStatisticsService : IMeetingStatisticsService
    {
        private readonly object _logger;
        private readonly IEntityQueryService _queryService;

        public MeetingStatisticsService(object logger, IEntityQueryService queryService)
        {
            _logger = logger;
            _queryService = queryService;
        }

        public EntityCollection RetrieveBySunday(DateTime sundayDate)
        {
            string dateStr = $"'{sundayDate:yyyy-M-d}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_sunday_date' operator='on' value={dateStr} />
                        </filter>
                      </entity>
                    </fetch>";
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據週報和連絡人查詢出席記錄
        /// </summary>
        public EntityCollection RetrieveByWeeklyReportAndContact(string weeklyReportName, string weeklyReportId, string contactName, string contactId)
        {
            weeklyReportName = $"'{weeklyReportName}'";
            weeklyReportId = $"'{{{weeklyReportId}}}'";
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_group_present_weekly_report_prese' operator='eq' uiname={weeklyReportName} uitype ='new_disciple_lessons' value={weeklyReportId} />
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                        </filter>
                      </entity>
                    </fetch>";

            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據連絡人和主日日期查詢出席記錄
        /// </summary>
        public EntityCollection RetrieveBySundayDateAndContact(string contactName, string contactId, DateTime sundayDate)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            string sundayDateString = $"'{sundayDate:yyyy-M-d}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                             <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                             <condition attribute='new_sunday_date' operator='on' value={sundayDateString} />
                        </filter>
                      </entity>
                    </fetch>";

            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據週報和連絡人查詢出席記錄 (替代方法)
        /// </summary>
        public EntityCollection RetrieveByWeeklyReportAndContactAlt(string contactName, string contactId, string weeklyReportName, string weeklyReportId)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            weeklyReportName = $"'{weeklyReportName}'";
            weeklyReportId = $"'{{{weeklyReportId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_group_present_weekly_report_prese' operator='eq' uiname={weeklyReportName} uitype='new_group_present_weekly_report' value={weeklyReportId} />
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                        </filter>
                      </entity>
                    </fetch>";

            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 查詢包含關懷到期日的出席記錄
        /// </summary>
        public EntityCollection RetrieveWithExpiredDateByContact(string contactName, string contactId)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                          <condition attribute='new_care_expire_date' operator='not-null' />
                        </filter>
                      </entity>
                    </fetch>";

            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據連絡人、小組和主日日期查詢出席記錄
        /// </summary>
        public EntityCollection RetrieveByContactSmallGroupAndSundayDate(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            smallGroupName = $"'{smallGroupName}'";
            smallGroupId = $"'{{{smallGroupId}}}'";
            string sundayDateString = $"'{sundayDate:yyyy-M-d}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_present_record'>
                            <attribute name='new_present_recordid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_list_new_present_record' operator='eq' uiname={smallGroupName} uitype='list' value={smallGroupId} />
                              <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                              <condition attribute='new_sunday_date' operator='on' value={sundayDateString} />
                            </filter>
                          </entity>
                        </fetch>";

            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}
