using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.ListOperations
{
    public interface IListService
    {
        void AddMembers(Guid listGuid, List<Guid> memberGuidList);
        void RemoveMember(Guid listGuid, Guid memberGuid);
        EntityCollection RetrieveMemberListCollectionByListId(Guid listId);
        EntityCollection RetrieveDynamicMemberList(Guid listId);
        EntityCollection QueryListByContactId(Guid contactId, string associationName);
        System.Collections.ArrayList GetAllMemberDataFromList(Guid listEntityId);
    }
}
