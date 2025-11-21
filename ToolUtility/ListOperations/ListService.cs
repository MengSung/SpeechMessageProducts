using System;
using System.Collections.Generic;
using System.Linq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Collections;
using Microsoft.Xrm.Sdk.Client; // Added for OrganizationServiceProxy

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
                // Marketing list member entity: listmember (listid, entityid)
                var entity = new Entity("listmember")
                {
                    ["listid"] = new EntityReference("list", listGuid),
                    ["entityid"] = new EntityReference("contact", member)
                };
                _crmClient.Create(entity);
            }
        }

        public void RemoveMember(Guid listGuid, Guid memberGuid)
        {
            // Query listmember records matching list + member, then delete
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet("listmemberid") };
            query.AddAttributeValue("listid", listGuid);
            query.AddAttributeValue("entityid", memberGuid);
            var coll = _crmClient.RetrieveMultiple(query);
            if (coll == null || coll.Entities.Count == 0) return;
            foreach (var lm in coll.Entities)
            {
                _crmClient.Delete("listmember", lm.Id);
            }
        }

        public EntityCollection RetrieveMemberListCollectionByListId(Guid listId)
        {
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet(true) };
            query.AddAttributeValue("listid", listId);
            return _queryService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveMemberListCollectionByListIdUsingService(IOrganizationService externalService, Guid listId)
        {
            if (externalService == null) return new EntityCollection();
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet(true) };
            query.AddAttributeValue("listid", listId);
            return externalService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveMemberListCollectionByListIdUsingProxy(OrganizationServiceProxy externalProxy, Guid listId)
        {
            if (externalProxy == null) return new EntityCollection();
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet(true) };
            query.AddAttributeValue("listid", listId);
            return externalProxy.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveDynamicMemberList(Guid listId)
        {
            var listEntity = _queryService.RetrieveEntity("list", listId);
            if (listEntity == null || !listEntity.Attributes.Contains("query")) return new EntityCollection();
            var fetchXml = listEntity.GetAttributeValue<string>("query");
            return _queryService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveDynamicMemberListUsingService(IOrganizationService externalService, Guid listId)
        {
            if (externalService == null) return new EntityCollection();
            var listEntity = externalService.Retrieve("list", listId, new ColumnSet("query"));
            if (listEntity == null || !listEntity.Attributes.Contains("query")) return new EntityCollection();
            var fetchXml = listEntity.GetAttributeValue<string>("query");
            return externalService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveDynamicMemberListUsingProxy(OrganizationServiceProxy externalProxy, Guid listId)
        {
            if (externalProxy == null) return new EntityCollection();
            var listEntity = externalProxy.Retrieve("list", listId, new ColumnSet("query"));
            if (listEntity == null || !listEntity.Attributes.Contains("query")) return new EntityCollection();
            var fetchXml = listEntity.GetAttributeValue<string>("query");
            return externalProxy.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection QueryListByContactId(Guid contactId, string associationName)
        {
            // associationName (e.g., "contact_list") could be used to build fetch; simplified placeholder
            var query = new QueryExpression("listmember")
            {
                ColumnSet = new ColumnSet(true)
            };
            query.Criteria.AddCondition("entityid", ConditionOperator.Equal, contactId);
            return _queryService.RetrieveMultiple(query);
        }

        public ArrayList GetAllMemberDataFromList(Guid listEntityId)
        {
            var members = new ArrayList();
            var coll = RetrieveMemberListCollectionByListId(listEntityId);
            foreach (var e in coll.Entities)
                members.Add(e);
            return members;
        }
    }
}
