using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>
/// 驗證 <see cref="PushUtility"/> 在導入共用 LINE workflow 後仍維持舊產品語意。
///
/// RichMenu 相關測試特別鎖住 ChurchReport 舊版授權選單的 menu key，
/// 確保產品端不再直接建立、上傳、刪除 RichMenu，而是委派給共用 assignment workflow。
/// </summary>
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
    public async Task SendMessage_with_sdk_messages_uses_shared_workflow_for_best_effort_path()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);
        var messages = new List<ISendMessage> { new TextMessage("sdk best effort") };

        await utility.SendMessage("Uuser", messages);

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uuser");
        workflow.Requests[0].Content.SdkMessages.Should().BeSameAs(messages);
        workflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.PushUtility.BestEffortSdkMessages");
    }

    [Fact]
    public async Task SendMessage_with_sdk_messages_swallows_workflow_failure_for_legacy_best_effort_behavior()
    {
        var workflow = new CapturingWorkflow
        {
            SendAsyncResultFactory = request => LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ProviderRejected,
                "line-provider-rejected",
                "LINE rejected the best-effort SDK message")
        };
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);
        var messages = new List<ISendMessage> { new TextMessage("sdk best effort") };

        var action = () => utility.SendMessage("Uuser", messages);

        await action.Should().NotThrowAsync();
        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Content.SdkMessages.Should().BeSameAs(messages);
    }

    [Fact]
    public async Task SendImage_uses_shared_workflow_for_best_effort_image_message()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);

        await utility.SendImage("Uuser", "https://example.test/original.png", "https://example.test/preview.png");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uuser");
        workflow.Requests[0].Content.SdkMessages.Should().NotBeNull();
        workflow.Requests[0].Content.SdkMessages![0].Should().BeOfType<ImageMessage>();
        workflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.PushUtility.SendImage");
    }

    [Fact]
    public async Task SendImage_uses_legacy_line_client_when_workflow_is_not_provided()
    {
        var handler = new RecordingLineHandler();
        using var httpClient = new HttpClient(handler);
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"));

        await utility.SendImage("Uuser", "https://example.test/original.png", "https://example.test/preview.png");

        handler.RequestUri.Should().Be("https://api.line.me/v2/bot/message/push");
        handler.AuthorizationHeader.Should().Be("Bearer test-token");
        handler.RequestBody.Should().Contain("\"to\":\"Uuser\"");
        handler.RequestBody.Should().Contain("\"type\":\"image\"");
        handler.RequestBody.Should().Contain("\"originalContentUrl\":\"https://example.test/original.png\"");
        handler.RequestBody.Should().Contain("\"previewImageUrl\":\"https://example.test/preview.png\"");
    }

    [Fact]
    public async Task Safe_best_effort_sdk_methods_use_matching_workflow_sources()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);
        var templateActions = new List<ITemplateAction>
        {
            new MessageTemplateAction("OK", "ok")
        };
        var confirmActions = new List<ITemplateAction>
        {
            new MessageTemplateAction("Yes", "yes"),
            new MessageTemplateAction("No", "no")
        };
        var imagemapActions = new List<IImagemapAction>
        {
            new MessageImagemapAction(new ImagemapArea(0, 0, 100, 100), "tap")
        };

        await utility.SendVideo("Uuser", "https://example.test/video.mp4", "https://example.test/preview.jpg");
        await utility.SendAudeo("Uuser", "https://example.test/audio.m4a", 1000);
        await utility.SendLocation("Uuser", "title", "address", 25.0m, 121.0m);
        await utility.SendSticker("Uuser", 1, 1);
        await utility.PostSerializedTemplate("Uuser", "alt", "https://example.test/thumb.jpg", "title", "text", templateActions);
        await utility.PostSerializedConfirm("Uuser", "alt", "confirm", confirmActions);
        await utility.PostSerializedImageMap("Uuser", "alt", "https://example.test/imagemap", 1040, 1040, imagemapActions);

        workflow.Requests.Select(request => request.Metadata["source"]).Should().Equal(
            "ChurchReport.PushUtility.SendVideo",
            "ChurchReport.PushUtility.SendAudio",
            "ChurchReport.PushUtility.SendLocation",
            "ChurchReport.PushUtility.SendSticker",
            "ChurchReport.PushUtility.PostSerializedTemplate",
            "ChurchReport.PushUtility.PostSerializedConfirm",
            "ChurchReport.PushUtility.PostSerializedImageMap");
        workflow.Requests.Should().OnlyContain(request => request.Recipient.PrimaryId == "Uuser");
        workflow.Requests.Should().OnlyContain(request => request.Content.SdkMessages != null && request.Content.SdkMessages.Count == 1);
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

    [Fact]
    public async Task SendReliableMessageAsync_uses_shared_workflow_with_retry_key_and_propagates_failure()
    {
        var workflow = new CapturingWorkflow
        {
            SendOrThrowExceptionFactory = request => new LineNotificationException(
                LineNotificationResult.Failure(
                    request,
                    LineNotificationStatus.ProviderUnavailable,
                    "line-provider-timeout",
                    "LINE retryable send failed"))
        };
        using var httpClient = new HttpClient(new NoopHttpMessageHandler());
        var utility = new PushUtility(new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"), workflow);

        var action = () => utility.SendReliableMessageAsync("Uuser", "required reliable", "retry-payment-001");

        var exception = await action.Should().ThrowAsync<LineNotificationException>();
        exception.Which.Result.Status.Should().Be(LineNotificationStatus.ProviderUnavailable);
        exception.Which.Result.ErrorCode.Should().Be("line-provider-timeout");
        exception.Which.Result.ErrorMessage.Should().Be("LINE retryable send failed");
        exception.Which.Result.RetryKey.Should().Be("retry-payment-001");
        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uuser");
        workflow.Requests[0].Content.Text.Should().Be("required reliable");
        workflow.Requests[0].RetryKey.Should().Be("retry-payment-001");
    }

    /// <summary>
    /// AddRichMenuMessage 應透過共用 assignment workflow 指派舊版授權選單。
    ///
    /// 這個測試保護遷移後的邊界：ChurchReport 仍保留原方法名稱與通知行為，
    /// 但 RichMenu 綁定動作改由 menu key <c>legacy-auth</c> 交給共用層解析。
    /// </summary>
    [Fact]
    public async Task AddRichMenuMessage_assigns_legacy_auth_menu_through_shared_assignment_workflow()
    {
        var notificationWorkflow = new CapturingWorkflow();
        var assignmentWorkflow = new CapturingRichMenuAssignmentWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var utility = new PushUtility(
            new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"),
            notificationWorkflow,
            lineRichMenuWorkflow: null,
            assignmentWorkflow);

        await utility.AddRichMenuMessage("Uuser");

        assignmentWorkflow.Assignments.Should().ContainSingle();
        assignmentWorkflow.Assignments[0].UserId.Should().Be("Uuser");
        assignmentWorkflow.Assignments[0].MenuKey.Should().Be("legacy-auth");
        notificationWorkflow.Requests.Should().ContainSingle();
        notificationWorkflow.Requests[0].Metadata["source"].Should().Be("ChurchReport.PushUtility.AddRichMenuMessage");
    }

    /// <summary>
    /// DeleteRichMenuMessage 應透過共用 assignment workflow 解除使用者 RichMenu 綁定。
    ///
    /// 舊實作會取得使用者目前 richMenuId 後直接刪除 provider 資源；
    /// 新流程只負責 unlink 使用者，避免產品端誤刪仍被其他使用者或環境共用的 RichMenu。
    /// </summary>
    [Fact]
    public async Task DeleteRichMenuMessage_unassigns_through_shared_assignment_workflow()
    {
        var assignmentWorkflow = new CapturingRichMenuAssignmentWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var utility = new PushUtility(
            new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2"),
            null,
            lineRichMenuWorkflow: null,
            assignmentWorkflow);

        await utility.DeleteRichMenuMessage("Uuser");

        assignmentWorkflow.Unassignments.Should().ContainSingle();
        assignmentWorkflow.Unassignments[0].Should().Be("Uuser");
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

    /// <summary>
    /// 捕捉舊版 create/upload/link workflow 請求的測試替身。
    ///
    /// 目前 PushUtility 的 RichMenu 指派已改走 assignment workflow；
    /// 保留這個替身是為了覆蓋其他仍接受舊 workflow 相依性的建構路徑，
    /// 並確保測試不需要真的呼叫 LINE 建立或刪除 RichMenu。
    /// </summary>
    private sealed class CapturingRichMenuWorkflow : ILineRichMenuWorkflow
    {
        public List<LineRichMenuCreateUploadAndLinkRequest> CreateRequests { get; } = new();

        public List<LineRichMenuDeleteLinkedRequest> DeleteRequests { get; } = new();

        public Task<LineRichMenuResult> CreateUploadAndLinkAsync(LineRichMenuCreateUploadAndLinkRequest request)
        {
            CreateRequests.Add(request);
            return Task.FromResult(LineRichMenuResult.Success(request.UserId, "rich-menu-test", request.Metadata));
        }

        public Task CreateUploadAndLinkOrThrowAsync(LineRichMenuCreateUploadAndLinkRequest request)
        {
            CreateRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task<LineRichMenuResult> DeleteLinkedRichMenuAsync(LineRichMenuDeleteLinkedRequest request)
        {
            DeleteRequests.Add(request);
            return Task.FromResult(LineRichMenuResult.Success(request.UserId, "rich-menu-test", request.Metadata));
        }

        public Task DeleteLinkedRichMenuOrThrowAsync(LineRichMenuDeleteLinkedRequest request)
        {
            DeleteRequests.Add(request);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 記錄 RichMenu 指派與解除指派請求的測試替身。
    ///
    /// 測試只關心 PushUtility 是否傳入正確的 lineUserId 與 menu key，
    /// 因此這裡直接回傳成功結果，避免把共用 workflow 本身的行為混進產品整合測試。
    /// </summary>
    private sealed class CapturingRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWorkflow
    {
        public List<(string UserId, string MenuKey)> Assignments { get; } = new();

        public List<string> Unassignments { get; } = new();

        public Task<LineRichMenuAssignmentResult> AssignAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default)
        {
            Assignments.Add((lineUserId, menuKey));
            return Task.FromResult(LineRichMenuAssignmentResult.Linked(null, menuKey, "rich-menu-test", changed: true));
        }

        public Task AssignOrThrowAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default)
        {
            Assignments.Add((lineUserId, menuKey));
            return Task.CompletedTask;
        }

        public Task<LineRichMenuAssignmentResult> UnassignAsync(string lineUserId, CancellationToken cancellationToken = default)
        {
            Unassignments.Add(lineUserId);
            return Task.FromResult(LineRichMenuAssignmentResult.Unlinked("legacy-auth", changed: true));
        }

        public Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default)
        {
            Unassignments.Add(lineUserId);
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

    private sealed class RecordingLineHandler : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        public string? AuthorizationHeader { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            RequestBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The test should use ILineNotificationWorkflow, not real HTTP.");
        }
    }
}

