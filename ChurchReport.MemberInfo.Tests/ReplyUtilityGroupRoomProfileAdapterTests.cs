using System.Net;
using System.Text;
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using Line.Messaging.Webhooks;
using LineMessagingProcessor;
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
}
