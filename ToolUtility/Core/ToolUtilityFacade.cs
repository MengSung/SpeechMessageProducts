using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
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
    }
}
