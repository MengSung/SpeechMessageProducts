/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 GoodShepherd DeterministicGuid 的跨目標穩定性，保護待辦與重試
 * 事件不得因執行目標、文化或程序狀態不同而產生不同正式識別。
 *
 * 主要功能與流程：測試以固定 purpose、GUID、日期與版本組合呼叫產生器，以獨立 Python 參考編碼得到的字面向量確認兩宿主
 * 輸出相同 GUID，且沒有依賴時間或外部服務。測試不連線、不讀秘密，
 * 不建立背景工作；每個斷言只消費當次純值。
 *
 * 使用邊界與維護：本測試是離線合成證據，不等同資料庫唯一性或實機驗收；若變更
 * 編碼格式，必須同步檢查 net462 與 net10.0 的輸出及既有事件鍵相容性。
 */
using System;
using System.Globalization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Rules;

/// <summary>保護跨目標確定性 GUID 契約的離線測試。</summary>
public sealed class DeterministicGuidTests
{
    /// <summary>固定輸入重算兩次必須相同，避免重試或待辦重複建立。</summary>
    [Fact]
    public void DeterministicGuid_SameInputs_SameGuidOnBothTargets()
    {
        var contactId = new Guid("11111111-2222-3333-4444-555555555555");
        var first = DeterministicGuid.Create("RichMenuGenerate", contactId, "zh-TW", 7);
        var second = DeterministicGuid.Create("RichMenuGenerate", contactId, "zh-TW", 7);

        Assert.Equal(first, second);
        Assert.Equal(new Guid("ddf156fe-6e97-9f56-8143-36549b9ff4b5"), first);
    }
    /// <summary>任意物件及可變陣列不能靠 ToString 冒充穩定身分，必須明確拒絕。</summary>
    [Fact]
    public void DeterministicGuid_UnsupportedMutableParts_Rejected()
    {
        Assert.Throws<ArgumentException>(() => DeterministicGuid.Create("Event", new object()));
        Assert.Throws<ArgumentException>(() => DeterministicGuid.Create("Event", new byte[2]));
        Assert.Throws<ArgumentException>(() => DeterministicGuid.Create("Event", 1.23d));
        Assert.Throws<ArgumentException>(() => DeterministicGuid.Create("Event", 1.23f));
        Assert.Throws<ArgumentException>(() => DeterministicGuid.Create("Event", DateTimeKind.Utc));
        Assert.Throws<ArgumentNullException>(() => DeterministicGuid.Create("Event", new object[] { null! }));
        Assert.Throws<System.Text.EncoderFallbackException>(() => DeterministicGuid.Create("Event", "\ud800"));
        Assert.Throws<ArgumentException>(() => DeterministicGuid.Create("Event", new int[2]));
        Assert.Throws<ArgumentException>(() => DeterministicGuid.Create("Event", new System.Text.StringBuilder("mutable")));
    }

    /// <summary>日期的 ticks 與 Kind 都屬於身分；原本 Convert.ToString 會丟失 Kind 與次秒。</summary>
    [Fact]
    public void DeterministicGuid_DatesPreservePrecisionAndKind()
    {
        var utc = new DateTime(638980000000000001L, DateTimeKind.Utc);
        Assert.NotEqual(DeterministicGuid.Create("Date", utc), DeterministicGuid.Create("Date", utc.AddTicks(1)));
        Assert.NotEqual(DeterministicGuid.Create("Date", utc),
            DeterministicGuid.Create("Date", DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
    }
    /// <summary>獨立 Python 參考編碼固定全部純量 tag 與 Unicode UTF-16 長度，兩宿主共用字面向量。</summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void DeterministicGuid_ScalarVector_IsInvariant(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var actual = DeterministicGuid.Create("Scalars", true, '中', (sbyte)-8, (byte)8, (short)-16, (ushort)16,
                -32, 32U, -64L, 64UL, 12.3400m, "甲|乙:😀");
            Assert.Equal(new Guid("5350984b-5291-2555-9114-92aada2b888d"), actual);
            Assert.Equal(DeterministicGuid.Create("Amount", 1.1m), DeterministicGuid.Create("Amount", 1.10m));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    /// <summary>以固定字面向量鎖定七位次秒、明確 Kind、offset 及 TimeSpan，不讀機器當地時區。</summary>
    [Fact]
    public void DeterministicGuid_DateVector_PreservesClockValues()
    {
        var clock = new DateTime(2026, 11, 14, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);
        var offset = new DateTimeOffset(2026, 11, 14, 20, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234567);
        var duration = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(5));
        Assert.Equal(new Guid("6cd21b12-8d59-2e52-8627-27f0d9eecd48"), DeterministicGuid.Create("Dates", clock,
            DateTime.SpecifyKind(clock, DateTimeKind.Unspecified), DateTime.SpecifyKind(clock, DateTimeKind.Local), offset, duration));
    }

    /// <summary>同值不同型別與不同字串邊界不得合併，purpose 也是固定身分的一部分。</summary>
    [Fact]
    public void DeterministicGuid_TypeOrderAndBoundaries_AreDistinct()
    {
        Assert.NotEqual(DeterministicGuid.Create("Event", 7), DeterministicGuid.Create("Event", 7L));
        Assert.NotEqual(DeterministicGuid.Create("Event", "ab", "c"), DeterministicGuid.Create("Event", "a", "bc"));
        Assert.NotEqual(DeterministicGuid.Create("Event", "a", "b"), DeterministicGuid.Create("Event", "b", "a"));
        Assert.NotEqual(DeterministicGuid.Create("One", "a"), DeterministicGuid.Create("Two", "a"));
    }
}
