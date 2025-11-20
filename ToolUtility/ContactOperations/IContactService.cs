using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.ContactOperations
{
    public interface IContactService
    {
        Entity RetrieveByContactId(string contactId);
        Entity RetrieveByLineId(string lineId);
        EntityCollection RetrieveCollectionByName(string contactFullName);
        Entity RetrieveByAccountNumber(string accountNumber, string password);
    }
}
