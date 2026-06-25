using FluentAssertions;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessage.Payments.Tests.Models;

public sealed class PaymentModelContractTests
{
    [Fact]
    public void Create_request_carries_profile_order_amount_and_callbacks()
    {
        var request = new PaymentCreateRequest
        {
            ProfileName = "JesusTest",
            ProductOrderId = "F202606250001",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Fee payment",
            PaymentMethod = "CreditCard",
            Callbacks = new PaymentCallbacks
            {
                ReturnUrl = "https://example.test/return",
                BackendUrl = "https://example.test/backend",
                SuccessUrl = "https://example.test/success",
                FailureUrl = "https://example.test/failure"
            }
        };

        request.ProfileName.Should().Be("JesusTest");
        request.ProductOrderId.Should().Be("F202606250001");
        request.Amount.Should().Be(1200m);
        request.Callbacks.BackendUrl.Should().EndWith("/backend");
    }

    [Fact]
    public void Create_result_uses_neutral_payment_page_and_provider_reference_fields()
    {
        var result = new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "F202606250001",
            ProviderOrderRef = "provider-order-123",
            PaymentPageUrl = "https://pay.example.test/start"
        };

        result.ProviderOrderRef.Should().Be("provider-order-123");
        result.PaymentPageUrl.Should().StartWith("https://");
    }

    [Fact]
    public void Query_request_uses_neutral_provider_reference_instead_of_qpay_token_terms()
    {
        var request = new PaymentQueryRequest
        {
            ProfileName = "JesusTest",
            ProductOrderId = "F202606250001",
            ProviderOrderRef = "provider-order-123"
        };

        request.ProviderOrderRef.Should().Be("provider-order-123");
        typeof(PaymentQueryRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain("PaymentToken");
    }

    [Fact]
    public void Callback_request_is_http_neutral()
    {
        var request = new PaymentCallbackRequest
        {
            ProfileName = "MyPayProduction",
            ProviderHint = PaymentProviderKind.MyPay,
            HttpMethod = "POST",
            ContentType = "application/x-www-form-urlencoded",
            RawBody = "order_id=F1&prc=250",
            Query = new Dictionary<string, string>(),
            Form = new Dictionary<string, string> { ["order_id"] = "F1", ["prc"] = "250" },
            Headers = new Dictionary<string, string> { ["User-Agent"] = "provider" }
        };

        request.Form["prc"].Should().Be("250");
        typeof(PaymentCallbackRequest)
            .GetProperties()
            .Select(property => property.PropertyType.FullName ?? property.PropertyType.Name)
            .Should()
            .NotContain(name => name.Contains("HttpRequest", StringComparison.Ordinal));
    }

    [Fact]
    public void Callback_result_carries_neutral_identifiers_and_acknowledgement()
    {
        var result = new PaymentCallbackResult
        {
            Status = PaymentStatus.Succeeded,
            ProductOrderId = "F202606250001",
            ProviderTransactionId = "provider-tx-123",
            Amount = 1200m,
            Currency = "TWD",
            Acknowledgement = PaymentCallbackAcknowledgement.PlainText("8888")
        };

        result.ProductOrderId.Should().Be("F202606250001");
        result.ProviderTransactionId.Should().Be("provider-tx-123");
        result.Acknowledgement.Kind.Should().Be(PaymentAckKind.PlainText);
    }

    [Fact]
    public void Acknowledgement_can_describe_provider_response_shape()
    {
        PaymentCallbackAcknowledgement.PlainText("8888").Kind.Should().Be(PaymentAckKind.PlainText);
        PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}").Kind.Should().Be(PaymentAckKind.Json);
        PaymentCallbackAcknowledgement.Redirect("https://example.test/success").Kind.Should().Be(PaymentAckKind.Redirect);
        PaymentCallbackAcknowledgement.None.Kind.Should().Be(PaymentAckKind.None);
    }
}
