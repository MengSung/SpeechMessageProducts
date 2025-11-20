using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.ListOperations
{
    public interface IListService
    {
        void AddMembers(Guid listGuid, List<Guid> memberGuidList);
        void RemoveMember(Guid listGuid, Guid memberGuid);
    }
}
