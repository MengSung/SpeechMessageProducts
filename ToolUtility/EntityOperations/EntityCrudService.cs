using System;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    public class EntityCrudService : IEntityCrudService
    {
        private readonly object _logger;
        private readonly ICrmClient _crmClient;

        public EntityCrudService(object logger, ICrmClient crmClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _crmClient = crmClient ?? throw new ArgumentNullException(nameof(crmClient));
        }

        public Guid CreateEntity(Entity entityToCreate)
        {
            if (entityToCreate == null) throw new ArgumentNullException(nameof(entityToCreate));
            return _crmClient.Create(entityToCreate);
        }

        public void UpdateEntity(Entity entityToUpdate)
        {
            if (entityToUpdate == null) throw new ArgumentNullException(nameof(entityToUpdate));
            _crmClient.Update(entityToUpdate);
        }

        public void DeleteEntity(string entityName, Guid entityId)
        {
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("entityId cannot be empty", nameof(entityId));
            _crmClient.Delete(entityName, entityId);
        }
    }
}
