/*
 * 檔案整體說明
 *
 * 目的與定位：以既有已授權名單與現行成員資料建構樹搜尋結果，處理名單匹配、會友列排序及未分組可見性；搜尋不得因文字命中而擴張原使用者範圍。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoTreeSearchBuilder。
 * 主要操作入口包含 Build。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs、
 * ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs 追查目前工作樹中的合作／使用位置。
 */
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
