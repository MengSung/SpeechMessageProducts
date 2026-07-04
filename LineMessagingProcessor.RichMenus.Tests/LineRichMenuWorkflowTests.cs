using System.Net;
using System.Text;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.RichMenus;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests;

public sealed class LineRichMenuWorkflowTests
{
    [Fact]
    public async Task CreateUploadAndLinkAsync_creates_uploads_and_links_rich_menu_in_order()
    {
        var handler = new SequencedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"richMenuId":"rich-menu-001"}""");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        var workflow = CreateWorkflow(handler);

        var result = await workflow.CreateUploadAndLinkAsync(new LineRichMenuCreateUploadAndLinkRequest
        {
            UserId = "Uuser",
            RichMenu = CreateRichMenu(),
            PngImageStreamFactory = () => new MemoryStream(new byte[] { 1, 2, 3 }),
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "test"
            }
        });

        result.Succeeded.Should().BeTrue();
        result.RichMenuId.Should().Be("rich-menu-001");
        result.Metadata["source"].Should().Be("test");
        handler.Requests.Select(request => request.Method.Method + " " + request.RequestUri).Should().Equal(
            "POST https://api.line.me/v2/bot/richmenu",
            "POST https://api-data.line.me/v2/bot/richmenu/rich-menu-001/content",
            "POST https://api.line.me/v2/bot/user/Uuser/richmenu/rich-menu-001");
        handler.Requests[1].Content!.Headers.ContentType!.MediaType.Should().Be("image/png");
        handler.Bodies[0].Should().Contain("\"name\":\"test richmenu\"");
    }

    [Fact]
    public async Task DeleteLinkedRichMenuAsync_gets_current_menu_then_unlinks_and_deletes()
    {
        var handler = new SequencedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"richMenuId":"rich-menu-001"}""");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.OK, "{}");
        var workflow = CreateWorkflow(handler);

        var result = await workflow.DeleteLinkedRichMenuAsync(new LineRichMenuDeleteLinkedRequest
        {
            UserId = "Uuser"
        });

        result.Succeeded.Should().BeTrue();
        result.RichMenuId.Should().Be("rich-menu-001");
        handler.Requests.Select(request => request.Method.Method + " " + request.RequestUri).Should().Equal(
            "GET https://api.line.me/v2/bot/user/Uuser/richmenu",
            "DELETE https://api.line.me/v2/bot/user/Uuser/richmenu",
            "DELETE https://api.line.me/v2/bot/richmenu/rich-menu-001");
    }

    [Fact]
    public async Task CreateUploadAndLinkAsync_returns_validation_failure_without_http_call_when_user_is_blank()
    {
        var handler = new SequencedHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.CreateUploadAndLinkAsync(new LineRichMenuCreateUploadAndLinkRequest
        {
            UserId = " ",
            RichMenu = CreateRichMenu(),
            PngImageStreamFactory = () => new MemoryStream(new byte[] { 1 })
        });

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ValidationFailed);
        result.ErrorCode.Should().Be("line-richmenu-user-required");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateUploadAndLinkOrThrowAsync_throws_standard_exception_when_provider_rejects_request()
    {
        var handler = new SequencedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"message":"invalid rich menu","details":[]}""");
        var workflow = CreateWorkflow(handler);

        var action = () => workflow.CreateUploadAndLinkOrThrowAsync(new LineRichMenuCreateUploadAndLinkRequest
        {
            UserId = "Uuser",
            RichMenu = CreateRichMenu(),
            PngImageStreamFactory = () => new MemoryStream(new byte[] { 1 })
        });

        var exception = await action.Should().ThrowAsync<LineRichMenuException>();
        exception.Which.Result.Status.Should().Be(LineRichMenuStatus.ProviderRejected);
        exception.Which.Result.ErrorCode.Should().Be("line-richmenu-provider-rejected");
        exception.Which.Result.ErrorMessage.Should().Be("invalid rich menu");
    }

    private static LineRichMenuWorkflow CreateWorkflow(SequencedHttpMessageHandler handler)
    {
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(sdkClient)));
    }

    private static RichMenu CreateRichMenu()
    {
        return new RichMenu
        {
            Size = ImagemapSize.RichMenuLong,
            Selected = false,
            Name = "test richmenu",
            ChatBarText = "open",
            Areas = new List<ActionArea>
            {
                new()
                {
                    Bounds = new ImagemapArea(0, 0, ImagemapSize.RichMenuLong.Width, ImagemapSize.RichMenuLong.Height),
                    Action = new MessageTemplateAction("Open", "OPEN")
                }
            }
        };
    }

    private sealed class SequencedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        public void Enqueue(HttpStatusCode statusCode, string body)
        {
            _responses.Enqueue((statusCode, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var response = _responses.Count == 0
                ? (HttpStatusCode.OK, "{}")
                : _responses.Dequeue();

            return new HttpResponseMessage(response.Item1)
            {
                Content = new StringContent(response.Item2, Encoding.UTF8, "application/json")
            };
        }
    }
}


