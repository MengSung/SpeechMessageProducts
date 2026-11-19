/*
 * 檔案整體說明
 *
 * 目的與定位：奉獻頁的歷史推導規則（純函式）。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationLastGift、DonationHistoryRow、DonationGivingHistory。
 * - 依奉獻次數排序常用類別（次數相同時較近期者優先），不足時依伺服器傳入的教會清單順序補滿；不保存全域類別名稱。
 * - 找出最近一次奉獻：以最新一張收費單為基準，收集同付款方式、建立時間相近的收費單。 定期定額自動扣款（有期數）不算「上次奉獻」；CRM 已不提供的類別會略過。
 * - 正式 ID 相同才合併同一提交的明細；缺少舊 lookup ID 的歷史列不會用顯示名稱推測身份。
 * 主要操作入口包含 DonationLastGift、DonationHistoryRow、RankPinnedCategories、FindLastGift、ToHistoryRows、Clean。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * P6-07公開邊界：DonationGivingHistory、DonationHistoryRow、DonationLastGift改為產品內部宣告；外部輸出與既有資源清理契約不變。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;

namespace ChurchReport.Services
{
    /// <summary>「同上次奉獻」：同一次送出的類別與金額。</summary>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    internal sealed record DonationLastGift(
        IReadOnlyList<DonationLineItemInput> Lines,
        int Total,
        string PayWay,
        DateTime Date);

    /// <summary>「奉獻清單」分頁的一列。</summary>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    internal sealed record DonationHistoryRow(
        DateTime PayDate,
        string Category,
        string Others,
        string PayWay,
        int Amount,
        string PaidPeriod);

    /// <summary>
    /// 奉獻頁的歷史推導規則（純函式）。
    /// </summary>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    internal static class DonationGivingHistory
    {
        public const int PinnedCount = 4;

        /// <summary>同一次送出建立的收費單彼此相差不超過此時間。</summary>
        public static readonly TimeSpan SameGiftWindow = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 依奉獻次數排序常用類別（次數相同時較近期者優先），不足時依已授權教會清單順序補滿。
        /// 可用清單必須是當次登入 contact 所屬教會的啟用選項；以資料庫 Guid 排名及補位，
        /// 即使顯示名稱相同也不會合併。呼叫端不得自報教會或跨請求共用清單。
        /// </summary>
        public static IReadOnlyList<string> RankPinnedCategories(
            IEnumerable<DedicationFee> fees,
            IReadOnlyList<DonationOfferingCategoryOption> available,
            int take = PinnedCount)
        {
            var availableList = Clean(available);
            var availableById = availableList.ToDictionary(
                item => Guid.ParseExact(item.Id, "D"),
                item => item);
            var availableSet = availableById.Keys.ToHashSet();

            var ranked = (fees ?? Enumerable.Empty<DedicationFee>())
                .Where(fee => fee != null && fee.Amount > 0
                    && fee.CategoryId.HasValue && availableSet.Contains(fee.CategoryId.Value))
                .GroupBy(fee => fee.CategoryId!.Value)
                .Select(group => new
                {
                    Id = group.Key,
                    Category = availableById[group.Key].Name,
                    Count = group.Count(),
                    Latest = group.Max(fee => fee.PayDate)
                })
                .OrderByDescending(item => item.Count)
                .ThenByDescending(item => item.Latest)
                .ThenBy(item => availableList.FindIndex(option => option.Id == item.Id.ToString("D")))
                .Select(item => item.Id.ToString("D"));

            var result = new List<string>(Math.Max(take, 0));
            foreach (var categoryId in ranked.Concat(availableList.Select(item => item.Id)))
            {
                if (result.Count >= take)
                {
                    break;
                }

                if (!result.Contains(categoryId, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(categoryId);
                }
            }

            return result;
        }

        /// <summary>
        /// 找出最近一次奉獻：以最新一張收費單為基準，收集同付款方式、建立時間相近的收費單。
        /// 定期定額自動扣款（有期數）不算「上次奉獻」；CRM 已不提供的類別會略過。
        /// </summary>
        public static DonationLastGift FindLastGift(
            IEnumerable<DedicationFee> fees,
            IReadOnlyList<DonationOfferingCategoryOption> available)
        {
            var availableSet = Clean(available)
                .Select(item => Guid.ParseExact(item.Id, "D"))
                .ToHashSet();
            var candidates = (fees ?? Enumerable.Empty<DedicationFee>())
                .Where(fee => fee != null
                    && fee.Amount > 0
                    && string.IsNullOrWhiteSpace(fee.PaidPeriod)
                    && fee.CategoryId.HasValue
                    && availableSet.Contains(fee.CategoryId.Value))
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
                // 只有正式 ID 相同才能合併；舊資料缺 lookup 時不以可能重名的文字猜測身份。
                var existing = fee.CategoryId.HasValue
                    ? lines.FirstOrDefault(line => line.CategoryId == fee.CategoryId)
                    : null;
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
                    CategoryId = fee.CategoryId,
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

        private static List<DonationOfferingCategoryOption> Clean(
            IReadOnlyList<DonationOfferingCategoryOption> available)
        {
            return (available ?? Array.Empty<DonationOfferingCategoryOption>())
                .Where(category => category is { IsSelectable: true }
                    && Guid.TryParseExact(category.Id, "D", out var id)
                    && id != Guid.Empty)
                .ToList();
        }
    }
}
