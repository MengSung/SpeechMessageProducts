using System.Net;
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class PushUtilityTests
{
    [Fact]
    public async Task SendMessageOrThrowAsync_posts_text_message_to_line_push_endpoint()
    {
        var handler = new RecordingLineHandler(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler);
        var pushUtility = new PushUtility(new LineMessagingClient(
            httpClient,
            "line-token",
            "https://line.example.test/v2"));

        await pushUtility.SendMessageOrThrowAsync("Udonor", "ATM instructions");

        handler.RequestUri.Should().Be("https://line.example.test/v2/bot/message/push");
        handler.AuthorizationHeader.Should().Be("Bearer line-token");
        handler.RequestBody.Should().Contain("\"to\":\"Udonor\"");
        handler.RequestBody.Should().Contain("\"text\":\"ATM instructions\"");
    }

    [Fact]
    public async Task SendMessageOrThrowAsync_throws_when_line_rejects_push_message()
    {
        var handler = new RecordingLineHandler(
            HttpStatusCode.BadRequest,
            "{\"message\":\"invalid user id\",\"details\":[]}");
        using var httpClient = new HttpClient(handler);
        var pushUtility = new PushUtility(new LineMessagingClient(
            httpClient,
            "line-token",
            "https://line.example.test/v2"));

        var act = () => pushUtility.SendMessageOrThrowAsync("bad-user", "ATM instructions");

        await act.Should().ThrowAsync<LineResponseException>()
            .WithMessage("invalid user id");
    }

    [Fact]
    public async Task SendMessageOrThrowAsync_rejects_empty_line_user_id_before_calling_line()
    {
        var handler = new RecordingLineHandler(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler);
        var pushUtility = new PushUtility(new LineMessagingClient(
            httpClient,
            "line-token",
            "https://line.example.test/v2"));

        var act = () => pushUtility.SendMessageOrThrowAsync("", "ATM instructions");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("UserId");
        handler.RequestUri.Should().BeNull();
    }

    private sealed class RecordingLineHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public RecordingLineHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

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

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content)
            };
        }
    }
}
