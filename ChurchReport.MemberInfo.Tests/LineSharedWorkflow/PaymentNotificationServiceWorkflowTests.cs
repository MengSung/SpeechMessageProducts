/*
 * 檔案整體說明
 *
 * 目的與定位：以可記錄的workflow替身確認付款通知保留retry key，且共用流程拒絕時向呼叫端傳遞失敗。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentNotificationServiceWorkflowTests、CapturingWorkflow。
 * 測試案例入口包含 SendLineMessage_uses_shared_workflow_with_retry_key、
 * SendLineMessage_throws_when_shared_workflow_rejects_notification、SendAsync、SendOrThrowAsync。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
