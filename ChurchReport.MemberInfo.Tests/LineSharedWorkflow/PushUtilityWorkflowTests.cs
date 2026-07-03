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

    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        public List<LineNotificationRequest> Requests { get; } = new();

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(LineNotificationResult.Success(request));
        }

        public Task SendOrThrowAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
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
