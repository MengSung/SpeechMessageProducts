using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 保護付款後 LINE 通知文案的編碼與基本語意。
/// 這些訊息會直接送給奉獻者或付款者；如果檔案被 Big5/UTF-8 來回轉碼破壞，
/// 編譯仍可能成功，但使用者收到的內容會變成亂碼，因此用測試鎖住可讀的繁體中文關鍵字。
/// </summary>
public sealed class PaymentMessageBuilderEncodingTests
{
    [Fact]
    public void Dedication_success_message_uses_readable_traditional_chinese_text()
    {
        var builder = new PaymentMessageBuilder();

        var message = builder.BuildDedicationSuccessMessage(
            "胡夢嵩",
            "ORDER-001",
            "TX-001",
            8m,
            "十一奉獻",
            new DateTime(2026, 7, 6, 12, 30, 0));

        message.Should().Contain("金流付款成功通知");
        message.Should().Contain("親愛的 胡夢嵩");
        message.Should().Contain("奉獻類別：十一奉獻");
        message.Should().Contain("付款金額：NT$ 8");
        message.Should().NotContain("嚗");
        message.Should().NotContain(((char)0xFFFD).ToString());
    }
}
