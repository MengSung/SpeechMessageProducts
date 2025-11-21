using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.FeeOperations
{
    public class FeeService : IFeeService
    {
        private readonly object _logger;
        private readonly IEntityQueryService _queryService;

        public FeeService(object logger, IEntityQueryService queryService)
        {
            _logger = logger;
            _queryService = queryService;
        }

        public EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
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
                              <condition attribute='new_dedication_booking_new_fee' operator='eq' uiname={dedicationBookingName} uitype ='new_dedication_booking' value={dedicationBookingId} />
                              <condition attribute='new_paid_period' operator='eq' value='{paidPeriod}' />
                            </filter>
                          </entity>
                        </fetch>";
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveDedicationBooking(string contactName, string contactId)
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
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection QueryDedicationContacts(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit)
        {
            dedicationNumber = $"'{dedicationNumber}'";
            contactName = $"'%{contactName}%'";
            homePhone = $"'%{homePhone}%'";
            mobile = $"'%{mobile}%'";
            nationId = $"'%{nationId}%'";
            lastSixDigit = $"'%{lastSixDigit}%'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                              <entity name='contact'>
                                <attribute name='fullname' />
                                <attribute name='telephone2' />
                                <attribute name='address2_line1' />
                                <attribute name='parentcustomerid' />
                                <attribute name='new_church_jobtitle' />
                                <attribute name='mobilephone' />
                                <attribute name='emailaddress1' />
                                <attribute name='pager' />
                                <attribute name='new_cell_list_contact' />
                                <attribute name='new_personal_id' />
                                <attribute name='new_last_six_digit' />
                                <attribute name='contactid' />
                                <order attribute='fullname' descending='false' />
                                <filter type='and'>
                                  <filter type='or'>
                                    <condition attribute='pager' operator='eq' value={dedicationNumber} />
                                    <condition attribute='fullname' operator='like' value={contactName} />
                                    <condition attribute='telephone2' operator='like' value={homePhone} />
                                    <condition attribute='mobilephone' operator='like' value={mobile} />
                                    <condition attribute='new_personal_id' operator='like' value={nationId} />
                                    <condition attribute='new_last_six_digit' operator='like' value={lastSixDigit} />
                                  </filter>
                                    <condition attribute='statuscode' operator='eq' value='1' />
                                </filter>
                              </entity>
                            </fetch>";
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection QueryDedicationContactsStartedNumber(string dedicationStartNumber)
        {
            dedicationStartNumber = $"'{dedicationStartNumber}%'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='3'>
                              <entity name='contact'>
                                <attribute name='fullname' />
                                <attribute name='pager' />
                                <attribute name='telephone2' />
                                <attribute name='address2_line1' />
                                <attribute name='parentcustomerid' />
                                <attribute name='new_church_jobtitle' />
                                <attribute name='mobilephone' />
                                <attribute name='emailaddress1' />
                                <attribute name='contactid' />
                                <order attribute='pager' descending='true' />
                                <filter type='and'>
                                  <condition attribute='pager' operator='like' value={dedicationStartNumber} />
                                </filter>
                              </entity>
                            </fetch>";
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}