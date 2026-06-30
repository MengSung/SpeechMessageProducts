using ChurchReport.Tools;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 付款完成後的產品流程已改用中性命名。
/// 這些流程負責 CRM 更新、LINE 通知與畫面回應，屬於 ChurchReport 產品層，
/// 不應再用 QPay 這種永豐金流 provider 名稱作為相容入口。
/// </summary>
public sealed class DonationPaymentReturnProcessorNamingTests
{
    [Fact]
    public void New_donation_fee_payment_processor_exists_as_primary_fee_return_processor()
    {
        var processorType = Type.GetType("ChurchReport.Tools.DonationFeePaymentProcessor, ChurchReport");

        processorType.Should().NotBeNull("收費單付款完成流程應由 DonationFeePaymentProcessor 作為主要入口");
        processorType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    [Fact]
    public void New_recurring_donation_payment_processor_exists_as_primary_recurring_return_processor()
    {
        var processorType = Type.GetType("ChurchReport.Tools.RecurringDonationPaymentProcessor, ChurchReport");

        processorType.Should().NotBeNull("定期定額付款完成流程應由 RecurringDonationPaymentProcessor 作為主要入口");
        processorType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    [Fact]
    public void New_payment_result_helper_and_debug_logger_exist()
    {
        Type.GetType("ChurchReport.Tools.DonationPaymentResultHelper, ChurchReport")
            .Should().NotBeNull("付款結果判斷 helper 應使用中性命名");
        Type.GetType("ChurchReport.Tools.DonationPaymentDebugLogger, ChurchReport")
            .Should().NotBeNull("付款除錯記錄 helper 應使用中性命名");
    }

    [Fact]
    public void ChurchReport_assembly_does_not_expose_legacy_qpay_payment_workflow_wrappers()
    {
        var removedCompatibilityTypeNames = new[]
        {
            "ChurchReport.Tools.QPayDedicationBookingProcessor",
            "ChurchReport.Tools.QPayFeeProcessor",
            "ChurchReport.Tools.QPayPaymentDebugLogger",
            "ChurchReport.Tools.QPayPaymentResultHelper"
        };

        var assembly = typeof(DonationFeePaymentProcessor).Assembly;
        var existingCompatibilityTypes = removedCompatibilityTypeNames
            .Where(typeName => assembly.GetType(typeName, throwOnError: false) != null)
            .OrderBy(typeName => typeName)
            .ToArray();

        existingCompatibilityTypes.Should().BeEmpty(
            "金流流程已改用中性命名，ChurchReport 產品層不應再保留 QPay 舊名稱相容包裝");
    }
}
