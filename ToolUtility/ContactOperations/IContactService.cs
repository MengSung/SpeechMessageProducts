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
    }
}
