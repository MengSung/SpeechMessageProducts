// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationGivingHistory.cs
// 所屬區塊：ChurchReport 奉獻付款服務層。
// 檔案責任：由會友已繳費的收費單推導奉獻頁的「常用類別」排序、「同上次奉獻」內容與「奉獻清單」列。
// 主要型別：DonationGivingHistory、DonationLastGift、DonationHistoryRow
// 主要成員：RankPinnedCategories、FindLastGift、ToHistoryRows
// 外部依賴：只讀取 DedicationFee 快照；不查 CRM、不寫 Session、不使用 static 可變快取。
// 隔離要求：呼叫端必須以伺服器端已驗證的奉獻 contact 取得收費單，這裡不接受任何會友識別資料。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
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
