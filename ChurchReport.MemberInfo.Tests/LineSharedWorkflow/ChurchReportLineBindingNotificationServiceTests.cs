using ChurchReport.Services;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

using LineUserProfile = Line.Messaging.UserProfile;

public sealed class ChurchReportLineBindingNotificationServiceTests
{
    [Fact]
    public void BuildBindingUrl_keeps_legacy_encoded_route_shape()
    {
        var url = ChurchReportLineBindingNotificationService.BuildBindingUrl("王小明", "U abc/123");

        url.Should().Be("https://tpehoc.speechmessage.com.tw:200/Home/LineBindingView/%E7%8E%8B%E5%B0%8F%E6%98%8E,U+abc%2F123");
    }

    [Fact]
    public async Task NotifyLineBindingAsync_reads_profile_and_sends_churchreport_message_through_workflow()
    {
        var profileProvider = new FakeProfileProvider("王小明");
        var workflow = new CapturingWorkflow();
        var service = new ChurchReportLineBindingNotificationService(profileProvider, workflow);

        await service.NotifyLineBindingAsync("Uline");

        profileProvider.RequestedLineUserIds.Should().ContainSingle().Which.Should().Be("Uline");
        workflow.Requests.Should().ContainSingle();
        var request = workflow.Requests[0];
        request.Recipient.PrimaryId.Should().Be("Uline");
        request.Content.Text.Should().Be(
            "請點擊以下網址進行牧養系統與Line的註冊:" +
            Environment.NewLine +
            "https://tpehoc.speechmessage.com.tw:200/Home/LineBindingView/%E7%8E%8B%E5%B0%8F%E6%98%8E,Uline");
        request.Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.LineBindingNotification");
        request.Metadata.Should().ContainKey("bindingUrl")
            .WhoseValue.Should().Contain("/Home/LineBindingView/");
    }

    [Fact]
    public async Task SendBindingPromptAsync_propagates_workflow_failure()
    {
        var requestForFailure = new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("Uline"),
            Content = LineNotificationContent.TextMessage("failed")
        };
        var workflow = new CapturingWorkflow
        {
            SendOrThrowAsyncException = new LineNotificationException(
                LineNotificationResult.Failure(
                    requestForFailure,
                    LineNotificationStatus.ProviderRejected,
                    "line-provider-rejected",
                    "LINE rejected the binding prompt."))
        };
        var service = new ChurchReportLineBindingNotificationService(
            new FakeProfileProvider("王小明"),
            workflow);

        var action = () => service.SendBindingPromptAsync("Uline", "王小明");

        await action.Should().ThrowAsync<LineNotificationException>();
    }

    private sealed class FakeProfileProvider : IChurchReportLineProfileProvider
    {
        private readonly string _displayName;

        public FakeProfileProvider(string displayName)
        {
            _displayName = displayName;
        }

        public List<string> RequestedLineUserIds { get; } = new();

        public Task<LineUserProfile?> GetUserProfileAsync(string lineUserId, CancellationToken cancellationToken = default)
        {
            RequestedLineUserIds.Add(lineUserId);
            return Task.FromResult<LineUserProfile?>(new LineUserProfile
            {
                DisplayName = _displayName,
                UserId = lineUserId
            });
        }
    }

    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        public List<LineNotificationRequest> Requests { get; } = new();

        public Exception? SendOrThrowAsyncException { get; set; }

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(LineNotificationResult.Success(request));
        }

        public Task SendOrThrowAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            if (SendOrThrowAsyncException != null)
            {
                throw SendOrThrowAsyncException;
            }

            return Task.CompletedTask;
        }
    }
}
