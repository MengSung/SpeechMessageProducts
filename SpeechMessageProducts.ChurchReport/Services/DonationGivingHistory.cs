/*
 * 檔案整體說明
 *
 * 目的與定位：奉獻頁的歷史推導規則（純函式）。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationLastGift、DonationHistoryRow、DonationGivingHistory。
 * - 沒有奉獻紀錄時的常用類別；只取目前 CRM 仍提供的類別。
 * - 依奉獻次數排序常用類別（次數相同時較近期者優先），不足時以預設類別、再以 CRM 類別順序補滿。
 * - 找出最近一次奉獻：以最新一張收費單為基準，收集同付款方式、建立時間相近的收費單。 定期定額自動扣款（有期數）不算「上次奉獻」；CRM 已不提供的類別會略過。
 * 主要操作入口包含 DonationLastGift、DonationHistoryRow、RankPinnedCategories、FindLastGift、ToHistoryRows、Clean。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Dedication/DonationGivingHistoryTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;

namespace ChurchReport.Services
{
    /// <summary>「同上次奉獻」：同一次送出的類別與金額。</summary>
    public sealed record DonationLastGift(
        IReadOnlyList<DonationLineItemInput> Lines,
        int Total,
        string PayWay,
        DateTime Date);

    /// <summary>「奉獻清單」分頁的一列。</summary>
    public sealed record DonationHistoryRow(
        DateTime PayDate,
        string Category,
        string Others,
        string PayWay,
        int Amount,
        string PaidPeriod);

    /// <summary>
    /// 奉獻頁的歷史推導規則（純函式）。
    /// </summary>
    public static class DonationGivingHistory
    {
        public const int PinnedCount = 4;

        /// <summary>沒有奉獻紀錄時的常用類別；只取目前 CRM 仍提供的類別。</summary>
        public static readonly IReadOnlyList<string> DefaultPinned = new[] { "十一奉獻", "感恩奉獻", "建堂奉獻", "宣教奉獻" };

        /// <summary>同一次送出建立的收費單彼此相差不超過此時間。</summary>
        public static readonly TimeSpan SameGiftWindow = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 依奉獻次數排序常用類別（次數相同時較近期者優先），不足時以預設類別、再以 CRM 類別順序補滿。
        /// </summary>
        public static IReadOnlyList<string> RankPinnedCategories(
            IEnumerable<DedicationFee> fees,
            IReadOnlyList<string> available,
            int take = PinnedCount)
        {
            var availableList = Clean(available);
            var availableSet = new HashSet<string>(availableList, StringComparer.Ordinal);

            var ranked = (fees ?? Enumerable.Empty<DedicationFee>())
                .Where(fee => fee != null && fee.Amount > 0 && availableSet.Contains(fee.Category ?? string.Empty))
                .GroupBy(fee => fee.Category, StringComparer.Ordinal)
                .Select(group => new
                {
                    Category = group.Key,
                    Count = group.Count(),
                    Latest = group.Max(fee => fee.PayDate)
                })
                .OrderByDescending(item => item.Count)
                .ThenByDescending(item => item.Latest)
                .ThenBy(item => availableList.IndexOf(item.Category))
                .Select(item => item.Category);

            var result = new List<string>(Math.Max(take, 0));
            foreach (var category in ranked
                .Concat(DefaultPinned.Where(availableSet.Contains))
                .Concat(availableList))
            {
                if (result.Count >= take)
                {
                    break;
                }

                if (!result.Contains(category))
                {
                    result.Add(category);
                }
            }

            return result;
        }

        /// <summary>
        /// 找出最近一次奉獻：以最新一張收費單為基準，收集同付款方式、建立時間相近的收費單。
        /// 定期定額自動扣款（有期數）不算「上次奉獻」；CRM 已不提供的類別會略過。
        /// </summary>
        public static DonationLastGift FindLastGift(IEnumerable<DedicationFee> fees, IReadOnlyList<string> available)
        {
            var availableSet = new HashSet<string>(Clean(available), StringComparer.Ordinal);
            var candidates = (fees ?? Enumerable.Empty<DedicationFee>())
                .Where(fee => fee != null
                    && fee.Amount > 0
                    && string.IsNullOrWhiteSpace(fee.PaidPeriod)
                    && availableSet.Contains(fee.Category ?? string.Empty))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var latest = candidates.OrderByDescending(fee => fee.DedicationDate).First();
            var sameGift = candidates
                .Where(fee => string.Equals(fee.PayWay, latest.PayWay, StringComparison.Ordinal)
                    && (latest.DedicationDate - fee.DedicationDate).Duration() <= SameGiftWindow)
                .OrderBy(fee => fee.DedicationDate);

            var lines = new List<DonationLineItemInput>();
            foreach (var fee in sameGift)
            {
                var existing = lines.FirstOrDefault(line => line.Category == fee.Category);
                if (existing != null)
                {
                    existing.Amount += fee.Amount;
                    continue;
                }

                if (lines.Count >= DonationLineItemNormalizer.MaxLines)
                {
                    continue;
                }

                lines.Add(new DonationLineItemInput
                {
                    Category = fee.Category,
                    Amount = fee.Amount,
                    Others = fee.Others ?? string.Empty
                });
            }

            return new DonationLastGift(lines, lines.Sum(line => line.Amount), latest.PayWay ?? string.Empty, latest.DedicationDate);
        }

        /// <summary>奉獻清單列：付款日期新到舊。</summary>
        public static IReadOnlyList<DonationHistoryRow> ToHistoryRows(IEnumerable<DedicationFee> fees)
        {
            return (fees ?? Enumerable.Empty<DedicationFee>())
                .Where(fee => fee != null)
                .OrderByDescending(fee => fee.PayDate)
                .ThenByDescending(fee => fee.DedicationDate)
                .Select(fee => new DonationHistoryRow(
                    fee.PayDate,
                    fee.Category ?? string.Empty,
                    fee.Others ?? string.Empty,
                    fee.PayWay ?? string.Empty,
                    fee.Amount,
                    fee.PaidPeriod ?? string.Empty))
                .ToList();
        }

        private static List<string> Clean(IReadOnlyList<string> available)
        {
            return (available ?? Array.Empty<string>())
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Select(category => category.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }
}
