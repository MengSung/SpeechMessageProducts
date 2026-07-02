using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public sealed class PaymentNotificationRetryKeyTests
{
    [Fact]
    public void BuildPaymentLineRetryKey_uses_order_id_when_available()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: "order-1001",
            productOrderId: "product-2002",
            status: "paid");

        key.Should().Be("churchreport:payment:order-1001:paid:payer-line-notice");
    }

    [Fact]
    public void BuildPaymentLineRetryKey_falls_back_to_product_order_id()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: " ",
            productOrderId: "product-2002",
            status: "paid");

        key.Should().Be("churchreport:payment:product-2002:paid:payer-line-notice");
    }

    [Fact]
    public void BuildPaymentLineRetryKey_returns_null_without_stable_identifier()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: null,
            productOrderId: " ",
            status: "paid");

        key.Should().BeNull();
    }

    [Fact]
    public void BuildPaymentLineRetryKey_normalizes_empty_status_to_unknown()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: "order-1001",
            productOrderId: "product-2002",
            status: " ");

        key.Should().Be("churchreport:payment:order-1001:unknown:payer-line-notice");
    }

    [Fact]
    public void BuildPaymentLineRetryKey_does_not_include_sensitive_or_personal_data()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: "order-1001",
            productOrderId: "product-2002",
            status: "paid");

        key.Should().NotContain("U1234567890abcdef");
        key.Should().NotContain("payer-name");
        key.Should().NotContain("card-token");
        key.Should().NotContain("payment received");
    }
}
