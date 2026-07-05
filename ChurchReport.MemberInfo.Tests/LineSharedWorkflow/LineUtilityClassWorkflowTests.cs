// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineUtilityClassWorkflowTests、class TestLineUtility、class CapturingWorkflow、class CapturingReplyWorkflow、class RecordingLineHandler、class ThrowingHttpMessageHandler
// 主要成員：SendMessage_with_sdk_messages_uses_shared_workflow_when_workflow_is_provided、SendMessage_with_sdk_messages_uses_default_shared_workflow_when_workflow_is_not_provided、Safe_best_effort_push_methods_use_shared_workflow_and_keep_product_statistics、MultiCastTextMessageAsync_splits_recipients_through_workflow_when_workflow_is_provided、SendMessage_sync_uses_shared_workflow_when_workflow_is_provided、ReplyTextMessage_uses_shared_reply_workflow_when_workflow_is_provided、ReplyImage_uses_shared_reply_workflow_when_workflow_is_provided、CreateLineUtility、SendAsync、SendOrThrowAsync
// 引用命名空間：ChurchReport.Tools、FluentAssertions、Line.Messaging、LineMessagingProcessor.RichMenus、LineMessagingProcessor.Workflows、ToolUtilityNameSpace、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;
using ToolUtilityNameSpace;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>
/// 驗證 <see cref="LineUtilityClass"/> 與共用 LINE workflow 的整合邊界。
///
/// 這個測試類保留舊工具類的建構方式，同時讓通知、回覆與 RichMenu assignment
/// 都可以被注入測試替身；如此可確認產品工具類只負責轉接，不直接碰 LINE RichMenu provider。
/// </summary>
public sealed class LineUtilityClassWorkflowTests
{
    private const string LineUtilitySubjectPrefix = "\u004C\u0069\u006E\u0065\u63A8\u64AD\u7D71\u8A08:";

    [Fact]
    public async Task SendMessage_with_sdk_messages_uses_shared_workflow_when_workflow_is_provided()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        using var utility = CreateLineUtility(httpClient, workflow);
        var messages = new List<ISendMessage> { new TextMessage("line utility sdk") };

