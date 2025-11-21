using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Crm.Sdk.Messages;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.AttachmentOperations;
using ToolUtilityNameSpace.LineMessaging;
using ToolUtilityNameSpace.ContactOperations;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.Interfaces;
using ToolUtilityNameSpace.Utilities;
using ToolUtilityNameSpace.AppointmentOperations; // added
using ToolUtilityNameSpace.LessonsOperations; // added
using ToolUtilityNameSpace.FeeOperations; // added

namespace ToolUtilityNameSpace.Core
{
    /// <summary>
    /// Light-weight Facade for ToolUtility functionality.
    /// This is the new Facade introduced in PR-04. It delegates to smaller services.
    /// It intentionally coexists with the legacy ToolUtilityClass during the refactor.
    /// </summary>
    public class ToolUtilityFacade : IDisposable
    {
        private readonly object _logger;
        private readonly ICrmClient _crmClient;

        private Lazy<IEntityQueryService> _queryService;
        private Lazy<IEntityCrudService> _crudService;
        private Lazy<IAttributeService> _attributeService;
        private Lazy<IContactService> _contactService;
        private Lazy<IListService> _listService;
        private Lazy<IAttachmentService> _attachmentService;
        private Lazy<ILineMessageService> _lineMessageService;
        private Lazy<IAppointmentService> _appointmentService;
        private Lazy<ILessonsService> _lessonsService;
        private Lazy<IFeeService> _feeService;

        private bool _disposed = false;

        public ToolUtilityFacade(object logger = null, ICrmClient crmClient = null)
        {
            _logger = logger ?? new object();
            // Allow null crmClient for graceful coexistence with legacy usages.
            _crmClient = crmClient;
            InitializeServices();
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                // dispose services if they implement IDisposable
                if (_queryService?.IsValueCreated == true) (_queryService.Value as IDisposable)?.Dispose();
                if (_crudService?.IsValueCreated == true) (_crudService.Value as IDisposable)?.Dispose();
                if (_attributeService?.IsValueCreated == true) (_attributeService.Value as IDisposable)?.Dispose();
                if (_contactService?.IsValueCreated == true) (_contactService.Value as IDisposable)?.Dispose();
                if (_listService?.IsValueCreated == true) (_listService.Value as IDisposable)?.Dispose();
                if (_attachmentService?.IsValueCreated == true) (_attachmentService.Value as IDisposable)?.Dispose();
                if (_lineMessageService?.IsValueCreated == true) (_lineMessageService.Value as IDisposable)?.Dispose();
                if (_appointmentService?.IsValueCreated == true) (_appointmentService.Value as IDisposable)?.Dispose();
                if (_lessonsService?.IsValueCreated == true) (_lessonsService.Value as IDisposable)?.Dispose();
                if (_feeService?.IsValueCreated == true) (_feeService.Value as IDisposable)?.Dispose();

                (_crmClient as IDisposable)?.Dispose();
            }
            _disposed = true;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void InitializeServices()
        {
            _queryService = new Lazy<IEntityQueryService>(() => new EntityQueryService(_logger, _crmClient));
            _crud_service_init();
            _attribute_service_init();
            _contact_service_init();
            _list_service_init();
            _attachment_service_init();
            _linemessage_service_init();
            _appointment_service_init();
            _lessons_service_init();
            _fee_service_init();
        }

        private void _crud_service_init()
        {
            _crudService = new Lazy<IEntityCrudService>(() => new EntityCrudService(_logger, _crmClient));
        }

        private void _attribute_service_init()
        {
            _attributeService = new Lazy<IAttributeService>(() => new AttributeServiceComposite(_logger));
        }

        private void _contact_service_init()
        {
            _contactService = new Lazy<IContactService>(() => new ContactService(_logger, _queryService.Value));
        }

        private void _list_service_init()
        {
            _listService = new Lazy<IListService>(() => new ListService(_logger, _queryService.Value, _crmClient));
        }

        private void _attachment_service_init()
        {
            _attachmentService = new Lazy<IAttachmentService>(() => new AttachmentService(_logger, _crmClient));
        }

        private void _linemessage_service_init()
        {
            _lineMessage_service_safe_init();
        }

        // Separate method to avoid referencing _crudService before init
        private void _lineMessage_service_safe_init()
        {
            _lineMessageService = new Lazy<ILineMessageService>(() => new LineMessageService(_logger, _crudService.Value));
        }

