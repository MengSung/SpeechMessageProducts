using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class PushUtilityWorkflowTests
{
    [Fact]
    public async Task SendMessage_uses_shared_workflow_when_workflow_is_provided()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new NoopHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);

        await utility.SendMessage("Uuser", "hello");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uuser");
        workflow.Requests[0].Content.Text.Should().Be("hello");
    }

    [Fact]
    public async Task SendMessage_swallows_workflow_failure_for_legacy_best_effort_behavior()
    {
        var workflow = new CapturingWorkflow
        {
            SendAsyncResultFactory = request => LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ProviderRejected,
                "line-provider-rejected",
                "LINE rejected the best-effort message")
        };
        using var httpClient = new HttpClient(new NoopHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);

        var action = () => utility.SendMessage("Uuser", "best effort");

        await action.Should().NotThrowAsync();
        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Content.Text.Should().Be("best effort");
    }

    [Fact]
    public async Task SendMessageOrThrowAsync_uses_shared_workflow_and_propagates_failure()
    {
        var workflow = new CapturingWorkflow
        {
            SendOrThrowExceptionFactory = request => new LineNotificationException(
                LineNotificationResult.Failure(
                    request,
                    LineNotificationStatus.ProviderRejected,
                    "line-provider-rejected",
                    "LINE rejected the required message"))
        };
        using var httpClient = new HttpClient(new NoopHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);

        var action = () => utility.SendMessageOrThrowAsync("Uuser", "required");

        await action.Should().ThrowAsync<LineNotificationException>();
        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uuser");
        workflow.Requests[0].Content.Text.Should().Be("required");
    }

    [Fact]
    public async Task SendMessagesOrThrowAsync_uses_shared_workflow_escape_hatch_for_required_sdk_messages()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new NoopHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);
        var messages = new List<ISendMessage> { new TextMessage("sdk") };

        await utility.SendMessagesOrThrowAsync("Uuser", messages);

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uuser");
        workflow.Requests[0].Content.SdkMessages.Should().BeSameAs(messages);
    }

    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        public List<LineNotificationRequest> Requests { get; } = new();

        public Func<LineNotificationRequest, LineNotificationResult>? SendAsyncResultFactory { get; set; }

        public Func<LineNotificationRequest, Exception>? SendOrThrowExceptionFactory { get; set; }

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(SendAsyncResultFactory?.Invoke(request) ?? LineNotificationResult.Success(request));
        }

        public Task SendOrThrowAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            if (SendOrThrowExceptionFactory != null)
            {
                throw SendOrThrowExceptionFactory(request);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NoopHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
