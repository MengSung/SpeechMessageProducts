using System;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    public class EntityCrudService : IEntityCrudService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public EntityCrudService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        public Guid CreateEntity(Entity entityToCreate)
        {
            if (entityToCreate == null) throw new ArgumentNullException(nameof(entityToCreate));
            return _organizationService.Create(entityToCreate);
        }

        public void UpdateEntity(Entity entityToUpdate)
        {
            if (entityToUpdate == null) throw new ArgumentNullException(nameof(entityToUpdate));
            _organizationService.Update(entityToUpdate);
        }

        public void DeleteEntity(string entityName, Guid entityId)
        {
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("entityId cannot be empty", nameof(entityId));
            _organizationService.Delete(entityName, entityId);
        }
    }
}