        private void _appointment_service_init()
        {
            _appointmentService = new Lazy<IAppointmentService>(() => new AppointmentService(_logger, _queryService.Value));
        }

        private void _lessons_service_init()
        {
            _lessonsService = new Lazy<ILessonsService>(() => new LessonsService(_logger, _queryService.Value));
        }

        private void _fee_service_init()
        {
            _feeService = new Lazy<IFeeService>(() => new FeeService(_logger, _queryService.Value));
        }

        public Entity RetrieveEntity(string entityName, Guid entityId)
            => _queryService.Value.RetrieveEntity(entityName, entityId);

        public Entity RetrieveContactByLineId(string lineId)
            => _contactService.Value.RetrieveByLineId(lineId);

        public EntityCollection RetrieveContactCollectionByName(string contactFullName)
            => _contactService.Value.RetrieveCollectionByName(contactFullName);

        public Guid CreateEntity(Entity entityToCreate)
            => _crudService.Value.CreateEntity(entityToCreate);

        public void UpdateEntity(Entity entityToUpdate)
            => _crudService.Value.UpdateEntity(entityToUpdate);

        public void DeleteEntity(string entityName, Guid entityId)
            => _crudService.Value.DeleteEntity(entityName, entityId);

        public bool GetEntityBoolAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetBoolAttribute(entity, propertyName);

        public void SetEntityBoolAttribute(ref Entity entity, string propertyName, bool propertyValue)
            => _attributeService.Value.SetBoolAttribute(ref entity, propertyName, propertyValue);

        public int GetEntityIntAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetIntAttribute(entity, propertyName);

        public string GetEntityStringAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetStringAttribute(entity, propertyName);

        public DateTime GetEntityDateTimeAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetDateTimeAttribute(entity, propertyName);

        public Money GetEntityMoneyAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetMoneyAttribute(entity, propertyName);

        public Guid GetEntityLookupAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetLookupAttribute(entity, propertyName);

        public void AddMembersToMarketingList(Guid listGuid, List<Guid> memberGuidList)
            => _list_service_value().AddMembers(listGuid, memberGuidList);

        public void RemoveMembersToMarketingList(Guid listGuid, Guid memberGuid)
            => _list_service_value().RemoveMember(listGuid, memberGuid);

        public void CreatePushLineMessage(string userId, string subject, string message)
            => _lineMessageService.Value.CreatePushMessage(userId, subject, message);

        public EntityCollection DownloadAnAttachment(ref IOrganizationService crmService, Guid entityId)
            => _attachmentService.Value.DownloadAttachment(ref crmService, entityId);

        public void UploadAnAttachment(ref IOrganizationService crmService, string entityName, string subject, string noteText, string fileName, string mimeType, byte[] documentBody, Guid toBeAttachedEntityId)
            => _attachmentService.Value.UploadAttachment(ref crmService, entityName, subject, noteText, fileName, mimeType, documentBody, toBeAttachedEntityId);

        public static void DeleteLastComma(ref string stringToProcess)
            => StringUtility.DeleteLastComma(ref stringToProcess);

        public string FilterDigit(string filteredString)
            => StringUtility.FilterDigit(filteredString);

        public void TraceByLevel(int totalLevel, int qualifiedLevel, string stringToProcess)
        {
            // Use TraceUtility with logger when available
            TraceUtility.TraceByLevel(_logger, totalLevel, qualifiedLevel, stringToProcess);
        }

        // Helper to safely access list service even if not initialized
        private IListService _list_service_value()
        {
            return _listService.Value;
        }


        public void SetEntityMoneyAttributeToNull(ref Entity entity, string propertyName)
            => _attributeService.Value.SetMoneyAttributeToNull(ref entity, propertyName);

        public void SetEntityDateTimeAttributeToNull(ref Entity entity, string propertyName)
            => _attributeService.Value.SetDateTimeAttributeToNull(ref entity, propertyName);

        public void SetEntityLookUpAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference)
            => _attributeService.Value.SetLookupAttribute(ref entity, propertyName, ref entityReference);

        public void SetEntityLookUpToNull(ref Entity entity, string propertyName)
            => _attributeService.Value.SetLookupToNull(ref entity, propertyName);

