// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.Contact.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：RetrieveContactByContactId、RetrieveContactByName、RetrieveContactEntityByName、RetrieveContactByName_ReturnString、RetrieveContactCollectionByName、RetrieveContactCollectionByNationId、RetrieveContactByLineId、RetrieveContactByAccountNumber、DoesAccountExist、RetrieveContactEntityByAccountNumber
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Client、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using System;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 聯絡人操作 (Partial Class 2/10)
    /// 包含：所有聯絡人相關的查詢方法
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region 聯絡人查詢方法
        public String RetrieveContactByContactId(String ContactId)
            => _facade.RetrieveContactByContactId(ContactId);

        public Entity RetrieveContactByContactId(ref IOrganizationService aOrganizationService, String ContactId, ref int Count)
            => _facade.RetrieveContactByContactId(ref aOrganizationService, ContactId, ref Count);

        public String RetrieveContactByName(String ContactFullName)
            => _facade.RetrieveContactByName(ContactFullName);

        public Entity RetrieveContactEntityByName(String ContactFullName)
            => _facade.RetrieveContactEntityByName(ContactFullName);

        public Entity RetrieveContactByName(ref IOrganizationService aOrganizationService, String ContactFullName)
            => _facade.RetrieveContactByName(ref aOrganizationService, ContactFullName);

        public String RetrieveContactByName_ReturnString(ref IOrganizationService aOrganizationService, String ContactFullName)
            => _facade.RetrieveContactByName_ReturnString(ref aOrganizationService, ContactFullName);

        public EntityCollection RetrieveContactCollectionByName(String ContactFullName)
            => _facade.RetrieveContactCollectionByName(ContactFullName);

        public EntityCollection RetrieveContactCollectionByNationId(String ContactFullName)
            => _facade.RetrieveContactCollectionByNationId(ContactFullName);

        public Entity RetrieveContactByLineId(String LineId)
            => _facade.RetrieveContactByLineId(LineId);

        public String RetrieveContactByAccountNumber(String AccountNumber, String aPassword)
            => _facade.RetrieveContactByAccountNumber(AccountNumber, aPassword);

        public Entity DoesAccountExist(String AccountNumber)
            => _facade.DoesAccountExist(AccountNumber);

        public Entity RetrieveContactEntityByAccountNumber(String AccountNumber, String aPassword)
            => _facade.RetrieveContactEntityByAccountNumber(AccountNumber, aPassword);

        public Entity RetrieveContactEntityByLineUserId(String LineUserId)
            => _facade.RetrieveContactEntityByLineUserId(LineUserId);

        public Entity RetrieveContactEntityByFullNameAndMobileNumber(String FullName, String MobileNumber)
            => _facade.RetrieveContactEntityByFullNameAndMobileNumber(FullName, MobileNumber);

        public EntityCollection RetrieveContactEntityByFullNameCollection(String FullName)
            => _facade.RetrieveContactEntityByFullNameCollection(FullName);

        public EntityCollection QueryDediccationContatsByFetchXml(String DedicationNumber, String ContactName, String HomePhone, String Mobile, String NationId, String LastSixDigit)
            => _facade.QueryDediccationContatsByFetchXml(DedicationNumber, ContactName, HomePhone, Mobile, NationId, LastSixDigit);

        public EntityCollection QueryContatsByStartedDedicationNumber(String DedicationStartNumber)
            => _facade.QueryContatsByStartedDedicationNumber(DedicationStartNumber);

        public Entity RetrieveContactCollectionByLineId(String LineId)
            => _facade.RetrieveContactByLineId(LineId);
        #endregion
    }
}
