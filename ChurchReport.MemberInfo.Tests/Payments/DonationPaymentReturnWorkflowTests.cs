using ChurchReport.Payments;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentReturnWorkflowTests
{
    [Fact]
    public void HandleReturn_dispatches_fee_category_to_fee_workflow()
    {
        var dispatcher = new RecordingDonationPaymentProductWorkflowDispatcher();
        var workflow = new DonationPaymentReturnWorkflow(dispatcher);
        var statusResult = CreateStatusResult("fee");

        var result = workflow.HandleReturn("NA0149_001", "PAYTOKEN", statusResult);

        dispatcher.FeeCallCount.Should().Be(1);
        dispatcher.DedicationBookingCallCount.Should().Be(0);
        dispatcher.LastWorkflowResult.Should().NotBeNull();
        dispatcher.LastWorkflowResult!.OrderNo.Should().Be("C202606260001");
        dispatcher.LastWorkflowResult.ProviderTransactionId.Should().Be("TS123");
        dispatcher.LastWorkflowResult.Amount.Should().Be(1200m);
        dispatcher.LastWorkflowResult.AmountMinorUnits.Should().Be("120000");
        dispatcher.LastWorkflowResult.PaymentOrganization.Should().Be("Jesus");
        dispatcher.LastWorkflowResult.ProductEntityId.Should().Be("fee-id");
        dispatcher.LastWorkflowResult.PaymentCategory.Should().Be("fee");
        dispatcher.LastWorkflowResult.PayType.Should().Be("C");
        dispatcher.LastWorkflowResult.Status.Should().Be("S");
        dispatcher.LastWorkflowResult.Description.Should().Be("S00000");
        dispatcher.LastWorkflowResult.LeftCCNo.Should().Be("1234");
        dispatcher.LastWorkflowResult.RightCCNo.Should().Be("5678");
        dispatcher.LastWorkflowResult.CCExpDate.Should().Be("1228");
        dispatcher.LastWorkflowResult.CCToken.Should().Be("cc-token");
        result.Should().BeSameAs(dispatcher.FeeResult);
    }

    [Fact]
    public void HandleReturn_dispatches_dedication_category_to_dedication_workflow()
    {
        var dispatcher = new RecordingDonationPaymentProductWorkflowDispatcher();
        var workflow = new DonationPaymentReturnWorkflow(dispatcher);
        var statusResult = CreateStatusResult("dedication_booking");

        var result = workflow.HandleReturn("NA0149_001", "PAYTOKEN", statusResult);

        dispatcher.FeeCallCount.Should().Be(0);
        dispatcher.DedicationBookingCallCount.Should().Be(1);
        dispatcher.LastWorkflowResult.Should().NotBeNull();
        dispatcher.LastWorkflowResult!.OrderNo.Should().Be("C202606260001");
        dispatcher.LastWorkflowResult.ProductEntityId.Should().Be("fee-id");
        dispatcher.LastWorkflowResult.PaymentCategory.Should().Be("dedication_booking");
        result.Should().BeSameAs(dispatcher.DedicationBookingResult);
    }

    private static PaymentStatusResult CreateStatusResult(string paymentCategory)
    {
        return new PaymentStatusResult
        {
            Status = PaymentStatus.Succeeded,
            ProductOrderId = "C202606260001",
            ProviderOrderRef = "PAYTOKEN",
            ProviderTransactionId = "TS123",
            Amount = 1200m,
            ProviderData = new Dictionary<string, string>
            {
                ["product_entity_id"] = "fee-id",
                ["payment_organization"] = "Jesus",
                ["payment_category"] = paymentCategory,
                ["provider_message"] = "S00000",
                ["pay_type"] = "C",
                ["left_cc_no"] = "1234",
                ["right_cc_no"] = "5678",
                ["cc_exp_date"] = "1228",
                ["cc_token"] = "cc-token"
            }
        };
    }

    private sealed class RecordingDonationPaymentProductWorkflowDispatcher : IDonationPaymentProductWorkflowDispatcher
    {
        public IActionResult FeeResult { get; } = new ViewResult { ViewName = "fee" };
        public IActionResult DedicationBookingResult { get; } = new ViewResult { ViewName = "dedication" };
        public int FeeCallCount { get; private set; }
        public int DedicationBookingCallCount { get; private set; }
        public DonationPaymentWorkflowResult? LastWorkflowResult { get; private set; }

        public IActionResult HandleFeeReturn(
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult paymentResult)
        {
            FeeCallCount++;
            LastWorkflowResult = paymentResult;
            return FeeResult;
        }

        public IActionResult HandleDedicationBookingReturn(
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult paymentResult)
        {
            DedicationBookingCallCount++;
            LastWorkflowResult = paymentResult;
            return DedicationBookingResult;
        }
    }
}
