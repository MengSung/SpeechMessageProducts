/*
 * 檔案整體說明
 *
 * 目的與定位：以HTTP及reply workflow替身驗證群組／聊天室profile與回覆轉接，使用者來源不額外查profile，保留既有訊息語意。
 *
 * 主要功能與流程：
 * 本檔宣告 ReplyUtilityGroupRoomProfileAdapterTests、CapturingHttpMessageHandler、CapturingReplyWorkflow、
 * ThrowingHttpMessageHandler。
 * 測試案例入口包含 EchoAsyncProcessor_group_source_gets_profile_through_processor_and_replies_with_display_name、
 * EchoAsyncProcessor_room_source_gets_profile_through_processor_and_replies_with_display_name、
 * EchoAsyncProcessor_user_source_replies_without_group_or_room_profile_lookup、
 * ReplyMessageAsync_uses_reply_workflow_when_workflow_is_provided、
 * EchoAsyncProcessor_user_source_uses_reply_workflow_when_workflow_is_provided、
 * PostSerializedConfirm_uses_reply_workflow_when_workflow_is_provided、CreateTextEvent、SendAsync；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Net;
using System.Text;
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using Line.Messaging.Webhooks;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public sealed class ReplyUtilityGroupRoomProfileAdapterTests
{
    [Fact]
    public async Task EchoAsyncProcessor_group_source_gets_profile_through_processor_and_replies_with_display_name()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Group User","userId":"Ugroup","pictureUrl":"https://example.com/group.png","statusMessage":"group"}""");
        using var httpClient = new HttpClient(handler);
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor);
        var ev = CreateTextEvent(EventSourceType.Group, "G123", "Ugroup", "reply-token", "hello");

        await utility.EchoAsyncProcessor(ev);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/group/G123/member/Ugroup");
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/reply");

        var replyBody = JObject.Parse(handler.Bodies[1]);
        replyBody["replyToken"]!.Value<string>().Should().Be("reply-token");
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("Group User");
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("hello");
    }

    [Fact]
    public async Task EchoAsyncProcessor_room_source_gets_profile_through_processor_and_replies_with_display_name()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Room User","userId":"Uroom","pictureUrl":"https://example.com/room.png","statusMessage":"room"}""");
        using var httpClient = new HttpClient(handler);
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor);
        var ev = CreateTextEvent(EventSourceType.Room, "R123", "Uroom", "reply-token", "hello");

        await utility.EchoAsyncProcessor(ev);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/room/R123/member/Uroom");
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/reply");

        var replyBody = JObject.Parse(handler.Bodies[1]);
        replyBody["replyToken"]!.Value<string>().Should().Be("reply-token");
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("Room User");
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("hello");
    }

    [Fact]
    public async Task EchoAsyncProcessor_user_source_replies_without_group_or_room_profile_lookup()
    {
        var handler = new CapturingHttpMessageHandler("{}");
        using var httpClient = new HttpClient(handler);
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor);
        var ev = CreateTextEvent(EventSourceType.User, "Udirect", "Udirect", "reply-token", "hello");

        await utility.EchoAsyncProcessor(ev);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/reply");

        var replyBody = JObject.Parse(handler.Bodies[0]);
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("hello");
    }

    [Fact]
    public async Task ReplyMessageAsync_uses_reply_workflow_when_workflow_is_provided()
    {
        var workflow = new CapturingReplyWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor, workflow);

        await utility.ReplyMessageAsync("reply-token", "workflow reply");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].ReplyToken.Should().Be("reply-token");
        workflow.Requests[0].Messages.Should().ContainSingle()
            .Which.Should().BeOfType<TextMessage>();
        workflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.ReplyUtility.ReplyMessageAsync");
    }

    [Fact]
    public async Task EchoAsyncProcessor_user_source_uses_reply_workflow_when_workflow_is_provided()
    {
        var workflow = new CapturingReplyWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor, workflow);
        var ev = CreateTextEvent(EventSourceType.User, "Udirect", "Udirect", "reply-token", "hello");

        await utility.EchoAsyncProcessor(ev);

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].ReplyToken.Should().Be("reply-token");
        workflow.Requests[0].Messages.Should().ContainSingle()
            .Which.Should().BeOfType<TextMessage>();
        workflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.ReplyUtility.EchoAsync");
    }

    [Fact]
    public async Task PostSerializedConfirm_uses_reply_workflow_when_workflow_is_provided()
    {
        var workflow = new CapturingReplyWorkflow();
        using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor, workflow);
        var actions = new List<ITemplateAction>
        {
            new MessageTemplateAction("Yes", "yes"),
            new MessageTemplateAction("No", "no")
        };

        await utility.PostSerializedConfirm("reply-token", "alt", "confirm", actions);

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].ReplyToken.Should().Be("reply-token");
        workflow.Requests[0].Messages.Should().ContainSingle()
            .Which.Should().BeOfType<TemplateMessage>();
        workflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.ReplyUtility.PostSerializedConfirm");
    }

    private static MessageEvent CreateTextEvent(
        EventSourceType sourceType,
        string sourceId,
        string userId,
        string replyToken,
        string text)
    {
        var source = new WebhookEventSource(sourceType, sourceId, userId);
        var message = new TextEventMessage("message-id", text);

        return new MessageEvent(source, timestamp: 0, message, replyToken);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public CapturingHttpMessageHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses.Length == 0 ? new[] { "{}" } : responses);
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var json = _responses.Count > 0 ? _responses.Dequeue() : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
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

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The test should use ILineReplyWorkflow, not real HTTP.");
        }
    }
}
