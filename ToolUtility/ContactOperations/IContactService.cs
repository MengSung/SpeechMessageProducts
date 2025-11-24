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
