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
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);

            var coll = _queryService.Value.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
        {
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);

            return _queryService.Value.RetrieveMultiple(query);
        }

        public string RetrieveContactByContactId(string contactId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("build_customer_id", "statecode");
            query.Values.AddRange(contactId, 0);

            var coll = _queryService.Value.RetrieveMultiple(query);
            if (coll != null && coll.Entities.Count > 0)
            {
                var e = coll.Entities[0];
                var sb = new System.Text.StringBuilder();
                if (e.Contains("fullname")) sb.AppendLine("姓名:" + e["fullname"].ToString());
                if (e.Contains("build_customer_id")) sb.AppendLine("身分證字號:" + e["build_customer_id"].ToString());
                if (e.Contains("telephone1")) sb.AppendLine("電話號碼:" + e["telephone1"].ToString());
                if (e.Contains("emailaddress1")) sb.AppendLine("電子郵件:" + e["emailaddress1"].ToString());
                return sb.ToString();
            }
            return string.Empty;
        }

        public Entity RetrieveContactByContactId(ref IOrganizationService organizationService, string contactId, ref int count)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("build_customer_id", "statecode");
            query.Values.AddRange(contactId, 0);

            var coll = organizationService.RetrieveMultiple(query);
            count = coll?.Entities?.Count ?? 0;
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public string RetrieveContactByName(string contactFullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(contactFullName, 0);

            var coll = _queryService.Value.RetrieveMultiple(query);
            if (coll != null && coll.Entities.Count > 0)
            {
                var e = coll.Entities[0];
                var sb = new System.Text.StringBuilder();
                if (e.Contains("fullname")) sb.AppendLine("姓名:" + e["fullname"].ToString());
                if (e.Contains("build_customer_id")) sb.AppendLine("身分證字號:" + e["build_customer_id"].ToString());
                if (e.Contains("telephone1")) sb.AppendLine("電話號碼:" + e["telephone1"].ToString());
                if (e.Contains("emailaddress1")) sb.AppendLine("電子郵件:" + e["emailaddress1"].ToString());
                return sb.ToString();
            }
            return string.Empty;
        }

        public Entity RetrieveContactEntityByName(string contactFullName)
            => _contactService.Value.RetrieveByContactId(contactFullName) ?? _contactService.Value.RetrieveByLineId(contactFullName);

        public Entity RetrieveContactByName(ref IOrganizationService organizationService, string contactFullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(contactFullName, 0);
            var coll = organizationService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public string RetrieveContactByName_ReturnString(ref IOrganizationService organizationService, string contactFullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(contactFullName, 0);
            var coll = organizationService.RetrieveMultiple(query);
            var sb = new System.Text.StringBuilder();
            if (coll != null)
            {
                foreach (var c in coll.Entities)
                {
                    if (c.Contains("fullname")) sb.AppendLine("姓名:" + c["fullname"].ToString());
                    if (c.Contains("telephone1")) sb.AppendLine("電話號碼:" + c["telephone1"].ToString());
                    if (c.Contains("emailaddress1")) sb.AppendLine("電子郵件:" + c["emailaddress1"].ToString());
                }
            }
            return sb.ToString();
        }

        public EntityCollection RetrieveContactCollectionByNationId(string nationId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_personal_id", "statecode");
            query.Values.AddRange(nationId, 0);
            return _queryService.Value.RetrieveMultiple(query);
        }
        public string RetrieveContactByAccountNumber(string accountNumber, string password)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_app_acount", "statecode");
            query.Values.AddRange(accountNumber, 0);
            var coll = _queryService.Value.RetrieveMultiple(query);
            if (coll != null && coll.Entities.Count > 0)
            {
                var e = coll.Entities[0];
                if (e.Contains("new_app_pass") && e.GetAttributeValue<string>("new_app_pass") == password)
                    return e.Id.ToString();
                if (!e.Contains("new_app_pass")) return "系統沒有設定密碼";
                return "密碼錯誤";
            }
            return "帳號錯誤";
        }

        public Entity DoesAccountExist(string accountNumber)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_app_acount", "statecode");
            query.Values.AddRange(accountNumber, 0);
            var coll = _queryService.Value.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public Entity RetrieveContactEntityByAccountNumber(string accountNumber, string password)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_app_acount", "statecode");
            query.Values.AddRange(accountNumber, 0);
            var coll = _queryService.Value.RetrieveMultiple(query);
            if (coll != null && coll.Entities.Count > 0)
            {
                var e = coll.Entities[0];
                if (e.Contains("new_app_pass") && e.GetAttributeValue<string>("new_app_pass") == password) return e;
            }
            return null;
        }

        public Entity RetrieveContactEntityByLineUserId(string lineUserId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_lineid", "statecode");
            query.Values.AddRange(lineUserId, 0);
            var coll = _queryService.Value.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public Entity RetrieveContactEntityByFullNameAndMobileNumber(string fullName, string mobileNumber)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "mobilephone", "statecode");
            query.Values.AddRange(fullName, mobileNumber, 0);
            var coll = _queryService.Value.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public EntityCollection RetrieveContactEntityByFullNameCollection(string fullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(fullName, 0);
            return _queryService.Value.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveMemberListCollectionByListId(Guid listId)
        {
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet(true) };
            query.AddAttributeValue("listid", listId);
            return _queryService.Value.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveMemberListCollectionByListId(ref IOrganizationService organizationService, Guid listId)
        {
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet(true) };
            query.AddAttributeValue("listid", listId);
            return organizationService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy organizationService, Guid listId)
        {
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet(true) };
            query.AddAttributeValue("listid", listId);
            return organizationService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService organizationService, Guid listId)
            => RetrieveMemberListCollectionByListId(ref organizationService, listId);

        public EntityCollection RetrieveDynamicMemberList(string strList)
        {
            var entity = _queryService.Value.RetrieveEntity("list", Guid.Parse(strList));
            var dynamicQuery = entity.Attributes["query"].ToString();
            return _queryService.Value.RetrieveMultiple(new FetchExpression(dynamicQuery));
        }

        public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
        {
            var entity = service.Retrieve("list", new Guid(strList), new ColumnSet("query"));
            var dynamicQuery = entity.Attributes["query"].ToString();
            return service.RetrieveMultiple(new FetchExpression(dynamicQuery));
        }

        public EntityCollection RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, string strList)
        {
            var entity = service.Retrieve("list", new Guid(strList), new ColumnSet("query"));
            var dynamicQuery = entity.Attributes["query"].ToString();
            return service.RetrieveMultiple(new FetchExpression(dynamicQuery));
        }

        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
            => RetrieveDynamicMemberList(service, strList);

        public EntityCollection RetrieveDynamicMemberList(Guid listId)
        {
            var entity = _queryService.Value.RetrieveEntity("list", listId);
            var dynamicQuery = entity.Attributes["query"].ToString();
            return _queryService.Value.RetrieveMultiple(new FetchExpression(dynamicQuery));
        }

        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid listId)
        {
            var entity = service.Retrieve("list", listId, new ColumnSet("query"));
            var dynamicQuery = entity.Attributes["query"].ToString();
            return service.RetrieveMultiple(new FetchExpression(dynamicQuery));
        }

        public EntityCollection RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid listId)
        {
            return RetrieveDynamicMemberList(service, listId.ToString());
        }

        public EntityCollection RetrieveDynamicMemberListCrm2011(ref IOrganizationService service, Guid listId)
            => RetrieveDynamicMemberList(ref service, listId);

        public EntityCollection QueryDediccationContatsByFetchXml(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit)
        {
            // reuse original fetchXml construction
            dedicationNumber = "'" + dedicationNumber + "'";
            contactName = "'%" + contactName + "%'";
            homePhone = "'%" + homePhone + "%'";
            mobile = "'%" + mobile + "%'";
            nationId = "'%" + nationId + "%'";
            lastSixDigit = "'%" + lastSixDigit + "%'";

            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                              <entity name='contact'>
                                <attribute name='fullname' />
                                <attribute name='telephone2' />
                                <attribute name='address2_line1' />
                                <attribute name='parentcustomerid' />
                                <attribute name='new_church_jobtitle' />
                                <attribute name='mobilephone' />
                                <attribute name='emailaddress1' />
                                <attribute name='pager' />
                                <attribute name='new_cell_list_contact' />
                                <attribute name='new_personal_id' />
                                <attribute name='new_last_six_digit' />
                                <attribute name='contactid' />
                                <order attribute='fullname' descending='false' />
                                <filter type='and'>
                                  <filter type='or'>
                                    <condition attribute='pager' operator='eq' value=" + dedicationNumber + @" />
                                    <condition attribute='fullname' operator='like' value=" + contactName + @"/>
                                    <condition attribute='telephone2' operator='like' value=" + homePhone + @" />
                                    <condition attribute='mobilephone' operator='like' value=" + mobile + @" />
                                    <condition attribute='new_personal_id' operator='like' value=" + nationId + @" />
                                    <condition attribute='new_last_six_digit' operator='like' value=" + lastSixDigit + @" />
                                  </filter>
                                    <condition attribute='statuscode' operator='eq' value='1' />
                                </filter>
                              </entity>
                            </fetch>";

            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection QueryContatsByStartedDedicationNumber(string dedicationStartNumber)
        {
            dedicationStartNumber = "'" + dedicationStartNumber + "%'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='3'>
                              <entity name='contact'>
                                <attribute name='fullname' />
                                <attribute name='pager' />
                                <attribute name='telephone2' />
                                <attribute name='address2_line1' />
                                <attribute name='parentcustomerid' />
                                <attribute name='new_church_jobtitle' />
                                <attribute name='mobilephone' />
                                <attribute name='emailaddress1' />
                                <attribute name='contactid' />
                                <order attribute='pager' descending='true' />
                                <filter type='and'>
                                  <condition attribute='pager' operator='like' value=" + dedicationStartNumber + @" />
                                </filter>
                              </entity>
                            </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public Guid RetrieveAccountCollectionByName(string accountName)
        {
            var query = new QueryByAttribute("account") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("name", "statecode");
            query.Values.AddRange(accountName, 0);
            var coll = _queryService.Value.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0].Id : Guid.Empty;
        }

        public EntityCollection RetrieveAppointmentsByDate(DateTime selectedDate)
        {
            var query = new QueryByAttribute("appointment") { ColumnSet = new ColumnSet(true) };
            // original didn't filter by date; keep same behaviour
            return _queryService.Value.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveAppointmentsByFetchXml(DateTime startDate, DateTime endDate)
        {
            string start = "'" + startDate.Year + "-" + startDate.Month + "-" + startDate.Day + "'";
            string end = "'" + endDate.Year + "-" + endDate.Month + "-" + endDate.Day + "'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value=" + start + @" />
                          <condition attribute='scheduledstart' operator='on-or-before' value=" + end + @" />
                        </filter>
                      </entity>
                    </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveAppointmentsByFetchXml(string contactName, string contactId)
        {
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='new_leave_signing_status' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <attribute name='new_hours' />
                        <attribute name='new_days' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_applier_appointment' operator='eq' uiname='" + contactName + @"' uitype='contact' value='{" + contactId + @"}' />
                          <condition attribute='scheduledstart' operator='this-year' />
                          <condition attribute='new_leave_signing_status' operator='in'>
                                <value> 100000004 </value >
                                <value> 100000001 </value >
                                <value> 100000007 </value >
                          </condition >
                        </filter>
                      </entity>
                    </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveAppointmentsByFetchXmlAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType)
        {
            string start = "'" + startDate.Year + "-" + startDate.Month + "-" + startDate.Day + "'";
            string end = "'" + endDate.Year + "-" + endDate.Month + "-" + endDate.Day + "'";
            string sType = "'" + scheduleType + "'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value=" + start + @" />
                          <condition attribute='scheduledstart' operator='on-or-before' value=" + end + @" />
                          <condition attribute='new_meeting_kind' operator='eq' value=" + sType + @" />
                        </filter>
                      </entity>
                    </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveEnrolledLessonsByFetchXml(DateTime startDate, DateTime endDate, string contactName, string contactId)
        {
            string s = "'" + startDate.Year + "-" + startDate.Month + "-" + startDate.Day + "'";
            string e = "'" + endDate.Year + "-" + endDate.Month + "-" + endDate.Day + "'";
            contactName = "'" + contactName + "'";
            contactId = "'{" + contactId + "}'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value=" + s + @" />
                            <condition attribute='new_class_end_date' operator='on-or-before' value=" + e + @" />
                        </filter>
                        <link-entity name='new_stor_lessons' from='new_new_disciple_lessons_new_stor_les' to='new_disciple_lessonsid' alias='ab'>
                          <filter type='and'>
                            <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname=" + contactName + @" uitype ='contact' value=" + contactId + @" />
                          </filter>
                        </link-entity>
                      </entity>
                    </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate)
        {
            string s = "'" + startDate.Year + "-" + startDate.Month + "-" + startDate.Day + "'";
            string e = "'" + endDate.Year + "-" + endDate.Month + "-" + endDate.Day + "'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value=" + s + @" />
                            <condition attribute='new_class_end_date' operator='on-or-before' value=" + e + @" />
                        </filter>
                      </entity>
                    </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveStorLessonsByFetchXml(string lessonName, string lessonId, string contactName, string contactId)
        {
            lessonName = "'" + lessonName + "'";
            lessonId = "'{" + lessonId + "}'";
            contactName = "'" + contactName + "'";
            contactId = "'{" + contactId + "}'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_stor_lessons'>
                        <attribute name='createdon' />
                        <attribute name='new_contact_new_stor_lessons' />
                        <attribute name='new_fee' />
                        <attribute name='new_pay_date' />
                        <attribute name='new_new_disciple_lessons_new_stor_les' />
                        <attribute name='new_stor_lessonsid' />
                        <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
                        <order attribute='new_contact_new_stor_lessons' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_enroll_status' operator='not-in'>
                            <value>100000007</value>
                            <value>100000009</value>
                            <value>100000003</value>
                          </condition>
                          <condition attribute='new_new_disciple_lessons_new_stor_les' operator='eq' uiname=" + lessonName + @" uitype='new_disciple_lessons' value=" + lessonId +  @" />
                          <condition attribute='statuscode' operator='ne' value='2' />
                          <condition attribute='statecode' operator='eq' value='0' />
                        </filter>
                        <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
                          <attribute name='telephone2' />
                          <attribute name='address2_line1' />
                          <attribute name='parentcustomerid' />
                          <attribute name='mobilephone' />
                          <attribute name='emailaddress1' />
                        </link-entity>
                      </entity>
                    </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId)
        {
            contactName = "'" + contactName + "'";
            contactId = "'{" + contactId + "}'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_dedication_booking'>
                            <attribute name='new_dedication_bookingid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_contact_new_dedication_booking' operator='eq' uiname=" + contactName + @" uitype='contact' value=" + contactId + @" />
                              <condition attribute='new_dedication_booking_status' operator='eq' value='100000001' />
                            </filter>
                          </entity>
                        </fetch>";
            return _queryService.Value.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime sundayDate)
        {
            string sundayDateString = @"'" + sundayDate.Year + "-" + sundayDate.Month + "-" + sundayDate.Day + @"'";
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

        public Entity RetrieveContactCollectionByLineId(string lineId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_lineid", "statecode");
            query.Values.AddRange(lineId, 0);
            var coll = _queryService.Value.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(Guid listId)
        {
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet(true) };
            query.AddAttributeValue("listid", listId);
            return _queryService.Value.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveDynamicMemberListDynamics365(Guid listId)
        {
            var entity = _queryService.Value.RetrieveEntity("list", listId);
            var dynamicQuery = entity.Attributes["query"].ToString();
            return _queryService.Value.RetrieveMultiple(new FetchExpression(dynamicQuery));
        }
    }
}
