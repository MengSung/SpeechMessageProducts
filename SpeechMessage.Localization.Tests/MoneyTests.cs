using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace SpeechMessage.Localization.Tests;

/// <summary>
/// 保護雙目標金額地基；先檢查產品組件確實有正式型別，避免只驗測試自己的替代實作。
/// 後續以固定金額、明確文化及拒絕向量保護不換匯、精度與隔離。
/// </summary>
public sealed class MoneyTests
{
    /// <summary>缺少正式 Money／formatter 時必須真紅；兩宿主都查實際引用的產品組件。</summary>
    [Fact(DisplayName = "產品包含正式金額及格式化契約")]
    public void MoneyContract_BothTargets_ContainsMoneyAndFormatter()
    {
        var assembly = typeof(SystemTextTable).Assembly;
        Assert.NotNull(assembly.GetType("SpeechMessage.Localization.Money", false));
        Assert.NotNull(assembly.GetType("SpeechMessage.Localization.IMoneyFormatter", false));
        Assert.NotNull(assembly.GetType("SpeechMessage.Localization.MoneyFormatter", false));
    }

    /// <summary>TWD 顯示零位是產品政策；固定 midpoint 向遠離零取整，但原金額精度不可改變。</summary>
    [Fact(DisplayName = "台幣顯示零位且保留儲存精度")]
    public void Twd_NoDecimals()
    {
        var money = new Money(1234.5m, "TWD");
        Assert.Equal("TWD 1,235", new MoneyFormatter().Format(money, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(1234.5m, money.Amount);
        Assert.Equal("TWD", money.Currency);
    }

    /// <summary>美元顯示兩位；decimal midpoint 明確處理以避免兩 runtime 的預設捨入差異。</summary>
    [Fact(DisplayName = "美元顯示兩位且明確處理中點")]
    public void Usd_TwoDecimals()
    {
        var formatter = new MoneyFormatter();
        Assert.Equal("USD 1,234.57", formatter.Format(new Money(1234.565m, "USD"), CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("USD 1.20", formatter.Format(new Money(1.2m, "USD"), CultureInfo.GetCultureInfo("en-US")));
    }

    /// <summary>R8：英美格式文化只決定分隔符，不能把台幣變成美元或不明的 dollar symbol。</summary>
    [Fact(DisplayName = "英美格式不會改變台幣身分")]
    public void Format_EnUsCulture_KeepsTwdCurrency()
    {
        Assert.Equal("TWD 1,000", new MoneyFormatter().Format(new Money(1000m, "TWD"), CultureInfo.GetCultureInfo("en-US")));
    }

    /// <summary>七幣白名單均以 ISO 身分輸出；台幣以外皆為兩位，不因文化符號碰巧相同而合併。</summary>
    [Theory(DisplayName = "每個白名單幣別都有明確身分與小數位")]
    [InlineData("TWD", "TWD 12")]
    [InlineData("HKD", "HKD 12.00")]
    [InlineData("SGD", "SGD 12.00")]
    [InlineData("MYR", "MYR 12.00")]
    [InlineData("USD", "USD 12.00")]
    [InlineData("GBP", "GBP 12.00")]
    [InlineData("AUD", "AUD 12.00")]
    public void Whitelist_ValidCurrency_PreservesIdentity(string currency, string expected)
    {
        Assert.Equal(expected, new MoneyFormatter().Format(new Money(12m, currency), CultureInfo.InvariantCulture));
    }

    /// <summary>未知、大小寫不符或帶空白的金融輸入直接拒絕，不能猜測或自動修正成另一種幣別。</summary>
    [Theory(DisplayName = "未知或非標準幣別拒絕")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("JPY")]
    [InlineData("usd")]
    [InlineData(" USD")]
    [InlineData("TWD ")]
    public void UnknownCurrency_Throws(string? currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(1m, currency!));
    }

    /// <summary>跨幣別相加不可暗中換匯；不同順序的加法入口都應拒絕。</summary>
    [Fact(DisplayName = "跨幣別加法拒絕")]
    public void Add_DifferentCurrencies_Throws()
    {
        var twd = new Money(1m, "TWD");
        var usd = new Money(1m, "USD");
        Assert.Throws<ArgumentException>(() => twd.Add(usd));
        Assert.Throws<ArgumentException>(() => usd + twd);
    }

    /// <summary>同幣別負數及小數精度完整相加；兩個入口結果相同且不修改來源。</summary>
    [Fact(DisplayName = "同幣別精確加法不修改來源")]
    public void Add_SameCurrency_PreservesDecimalPrecision()
    {
        var left = new Money(1.23456789m, "TWD");
        var right = new Money(-0.00000001m, "TWD");
        var expected = new Money(1.23456788m, "TWD");
        Assert.Equal(expected, left.Add(right));
        Assert.Equal(expected, left + right);
        Assert.Equal(1.23456789m, left.Amount);
        Assert.Equal(-0.00000001m, right.Amount);
    }

    /// <summary>正負 decimal 邊界均拒絕溢位，不返回截斷、無限大或錯誤金額。</summary>
    [Fact(DisplayName = "加法正負溢位皆拒絕")]
    public void Add_DecimalOverflow_Throws()
    {
        Assert.Throws<OverflowException>(() => new Money(decimal.MaxValue, "USD") + new Money(1m, "USD"));
        Assert.Throws<OverflowException>(() => new Money(decimal.MinValue, "USD").Add(new Money(-1m, "USD")));
    }

    /// <summary>預設 struct 沒有合法幣別，Currency、格式化及任一側加法均拒絕，不能冒充台幣零元。</summary>
    [Fact(DisplayName = "預設金額不可用於顯示或運算")]
    public void DefaultMoney_FailsClosed()
    {
        var invalid = default(Money);
        Assert.Throws<InvalidOperationException>(() => invalid.Currency);
        Assert.Throws<InvalidOperationException>(() => invalid.Add(new Money(0m, "TWD")));
        Assert.Throws<InvalidOperationException>(() => new Money(0m, "TWD") + invalid);
        Assert.Throws<InvalidOperationException>(() => new MoneyFormatter().Format(invalid, CultureInfo.InvariantCulture));
        Assert.NotEqual(new Money(0m, "TWD"), invalid);
    }

    /// <summary>值相等包含幣別且不依 decimal scale；含 default 的相等仍維持反身性與雜湊契約。</summary>
    [Fact(DisplayName = "金額相等與雜湊包含幣別且不依顯示文字")]
    public void Equality_AmountAndCurrency_ObeysValueContract()
    {
        var first = new Money(1.00m, "TWD");
        var second = new Money(1m, "TWD");
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(new Money(1m, "USD")));
        Assert.False(first.Equals(null));
        Assert.False(first.Equals("TWD 1"));
        Assert.True(default(Money).Equals(default(Money)));
        Assert.Equal(default(Money).GetHashCode(), default(Money).GetHashCode());
    }

    /// <summary>零、負 midpoint、最大／最小值皆可格式化；不因補小數位或取絕對值而溢位。</summary>
    [Theory(DisplayName = "金額顯示邊界與負中點兩產品一致")]
    [InlineData("0", "TWD", "TWD 0")]
    [InlineData("-0.0001", "TWD", "TWD 0")]
    [InlineData("-0.0001", "USD", "USD 0.00")]
    [InlineData("-1.5", "TWD", "TWD -2")]
    [InlineData("-1.005", "USD", "USD -1.01")]
    [InlineData("79228162514264337593543950335", "USD", "USD 79,228,162,514,264,337,593,543,950,335.00")]
    [InlineData("-79228162514264337593543950335", "TWD", "TWD -79,228,162,514,264,337,593,543,950,335")]
    public void Format_Boundaries_Consistent(string amount, string currency, string expected)
    {
        Assert.Equal(expected, new MoneyFormatter().Format(new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), currency), CultureInfo.InvariantCulture));
    }

