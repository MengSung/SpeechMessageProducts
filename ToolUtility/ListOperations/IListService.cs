using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;

namespace ToolUtilityNameSpace.ListOperations
{
    public interface IListService
    {
        void AddMembers(Guid listGuid, List<Guid> memberGuidList);
        void RemoveMember(Guid listGuid, Guid memberGuid);

        // Member list retrieval
        EntityCollection RetrieveMemberListCollectionByListId(Guid listId);
        EntityCollection RetrieveMemberListCollectionByListIdUsingService(IOrganizationService externalService, Guid listId);
        EntityCollection RetrieveMemberListCollectionByListIdUsingProxy(OrganizationServiceProxy externalProxy, Guid listId);

        // Dynamic member list retrieval
        EntityCollection RetrieveDynamicMemberList(Guid listId);
        EntityCollection RetrieveDynamicMemberListUsingService(IOrganizationService externalService, Guid listId);
        EntityCollection RetrieveDynamicMemberListUsingProxy(OrganizationServiceProxy externalProxy, Guid listId);

        // New: list entity fetches
        EntityCollection RetrieveLists();
        EntityCollection RetrieveSmallGroupLists();

        EntityCollection QueryListByContactId(Guid contactId, string associationName);
        System.Collections.ArrayList GetAllMemberDataFromList(Guid listEntityId);
    }
}
