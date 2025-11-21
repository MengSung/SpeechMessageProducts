using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    public interface IMeetingStatisticsService
    {
        EntityCollection RetrieveBySunday(DateTime sundayDate);
    }
}
