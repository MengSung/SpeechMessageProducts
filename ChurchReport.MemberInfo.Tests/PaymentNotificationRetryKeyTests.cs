/*
 * 檔案整體說明
 *
 * 目的與定位：保護付款通知重試鍵使用穩定訂單識別、無識別時不偽造鍵，並排除敏感及個人資料。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentNotificationRetryKeyTests。
 * 測試案例入口包含 BuildPaymentLineRetryKey_uses_order_id_when_available、
 * BuildPaymentLineRetryKey_falls_back_to_product_order_id、
 * BuildPaymentLineRetryKey_returns_null_without_stable_identifier、
 * BuildPaymentLineRetryKey_normalizes_empty_status_to_unknown、
 * BuildPaymentLineRetryKey_does_not_include_sensitive_or_personal_data。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