    /// <summary>傳入文化可自訂分隔與負號；只借讀、不修改，其貨幣符號不能覆蓋 ISO 幣別。</summary>
    [Fact(DisplayName = "自訂文化只改數字格式且不被修改")]
    public void Format_ExplicitCulture_DoesNotMutateOrChangeCurrency()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.NumberFormat.NumberGroupSeparator = "_";
        culture.NumberFormat.NumberDecimalSeparator = ":";
        culture.NumberFormat.NegativeSign = "~";
        culture.NumberFormat.CurrencySymbol = "DO-NOT-USE";
        Assert.Equal("GBP ~1_234:50", new MoneyFormatter().Format(new Money(-1234.5m, "GBP"), culture));
        Assert.Equal("_", culture.NumberFormat.NumberGroupSeparator);
        Assert.Equal(":", culture.NumberFormat.NumberDecimalSeparator);
        Assert.Equal("DO-NOT-USE", culture.NumberFormat.CurrencySymbol);
        Assert.Equal("USD 1.234,50", new MoneyFormatter().Format(new Money(1234.5m, "USD"), CultureInfo.GetCultureInfo("de-DE")));
    }

    /// <summary>缺少文化必須明確拒絕，不能使用伺服器全域文化補值。</summary>
    [Fact(DisplayName = "格式文化不可省略")]
    public void Format_NullCulture_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MoneyFormatter().Format(new Money(1m, "TWD"), null!));
    }

    /// <summary>注入文化讀取故障；原 Exception 必須交回 owner，下一次呼叫可恢復而無殘留狀態。</summary>
    [Fact(DisplayName = "格式化故障保留原例外且可重試")]
    public void Format_CultureFault_PreservesExceptionAndRetries()
    {
        var error = new InvalidOperationException("synthetic-format-fault");
        var formatter = new MoneyFormatter();
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => formatter.Format(new Money(1m, "USD"), new FailingCulture(error))));
        Assert.Equal("USD 1.00", formatter.Format(new Money(1m, "USD"), CultureInfo.InvariantCulture));
    }

    /// <summary>同一 formatter 千次 A/B 並行；不同幣別與文化不能互污染，全部 Task 在回傳前已排空。</summary>
    [Fact(DisplayName = "千次幣別與文化並行互不洩漏")]
    public async Task Format_ABParallel_NoCultureOrCurrencyLeak()
    {
        var formatter = new MoneyFormatter();
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
        {
            bool first = index % 2 == 0;
            string result = formatter.Format(new Money(1234.5m, first ? "TWD" : "USD"), CultureInfo.GetCultureInfo(first ? "en-US" : "de-DE"));
            Assert.Equal(first ? "TWD 1,235" : "USD 1.234,50", result);
        })));
    }

    /// <summary>長生命 formatter 不得保留呼叫者文化；百次弱參照在同步呼叫結束及強制 GC 後全回基線。</summary>
    [Fact(DisplayName = "格式器不保留呼叫者文化")]
    public void Format_CallerCultures_AreCollectible()
    {
        var formatter = new MoneyFormatter();
        var references = Enumerable.Range(0, 100).Select(_ => FormatTemporaryCulture(formatter)).ToArray();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(formatter);
    }

    /// <summary>獨立 stack frame 排除測試 JIT 暫存參照；只回弱參照，不建立計時器或背景工作。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference FormatTemporaryCulture(MoneyFormatter formatter)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        Assert.Equal("USD 1.00", formatter.Format(new Money(1m, "USD"), culture));
        return new WeakReference(culture);
    }

    /// <summary>故障注入用文化只保存測試例外，生命週期限同步呼叫，不接觸系統文化或秘密。</summary>
    private sealed class FailingCulture : CultureInfo
    {
        /// <summary>本測試擁有的合成故障，不由 formatter 保存或轉換。</summary>
        private readonly Exception error;
        /// <summary>建立單次故障來源；不配置外部資源。</summary>
        public FailingCulture(Exception error) : base("en-US") => this.error = error;
        /// <summary>真正讀格式資訊時拋原例外，setter 僅委派基底以滿足文化型別契約。</summary>
        public override NumberFormatInfo NumberFormat { get => throw error; set => base.NumberFormat = value; }
    }
}
