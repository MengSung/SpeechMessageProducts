using ChurchReport.Tools;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 付款完成後的產品流程處理器已改用中性命名。
/// 這些類別負責 CRM 收費單更新、定期定額認獻單更新、LINE 通知與結果頁面，
/// 因此它們留在 ChurchReport 產品層，不放進可重用的金流核心。
/// </summary>
public sealed class DonationPaymentReturnProcessorNamingTests
{
    [Fact]
    public void New_donation_fee_payment_processor_exists_as_primary_fee_return_processor()
    {
        var processorType = Type.GetType("ChurchReport.Tools.DonationFeePaymentProcessor, ChurchReport");

        processorType.Should().NotBeNull("收費單付款完成後流程應使用 DonationFeePaymentProcessor 作為主要名稱");
        processorType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    [Fact]
    public void Legacy_qpay_fee_processor_remains_as_compatibility_alias()
    {
        var processorType = Type.GetType("ChurchReport.Tools.DonationFeePaymentProcessor, ChurchReport");
        var legacyType = typeof(QPayFeeProcessor);

        processorType.Should().NotBeNull();
        legacyType.Should().BeAssignableTo(processorType!,
            "QPayFeeProcessor 只能作為舊呼叫端的薄相容包裝，實際流程應轉交給 DonationFeePaymentProcessor");
    }

    [Fact]
    public void New_recurring_donation_payment_processor_exists_as_primary_recurring_return_processor()
    {
        var processorType = Type.GetType("ChurchReport.Tools.RecurringDonationPaymentProcessor, ChurchReport");

        processorType.Should().NotBeNull("定期定額奉獻付款完成後流程應使用 RecurringDonationPaymentProcessor 作為主要名稱");
        processorType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    [Fact]
    public void Legacy_qpay_dedication_booking_processor_remains_as_compatibility_alias()
    {
        var processorType = Type.GetType("ChurchReport.Tools.RecurringDonationPaymentProcessor, ChurchReport");
        var legacyType = typeof(QPayDedicationBookingProcessor);

        processorType.Should().NotBeNull();
        legacyType.Should().BeAssignableTo(processorType!,
            "QPayDedicationBookingProcessor 只能作為舊呼叫端的薄相容包裝，實際流程應轉交給 RecurringDonationPaymentProcessor");
    }

    [Fact]
    public void New_payment_result_helper_and_debug_logger_exist()
    {
        Type.GetType("ChurchReport.Tools.DonationPaymentResultHelper, ChurchReport")
            .Should().NotBeNull("付款結果判斷不應再以 QPay 命名作為主要入口");
        Type.GetType("ChurchReport.Tools.DonationPaymentDebugLogger, ChurchReport")
            .Should().NotBeNull("付款除錯記錄不應再以 QPay 命名作為主要入口");
    }
}
