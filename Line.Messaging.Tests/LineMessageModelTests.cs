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

