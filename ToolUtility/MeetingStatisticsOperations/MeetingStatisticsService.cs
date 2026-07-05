// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/MeetingStatisticsOperations/MeetingStatisticsService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class MeetingStatisticsService
// 主要成員：RetrieveBySunday、RetrieveByWeeklyReportAndContact、RetrieveBySundayDateAndContact、RetrieveByWeeklyReportAndContactAlt、RetrieveWithExpiredDateByContact、RetrieveByContactSmallGroupAndSundayDate
// 引用命名空間：System、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、ToolUtilityNameSpace.EntityOperations
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
        private readonly IOrganizationService _organizationService;

        public MeetingStatisticsService(object logger, IOrganizationService organizationService)
        {
            _logger = logger;
            _organizationService = organizationService;
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
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
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

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
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

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
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

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
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

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
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

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}
