using System.Net;
using System.Text;
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LineMessagingProcessor.Tests;

public sealed class LineMessagingProcessorSendMessageTests
{
    [Fact]
    public async Task SendMessage_delegates_normal_text_push_to_line_sdk()
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        await processor.SendMessage("U1234567890abcdef", "hello");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("text");
        body["messages"]![0]!["text"]!.Value<string>().Should().Be("hello");
    }

    [Fact]
    public async Task SendMessage_preserves_legacy_confirmation_code_message_text()
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        await processor.SendMessage("U1234567890abcdef", "顯示認證");

        handler.Requests.Should().ContainSingle();

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
        body["messages"]![0]!["text"]!.Value<string>().Should().Be("認證:U1234567890abcdef");
    }

    [Theory]
    [InlineData(null, "hello", "UserId")]
    [InlineData("", "hello", "UserId")]
    [InlineData(" ", "hello", "UserId")]
    [InlineData("U1234567890abcdef", null, "Message")]
    [InlineData("U1234567890abcdef", "", "Message")]
    [InlineData("U1234567890abcdef", " ", "Message")]
    public async Task SendMessage_rejects_blank_required_fields_before_http_call(
        string? userId,
        string? message,
        string expectedParameterName)
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        Func<Task> action = () => processor.SendMessage(userId!, message!);

        var exception = await action.Should().ThrowAsync<ArgumentException>();
        exception.Which.ParamName.Should().Be(expectedParameterName);
        handler.Requests.Should().BeEmpty();
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
