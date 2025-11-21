using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace.FeeOperations
{
    public interface IFeeService
    {
        EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod);
        EntityCollection RetrieveDedicationBooking(string contactName, string contactId);
        EntityCollection QueryDedicationContacts(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit);
        EntityCollection QueryDedicationContactsStartedNumber(string dedicationStartNumber);
    }
}