// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorMoneyToChineseTests.cs
// 檔案責任：固定奉獻金額轉財務中文大寫的功能契約，防止註解整理或效能修改破壞收據文字。
// 測試保護：測試直接呼叫真實 DonationPaymentProcessor.MoneyToChinese；使用未初始化物件是因為
// 該方法是純轉換邏輯，不需要 CRM、LINE 或金流相依服務，避免測試建立外部資源與跨測試狀態。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF，並以 final CRLF 結尾。
// ============================================================================
using System;
using System.Runtime.CompilerServices;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證奉獻金額轉換的數字、位值、小數與錯誤輸入契約。
/// </summary>
public sealed class DonationPaymentProcessorMoneyToChineseTests
{
    /// <summary>
    /// 建立只用於純轉換方法測試的 processor 物件；不執行會連線 CRM/LINE 的建構式。
    /// </summary>
    private static DonationPaymentProcessor CreateProcessor()
    {
        return (DonationPaymentProcessor)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentProcessor));
    }

    [Theory]
    [InlineData("0", "零圓整")]
    [InlineData("3", "參圓整")]
    [InlineData("5", "伍圓整")]
    [InlineData("6", "陸圓整")]
    [InlineData("8", "捌圓整")]
    [InlineData("123.45", "壹佰貳拾參圓肆角伍分")]
    [InlineData("1000", "壹仟圓整")]
    [InlineData("1001", "壹仟零壹圓整")]
    [InlineData("10000", "壹萬圓整")]
    [InlineData("10001", "壹萬零壹圓整")]
    [InlineData("100000000", "壹億圓整")]
    [InlineData("0.05", "伍分")]
    [InlineData("1,234.50", "壹仟貳佰參拾肆圓伍角")]
    [InlineData("100000001", "壹億零壹圓整")]
    [InlineData("100010000", "壹億零壹萬圓整")]
    public void MoneyToChinese_should_preserve_financial_digit_and_place_values(string input, string expected)
    {
        var result = CreateProcessor().MoneyToChinese(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void MoneyToChinese_should_fail_closed_to_zero_for_blank_or_invalid_input(string input)
    {
        CreateProcessor().MoneyToChinese(input).Should().Be("零圓整");
    }

    [Fact]
    public void MoneyToChinese_should_preserve_negative_sign_without_sharing_state()
    {
        var processor = CreateProcessor();

        processor.MoneyToChinese("-12.30").Should().Be("負壹拾貳圓參角");
        processor.MoneyToChinese("12.30").Should().Be("壹拾貳圓參角");
    }
}
