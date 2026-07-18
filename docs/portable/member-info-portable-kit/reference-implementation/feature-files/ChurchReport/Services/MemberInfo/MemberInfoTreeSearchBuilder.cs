using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.ViewModels.MemberInfoTree;

namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoTreeSearchBuilder
    {
        public static MemberInfoTreeSearchResultViewModel Build(
            IEnumerable<GroupMembershipRow>? memberships,
            IReadOnlyCollection<string>? matchingContactIds,
            bool includeUngrouped,
            IEnumerable<GroupMemberRowViewModel>? rows = null)
        {
            // matchingContactIds 由控制器完成批次授權後傳入；在 builder 內轉為不分大小寫的集合，
            // 後續樹節點、未分組旗標及完整列都只認這份 allowed ids，形成最後一道資料邊界。
            var matches = new HashSet<string>(
                (matchingContactIds ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            var currentMemberships = (memberships ?? Enumerable.Empty<GroupMembershipRow>())
                .Where(row => row != null &&
                              row.IsCurrent &&
                              !string.IsNullOrWhiteSpace(row.ListId) &&
                              !string.IsNullOrWhiteSpace(row.ContactId))
                .ToList();
            // 先完成 allowed-id 過濾與 ContactId 去重，再套用共用 metadata rank 排序；
            // 這個順序同時避免未授權列影響排序，也保留同一會友第一筆完整資料列。
            var authorizedRows = (rows ?? Enumerable.Empty<GroupMemberRowViewModel>())
                .Where(row => row != null &&
                              !string.IsNullOrWhiteSpace(row.ContactId) &&
                              matches.Contains(row.ContactId))
                .GroupBy(row => row.ContactId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            var result = new MemberInfoTreeSearchResultViewModel
            {
                MatchingListIds = currentMemberships
                    .Where(row => matches.Contains(row.ContactId))
                    .Select(row => row.ListId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Rows = MemberInfoCommitmentTypeSort.OrderRows(authorizedRows)
            };

            if (includeUngrouped && matches.Count > 0)
            {
                var groupedContacts = new HashSet<string>(
                    currentMemberships.Select(row => row.ContactId),
                    StringComparer.OrdinalIgnoreCase);
                result.HasUngrouped = matches.Any(id => !groupedContacts.Contains(id));
            }

            return result;
        }
    }
}
