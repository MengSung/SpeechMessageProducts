// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentReturnWorkflowTests、class RecordingDonationPaymentProductWorkflowDispatcher
// 主要成員：HandleReturn_dispatches_fee_category_to_fee_workflow、HandleReturn_dispatches_dedication_category_to_dedication_workflow、CreateStatusResult、HandleFeeReturn、HandleDedicationBookingReturn、FeeResult、DedicationBookingResult、FeeCallCount、DedicationBookingCallCount、LastWorkflowResult
// 引用命名空間：ChurchReport.Payments、FluentAssertions、Microsoft.AspNetCore.Mvc、SpeechMessage.Payments.Models、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Payments;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Models;
using System.Threading.Tasks;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentReturnWorkflowTests
{
    [Fact]
    public async Task HandleReturn_dispatches_fee_category_to_fee_workflow()
    {
        var dispatcher = new RecordingDonationPaymentProductWorkflowDispatcher();
        var workflow = new DonationPaymentReturnWorkflow(dispatcher);
        var statusResult = CreateStatusResult("fee");

        var result = await workflow.HandleReturnAsync("NA0149_001", "PAYTOKEN", statusResult);

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
    public async Task HandleReturn_dispatches_dedication_category_to_dedication_workflow()
    {
        var dispatcher = new RecordingDonationPaymentProductWorkflowDispatcher();
        var workflow = new DonationPaymentReturnWorkflow(dispatcher);
        var statusResult = CreateStatusResult("dedication_booking");

        var result = await workflow.HandleReturnAsync("NA0149_001", "PAYTOKEN", statusResult);

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

        public Task<IActionResult> HandleFeeReturnAsync(
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult paymentResult)
        {
            FeeCallCount++;
            LastWorkflowResult = paymentResult;
            return Task.FromResult(FeeResult);
        }

        public Task<IActionResult> HandleDedicationBookingReturnAsync(
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult paymentResult)
        {
            DedicationBookingCallCount++;
            LastWorkflowResult = paymentResult;
            return Task.FromResult(DedicationBookingResult);
        }
    }
}
