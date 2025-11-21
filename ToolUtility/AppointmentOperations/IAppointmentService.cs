using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AppointmentOperations
{
    public interface IAppointmentService
    {
        EntityCollection RetrieveByDate(DateTime selectedDate);
        EntityCollection RetrieveByDateRange(DateTime startDate, DateTime endDate);
        EntityCollection RetrieveByContactWithinYear(string contactName, string contactId);
        EntityCollection RetrieveByDateRangeAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType);
    }
}
