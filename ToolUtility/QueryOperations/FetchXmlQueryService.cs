using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// FetchXML 查詢服務實作
    /// 專責處理所有的 FetchXML 查詢
    /// ? Phase 3.1: 查詢優化 - 添加 top 限制、減少 link-entity
    /// </summary>
    public class FetchXmlQueryService : IFetchXmlQueryService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;
        
        // ? 預設查詢限制，防止返回過多資料
        private const int DEFAULT_TOP_LIMIT = 5000;
        private const int SMALL_QUERY_LIMIT = 1000;

        public FetchXmlQueryService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 根據聯絡人查詢學習課紀錄 (使用 FetchXML)
        /// ? Phase 3.1: 優化 - 添加 top 限制、簡化 link-entity
        /// </summary>
        public EntityCollection RetrieveStorLessonsByFetchXml(string contactName, string contactId)
        {
            try
            {
                contactName = $"'{contactName}'";
                contactId = $"'{{{contactId}}}'";

                // ? 優化 1: 添加 top='1000' 限制
                // ? 優化 2: 只查詢必要欄位，移除不需要的 link-entity 欄位
                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='{SMALL_QUERY_LIMIT}'>
                          <entity name='new_stor_lessons'>
                            <attribute name='createdon' />
                            <attribute name='new_contact_new_stor_lessons' />
                            <attribute name='new_fee' />
                            <attribute name='new_pay_date' />
                            <attribute name='new_current_complete' />
                            <attribute name='new_new_disciple_lessons_new_stor_les' />
                            <attribute name='new_stor_lessonsid' />
                            <order attribute='createdon' descending='true' />
                            <filter type='and'>
                                <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                                <condition attribute='statecode' operator='eq' value='0' />
                            </filter>
                            <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='contact'>
                              <attribute name='mobilephone' />
                              <attribute name='emailaddress1' />
                            </link-entity>
                            <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' to='new_new_disciple_lessons_new_stor_les' alias='lesson'>
                              <attribute name='new_name' />
                              <filter type='and'>
                                <condition attribute='new_classification' operator='in'>
                                  <value>100000000</value>
                                  <value>100000001</value>
                                </condition>
                              </filter>
                            </link-entity>
                          </entity>
                        </fetch>";

                var fetchXmlNoConstrain = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='{SMALL_QUERY_LIMIT}'>
                          <entity name='new_stor_lessons'>
                            <attribute name='createdon' />
                            <attribute name='new_contact_new_stor_lessons' />
                            <attribute name='new_fee' />
                            <attribute name='new_pay_date' />
                            <attribute name='new_current_complete' />
                            <attribute name='new_new_disciple_lessons_new_stor_les' />
                            <attribute name='new_stor_lessonsid' />
                            <order attribute='createdon' descending='true' />
                            <filter type='and'>
                                <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                                <condition attribute='statecode' operator='eq' value='0' />
                            </filter>
                            <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='contact'>
                              <attribute name='mobilephone' />
                              <attribute name='emailaddress1' />
                            </link-entity>
                            <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' to='new_new_disciple_lessons_new_stor_les' alias='lesson'>
                              <attribute name='new_name' />
                            </link-entity>
                          </entity>
                        </fetch>";

                // ? 優化 1: 添加 top='1000' 限制
                // ? 優化 2: 只查詢必要欄位，移除不需要的 link-entity 欄位
                // new_classification 過濾已移除，返回所有分類的課程
                var fetchXmlRemoveConstrain = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='{SMALL_QUERY_LIMIT}'>
                          <entity name='new_stor_lessons'>
                            <attribute name='createdon' />
                            <attribute name='new_contact_new_stor_lessons' />
                            <attribute name='new_fee' />
                            <attribute name='new_pay_date' />
                            <attribute name='new_current_complete' />
                            <attribute name='new_new_disciple_lessons_new_stor_les' />
                            <attribute name='new_stor_lessonsid' />
                            <order attribute='createdon' descending='true' />
                            <filter type='and'>
                                <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                                <condition attribute='statecode' operator='eq' value='0' />
                            </filter>
                            <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='contact'>
                              <attribute name='mobilephone' />
                              <attribute name='emailaddress1' />
                            </link-entity>
                            <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' to='new_new_disciple_lessons_new_stor_les' alias='lesson'>
                              <attribute name='new_name' />
                            </link-entity>
                          </entity>
                        </fetch>";

                var fetchRequest = new RetrieveMultipleRequest
                {
                    //Query = new FetchExpression(fetchXml)
                    Query = new FetchExpression(fetchXmlRemoveConstrain)
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
        /// 根據課程查詢學習課紀錄 (使用 FetchXML)
        /// ? Phase 3.1: 優化 - 添加 top 限制、簡化查詢
        /// </summary>
        public EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(string lessonName, string lessonId)
        {
            try
            {
                lessonName = $"'{lessonName}'";
                lessonId = $"'{{{lessonId}}}'";

                // ? 優化: 添加 top 限制、減少不必要的欄位
                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='{SMALL_QUERY_LIMIT}'>
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
                          <condition attribute='statuscode' operator='ne' value='2' />
                          <condition attribute='statecode' operator='eq' value='0' />
                        </filter>
                        <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='contact'>
                          <attribute name='fullname' />
                          <attribute name='mobilephone' />
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
        /// 根據聯絡人查詢奉獻預約 (使用 FetchXML)
        /// ? Phase 3.1: 優化 - 添加 top 限制
        /// </summary>
        public EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId)
        {
            try
            {
                contactName = $"'{contactName}'";
                contactId = $"'{{{contactId}}}'";

                // ? 優化: 添加 top 限制、按創建日期倒序
                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='{SMALL_QUERY_LIMIT}'>
                          <entity name='new_dedication_booking'>
                            <attribute name='new_dedication_bookingid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <attribute name='new_dedication_booking_status' />
                            <order attribute='createdon' descending='true' />
                            <filter type='and'>
                              <condition attribute='new_contact_new_dedication_booking' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                              <condition attribute='new_dedication_booking_status' operator='eq' value='100000001' />
                              <condition attribute='statecode' operator='eq' value='0' />
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
        /// ? Phase 3.1: 優化 - 添加 top 限制
        /// </summary>
        public EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime sundayDate)
        {
            try
            {
                string sundayDateString = $"'{sundayDate.Year}-{sundayDate.Month:D2}-{sundayDate.Day:D2}'";

                // ? 優化: 添加 top 限制（通常只有少數記錄）
                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='100'>
                          <entity name='new_meeting_statistics'>
                            <attribute name='new_meeting_statisticsid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <attribute name='new_sunday_date' />
                            <order attribute='createdon' descending='true' />
                            <filter type='and'>
                              <condition attribute='statuscode' operator='eq' value='1' />
                              <condition attribute='new_sunday_date' operator='on' value={sundayDateString} />
                              <condition attribute='statecode' operator='eq' value='0' />
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
        /// 根據奉獻預約和已付期數查詢收費單 (使用 FetchXML)
        /// ? Phase 3.1: 優化 - 添加 top 限制
        /// </summary>
        public EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
        {
            try
            {
                dedicationBookingName = $"'{dedicationBookingName}'";
                dedicationBookingId = $"'{{{dedicationBookingId}}}'";

                // ? 優化: 添加 top 限制
                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='500'>
                          <entity name='new_fee'>
                            <attribute name='new_feeid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <attribute name='new_paid_period' />
                            <order attribute='createdon' descending='true' />
                            <filter type='and'>
                              <condition attribute='new_dedication_booking_new_fee' operator='eq' uiname={dedicationBookingName} uitype='new_dedication_booking' value={dedicationBookingId} />
                              <condition attribute='new_paid_period' operator='eq' value='{paidPeriod}' />
                              <condition attribute='statecode' operator='eq' value='0' />
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
        /// ? Phase 3.1: 優化 - 添加 top 限制
        /// </summary>
        public EntityCollection RetrieveListByFetchXml()
        {
            try
            {
                // ? 優化: 添加 top 限制（小組名單通常不會超過 500 個）
                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='500'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='false' />
                        <filter type='and'>
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='new_app_named' operator='eq' value='1' />
                          <condition attribute='statecode' operator='eq' value='0' />
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
        /// ? Phase 3.1: 優化 - 添加 top 限制、簡化查詢
        /// </summary>
        public EntityCollection RetrieveSmallGroupListCollectionByFetchXml()
        {
            try
            {
                // ? 優化: 添加 top 限制
                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='500'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='new_contact_race_leager_list' />
                        <attribute name='new_contact_family_leader_list' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_app_named' operator='eq' value='1' />
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='listname' operator='not-like' value='%測試%' />
                          <condition attribute='statecode' operator='eq' value='0' />
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
    }
}
