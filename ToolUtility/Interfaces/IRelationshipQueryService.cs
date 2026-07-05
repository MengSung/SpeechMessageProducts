// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Interfaces/IRelationshipQueryService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IRelationshipQueryService
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
    /// N:1 和 N:N 關聯查詢服務介面
    /// </summary>
    public interface IRelationshipQueryService
    {
        /// <summary>
        /// 查詢 N:1 關聯的集合
        /// </summary>
        EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢 N:1 關聯的集合(根據名稱排序)
        /// </summary>
        EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢 N:1 關聯(使用 LinkEntity 取得關聯資料)
        /// </summary>
        EntityCollection RetrieveManyToOneWithLinkEntity();

        /// <summary>
        /// 查詢週報(根據主日日期和N:1關聯)
        /// </summary>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName,
            string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢 N:N (ManyToMany) 的集合
        /// </summary>
        EntityCollection QueryManyToMany(string conditionAttributeName, string entityNameToSearch,
            string linkFromEntityName, string linkFromAttributeName, string linkToEntityName,
            string linkToAttributeName, string attributeName, Guid entityIdValue);

        /// <summary>
        /// 連絡人相關的各類名單 (N:N查詢)
        /// </summary>
        EntityCollection QueryListOfContactManyToMany(Guid contactId);
    }
}
