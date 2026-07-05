using System.Net;
using System.Text;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.RichMenus;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests;

/// <summary>
/// 驗證低階 RichMenu workflow 與 LINE Messaging API endpoint 的串接順序。
/// 這組測試使用自訂 HttpMessageHandler 捕捉 HTTP request，避免打真實 LINE API。
/// </summary>
public sealed class LineRichMenuWorkflowTests
{
    /// <summary>
    /// 建立、上傳圖片、連結使用者應依 LINE API 要求順序送出三個 HTTP request。
    /// </summary>
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

    /// <summary>
    /// 刪除已連結 RichMenu 時，workflow 應先查使用者目前 richMenuId，再 unlink，最後 delete provider menu。
    /// </summary>
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

    /// <summary>
    /// 本機驗證失敗時不應送出任何 HTTP request，避免把明顯錯誤送到 LINE provider。
    /// </summary>
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

    /// <summary>
    /// LINE provider 明確拒絕 request 時，OrThrow 變體應丟出標準 RichMenu 例外並保留 provider-rejected 狀態。
    /// </summary>
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

    /// <summary>
    /// 建立 workflow 測試用的 SDK client，讓測試可以控制 HTTP response sequence。
    /// </summary>
    private static LineRichMenuWorkflow CreateWorkflow(SequencedHttpMessageHandler handler)
    {
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(sdkClient)));
    }

    /// <summary>
    /// 建立符合 LINE RichMenu 基本要求的測試版面。
    /// </summary>
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

    /// <summary>
    /// 依序回傳預先排好的 HTTP response，並捕捉 workflow 送出的 request 與 body。
    /// </summary>
    private sealed class SequencedHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>
        /// 測試預先排好的 provider responses。
        /// </summary>
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses = new();

        /// <summary>
        /// workflow 送出的 HTTP requests，供測試檢查 endpoint 與 method。
        /// </summary>
        public List<HttpRequestMessage> Requests { get; } = new();

        /// <summary>
        /// workflow 送出的 request bodies，供測試檢查 RichMenu create payload。
        /// </summary>
        public List<string> Bodies { get; } = new();

        /// <summary>
        /// 加入下一個 provider response。
        /// </summary>
        public void Enqueue(HttpStatusCode statusCode, string body)
        {
            _responses.Enqueue((statusCode, body));
        }

        /// <summary>
        /// 捕捉 request，取出下一個 response，模擬 LINE API 回應。
        /// </summary>
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


