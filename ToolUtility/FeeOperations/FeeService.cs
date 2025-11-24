using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.FeeOperations
{
    public class FeeService : IFeeService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public FeeService(object logger, IOrganizationService organizationService)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        public EntityCollection RetrieveDedicationBooking(string contactName, string contactId)
        {
            // Placeholder implementation
            return new EntityCollection();
        }

        public EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
        {
            // Placeholder implementation
            return new EntityCollection();
        }

        /// <summary>
        /// 根據連絡人查詢奉獻收費單
        /// </summary>
        public EntityCollection RetrieveDedicationFee(string contactName, string contactId)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_fee'>
                            <attribute name='new_feeid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <attribute name='new_pay_date' />
                            <attribute name='new_fee_shoud_pay' />
                            <attribute name='new_fee_really_paid' />
                            <attribute name='new_pay_way' />
                            <attribute name='new_category' />
                            <attribute name='new_others' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_contact_new_fee' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                              <condition attribute='new_category' operator='not-null' />
                            </filter>
                          </entity>
                        </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據連絡人和日期範圍查詢奉獻收費單
        /// </summary>
        public EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            string startDateString = $"'{startDate:yyyy-M-d}'";
            string endDateString = $"'{endDate:yyyy-M-d}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                                  <entity name='new_fee'>
                                    <attribute name='new_feeid' />
                                    <attribute name='new_name' />
                                    <attribute name='createdon' />
                                    <attribute name='new_pay_date' />
                                    <attribute name='new_fee_shoud_pay' />
                                    <attribute name='new_fee_really_paid' />
                                    <attribute name='new_pay_way' />
                                    <attribute name='new_category' />
                                    <attribute name='new_others' />
                                    <attribute name='new_paid_period' />
                                    <order attribute='new_name' descending='false' />
                                    <filter type='and'>
                                      <condition attribute='new_contact_new_fee' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                                      <condition attribute='new_category' operator='not-null' />
                                      <condition attribute='new_pay_status' operator='in'>
                                        <value>100000001</value>
                                        <value>100000002</value>
                                        <value>100000003</value>
                                        <value>100000004</value>
                                        <value>100000006</value>
                                      </condition>
                                      <condition attribute='new_pay_date' operator='on-or-after'  value={startDateString} />
                                      <condition attribute='new_pay_date' operator='on-or-before' value={endDateString} />
                                    </filter>
                                  </entity>
                                </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}