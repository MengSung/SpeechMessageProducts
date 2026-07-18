using System;
using System.Collections.Generic;

namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoScopeGuard
    {
        public static bool IsContactAllowed(
            string access,
            IReadOnlyCollection<string> shepherdContactIds,
            string requestedContactId)
        {
            if (string.IsNullOrWhiteSpace(requestedContactId))
            {
                return false;
            }

            if (string.Equals(access, MemberInfoAccess.Church, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(access, MemberInfoAccess.ShepherdList, StringComparison.Ordinal))
            {
                return false;
            }

            if (shepherdContactIds == null || shepherdContactIds.Count == 0)
            {
                return false;
            }

            foreach (var id in shepherdContactIds)
            {
                if (string.Equals(id, requestedContactId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsListAllowed(
            string access,
            IReadOnlyCollection<string> visibleListIds,
            string requestedListId)
        {
            if (string.IsNullOrWhiteSpace(requestedListId) ||
                (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList) ||
                visibleListIds == null ||
                visibleListIds.Count == 0)
            {
                return false;
            }

            foreach (var visibleListId in visibleListIds)
            {
                if (string.Equals(visibleListId, requestedListId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
