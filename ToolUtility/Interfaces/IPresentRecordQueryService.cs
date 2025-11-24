using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 個人聚會與靈修記錄查詢服務介面
    /// </summary>
    public interface IPresentRecordQueryService
    {
        /// <summary>
        /// 搜尋主日日期是最近N週的靈修單
        /// </summary>
        EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int weekPeriod);

        /// <summary>
        /// 根據主日日期排序查詢出席記錄
        /// </summary>
        EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 使用 FetchXML 查詢最近N週的出席記錄
        /// </summary>
        EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId);

        /// <summary>
        /// 根據週報和聯絡人ID查詢出席記錄
        /// </summary>
        EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId);

        /// <summary>
        /// 根據日期範圍查詢實體清單
        /// </summary>
        EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢週報(根據主日日期)
        /// </summary>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId);

        /// <summary>
        /// 查詢週報(主日日期前兩個月)
        /// </summary>
        EntityCollection QueryWeeklyReportBeforeTwoMonthOfSunday(DateTime sunday, Guid listEntityId);

        /// <summary>
        /// 根據聯絡人ID查詢名單
        /// </summary>
        EntityCollection QueryListByContactId(Guid contactId, string associationName);
    }
}
