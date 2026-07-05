// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/PaymentNotificationRetryKeyTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentNotificationRetryKeyTests
// 主要成員：BuildPaymentLineRetryKey_uses_order_id_when_available、BuildPaymentLineRetryKey_falls_back_to_product_order_id、BuildPaymentLineRetryKey_returns_null_without_stable_identifier、BuildPaymentLineRetryKey_normalizes_empty_status_to_unknown、BuildPaymentLineRetryKey_does_not_include_sensitive_or_personal_data
// 引用命名空間：ChurchReport.Services、FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
