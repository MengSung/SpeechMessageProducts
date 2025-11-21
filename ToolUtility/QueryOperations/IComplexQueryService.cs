using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 複雜查詢服務介面 - 處理 Many-to-One、週報、出席記錄等複雜查詢
    /// </summary>
    public interface IComplexQueryService
    {
        #region Many-to-One 關係查詢
        /// <summary>
        /// 查詢 Many-to-One 關係集合
        /// </summary>
        EntityCollection RetrieveManyToOneCollection();

        /// <summary>
        /// 根據聯絡人ID查詢血液報告
        /// </summary>
        Entity QueryBloodReportByContactId(Guid contactId);

        /// <summary>
        /// 查詢出席記錄（根據聯絡人ID和主日日期，最近N個月）
        /// </summary>
        EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int monthPeriod);

        /// <summary>
        /// 查詢 Many-to-One 關係
        /// </summary>
        EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢出席記錄並按主日日期排序
        /// </summary>
        EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 使用 FetchXML 查詢出席記錄並按主日日期排序
        /// </summary>
        EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId);

        /// <summary>
        /// 根據週報ID和聯絡人ID查詢出席記錄
        /// </summary>
        EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId);

        /// <summary>
        /// 根據日期查詢實體列表
        /// </summary>
        EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);
        #endregion

        #region 週報查詢
        /// <summary>
        /// 根據主日日期查詢週報
        /// </summary>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 根據主日日期和名單ID查詢週報
        /// </summary>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId);

        /// <summary>
        /// 查詢主日前兩個月的週報
        /// </summary>
        EntityCollection QueryWeeklyReportBeforeTwoMonthsOfSunday(DateTime sunday, Guid listEntityId);
        #endregion

        #region 名單查詢
        /// <summary>
        /// 查詢名單並按名單名稱排序
        /// </summary>
        EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 根據聯絡人ID查詢名單
        /// </summary>
        EntityCollection QueryListByContactId(Guid contactId, string associationName);
        #endregion

        #region 成員名單查詢
        /// <summary>
        /// 根據 Line ID 查詢聯絡人集合
        /// </summary>
        Entity RetrieveContactCollectionByLineId(string lineId);

        /// <summary>
        /// 根據名單ID查詢成員名單集合（Dynamics 365）
        /// </summary>
        EntityCollection RetrieveMemberListCollectionByListIdDynamics365(Guid listId);

        /// <summary>
        /// 查詢動態成員名單（Dynamics 365）
        /// </summary>
        EntityCollection RetrieveDynamicMemberListDynamics365(Guid listId);
        #endregion
    }
}
