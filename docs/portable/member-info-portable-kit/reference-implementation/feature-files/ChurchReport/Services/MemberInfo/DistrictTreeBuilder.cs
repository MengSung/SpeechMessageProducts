using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.ViewModels.MemberInfoTree;

namespace ChurchReport.Services.MemberInfo
{
    public static class DistrictTreeBuilder
    {
        public const string UnknownRaceLeaderName = "區長未填";
        // 未填牧區刻意序列化成空字串，避免 UI 顯示人造的「未填牧區」標籤；
        // UnknownRaceLeaderKey 負責把未填區長的小組歸入同一節點；UnknownRaceLeaderName 則負責顯示文字，
        // 並作為「未填區長排在最後」的排序判斷。Key 與 Name 的責任不同，不可互換。
        public const string MissingAreaName = "";
        public const string MissingGroupLeaderName = "小組長未填";
        private const string UnknownRaceLeaderKey = "__unknown_race__";

        public static DistrictTreeViewModel Build(
            IEnumerable<SmallGroupDescriptor>? groups,
            IEnumerable<GroupMembershipRow>? memberships,
            IReadOnlyCollection<string>? allCurrentContactIds,
            bool includeUngrouped,
            string? scope)
        {
            var allCurrentContactCount = (allCurrentContactIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return Build(groups, memberships, allCurrentContactCount, includeUngrouped, scope);
        }

        public static DistrictTreeViewModel Build(
            IEnumerable<SmallGroupDescriptor>? groups,
            IEnumerable<GroupMembershipRow>? memberships,
            int allCurrentContactCount,
            bool includeUngrouped,
            string? scope)
        {
            var groupList = (groups ?? Enumerable.Empty<SmallGroupDescriptor>())
                .Where(group => group != null && !string.IsNullOrWhiteSpace(group.ListId))
                .GroupBy(group => group.ListId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var visibleListIds = new HashSet<string>(groupList.Select(group => group.ListId), StringComparer.OrdinalIgnoreCase);
            var currentMemberships = (memberships ?? Enumerable.Empty<GroupMembershipRow>())
                .Where(row => row != null &&
                              row.IsCurrent &&
                              visibleListIds.Contains(row.ListId) &&
                              !string.IsNullOrWhiteSpace(row.ContactId))
                .ToList();

            var membersByList = currentMemberships
                .GroupBy(row => row.ListId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => new HashSet<string>(group.Select(row => row.ContactId), StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);

            var districtNodes = new Dictionary<string, DistrictNodeViewModel>(StringComparer.OrdinalIgnoreCase);
            var districtMembers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupList)
            {
                var districtKey = string.IsNullOrWhiteSpace(group.RaceLeaderKey)
                    ? UnknownRaceLeaderKey
                    : group.RaceLeaderKey.Trim();
                if (!districtNodes.TryGetValue(districtKey, out var district))
                {
                    district = new DistrictNodeViewModel
                    {
                        RaceLeaderKey = districtKey,
                        RaceLeaderName = string.IsNullOrWhiteSpace(group.RaceLeaderName)
                            ? UnknownRaceLeaderName
                            : group.RaceLeaderName.Trim(),
                        AreaName = string.IsNullOrWhiteSpace(group.AreaName)
                            ? MissingAreaName
                            : group.AreaName.Trim()
                    };
                    districtNodes[districtKey] = district;
                    districtMembers[districtKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                membersByList.TryGetValue(group.ListId, out var groupMembers);
                district.Groups.Add(new GroupNodeViewModel
                {
                    ListId = group.ListId,
                    GroupName = (group.GroupName ?? string.Empty).Trim(),
                    LeaderName = string.IsNullOrWhiteSpace(group.LeaderName)
                        ? MissingGroupLeaderName
                        : group.LeaderName.Trim(),
                    GroupTime = (group.GroupTime ?? string.Empty).Trim(),
                    GroupPlace = (group.GroupPlace ?? string.Empty).Trim(),
                    MemberCount = groupMembers?.Count ?? 0
                });

                if (groupMembers != null)
                {
                    districtMembers[districtKey].UnionWith(groupMembers);
                }
            }

            foreach (var entry in districtNodes)
            {
                entry.Value.MemberCount = districtMembers[entry.Key].Count;
                entry.Value.Groups = entry.Value.Groups
                    .OrderBy(group => group.GroupName, StringComparer.Ordinal)
                    .ThenBy(group => group.ListId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                // 此處以送往前端分頁前、已完整建置並排序的群組集合計算總數，
                // 因此 GroupCount 不受前端分頁影響，也不包含另外呈現的未分組節點。
                entry.Value.GroupCount = entry.Value.Groups.Count;
            }

            var result = new DistrictTreeViewModel
            {
                Scope = scope ?? string.Empty,
                // 空白牧區名稱按字典序原本會排在最前；先依區長哨兵分桶，保證有填區長的節點優先，
                // 再以牧區、區長姓名及唯一鍵收斂成可重現的順序，避免 CRM 回傳順序造成畫面跳動。
                Districts = districtNodes.Values
                    .OrderBy(
                        district => string.Equals(
                            district.RaceLeaderName,
                            UnknownRaceLeaderName,
                            StringComparison.Ordinal)
                            ? 1
                            : 0)
                    .ThenBy(district => district.AreaName, StringComparer.Ordinal)
                    .ThenBy(district => district.RaceLeaderName, StringComparer.Ordinal)
                    .ThenBy(district => district.RaceLeaderKey, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            if (includeUngrouped)
            {
                var groupedContactIds = new HashSet<string>(
                    currentMemberships.Select(row => row.ContactId),
                    StringComparer.OrdinalIgnoreCase);
                var ungroupedCount = Math.Max(0, allCurrentContactCount - groupedContactIds.Count);
                result.Ungrouped = new UngroupedNodeViewModel { MemberCount = ungroupedCount };
            }

            return result;
        }
    }
}
