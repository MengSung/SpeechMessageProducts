using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.AppointmentOperations
{
    public class AppointmentService : IAppointmentService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public AppointmentService(object logger,IOrganizationService organizationService)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        public EntityCollection RetrieveByDate(DateTime selectedDate)
        {
            // Legacy method did not filter by date; keep behavior
            var query = new QueryByAttribute("appointment") { ColumnSet = new ColumnSet(true) };
            return _organizationService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveByDateRange(DateTime startDate, DateTime endDate)
        {
            string start = $"'{startDate:yyyy-M-d}'";
            string end = $"'{endDate:yyyy-M-d}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value={start} />
                          <condition attribute='scheduledstart' operator='on-or-before' value={end} />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveByContactWithinYear(string contactName, string contactId)
        {
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='new_leave_signing_status' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <attribute name='new_hours' />
                        <attribute name='new_days' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_applier_appointment' operator='eq' uiname='{contactName}' uitype='contact' value='{{{contactId}}}' />
                          <condition attribute='scheduledstart' operator='this-year' />
                          <condition attribute='new_leave_signing_status' operator='in'>
                                <value> 100000004 </value >
                                <value> 100000001 </value >
                                <value> 100000007 </value >
                          </condition >
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveByDateRangeAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType)
        {
            string start = $"'{startDate:yyyy-M-d}'";
            string end = $"'{endDate:yyyy-M-d}'";
            string sType = $"'{scheduleType}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value={start} />
                          <condition attribute='scheduledstart' operator='on-or-before' value={end} />
                          <condition attribute='new_meeting_kind' operator='eq' value={sType} />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}
