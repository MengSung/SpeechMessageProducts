using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;

namespace ToolUtilityNameSpace.EntityOperations
{
    public interface IEntityQueryService
    {
        Entity RetrieveEntity(string entityName, Guid entityId);
        Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue);
        EntityCollection RetrieveMultiple(QueryBase query);
        Guid RetrieveAccountByName(string accountName);
        EntityCollection RetrieveTaskBySubject(string subject);
        
        /// <summary>
        /// 執行 RetrieveMultipleRequest 並返回結果
        /// </summary>
        EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request);
    }
}
