/*
 * 檔案整體說明
 *
 * 目的與定位：保存完整 decimal 金額及第一版核定的 ISO 幣別；不換匯、不在建構或加法時捨入。 只有純值，不引用使用者、租戶、連線或可變文化；可跨執行緒傳遞且無資源釋放責任。 default 沒有合法幣別，
 * 禁止進入金融運算或格式化，避免默認為某種貨幣的零。
 *
 * 主要功能與流程：
 * 本檔宣告 Money。
 * - 只保留已驗證的固定 ISO 字串；null 只可能來自 default，沒有共享可變表。
 * - 建立不經捨入的金額；負數與零合法。幣別必須精確命中七種大寫白名單， 不自動修剪或改大小寫，以免掩蓋來源金融資料錯誤。此驗證不是授權或換匯。
 * - 原始金額，不受顯示小數位影響；單獨讀取此數值不代表 default 已成為有效貨幣。
 * - 不可變 ISO 身分；default 沒有幣別時明確拒絕，不推定為 TWD 或格式文化的貨幣。
 * 主要操作入口包含 Add、Equals、GetHashCode。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Localization.Tests/MoneyTests.cs、
 * ToolUtility.Tests/AttributeOperations/AttributeServiceCompositeTests.cs、
 * ToolUtility.Tests/AttributeOperations/MoneyAttributeServiceTests.cs、
 * ToolUtility.Tests/DataOperations/AttributeConversionTests.cs、SpeechMessage.Localization/MoneyFormatter.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace SpeechMessage.Localization
{
    /// <summary>
    /// 保存完整 decimal 金額及第一版核定的 ISO 幣別；不換匯、不在建構或加法時捨入。
    /// 只有純值，不引用使用者、租戶、連線或可變文化；可跨執行緒傳遞且無資源釋放責任。
    /// default 沒有合法幣別，禁止進入金融運算或格式化，避免默認為某種貨幣的零。
    /// </summary>
    public readonly struct Money : IEquatable<Money>
    {
        /// <summary>只保留已驗證的固定 ISO 字串；null 只可能來自 default，沒有共享可變表。</summary>
        private readonly string? currency;

        /// <summary>
        /// 建立不經捨入的金額；負數與零合法。幣別必須精確命中七種大寫白名單，
        /// 不自動修剪或改大小寫，以免掩蓋來源金融資料錯誤。此驗證不是授權或換匯。
        /// </summary>
        /// <param name="amount">原始 decimal 值，保留可表示的完整精度。</param>
        /// <param name="currency">TWD、HKD、SGD、MYR、USD、GBP 或 AUD。</param>
        /// <exception cref="ArgumentException">幣別缺少或不在白名單；尚未發生任何 I/O。</exception>
        public Money(decimal amount, string currency)
        {
            this.currency = currency switch
            {
                "TWD" => "TWD",
                "HKD" => "HKD",
                "SGD" => "SGD",
                "MYR" => "MYR",
                "USD" => "USD",
                "GBP" => "GBP",
                "AUD" => "AUD",
                _ => throw new ArgumentException("Currency must be an approved uppercase ISO code.", nameof(currency))
            };
            Amount = amount;
        }

        /// <summary>原始金額，不受顯示小數位影響；單獨讀取此數值不代表 default 已成為有效貨幣。</summary>
        public decimal Amount { get; }

        /// <summary>不可變 ISO 身分；default 沒有幣別時明確拒絕，不推定為 TWD 或格式文化的貨幣。</summary>
        /// <exception cref="InvalidOperationException">未透過驗證建構式建立的 default 金額。</exception>
        public string Currency => currency ?? throw new InvalidOperationException("A default Money has no valid currency.");

        /// <summary>
        /// 僅允許同一幣別精確相加；先驗證兩值有效及幣別相符，再以 checked decimal 運算。
        /// 不修改來源、不捨入或捕捉溢位；呼叫端必須決定可預期拒絕或真正失敗的通知邊界。
        /// </summary>
        /// <param name="other">同幣別的有效金額。</param>
        /// <returns>同幣別的新金額值。</returns>
        /// <exception cref="ArgumentException">不同幣別；不進行換匯。</exception>
        /// <exception cref="OverflowException">結果超過 decimal 可表示範圍。</exception>
        public Money Add(Money other)
        {
            string ownCurrency = Currency;
            if (!string.Equals(ownCurrency, other.Currency, StringComparison.Ordinal))
                throw new ArgumentException("Amounts with different currencies cannot be added.", nameof(other));
            return new Money(checked(Amount + other.Amount), ownCurrency);
        }

        /// <summary>相等必須同時具有相同數值與 ISO 身分；default 與 default 可相等，但仍不能用於金融操作。</summary>
        public bool Equals(Money other) => Amount == other.Amount && string.Equals(currency, other.currency, StringComparison.Ordinal);
        /// <summary>只比較相同型別的值，不以格式文字、文化或 SDK 金額物件替代幣別身分。</summary>
        public override bool Equals(object? obj) => obj is Money other && Equals(other);
        /// <summary>數值相同但 scale 不同仍產生相同雜湊；包含幣別以分開不同金融身分。</summary>
        public override int GetHashCode() => unchecked((Amount.GetHashCode() * 397) ^ (currency is null ? 0 : StringComparer.Ordinal.GetHashCode(currency)));
        /// <summary>委派唯一的同幣別驗證與加法實作；不建立額外的運算政策。</summary>
        public static Money operator +(Money left, Money right) => left.Add(right);
        /// <summary>以數值與幣別比較，不涉及顯示捨入。</summary>
        public static bool operator ==(Money left, Money right) => left.Equals(right);
        /// <summary>同一值相等關係的反向，包含 default 與幣別差異。</summary>
        public static bool operator !=(Money left, Money right) => !left.Equals(right);
    }
}
