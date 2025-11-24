using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// FetchXML 查詢服務實作
    /// 專門處理複雜的 FetchXML 查詢
    /// </summary>
    public class FetchXmlQueryService : IFetchXmlQueryService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public FetchXmlQueryService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 根據聯絡人查詢學員上課記錄 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveStorLessonsByFetchXml(string contactName, string contactId)
        {
            try
            {
                contactName = $"'{contactName}'";
                contactId = $"'{{{contactId}}}'";

                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_stor_lessons'>
                            <attribute name='createdon' />
                            <attribute name='new_contact_new_stor_lessons' />
                            <attribute name='new_fee' />
                            <attribute name='new_pay_date' />
                            <attribute name='new_current_complete' />
                            <attribute name='new_new_disciple_lessons_new_stor_les' />
                            <attribute name='new_stor_lessonsid' />
                            <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
                            <order attribute='new_contact_new_stor_lessons' descending='false' />
                            <filter type='and'>
                                <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                            </filter>
                            <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
                              <attribute name='telephone2' />
                              <attribute name='address2_line1' />
                              <attribute name='parentcustomerid' />
                              <attribute name='mobilephone' />
                              <attribute name='emailaddress1' />
                            </link-entity>
                            <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' to='new_new_disciple_lessons_new_stor_les' alias='ab'>
                              <filter type='and'>
                                <condition attribute='new_classification' operator='in'>
                                  <value>100000000</value>
                                  <value>100000001</value>
                                </condition>
                              </filter>
                            </link-entity>
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
                SafeLogError(ex, "RetrieveStorLessonsByFetchXml 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據課程查詢學員上課記錄 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(string lessonName, string lessonId)
        {
            try
            {
                lessonName = $"'{lessonName}'";
                lessonId = $"'{{{lessonId}}}'";

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

                var fetchRequest = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                var response = (RetrieveMultipleResponse)_organizationService.Execute(fetchRequest);
                return response.EntityCollection;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "RetrieveStorLessonsByDiscipleLessonsFetchXml 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據聯絡人查詢認獻記錄 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId)
        {
            try
            {
                contactName = $"'{contactName}'";
                contactId = $"'{{{contactId}}}'";

                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_dedication_booking'>
                            <attribute name='new_dedication_bookingid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_contact_new_dedication_booking' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                              <condition attribute='new_dedication_booking_status' operator='eq' value='100000001' />
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
                SafeLogError(ex, "RetrieveDedicationBookingByFetchXml 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據主日日期查詢聚會統計記錄 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime sundayDate)
        {
            try
            {
                string sundayDateString = $"'{sundayDate.Year}-{sundayDate.Month}-{sundayDate.Day}'";

                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_meeting_statistics'>
                            <attribute name='new_meeting_statisticsid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='statuscode' operator='eq' value='1' />
                             <condition attribute='new_sunday_date' operator='on' value={sundayDateString} />
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
                SafeLogError(ex, "RetrieveMeetingStatisticsByFetchXml 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據認獻預約和繳費期間查詢收費單 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
        {
            try
            {
                dedicationBookingName = $"'{dedicationBookingName}'";
                dedicationBookingId = $"'{{{dedicationBookingId}}}'";

                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_fee'>
                            <attribute name='new_feeid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_dedication_booking_new_fee' operator='eq' uiname={dedicationBookingName} uitype='new_dedication_booking' value={dedicationBookingId} />
                              <condition attribute='new_paid_period' operator='eq' value='{paidPeriod}' />
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
                SafeLogError(ex, "RetrieveFeeByFetchXml 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 查詢所有需要點名的小組名單 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveListByFetchXml()
        {
            try
            {
                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='new_app_named' operator='eq' value='1' />
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
                SafeLogError(ex, "RetrieveListByFetchXml 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 查詢所有小組名單集合 (使用 FetchXML)
        /// </summary>
        public EntityCollection RetrieveSmallGroupListCollectionByFetchXml()
        {
            try
            {
                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='new_contact_race_leager_list' />
                        <attribute name='new_contact_family_leader_list' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                          <condition attribute='new_app_named' operator='eq' value='1' />
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='listname' operator='not-like' value='%幸福%' />
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
                SafeLogError(ex, "RetrieveSmallGroupListCollectionByFetchXml 發生錯誤");
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
