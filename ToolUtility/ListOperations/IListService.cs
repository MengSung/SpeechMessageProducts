using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;

namespace ToolUtilityNameSpace.ListOperations
{
    public interface IListService
    {
        // SDK-based member management (recommended for bulk operations)
        void AddMembers(Guid listGuid, List<Guid> memberGuidList);
        void AddMembersUsingSdk(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service);
        void RemoveMember(Guid listGuid, Guid memberGuid);
        void RemoveMemberUsingSdk(Guid listGuid, Guid memberGuid, IOrganizationService service);

        // Member list retrieval
        EntityCollection RetrieveMemberListCollectionByListId(Guid listId);
        EntityCollection RetrieveMemberListCollectionByListIdUsingService(IOrganizationService externalService, Guid listId);
        EntityCollection RetrieveMemberListCollectionByListIdUsingProxy(IOrganizationService externalService, Guid listId);

        // Dynamic member list retrieval
        EntityCollection RetrieveDynamicMemberList(Guid listId);
        EntityCollection RetrieveDynamicMemberListUsingService(IOrganizationService externalService, Guid listId);
        EntityCollection RetrieveDynamicMemberListUsingProxy(IOrganizationService externalService, Guid listId);

        // New: list entity fetches
        EntityCollection RetrieveLists();
        EntityCollection RetrieveSmallGroupLists();

        EntityCollection QueryListByContactId(Guid contactId, string associationName);
        System.Collections.ArrayList GetAllMemberDataFromList(Guid listEntityId);
        
        /// <summary>
        /// 根據名單名稱查詢名單實體
        /// </summary>
        Entity RetrieveListEntityByName(string listName);
        
        /// <summary>
        /// 根據連絡人查詢所屬的名單
        /// </summary>
        EntityCollection RetrieveListByContact(string contactName);
        
        /// <summary>
        /// 根據賽跑領袖查詢名單
        /// </summary>
        EntityCollection RetrieveListByRacerLeader(string contactName, string contactId);
    }
}
