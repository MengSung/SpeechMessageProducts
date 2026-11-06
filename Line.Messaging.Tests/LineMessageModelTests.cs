/*
 * 檔案整體說明
 *
 * 目的與定位：驗證文字訊息拒絕空內容及優惠券delivery tag長度限制，讓無效模型在送出HTTP前被辨識。
 *
 * 主要功能與流程：
 * 本檔宣告 LineMessageModelTests。
 * 測試案例入口包含 TextV2Message_rejects_null_text_with_clear_exception、
 * CouponMessage_rejects_delivery_tag_longer_than_line_limit。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using FluentAssertions;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessageModelTests
{
    [Fact]
    public void TextV2Message_rejects_null_text_with_clear_exception()
    {
        var action = () => new TextV2Message(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("text");
    }

    [Fact]
    public void CouponMessage_rejects_delivery_tag_longer_than_line_limit()
    {
        var longDeliveryTag = new string('a', 31);

        var action = () => new CouponMessage("coupon-001", longDeliveryTag);

        action.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("deliveryTag");
    }
}
