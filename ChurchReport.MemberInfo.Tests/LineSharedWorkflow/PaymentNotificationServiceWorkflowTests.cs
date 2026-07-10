// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentNotificationServiceWorkflowTests、class CapturingWorkflow
// 主要成員：SendLineMessage_uses_shared_workflow_with_retry_key、SendLineMessage_throws_when_shared_workflow_rejects_notification、SendAsync、SendOrThrowAsync、Requests、Result
// 引用命名空間：ChurchReport.Services、FluentAssertions、LineMessagingProcessor.Workflows、Microsoft.Extensions.Logging.Abstractions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Services;
using FluentAssertions;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class PaymentNotificationServiceWorkflowTests
{
    [Fact]
    public void SendLineMessage_uses_shared_workflow_with_retry_key()
    {
        var workflow = new CapturingWorkflow();
        var service = new PaymentNotificationService(
            NullLogger<PaymentNotificationService>.Instance,
            new PaymentMessageBuilder(),
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance),
            workflow);

        service.SendLineMessage("Udonor", "payment received", "retry-001");

        workflow.Requests.Should().ContainSingle();
        var request = workflow.Requests[0];
        request.Recipient.PrimaryId.Should().Be("Udonor");
        request.Content.Text.Should().Be("payment received");
        request.RetryKey.Should().Be("retry-001");
    }

    [Fact]
    public async Task SendLineMessageAsync_uses_shared_workflow_with_retry_key()
    {
        var workflow = new CapturingWorkflow();
        var service = new PaymentNotificationService(
            NullLogger<PaymentNotificationService>.Instance,
            new PaymentMessageBuilder(),
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance),
            workflow);

        await service.SendLineMessageAsync("Udonor", "payment received", "retry-001");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].RetryKey.Should().Be("retry-001");
    }

    [Fact]
    public void SendLineMessage_throws_when_shared_workflow_rejects_notification()
    {
        var workflow = new CapturingWorkflow
        {
            Result = LineNotificationResult.Failure(
                new LineNotificationRequest
                {
                    Recipient = LineNotificationRecipient.User(" "),
                    Content = LineNotificationContent.TextMessage("payment received")
                },
                LineNotificationStatus.ValidationFailed,
                "line-recipient-id-required",
                "Line notification recipient id is required.")
        };
        var service = new PaymentNotificationService(
            NullLogger<PaymentNotificationService>.Instance,
            new PaymentMessageBuilder(),
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance),
            workflow);

        var action = () => service.SendLineMessage(" ", "payment received", "retry-001");

        action.Should().Throw<LineNotificationException>()
            .Which.Result.ErrorCode.Should().Be("line-recipient-id-required");
    }

    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        public List<LineNotificationRequest> Requests { get; } = new();

        public LineNotificationResult? Result { get; set; }

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(Result ?? LineNotificationResult.Success(request));
        }

        public async Task SendOrThrowAsync(LineNotificationRequest request)
        {
            var result = await SendAsync(request);
            if (!result.Succeeded)
            {
                throw new LineNotificationException(result);
            }
        }
    }
}
