/*
 * 檔案整體說明
 *
 * 目的與定位：透過profile及通知workflow替身檢查綁定URL形狀、產品訊息及終端錯誤傳遞，保護舊綁定行為與共用流程邊界。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchReportLineBindingNotificationServiceTests、FakeProfileProvider、CapturingWorkflow。
 * 測試案例入口包含 BuildBindingUrl_keeps_legacy_encoded_route_shape、
 * NotifyLineBindingAsync_reads_profile_and_sends_churchreport_message_through_workflow、
 * SendBindingPromptAsync_propagates_workflow_failure、GetUserProfileAsync、SendAsync、SendOrThrowAsync。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
