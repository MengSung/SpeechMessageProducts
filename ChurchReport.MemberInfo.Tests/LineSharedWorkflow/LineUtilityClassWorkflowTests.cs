using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using ToolUtilityNameSpace;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

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
    public async Task SendMessage_with_sdk_messages_uses_legacy_line_client_when_workflow_is_not_provided()
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

    private static LineUtilityClass CreateLineUtility(
        HttpClient httpClient,
        ILineNotificationWorkflow? lineNotificationWorkflow,
        List<(string UserId, string Subject, string Message)>? pushStatisticCalls = null)
    {
        var validFlag = true;
        var toolUtility = new ToolUtilityClass(ref validFlag);
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.test/v2");

        return new TestLineUtility(toolUtility, lineClient, lineNotificationWorkflow, pushStatisticCalls);
    }

    private sealed class TestLineUtility : LineUtilityClass
    {
        public TestLineUtility(
            ToolUtilityClass toolUtility,
            LineMessagingClient lineClient,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            List<(string UserId, string Subject, string Message)>? pushStatisticCalls)
            : base(
                toolUtility,
                lineClient,
                lineNotificationWorkflow,
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
