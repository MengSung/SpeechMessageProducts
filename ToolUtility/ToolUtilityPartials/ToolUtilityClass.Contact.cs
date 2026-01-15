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
