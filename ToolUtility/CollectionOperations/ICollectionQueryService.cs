using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace.CollectionOperations
{
    public interface ICollectionQueryService
    {
        EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue);
        EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId);

    }
}
