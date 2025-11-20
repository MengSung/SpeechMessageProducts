using System;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.EntityOperations
{
    public class EntityQueryService : IEntityQueryService
    {
        private readonly object _logger;
        private readonly ICrmClient _crmClient;

        public EntityQueryService(object logger, ICrmClient crmClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _crmClient = crmClient ?? throw new ArgumentNullException(nameof(crmClient));
        }

        public Entity RetrieveEntity(string entityName, Guid entityId)
        {
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));

            return _crmClient.Retrieve(entityName, entityId, new ColumnSet(true));
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return _crmClient.RetrieveMultiple(query);
        }
    }
}
