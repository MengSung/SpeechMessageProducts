using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.LessonsOperations
{
    public class LessonsService : ILessonsService
    {
        private readonly object _logger;
        private readonly IEntityQueryService _queryService;

        public LessonsService(object logger, IEntityQueryService queryService)
        {
            _logger = logger;
            _queryService = queryService;
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
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
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
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveStorLessons(string lessonName, string lessonId, string contactName, string contactId)
        {
            lessonName = $"'{lessonName}'";
            lessonId = $"'{{{lessonId}}}'";
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_stor_lessons'>
                        <attribute name='createdon' />
                        <attribute name='new_contact_new_stor_lessons' />
                        <attribute name='new_fee' />
                        <attribute name='new_pay_date' />
                        <attribute name='new_new_disciple_lessons_new_stor_les' />
                        <attribute name='new_stor_lessonsid' />
                        <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
                        <order attribute='new_contact_new_stor_lessons' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_enroll_status' operator='not-in'>
                            <value>100000007</value>
                            <value>100000009</value>
                            <value>100000003</value>
                          </condition>
                          <condition attribute='new_new_disciple_lessons_new_stor_les' operator='eq' uiname={lessonName} uitype='new_disciple_lessons' value={lessonId} />
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
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}
