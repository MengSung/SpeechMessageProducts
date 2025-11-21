using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.CollectionOperations
{
    public class CollectionQueryService : ICollectionQueryService
    {
        private readonly object _logger;
        private readonly IEntityQueryService _entityQueryService;

        public CollectionQueryService(object logger, IEntityQueryService entityQueryService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _entityQueryService = entityQueryService ?? throw new ArgumentNullException(nameof(entityQueryService));
        }

        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
        {
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            return _entityQueryService.RetrieveMultiple(query);
        }
    }
}
