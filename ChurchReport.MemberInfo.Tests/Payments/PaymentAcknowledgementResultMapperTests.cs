using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class PaymentAcknowledgementResultMapperTests
{
    [Fact]
    public void Map_returns_plain_text_content_result()
    {
        var result = PaymentAcknowledgementResultMapper.Map(PaymentCallbackAcknowledgement.PlainText("8888"));

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("8888");
        content.ContentType.Should().Be("text/plain");
        content.StatusCode.Should().Be(200);
    }

    [Fact]
    public void Map_returns_json_content_result()
    {
        var result = PaymentAcknowledgementResultMapper.Map(PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}"));

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("application/json");
    }

    [Fact]
    public void Map_returns_redirect_result()
    {
        var result = PaymentAcknowledgementResultMapper.Map(PaymentCallbackAcknowledgement.Redirect("https://example.test/success"));

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("https://example.test/success");
    }

    [Fact]
    public void Map_returns_status_code_result_for_none()
    {
        var result = PaymentAcknowledgementResultMapper.Map(PaymentCallbackAcknowledgement.None);

        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(200);
    }
}
