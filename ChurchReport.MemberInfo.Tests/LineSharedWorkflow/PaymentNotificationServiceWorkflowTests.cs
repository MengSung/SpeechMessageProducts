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
