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

        public Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));
            if (string.IsNullOrEmpty(fieldName)) throw new ArgumentNullException(nameof(fieldName));
            if (fieldValue == null) throw new ArgumentNullException(nameof(fieldValue));

            var query = new QueryByAttribute(entityName)
            {
                ColumnSet = new ColumnSet(true)
            };
            query.Attributes.AddRange(fieldName);
            query.Values.AddRange(fieldValue);
            var result = _crmClient.RetrieveMultiple(query);
            return (result != null && result.Entities.Count > 0) ? result.Entities[0] : null;
        }
    }
}
