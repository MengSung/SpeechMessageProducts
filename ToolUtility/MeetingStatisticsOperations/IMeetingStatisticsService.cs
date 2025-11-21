using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    /// <summary>
    /// 個人聚會與靈修記錄服務介面
    /// </summary>
    public interface IMeetingStatisticsService
    {
        EntityCollection RetrieveBySunday(DateTime sundayDate);
        
        /// <summary>
        /// 根據週報和連絡人查詢出席記錄
        /// </summary>
        EntityCollection RetrieveByWeeklyReportAndContact(string weeklyReportName, string weeklyReportId, string contactName, string contactId);
        
        /// <summary>
        /// 根據連絡人和主日日期查詢出席記錄
        /// </summary>
        EntityCollection RetrieveBySundayDateAndContact(string contactName, string contactId, DateTime sundayDate);
        
        /// <summary>
        /// 根據週報和連絡人查詢出席記錄
        /// </summary>
        EntityCollection RetrieveByWeeklyReportAndContactAlt(string contactName, string contactId, string weeklyReportName, string weeklyReportId);
        
        /// <summary>
        /// 查詢包含關懷到期日的出席記錄
        /// </summary>
        EntityCollection RetrieveWithExpiredDateByContact(string contactName, string contactId);
        
        /// <summary>
        /// 根據連絡人、小組和主日日期查詢出席記錄
        /// </summary>
        EntityCollection RetrieveByContactSmallGroupAndSundayDate(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate);
    }
}
