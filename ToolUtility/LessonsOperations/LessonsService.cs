// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/LessonsOperations/LessonsService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class LessonsService
// 主要成員：RetrieveEnrolledLessons、RetrieveLessonsByMonth、RetrieveStorLessons
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

namespace ToolUtilityNameSpace.LessonsOperations
{
    public class LessonsService : ILessonsService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public LessonsService(object logger, IOrganizationService organizationService)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        public EntityCollection RetrieveEnrolledLessons(DateTime startDate, DateTime endDate, string contactName, string contactId)
        {
            string s = $"'{startDate:yyyy-M-d}'";
            string e = $"'{endDate:yyyy-M-d}'";
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value={s} />
                            <condition attribute='new_class_end_date' operator='on-or-before' value={e} />
                        </filter>
                        <link-entity name='new_stor_lessons' from='new_new_disciple_lessons_new_stor_les' to='new_disciple_lessonsid' alias='ab'>
                          <filter type='and'>
                            <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname={contactName} uitype ='contact' value={contactId} />
                          </filter>
                        </link-entity>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate)
        {
            string s = $"'{startDate:yyyy-M-d}'";
            string e = $"'{endDate:yyyy-M-d}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value={s} />
                            <condition attribute='new_class_end_date' operator='on-or-before' value={e} />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveStorLessons(string lessonName, string lessonId, string contactName, string contactId)
        {
            lessonName = $"'{lessonName}'";
            lessonId = $"'{{{lessonId}}}'";
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='5'>
                      <entity name='new_stor_lessons'>
                        <attribute name='createdon' />
                        <attribute name='new_contact_new_stor_lessons' />
                        <attribute name='new_fee' />
                        <attribute name='new_pay_date' />
                        <attribute name='new_new_disciple_lessons_new_stor_les' />
                        <attribute name='new_stor_lessonsid' />
                        <order attribute='createdon' descending='true' />
                        <filter type='and'>
                          <condition attribute='new_enroll_status' operator='not-in'>
                            <value>100000007</value>
                            <value>100000009</value>
                            <value>100000003</value>
                          </condition>
                          <condition attribute='new_new_disciple_lessons_new_stor_les' operator='eq' uiname={lessonName} uitype='new_disciple_lessons' value={lessonId} />
                          <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                          <condition attribute='statuscode' operator='ne' value='2' />
                          <condition attribute='statecode' operator='eq' value='0' />
                        </filter>
                        <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
                          <attribute name='telephone2' />
                          <attribute name='address2_line1' />
                          <attribute name='parentcustomerid' />
                          <attribute name='mobilephone' />
                          <attribute name='emailaddress1' />
                        </link-entity>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}
