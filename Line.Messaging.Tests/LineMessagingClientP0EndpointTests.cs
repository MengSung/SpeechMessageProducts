using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessagingClientP0EndpointTests
{
    [Fact]
    public async Task Get_message_delivery_uses_single_v2_segment()
    {
        var handler = new CapturingHttpMessageHandler("""{"status":"ready","success":0}""");
        var client = CreateClient(handler);

        await client.GetMessageDeliveryAsync(new DateTime(2026, 7, 2));

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.line.me/v2/bot/insight/message/delivery?date=20260702");
    }

    [Fact]
    public async Task Get_content_stream_uses_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("binary-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler);

        using var result = await client.GetContentStreamAsync("message-123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content");
    }

    [Fact]
    public async Task Get_content_bytes_uses_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("binary-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler);

        await client.GetContentBytesAsync("message-123");

        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content");
    }

    [Fact]
    public async Task Verify_content_preparation_uses_transcoding_endpoint_on_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("""{"status":"succeeded"}""");
        var client = CreateClient(handler);

        var isReady = await client.VerifyContentPreparationAsync("message-123");

        isReady.Should().BeTrue();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content/transcoding");
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("failed")]
    public async Task Verify_content_preparation_returns_false_until_transcoding_succeeds(string status)
    {
        var handler = new CapturingHttpMessageHandler($$"""{"status":"{{status}}"}""");
        var client = CreateClient(handler);

        var isReady = await client.VerifyContentPreparationAsync("message-123");

        isReady.Should().BeFalse();
    }

    [Fact]
    public async Task Get_content_preview_uses_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("preview-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler);

        using var result = await client.GetContentPreviewAsync("message-123");

        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content/preview");
    }

    [Fact]
    public async Task Rich_menu_image_download_and_upload_use_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("image-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler);

        using var download = await client.DownloadRichMenuImageAsync("rich-1");
        await client.UploadRichMenuJpegImageAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "rich-1");
        await client.UploadRichMenuPngImageAsync(new MemoryStream(new byte[] { 4, 5, 6 }), "rich-1");

        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().Equal(
            "https://api-data.line.me/v2/bot/richmenu/rich-1/content",
            "https://api-data.line.me/v2/bot/richmenu/rich-1/content",
            "https://api-data.line.me/v2/bot/richmenu/rich-1/content");
        handler.Requests.Select(request => request.Method).Should().Equal(HttpMethod.Get, HttpMethod.Post, HttpMethod.Post);
    }

    [Fact]
    public async Task Mark_as_read_uses_official_chat_endpoint_and_token_payload()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.MarkAsReadByTokenAsync("mark-token-123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.line.me/v2/bot/chat/markAsRead");

        var body = JObject.Parse(handler.Bodies[0]);
        body["markAsReadToken"]!.Value<string>().Should().Be("mark-token-123");
        body["chatId"].Should().BeNull();
    }

    [Fact]
    public async Task Legacy_mark_as_read_fails_clearly_instead_of_sending_chat_id_as_token()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

#pragma warning disable CS0618
        Func<Task> action = () => client.MarkAsReadAsync("U1234567890abcdef");
