using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    public class MeetingStatisticsService : IMeetingStatisticsService
    {
        private readonly object _logger;
        private readonly IEntityQueryService _queryService;

        public MeetingStatisticsService(object logger, IEntityQueryService queryService)
        {
            _logger = logger;
            _queryService = queryService;
        }

        public EntityCollection RetrieveBySunday(DateTime sundayDate)
        {
            string sundayDateString = $"'{sundayDate:yyyy-M-d}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_meeting_statistics'>
                            <attribute name='new_meeting_statisticsid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='statuscode' operator='eq' value='1' />
                              <condition attribute='new_sunday_date' operator='on' value={sundayDateString} />
                            </filter>
                          </entity>
                        </fetch>";
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}
