/*
 * 檔案整體說明
 *
 * 目的與定位：定期定額扣款期數的解析規則。 奉獻頁下拉選單送出「3個月」「6個月」「12個月」「18個月」「24個月」； 不在允許清單內的值回傳 0，由金流 adapter 套用既有預設（12 期）。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationRecurringPeriods。
 * 主要操作入口包含 Parse。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
