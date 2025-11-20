using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    public interface IEntityCrudService
    {
        Guid CreateEntity(Entity entityToCreate);
        void UpdateEntity(Entity entityToUpdate);
        void DeleteEntity(string entityName, Guid entityId);
    }
}
