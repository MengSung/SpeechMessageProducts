/*
 * 檔案整體說明
 *
 * 目的與定位：無欄位、無快取的雙目標金額顯示器；每次只借用文化至同步呼叫結束。 無使用者／租戶狀態、I/O、背景工作或 disposable owner，可安全共用。 TWD 顯示零位是產品政策而非 ISO 
 * minor-unit 宣稱；其餘核定幣別顯示兩位。
 *
 * 主要功能與流程：
 * 本檔宣告 IMoneyFormatter、MoneyFormatter。
 * - 格式文化只影響數字分隔與負號，不能決定幣別。回傳 ISO 代碼與數字， 使多種 dollar 貨幣不會因相同符號而混淆；不得保存或修改呼叫者文化。
 * - 驗證有效金額及文化後，用 decimal 明確執行 AwayFromZero 顯示捨入， 排除 .NET Framework 與新 .NET 的預設 midpoint 差異；原 Money 精度不變。 不用 C 格式，
 * 避免文化的預設貨幣符號把 TWD 誤顯為 USD。真正格式故障原樣交回 owner， 不吞例外或自行通知；無任何可能保留到下次操作的可變候選。
 * 主要操作入口包含 Format。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Localization.Tests/MoneyTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Globalization;

namespace SpeechMessage.Localization
{
    /// <summary>只顯示既有金額、不做換匯的純格式化契約；必須由呼叫者提供明確格式文化。</summary>
    public interface IMoneyFormatter
    {
        /// <summary>
        /// 格式文化只影響數字分隔與負號，不能決定幣別。回傳 ISO 代碼與數字，
        /// 使多種 dollar 貨幣不會因相同符號而混淆；不得保存或修改呼叫者文化。
        /// </summary>
        /// <param name="money">含明確白名單幣別的金額，default 必須拒絕。</param>
        /// <param name="formatCulture">此操作借用的文化；不可為 null，不得並行修改傳入實例。</param>
        /// <returns>ISO 代碼、一個 ASCII 空白與明確文化的金額顯示。</returns>
        string Format(Money money, CultureInfo formatCulture);
    }

    /// <summary>
    /// 無欄位、無快取的雙目標金額顯示器；每次只借用文化至同步呼叫結束。
    /// 無使用者／租戶狀態、I/O、背景工作或 disposable owner，可安全共用。
    /// TWD 顯示零位是產品政策而非 ISO minor-unit 宣稱；其餘核定幣別顯示兩位。
    /// </summary>
    public sealed class MoneyFormatter : IMoneyFormatter
    {
        /// <summary>
        /// 驗證有效金額及文化後，用 decimal 明確執行 AwayFromZero 顯示捨入，
        /// 排除 .NET Framework 與新 .NET 的預設 midpoint 差異；原 Money 精度不變。
        /// 不用 C 格式，避免文化的預設貨幣符號把 TWD 誤顯為 USD。真正格式故障原樣交回 owner，
        /// 不吞例外或自行通知；無任何可能保留到下次操作的可變候選。
        /// </summary>
        public string Format(Money money, CultureInfo formatCulture)
        {
#if NETFRAMEWORK
            if (formatCulture is null) throw new ArgumentNullException(nameof(formatCulture));
#else
            ArgumentNullException.ThrowIfNull(formatCulture);
#endif
            string currency = money.Currency;
            int digits = string.Equals(currency, "TWD", StringComparison.Ordinal) ? 0 : 2;
            decimal displayAmount = Math.Round(money.Amount, digits, MidpointRounding.AwayFromZero);
            return currency + " " + displayAmount.ToString(digits == 0 ? "N0" : "N2", formatCulture);
        }
    }
}
