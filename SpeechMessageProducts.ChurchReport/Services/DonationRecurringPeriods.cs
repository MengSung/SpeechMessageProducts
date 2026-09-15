// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationRecurringPeriods.cs
// 所屬區塊：ChurchReport 奉獻付款服務層。
// 檔案責任：把前端送出的定期定額期數文字（例如「12個月」）轉成整數期數。
// 主要型別：DonationRecurringPeriods
// 主要成員：Allowed、Parse
// 維護重點：DonationPaymentProcessor.TransferToDeductTotalNum 原本以中文字串比對，
//           但比對字串在 Big5 轉 UTF-8 時被破壞，所有期數都解析成 0 而退回預設 12 期。
//           改為只取 ASCII 數字並限制在允許的期數，不再依賴中文字面值。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Services
{
    /// <summary>
    /// 定期定額扣款期數的解析規則。
    /// 奉獻頁下拉選單送出「3個月」「6個月」「12個月」「18個月」「24個月」；
    /// 不在允許清單內的值回傳 0，由金流 adapter 套用既有預設（12 期）。
    /// </summary>
    public static class DonationRecurringPeriods
    {
        public static readonly IReadOnlyList<int> Allowed = new[] { 3, 6, 12, 18, 24 };

        public static int Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var digits = new string(text.Where(character => character >= '0' && character <= '9').ToArray());
            return int.TryParse(digits, out var periods) && Allowed.Contains(periods) ? periods : 0;
        }
    }
}