#pragma warning restore CS0618

        await action.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*markAsReadToken*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_rich_menu_batch_progress_uses_progress_query_endpoint()
    {
        var handler = new CapturingHttpMessageHandler("""{"phase":"succeeded"}""");
        var client = CreateClient(handler);

        await client.GetRichMenuBatchProgressAsync("request-123");

        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.line.me/v2/bot/richmenu/progress/batch?requestId=request-123");
    }

    [Fact]
    public async Task Validate_rich_menu_batch_uses_official_validate_batch_endpoint()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.ValidateRichMenuBatchRequestAsync(new List<RichMenuBatchOperation>());

        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.line.me/v2/bot/richmenu/validate/batch");
    }

    [Fact]
    public async Task Insight_endpoints_do_not_duplicate_v2_segment()
    {
        var handler = new CapturingHttpMessageHandler("""{"status":"ready"}""");
        var client = CreateClient(handler);

        await client.GetMessageDeliveryAsync(new DateTime(2026, 7, 2));
        await client.GetFollowerStatisticsAsync(new DateTime(2026, 7, 2));
        await client.GetFriendDemographicsAsync();
        await client.GetUserInteractionStatisticsAsync("request-123");
        await client.GetStatisticsPerUnitAsync("unit one", "20260701", "20260702");
        await client.GetAggregationInfoAsync();
        await client.GetAggregationUnitNameListAsync(10, "next-token");

        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().AllSatisfy(url =>
            url.Should().NotContain("/v2/v2/"));
    }

    [Fact]
    public async Task Coupon_and_membership_endpoints_do_not_duplicate_v2_segment()
    {
        var handler = new CapturingHttpMessageHandler("""{}""");
        var client = CreateClient(handler);

        await client.CreateCouponAsync(new CreateCouponRequest());
        await client.CloseCouponAsync("coupon-1");
        await client.GetCouponListAsync();
        await client.GetCouponAsync("coupon-1");
        await client.GetMembershipSubscriptionAsync("user-1");
        await client.GetMembershipUserIdsAsync("membership-1", 100, "next-token");
        await client.GetMembershipPlansAsync();

        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().AllSatisfy(url =>
            url.Should().NotContain("/v2/v2/"));
    }

    [Fact]
    public async Task Custom_api_base_uri_is_normalized_to_one_v2_segment()
    {
        var handler = new CapturingHttpMessageHandler("""{"status":"ready","success":0}""");
        var client = CreateClient(handler, "https://gateway.internal/line");

        await client.GetMessageDeliveryAsync(new DateTime(2026, 7, 2));
        await client.MarkAsReadByTokenAsync("mark-token-123");

        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().Equal(
            "https://gateway.internal/line/v2/bot/insight/message/delivery?date=20260702",
            "https://gateway.internal/line/v2/bot/chat/markAsRead");
    }

    [Fact]
    public async Task Custom_api_base_uri_is_reused_for_data_endpoints()
    {
        var handler = new CapturingHttpMessageHandler("binary-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler, "https://gateway.internal/line");

        using var result = await client.GetContentStreamAsync("message-123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://gateway.internal/line/v2/bot/message/message-123/content");
    }

    [Fact]
    public async Task Issue_channel_access_token_normalizes_custom_api_base_uri()
    {
        var handler = new CapturingHttpMessageHandler("""{"access_token":"issued-token","expires_in":3600}""");
        using var httpClient = new HttpClient(handler);

        var token = await LineMessagingClient.IssueChannelAccessTokenAsync(
            httpClient,
            "channel-id",
            "channel-secret",
            "https://gateway.internal/line");

        token.AccessToken.Should().Be("issued-token");
        token.ExpiresIn.Should().Be(3600);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://gateway.internal/line/v2/oauth/accessToken");
    }

    [Fact]
    public async Task Revoke_channel_access_token_normalizes_custom_api_base_uri()
    {
        var handler = new CapturingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        await LineMessagingClient.RevokeChannelAccessTokenAsync(
            httpClient,
            "token-to-revoke",
            "https://gateway.internal/line");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://gateway.internal/line/v2/oauth/revoke");

        var formValues = handler.Bodies[0]
            .Split('&')
            .Select(part => part.Split('='))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1]));
        formValues["access_token"].Should().Be("token-to-revoke");
    }

    private static LineMessagingClient CreateClient(CapturingHttpMessageHandler handler, string uri = "https://api.line.me/v2")
    {
        var httpClient = new HttpClient(handler);
        return new LineMessagingClient(httpClient, "test-token", uri);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly string _mediaType;

        public CapturingHttpMessageHandler(
            string responseBody = "{}",
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string mediaType = "application/json")
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _mediaType = mediaType;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, _mediaType)
            };
        }
    }
}