        public string GetEntityLookupDisplayName(Entity entity, string propertyName)
            => _attributeService.Value.GetLookupDisplayName(entity, propertyName);

        public int GetOptionSetAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetOptionSetAttribute(entity, propertyName);

        public void SetOptionSetAttribute(ref Entity entity, string propertyName, int value)
            => _attributeService.Value.SetOptionSetAttribute(ref entity, propertyName, value);

        public void SetOptionSetAttributeNull(ref Entity entity, string propertyName)
            => _attributeService.Value.SetOptionSetAttributeNull(ref entity, propertyName);

        public float GetEntityFloatAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetFloatAttribute(entity, propertyName);

        public void SetEntityFloatAttribute(ref Entity entity, string propertyName, float value)
            => _attributeService.Value.SetFloatAttribute(ref entity, propertyName, value);

        public void SetEntityFloatAttributeToNull(Entity entity, string propertyName)
            => _attributeService.Value.SetFloatAttributeToNull(entity, propertyName);

        public double GetEntityDoubleAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetDoubleAttribute(entity, propertyName);

        public void SetEntityDoubleAttribute(ref Entity entity, string propertyName, double value)
            => _attributeService.Value.SetDoubleAttribute(ref entity, propertyName, value);

        public void SetEntityDoubleAttributeToNull(Entity entity, string propertyName)
            => _attributeService.Value.SetDoubleAttributeToNull(entity, propertyName);

