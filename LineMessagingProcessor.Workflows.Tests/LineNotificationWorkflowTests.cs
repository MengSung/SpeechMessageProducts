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
    public async Task SendAsync_posts_image_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.ImageMessage(
                "https://example.test/original.jpg",
                "https://example.test/preview.jpg")
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("image");
        body["messages"]![0]!["originalContentUrl"]!.Value<string>().Should().Be("https://example.test/original.jpg");
        body["messages"]![0]!["previewImageUrl"]!.Value<string>().Should().Be("https://example.test/preview.jpg");
    }

    [Fact]
    public async Task SendAsync_posts_flex_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.FlexMessage(FlexMessage.CreateBubbleMessage("repair notice"))
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("flex");
        body["messages"]![0]!["altText"]!.Value<string>().Should().Be("repair notice");
        body["messages"]![0]!["contents"]!["type"]!.Value<string>().Should().Be("bubble");
    }

    [Fact]
    public async Task SendAsync_posts_text_v2_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.TextMessageV2("hello v2")
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("textV2");
        body["messages"]![0]!["text"]!.Value<string>().Should().Be("hello v2");
    }

    [Fact]
    public async Task SendAsync_posts_coupon_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.CouponMessage("coupon-001", "invoice-reminder")
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("coupon");
        body["messages"]![0]!["couponId"]!.Value<string>().Should().Be("coupon-001");
        body["messages"]![0]!["deliveryTag"]!.Value<string>().Should().Be("invoice-reminder");
    }

    [Fact]
    public async Task SendAsync_posts_text_message_with_quick_reply_created_by_product_friendly_factories()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);
        var quickReply = LineQuickReplyFactory.Create(
            LineQuickReplyFactory.MessageAction("確認", "CONFIRM"));

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.TextMessage("請選擇", quickReply)
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("text");
        body["messages"]![0]!["quickReply"]!["items"]![0]!["type"]!.Value<string>().Should().Be("action");
        body["messages"]![0]!["quickReply"]!["items"]![0]!["action"]!["type"]!.Value<string>().Should().Be("message");
        body["messages"]![0]!["quickReply"]!["items"]![0]!["action"]!["label"]!.Value<string>().Should().Be("確認");
        body["messages"]![0]!["quickReply"]!["items"]![0]!["action"]!["text"]!.Value<string>().Should().Be("CONFIRM");
    }

    [Fact]
    public async Task SendAsync_posts_confirm_template_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.ConfirmTemplateMessage(
                "confirm alt",
                "是否確認?",
                new[]
                {
                    LineTemplateActionFactory.Message("是", "YES"),
                    LineTemplateActionFactory.Message("否", "NO")
                })
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("template");
        body["messages"]![0]!["altText"]!.Value<string>().Should().Be("confirm alt");
        body["messages"]![0]!["template"]!["type"]!.Value<string>().Should().Be("confirm");
        body["messages"]![0]!["template"]!["actions"]!.Should().HaveCount(2);
    }

    [Fact]
    public async Task SendAsync_posts_buttons_template_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.ButtonsTemplateMessage(
                "buttons alt",
                "維修單已建立",
                "維修通知",
                "https://example.test/repair.png",
                new[]
                {
                    LineTemplateActionFactory.Uri("查看", "https://example.test/tickets/1001")
                })
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("template");
        body["messages"]![0]!["template"]!["type"]!.Value<string>().Should().Be("buttons");
        body["messages"]![0]!["template"]!["thumbnailImageUrl"]!.Value<string>().Should().Be("https://example.test/repair.png");
        body["messages"]![0]!["template"]!["actions"]![0]!["type"]!.Value<string>().Should().Be("uri");
    }

    [Fact]
    public async Task SendAsync_posts_carousel_template_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.CarouselTemplateMessage(
                "carousel alt",
                new[]
                {
                    LineCarouselColumnFactory.Column(
                        "發票提醒",
                        "第 1 筆",
                        "https://example.test/invoice.png",
                        new[] { LineTemplateActionFactory.Postback("付款", "invoice=1") })
                })
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["template"]!["type"]!.Value<string>().Should().Be("carousel");
        body["messages"]![0]!["template"]!["columns"]![0]!["title"]!.Value<string>().Should().Be("發票提醒");
        body["messages"]![0]!["template"]!["columns"]![0]!["actions"]![0]!["type"]!.Value<string>().Should().Be("postback");
    }

    [Fact]
    public async Task SendAsync_posts_image_carousel_template_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.ImageCarouselTemplateMessage(
                "image carousel alt",
                new[]
                {
                    LineCarouselColumnFactory.ImageColumn(
                        "https://example.test/item.png",
                        LineTemplateActionFactory.Uri("開啟", "https://example.test/items/1"))
                })
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["template"]!["type"]!.Value<string>().Should().Be("image_carousel");
        body["messages"]![0]!["template"]!["columns"]![0]!["imageUrl"]!.Value<string>().Should().Be("https://example.test/item.png");
    }

    [Fact]
    public async Task SendAsync_posts_sticker_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.StickerMessage("1", "13")
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("sticker");
        body["messages"]![0]!["packageId"]!.Value<string>().Should().Be("1");
        body["messages"]![0]!["stickerId"]!.Value<string>().Should().Be("13");
    }

    [Fact]
    public async Task SendAsync_posts_video_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.VideoMessage(
                "https://example.test/video.mp4",
                "https://example.test/video-preview.jpg")
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("video");
        body["messages"]![0]!["originalContentUrl"]!.Value<string>().Should().Be("https://example.test/video.mp4");
        body["messages"]![0]!["previewImageUrl"]!.Value<string>().Should().Be("https://example.test/video-preview.jpg");
    }

    [Fact]
    public async Task SendAsync_posts_audio_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.AudioMessage("https://example.test/audio.m4a", 32000)
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("audio");
        body["messages"]![0]!["originalContentUrl"]!.Value<string>().Should().Be("https://example.test/audio.m4a");
        body["messages"]![0]!["duration"]!.Value<long>().Should().Be(32000);
    }

    [Fact]
    public async Task SendAsync_posts_location_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.LocationMessage("公司", "台北市信義區", 25.0330m, 121.5654m)
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("location");
        body["messages"]![0]!["title"]!.Value<string>().Should().Be("公司");
        body["messages"]![0]!["latitude"]!.Value<decimal>().Should().Be(25.0330m);
    }

    [Fact]
    public async Task SendAsync_posts_imagemap_message_created_by_product_friendly_wrapper()
    {
        var handler = new CapturingHttpMessageHandler();
        var workflow = CreateWorkflow(handler);

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
            Content = LineNotificationContent.ImagemapMessage(
                "https://example.test/imagemap",
                "imagemap alt",
                1040,
                1040,
                new[]
                {
                    LineImagemapActionFactory.Message("AREA1", 0, 0, 520, 1040, "左側")
                })
        });

        result.Succeeded.Should().BeTrue();

        var body = JObject.Parse(handler.Bodies[0]);
        body["messages"]![0]!["type"]!.Value<string>().Should().Be("imagemap");
        body["messages"]![0]!["baseUrl"]!.Value<string>().Should().Be("https://example.test/imagemap");
        body["messages"]![0]!["baseSize"]!["width"]!.Value<int>().Should().Be(1040);
        body["messages"]![0]!["actions"]![0]!["type"]!.Value<string>().Should().Be("message");
    }

    [Theory]
    [InlineData("", "https://example.test/preview.jpg", "originalContentUrl")]
    [InlineData(" ", "https://example.test/preview.jpg", "originalContentUrl")]
    [InlineData("http://example.test/original.jpg", "https://example.test/preview.jpg", "originalContentUrl")]
    [InlineData("https://example.test/original.jpg", "", "previewImageUrl")]
    [InlineData("https://example.test/original.jpg", " ", "previewImageUrl")]
    [InlineData("https://example.test/original.jpg", "http://example.test/preview.jpg", "previewImageUrl")]
    public void ImageMessage_rejects_blank_urls_before_http_call(
        string originalContentUrl,
        string previewImageUrl,
        string expectedParameterName)
    {
        var action = () => LineNotificationContent.ImageMessage(originalContentUrl, previewImageUrl);

        action.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be(expectedParameterName);
    }

    [Fact]
    public void QuickReplyFactory_rejects_more_than_thirteen_items_before_http_call()
    {
        var items = Enumerable.Range(1, 14)
            .Select(index => LineQuickReplyFactory.MessageAction($"選項{index}", $"OPT-{index}"))
            .ToArray();

        var action = () => LineQuickReplyFactory.Create(items);

        action.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("items");
    }

    [Fact]
    public void ConfirmTemplateMessage_rejects_action_count_other_than_two_before_http_call()
    {
        var action = () => LineNotificationContent.ConfirmTemplateMessage(
            "confirm alt",
            "是否確認?",
            new[] { LineTemplateActionFactory.Message("是", "YES") });

        action.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("actions");
    }

    [Theory]
    [InlineData("http://example.test/video.mp4", "https://example.test/preview.jpg", "originalContentUrl")]
    [InlineData("https://example.test/video.mp4", "http://example.test/preview.jpg", "previewImageUrl")]
    public void VideoMessage_rejects_non_https_urls_before_http_call(
        string originalContentUrl,
        string previewImageUrl,
        string expectedParameterName)
    {
        var action = () => LineNotificationContent.VideoMessage(originalContentUrl, previewImageUrl);

        action.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be(expectedParameterName);
    }

    [Fact]
    public void AudioMessage_rejects_non_positive_duration_before_http_call()
    {
        var action = () => LineNotificationContent.AudioMessage("https://example.test/audio.m4a", 0);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("durationMilliseconds");
    }

    [Theory]
    [InlineData(-91, 121.5654, "latitude")]
    [InlineData(25.0330, 181, "longitude")]
    public void LocationMessage_rejects_invalid_coordinates_before_http_call(
        decimal latitude,
        decimal longitude,
        string expectedParameterName)
    {
        var action = () => LineNotificationContent.LocationMessage("公司", "台北市信義區", latitude, longitude);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(expectedParameterName);
    }

    [Fact]
    public void FlexMessage_rejects_null_message_before_http_call()
    {
        var action = () => LineNotificationContent.FlexMessage(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("message");
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
