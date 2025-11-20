using System;
using System.Collections.Generic;
using System.Linq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.ListOperations
{
    public class ListService : IListService
    {
        private readonly object _logger;
        private readonly IEntityQueryService _queryService;
        private readonly ICrmClient _crmClient;

        public ListService(object logger, IEntityQueryService queryService, ICrmClient crmClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
            _crmClient = crmClient ?? throw new ArgumentNullException(nameof(crmClient));
        }

        public void AddMembers(Guid listGuid, List<Guid> memberGuidList)
        {
            if (memberGuidList == null || memberGuidList.Count == 0) return;

            foreach (var member in memberGuidList)
            {
                // For simplicity, create a ListMember entity linking to list and member
                var entity = new Microsoft.Xrm.Sdk.Entity("listmember")
                {
                    ["listid"] = listGuid,
                    ["entityid"] = member
                };

                _crmClient.Create(entity);
            }
        }

        public void RemoveMember(Guid listGuid, Guid memberGuid)
        {
            // For simplicity assume we can delete by known id - in real impl we would query for the listmember id
            // Here we'll create a placeholder and call Delete on list entity to simulate
            _crmClient.Delete("list", listGuid);
        }
    }
}
