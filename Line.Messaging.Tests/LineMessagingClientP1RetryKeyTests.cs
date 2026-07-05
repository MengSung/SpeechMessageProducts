// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs
// 所屬區塊：LINE Messaging SDK 測試專案，驗證 API 端點、序列化與 Client 行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessagingClientP1RetryKeyTests、class CapturingHttpMessageHandler
// 主要成員：Push_message_with_retry_key_sends_line_retry_header、Push_message_existing_overload_does_not_send_retry_header、Multicast_message_with_retry_key_sends_line_retry_header_and_keeps_body、Broadcast_message_with_retry_key_sends_line_retry_header、Empty_retry_key_does_not_send_retry_header、CreateClient、SendAsync、Requests、Bodies
// 引用命名空間：FluentAssertions、Line.Messaging、Newtonsoft.Json.Linq、System.Net、System.Text、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessagingClientP1RetryKeyTests
{
    [Fact]
    public async Task Push_message_with_retry_key_sends_line_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") },
            "fee-1001-notification");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("fee-1001-notification");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
        body["messages"]!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Push_message_existing_overload_does_not_send_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") });

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    [Fact]
    public async Task Multicast_message_with_retry_key_sends_line_retry_header_and_keeps_body()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.MultiCastMessageAsync(
            new List<string> { "U111", "U222" },
            new List<ISendMessage> { new TextMessage("batch notice") },
            "batch-20260702-001");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/multicast");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("batch-20260702-001");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Select(token => token.Value<string>()).Should().Equal("U111", "U222");
        body["messages"]!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Broadcast_message_with_retry_key_sends_line_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.BroadcastMessageAsync(
            new List<ISendMessage> { new TextMessage("global notice") },
            "broadcast-20260702-001");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/broadcast");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("broadcast-20260702-001");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_retry_key_does_not_send_retry_header(string? retryKey)
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") },
            retryKey);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    private static LineMessagingClient CreateClient(CapturingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly string _mediaType;

        public CapturingHttpMessageHandler(string responseBody = "{}", HttpStatusCode statusCode = HttpStatusCode.OK, string mediaType = "application/json")
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