        await utility.SendMessage("Uuser", messages);

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uuser");
        workflow.Requests[0].Content.SdkMessages.Should().BeSameAs(messages);
        workflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.LineUtilityClass.BestEffortSdkMessages");
    }

    [Fact]
    public async Task SendMessage_with_sdk_messages_uses_default_shared_workflow_when_workflow_is_not_provided()
    {
        var handler = new RecordingLineHandler();
        using var httpClient = new HttpClient(handler);
        using var utility = CreateLineUtility(httpClient, lineNotificationWorkflow: null);
        var messages = new List<ISendMessage> { new TextMessage("legacy fallback") };

        await utility.SendMessage("Uuser", messages);

        handler.RequestUri.Should().Be("https://api.line.test/v2/bot/message/push");
        handler.AuthorizationHeader.Should().Be("Bearer test-token");
        handler.RequestBody.Should().Contain("\"to\":\"Uuser\"");
        handler.RequestBody.Should().Contain("\"type\":\"text\"");
        handler.RequestBody.Should().Contain("\"text\":\"legacy fallback\"");
    }

    [Fact]
    public async Task Safe_best_effort_push_methods_use_shared_workflow_and_keep_product_statistics()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var statistics = new List<(string UserId, string Subject, string Message)>();
        using var utility = CreateLineUtility(httpClient, workflow, statistics);
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
        var flexMessage = FlexMessage.CreateBubbleMessage("alt text");

        await utility.SendMessageAsync("Uuser", "text");
        await utility.SendImage("Uuser", "https://example.test/original.png", "https://example.test/preview.png");
        await utility.SendVideo("Uuser", "https://example.test/video.mp4", "https://example.test/preview.jpg");
        await utility.SendAudeo("Uuser", "https://example.test/audio.m4a", 1000);
        await utility.SendLocation("Uuser", "title", "address", 25.0m, 121.0m);
        await utility.SendSticker("Uuser", 1, 1);
        await utility.PostSerializedTemplate("Uuser", "alt", "https://example.test/thumb.jpg", "title", "text", templateActions);
        await utility.PostSerializedFlex("Uuser", flexMessage);
        await utility.PostSerializedConfirm("Uuser", "alt", "confirm", confirmActions);
        await utility.PostSerializedImageMap("Uuser", "alt", "https://example.test/imagemap", 1040, 1040, imagemapActions);

        workflow.Requests.Select(request => request.Metadata["source"]).Should().Equal(
            "ChurchReport.LineUtilityClass.SendMessageAsync",
            "ChurchReport.LineUtilityClass.SendImage",
            "ChurchReport.LineUtilityClass.SendVideo",
            "ChurchReport.LineUtilityClass.SendAudio",
            "ChurchReport.LineUtilityClass.SendLocation",
            "ChurchReport.LineUtilityClass.SendSticker",
            "ChurchReport.LineUtilityClass.PostSerializedTemplate",
            "ChurchReport.LineUtilityClass.PostSerializedFlex",
            "ChurchReport.LineUtilityClass.PostSerializedConfirm",
            "ChurchReport.LineUtilityClass.PostSerializedImageMap");
        workflow.Requests.Should().OnlyContain(request => request.Recipient.PrimaryId == "Uuser");
        workflow.Requests.Should().OnlyContain(request => request.Content.SdkMessages != null && request.Content.SdkMessages.Count == 1);

        statistics.Select(call => call.Subject).Should().Equal(
            LineUtilitySubjectPrefix + "\u6587\u5B57",
            LineUtilitySubjectPrefix + "\u5716\u7247",
            LineUtilitySubjectPrefix + "\u5F71\u7247",
            LineUtilitySubjectPrefix + "\u8072\u97F3",
            LineUtilitySubjectPrefix + "\u5EA7\u6A19",
            LineUtilitySubjectPrefix + "\u8CBC\u5716",
            LineUtilitySubjectPrefix + "Template",
            LineUtilitySubjectPrefix + "Flex",
            LineUtilitySubjectPrefix + "Confirm",
            LineUtilitySubjectPrefix + "ImageMap");
    }

    [Fact]
    public async Task MultiCastTextMessageAsync_splits_recipients_through_workflow_when_workflow_is_provided()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        using var utility = CreateLineUtility(httpClient, workflow);

        await utility.MultiCastTextMessageAsync(new[] { "Uone", "Utwo" }, "broadcast");

        workflow.Requests.Should().HaveCount(2);
        workflow.Requests.Select(request => request.Recipient.PrimaryId).Should().Equal("Uone", "Utwo");
        workflow.Requests.Should().OnlyContain(request =>
            request.Metadata["source"] == "ChurchReport.LineUtilityClass.MultiCastTextMessageAsync" &&
            request.Metadata["deliveryMode"] == "multicast-split");
        workflow.Requests.Should().OnlyContain(request =>
            request.Content.SdkMessages != null &&
            request.Content.SdkMessages.Count == 1);
    }

    [Fact]
    public void SendMessage_sync_uses_shared_workflow_when_workflow_is_provided()
    {
        var workflow = new CapturingWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var statistics = new List<(string UserId, string Subject, string Message)>();
        using var utility = CreateLineUtility(httpClient, workflow, statistics);

        utility.SendMessage("Uuser", "sync text");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uuser");
        workflow.Requests[0].Content.SdkMessages.Should().NotBeNull();
        workflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.LineUtilityClass.SendMessage.Sync");
        statistics.Should().ContainSingle();
    }

    [Fact]
    public async Task ReplyTextMessage_uses_shared_reply_workflow_when_workflow_is_provided()
    {
        var replyWorkflow = new CapturingReplyWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        using var utility = CreateLineUtility(
            httpClient,
            lineNotificationWorkflow: null,
            pushStatisticCalls: null,
            lineReplyWorkflow: replyWorkflow);

        await utility.ReplyTextMessage("reply-token", "reply text");

        replyWorkflow.Requests.Should().ContainSingle();
        replyWorkflow.Requests[0].ReplyToken.Should().Be("reply-token");
        replyWorkflow.Requests[0].Messages.Should().ContainSingle()
            .Which.Should().BeOfType<TextMessage>();
        replyWorkflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.ReplyUtility.ReplyMessageAsync");
    }

    [Fact]
    public async Task ReplyImage_uses_shared_reply_workflow_when_workflow_is_provided()
    {
        var replyWorkflow = new CapturingReplyWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        using var utility = CreateLineUtility(
            httpClient,
            lineNotificationWorkflow: null,
            pushStatisticCalls: null,
            lineReplyWorkflow: replyWorkflow);

        await utility.ReplyImage(
            "reply-token",
            "https://example.test/original.png",
            "https://example.test/preview.png");

        replyWorkflow.Requests.Should().ContainSingle();
        replyWorkflow.Requests[0].ReplyToken.Should().Be("reply-token");
        replyWorkflow.Requests[0].Messages.Should().ContainSingle()
            .Which.Should().BeOfType<ImageMessage>();
        replyWorkflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.ReplyUtility.ReplyMessage");
    }

    /// <summary>
    /// 建立可注入各種共用 workflow 的 LineUtilityClass 測試實例。
    ///
    /// <paramref name="lineRichMenuAssignmentWorkflow"/> 參數保留給 RichMenu 指派流程測試：
    /// 產品工具類只應把使用者與 menu key 傳入共用 workflow，不應在測試中真的建立或刪除 LINE RichMenu。
    /// </summary>
    private static LineUtilityClass CreateLineUtility(
        HttpClient httpClient,
        ILineNotificationWorkflow? lineNotificationWorkflow,
        List<(string UserId, string Subject, string Message)>? pushStatisticCalls = null,
        ILineReplyWorkflow? lineReplyWorkflow = null,
        ILineRichMenuAssignmentWorkflow? lineRichMenuAssignmentWorkflow = null)
    {
        var validFlag = true;
        var toolUtility = new ToolUtilityClass(ref validFlag);
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.test/v2");

        return new TestLineUtility(toolUtility, lineClient, lineNotificationWorkflow, lineReplyWorkflow, lineRichMenuAssignmentWorkflow, pushStatisticCalls);
    }

    /// <summary>
    /// 暴露受保護建構路徑的測試子類別。
    ///
    /// 這個子類別固定不注入舊版 create/upload/link RichMenu workflow，
    /// 讓測試能專注於新的 assignment workflow 相依性是否正確傳遞到基底類別。
    /// </summary>
    private sealed class TestLineUtility : LineUtilityClass
    {
        public TestLineUtility(
            ToolUtilityClass toolUtility,
            LineMessagingClient lineClient,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineReplyWorkflow? lineReplyWorkflow,
            ILineRichMenuAssignmentWorkflow? lineRichMenuAssignmentWorkflow,
            List<(string UserId, string Subject, string Message)>? pushStatisticCalls)
            : base(
                toolUtility,
                lineClient,
                lineNotificationWorkflow,
                lineReplyWorkflow,
                lineRichMenuWorkflow: null,
                lineRichMenuAssignmentWorkflow,
                (userId, subject, message) => pushStatisticCalls?.Add((userId, subject, message)))
        {
        }
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

    private sealed class CapturingReplyWorkflow : ILineReplyWorkflow
    {
        public List<LineReplyRequest> Requests { get; } = new();

        public Task<LineReplyResult> ReplyAsync(LineReplyRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(LineReplyResult.Success(request));
        }

        public Task ReplyOrThrowAsync(LineReplyRequest request)
        {
            Requests.Add(request);
            return Task.CompletedTask;
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