        // --- Additional legacy methods implemented using query service or forwarded to specialized services ---
        public Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            return _queryService.Value.RetrieveEntityByField(entityName, fieldName, fieldValue);
        }

        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
        {
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);

            return _queryService.Value.RetrieveMultiple(query);
        }

        // Delegated to ContactService
        public string RetrieveContactByContactId(string contactId)
            => _contactService.Value.GetContactInfoByContactId(contactId);

        public Entity RetrieveContactByContactId(ref IOrganizationService organizationService, string contactId, ref int count)
            => _contactService.Value.RetrieveByContactId(organizationService, contactId, ref count);

        // Delegated to ContactService
        public string RetrieveContactByName(string contactFullName)
            => _contactService.Value.GetContactInfoByFullName(contactFullName);

        public Entity RetrieveContactEntityByName(string contactFullName)
            => _contactService.Value.RetrieveByContactId(contactFullName) ?? _contactService.Value.RetrieveByLineId(contactFullName);

        public Entity RetrieveContactByName(ref IOrganizationService organizationService, string contactFullName)
            => _contactService.Value.RetrieveByFullName(organizationService, contactFullName);

        public string RetrieveContactByName_ReturnString(ref IOrganizationService organizationService, string contactFullName)
            => _contactService.Value.GetContactInfoByFullName(organizationService, contactFullName);

        public EntityCollection RetrieveContactCollectionByNationId(string nationId)
            => _contactService.Value.RetrieveCollectionByNationId(nationId);

        public string RetrieveContactByAccountNumber(string accountNumber, string password)
            => _contactService.Value.AccountLogin(accountNumber, password);

        public Entity DoesAccountExist(string accountNumber)
            => _contactService.Value.RetrieveAccountEntity(accountNumber);

        public Entity RetrieveContactEntityByAccountNumber(string accountNumber, string password)
            => _contactService.Value.RetrieveByAccountNumber(accountNumber, password);

        public Entity RetrieveContactEntityByLineUserId(string lineUserId)
            => _contactService.Value.RetrieveByLineId(lineUserId);

        public Entity RetrieveContactEntityByFullNameAndMobileNumber(string fullName, string mobileNumber)
            => _contactService.Value.RetrieveByFullNameAndMobile(fullName, mobileNumber);

        public EntityCollection RetrieveContactEntityByFullNameCollection(string fullName)
            => _contactService.Value.RetrieveCollectionByFullName(fullName);

        public Entity RetrieveContactCollectionByLineId(string lineId)
            => _contactService.Value.RetrieveByLineIdForCollection(lineId);

        public EntityCollection RetrieveMemberListCollectionByListId(Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListId(listId);

        public EntityCollection RetrieveMemberListCollectionByListId(ref IOrganizationService organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingService(organizationService, listId);

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingProxy(organizationService, listId);

        public EntityCollection RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService organizationService, Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListIdUsingService(organizationService, listId);

        public EntityCollection RetrieveDynamicMemberList(string strList)
            => _listService.Value.RetrieveDynamicMemberList(Guid.Parse(strList));

        public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
            => _listService.Value.RetrieveDynamicMemberListUsingService(service, Guid.Parse(strList));

        public EntityCollection RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, string strList)
            => _listService.Value.RetrieveDynamicMemberListUsingProxy(service, Guid.Parse(strList));

        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
            => _listService.Value.RetrieveDynamicMemberListUsingService(service, Guid.Parse(strList));

        public EntityCollection RetrieveDynamicMemberList(Guid listId)
            => _listService.Value.RetrieveDynamicMemberList(listId);

        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid listId)
            => _listService.Value.RetrieveDynamicMemberListUsingService(service, listId);

        public EntityCollection RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid listId)
            => _listService.Value.RetrieveDynamicMemberListUsingProxy(service, listId);

        public EntityCollection RetrieveDynamicMemberListCrm2011(ref IOrganizationService service, Guid listId)
            => _listService.Value.RetrieveDynamicMemberListUsingService(service, listId);

        public EntityCollection QueryDediccationContatsByFetchXml(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit)
            => _feeService.Value.QueryDedicationContacts(dedicationNumber, contactName, homePhone, mobile, nationId, lastSixDigit);

        public EntityCollection QueryContatsByStartedDedicationNumber(string dedicationStartNumber)
            => _feeService.Value.QueryDedicationContactsStartedNumber(dedicationStartNumber);

        public EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId)
            => _feeService.Value.RetrieveDedicationBooking(contactName, contactId);

        public EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime sundayDate)
        {
            string sundayDateString = @"'" + sundayDate.Year + "-" + sundayDate.Month + "-" + sundayDate.Day + "'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_meeting_statistics'>
                            <attribute name='new_meeting_statisticsid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='statuscode' operator='eq' value='1' />
                             <condition attribute='new_sunday_date' operator='on' value=" + sundayDateString + @" />
                            </filter>
                          </entity>
                        </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
        {
            dedicationBookingName = "'" + dedicationBookingName + "'";
            dedicationBookingId = "'{" + dedicationBookingId + "}'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_fee'>
                            <attribute name='new_feeid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_dedication_booking_new_fee' operator='eq' uiname=" + dedicationBookingName + @" uitype ='new_dedication_booking' value=" + dedicationBookingId + @" />
                              <condition attribute='new_paid_period' operator='eq' value='" + paidPeriod + @"' />
                            </filter>
                          </entity>
                        </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveListByFetchXml()
        {
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='new_app_named' operator='eq' value='1' />
                        </filter>
                      </entity>
                    </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveSmallGroupListCollectionByFetchXml()
        {
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='new_contact_race_leager_list' />
                        <attribute name='new_contact_family_leader_list' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                          <condition attribute='new_app_named' operator='eq' value='1' />
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='listname' operator='not-like' value='%幸福%' />
                        </filter>
                      </entity>
                    </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection QueryManyToMany(string conditionAttributeName, string entityNameToSearch, string linkFromEntityName, string linkFromAttributeName, string linkToEntityName, string linkToAttributeName, string attributeName, Guid entityIdValue)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryListOfContactManyToMany(Guid contactId)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryEntityList(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection RetrieveManyToOneCollection()
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public Entity QueryBloodReportByContactId(Guid contactId)
        {
            // Simplified: return null for now
            return null;
        }

        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid aListEntityId, Guid contactId, int monthPeriod)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryPresentRecordSortBySunday_BACKUP(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid aContactId, Guid aWeeklyReportEntityId)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection RetrieveManyToOneRelationship()
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryWeeklyReportBySunday(DateTime aSunday, string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryWeeklyReportBySunday(DateTime aSunday, Guid aListEntityId)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
        {
            // Simplified: return empty collection for now
            return new EntityCollection();
        }

        public EntityCollection RetrieveContactCollectionByLineIdDynamics365(Guid listId)
            => _listService.Value.RetrieveMemberListCollectionByListId(listId);

        public EntityCollection RetrieveDynamicMemberListDynamics365(Guid listId)
            => _listService.Value.RetrieveDynamicMemberList(listId);
    }
}
