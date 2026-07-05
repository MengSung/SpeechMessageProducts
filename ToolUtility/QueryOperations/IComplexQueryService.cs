// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/QueryOperations/IComplexQueryService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IComplexQueryService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
