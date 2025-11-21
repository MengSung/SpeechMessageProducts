using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace.FeeOperations
{
    public interface IFeeService
    {
        EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod);
        EntityCollection RetrieveDedicationBooking(string contactName, string contactId);
        
        /// <summary>
        /// 根據連絡人查詢奉獻收費單
        /// </summary>
        EntityCollection RetrieveDedicationFee(string contactName, string contactId);
        
        /// <summary>
        /// 根據連絡人和日期範圍查詢奉獻收費單
        /// </summary>
        EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate);
    }
}