using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.EntityOperations
{
    public interface IEntityQueryService
    {
        Entity RetrieveEntity(string entityName, Guid entityId);
        EntityCollection RetrieveMultiple(QueryBase query);
    }
}
