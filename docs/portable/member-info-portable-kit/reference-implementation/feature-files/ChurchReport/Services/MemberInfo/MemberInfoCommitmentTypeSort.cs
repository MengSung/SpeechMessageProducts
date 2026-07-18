using ChurchReport.ViewModels.MemberInfoTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Services.MemberInfo
{
    /// <summary>
    /// 遠端分頁使用的委身類型區段：已設定選項、metadata 未知舊值、真正空白。
    /// Unknown 與 Empty 不會因使用者切換反向排序而跑到已設定選項之前。
    /// </summary>
    public enum MemberInfoCommitmentTypeSegmentKind
    {
        Configured,
        Unknown,
        Empty
    }

    /// <summary>
    /// 全域排序中的一個連續區段；Configured 才會有 OptionSet Value。
    /// </summary>
    public readonly record struct MemberInfoCommitmentTypeSegment(
        MemberInfoCommitmentTypeSegmentKind Kind,
        int? Value,
        int Count);

    /// <summary>
    /// 某一頁與區段的交集，Skip／Take 均以該區段自身為基準。
    /// </summary>
    public readonly record struct MemberInfoCommitmentTypeSlice(
        MemberInfoCommitmentTypeSegmentKind Kind,
        int? Value,
        int Skip,
        int Take);

    /// <summary>
    /// 三種會友表格共用的 metadata rank 排序及分段頁面規則。
    /// </summary>
    public static class MemberInfoCommitmentTypeSort
    {
        public const string Selector = "MembershipStatusOrder";

        /// <summary>
        /// 已設定選項依 metadata rank 排序；未知值與空白永遠固定在後兩區。
        /// 同一區段依姓名、ContactId 穩定排序，確保搜尋去重及遠端跨頁結果可重現。
        /// </summary>
        public static List<GroupMemberRowViewModel> OrderRows(
            IEnumerable<GroupMemberRowViewModel> rows,
            bool descending = false)
        {
            var source = (rows ?? Enumerable.Empty<GroupMemberRowViewModel>())
                .Where(row => row != null)
                .ToList();
            var configured = source.Where(row => row.MembershipStatusOrder.HasValue);
            var orderedConfigured = descending
                ? configured.OrderByDescending(row => row.MembershipStatusOrder.Value)
                : configured.OrderBy(row => row.MembershipStatusOrder.Value);

            var stableConfigured = orderedConfigured
                .ThenBy(row => row.FullName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(row => row.ContactId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var unknown = source
                .Where(row => !row.MembershipStatusOrder.HasValue &&
                              row.HasMembershipStatusValue)
                .OrderBy(row => row.FullName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(row => row.ContactId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var empty = source
                .Where(row => !row.MembershipStatusOrder.HasValue &&
                              !row.HasMembershipStatusValue)
                .OrderBy(row => row.FullName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(row => row.ContactId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            return stableConfigured.Concat(unknown).Concat(empty).ToList();
        }

        /// <summary>
        /// 依 metadata values 建立遠端查詢區段。Configured 的先後來自輸入序列；
        /// 未列在 metadata 的所有非空 raw values 合併成 Unknown，null 則是最後的 Empty。
        /// </summary>
        public static IReadOnlyList<MemberInfoCommitmentTypeSegment> BuildSegments(
            IEnumerable<int> configuredValues,
            IReadOnlyDictionary<int, int> countsByValue,
            int nullCount,
            bool descending = false)
        {
            var configured = (configuredValues ?? Enumerable.Empty<int>())
                .Distinct()
                .ToList();
            var configuredSet = configured.ToHashSet();
            if (descending)
            {
                configured.Reverse();
            }

            var counts = countsByValue ?? new Dictionary<int, int>();
            var segments = new List<MemberInfoCommitmentTypeSegment>();
            foreach (var value in configured)
            {
                var count = counts.TryGetValue(value, out var found)
                    ? Math.Max(0, found)
                    : 0;
                if (count > 0)
                {
                    segments.Add(new MemberInfoCommitmentTypeSegment(
                        MemberInfoCommitmentTypeSegmentKind.Configured,
                        value,
                        count));
                }
            }

            var unknownCount = counts
                .Where(pair => !configuredSet.Contains(pair.Key))
                .Sum(pair => Math.Max(0, pair.Value));
            if (unknownCount > 0)
            {
                segments.Add(new MemberInfoCommitmentTypeSegment(
                    MemberInfoCommitmentTypeSegmentKind.Unknown,
                    null,
                    unknownCount));
            }

            var normalizedNullCount = Math.Max(0, nullCount);
            if (normalizedNullCount > 0)
            {
                segments.Add(new MemberInfoCommitmentTypeSegment(
                    MemberInfoCommitmentTypeSegmentKind.Empty,
                    null,
                    normalizedNullCount));
            }
            return segments;
        }

        /// <summary>
        /// 把全域 skip/take 投影到各區段的局部範圍；只回傳真正與頁面相交的正數切片。
        /// </summary>
        public static IReadOnlyList<MemberInfoCommitmentTypeSlice> PlanSlices(
            int skip,
            int take,
            IEnumerable<MemberInfoCommitmentTypeSegment> segments)
        {
            var remainingSkip = Math.Max(0, skip);
            var remainingTake = Math.Max(0, take);
            if (remainingTake == 0)
            {
                return Array.Empty<MemberInfoCommitmentTypeSlice>();
            }

            var slices = new List<MemberInfoCommitmentTypeSlice>();
            foreach (var segment in segments ??
                     Enumerable.Empty<MemberInfoCommitmentTypeSegment>())
            {
                var count = Math.Max(0, segment.Count);
                if (remainingSkip >= count)
                {
                    remainingSkip -= count;
                    continue;
                }

                var localSkip = remainingSkip;
                var available = count - localSkip;
                var localTake = Math.Min(remainingTake, available);
                if (localTake > 0)
                {
                    slices.Add(new MemberInfoCommitmentTypeSlice(
                        segment.Kind,
                        segment.Value,
                        localSkip,
                        localTake));
                    remainingTake -= localTake;
                }
                remainingSkip = 0;
                if (remainingTake == 0)
                {
                    break;
                }
            }
            return slices;
        }

    }
}
