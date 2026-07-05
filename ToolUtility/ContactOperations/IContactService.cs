// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ContactOperations/IContactService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IContactService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.ContactOperations
{
    public interface IContactService
    {
        // Basic retrievals
        Entity RetrieveByContactId(string contactId);
        Entity RetrieveByLineId(string lineId);
        EntityCollection RetrieveCollectionByLineId(string lineId);
        EntityCollection RetrieveCollectionByName(string contactFullName);
        Entity RetrieveByAccountNumber(string accountNumber, string password);

        // Additional retrieval variants used by facade legacy methods
        string GetContactInfoByContactId(string contactId);
        Entity RetrieveByContactId(IOrganizationService externalService, string contactId, ref int count);
        string GetContactInfoByFullName(string fullName);
        Entity RetrieveByFullName(string fullName);
        Entity RetrieveByFullName(IOrganizationService externalService, string fullName);
        string GetContactInfoByFullName(IOrganizationService externalService, string fullName);
        EntityCollection RetrieveCollectionByNationId(string nationId);
        string AccountLogin(string accountNumber, string password); // returns id or error message
        Entity RetrieveAccountEntity(string accountNumber); // existence check
        Entity RetrieveByFullNameAndMobile(string fullName, string mobileNumber);
        EntityCollection RetrieveCollectionByFullName(string fullName);
        Entity RetrieveByLineIdForCollection(string lineId); // same as RetrieveByLineId kept for backward naming

        // FetchXml 查詢方法 (用於複雜查詢)
        /// <summary>
        /// 使用 FetchXML 查詢奉獻聯絡人
        /// </summary>
        EntityCollection QueryDediccationContatsByFetchXml(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit);

        /// <summary>
        /// 根據開頭奉獻編號查詢聯絡人
        /// </summary>
        EntityCollection QueryContatsByStartedDedicationNumber(string dedicationStartNumber);
    }
}
