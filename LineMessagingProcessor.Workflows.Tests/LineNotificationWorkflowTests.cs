using System.Net;
using System.Text;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LineMessagingProcessor.Workflows.Tests;

public sealed class LineNotificationWorkflowTests
{
    [Fact]
    public async Task SendAsync_posts_text_message_through_processor()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.TextMessage("hello")
        });

        result.Succeeded.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("text");
        body["messages"]![0]!["text"]!.Value<string>().Should().Be("hello");
    }

    [Fact]
    public async Task SendAsync_passes_retry_key_to_processor()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.TextMessage("payment received"),
            RetryKey = "churchreport:payment:order-1001:paid:payer-line-notice"
        });

        result.Succeeded.Should().BeTrue();
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("churchreport:payment:order-1001:paid:payer-line-notice");
    }

    [Fact]
    public async Task SendAsync_returns_validation_result_without_http_call_when_recipient_is_blank()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(" "),
            Content = LineNotificationContent.TextMessage("hello")
        });

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineNotificationStatus.ValidationFailed);
        result.ErrorCode.Should().Be("line-recipient-id-required");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_rejects_multi_user_recipient_instead_of_sending_only_first_user()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.Users(new[] { "Ufirst", "Usecond" }),
            Content = LineNotificationContent.TextMessage("hello")
        });

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineNotificationStatus.ValidationFailed);
        result.ErrorCode.Should().Be("line-recipient-users-not-supported");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendOrThrowAsync_throws_standard_exception_when_send_fails()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.BadRequest, """{"message":"invalid user id","details":[]}""");
        var workflow = CreateWorkflow(handler);

        var action = () => workflow.SendOrThrowAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("bad-user"),
            Content = LineNotificationContent.TextMessage("hello")
        });

        var exception = await action.Should().ThrowAsync<LineNotificationException>();
        exception.Which.Result.Status.Should().Be(LineNotificationStatus.ProviderRejected);
        exception.Which.Result.ErrorMessage.Should().Be("invalid user id");
    }

    private static LineNotificationWorkflow CreateWorkflow(CapturingHttpMessageHandler handler)
    {
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        return new LineNotificationWorkflow(new LineMessagingProcessorClass(sdkClient));
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public CapturingHttpMessageHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "{}")
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
