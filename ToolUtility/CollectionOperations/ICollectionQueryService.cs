using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.CollectionOperations
{
    public interface ICollectionQueryService
    {
        EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue);
    }
}
