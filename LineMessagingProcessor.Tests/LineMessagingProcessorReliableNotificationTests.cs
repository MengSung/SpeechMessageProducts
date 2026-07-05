// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Tests/LineMessagingProcessorReliableNotificationTests.cs
// 所屬區塊：LINE 訊息處理核心測試專案。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessagingProcessorReliableNotificationTests、class CapturingHttpMessageHandler
// 主要成員：SendReliableMessageAsync_passes_retry_key_to_line_sdk、SendReliableMessageAsync_with_blank_retry_key_keeps_non_retry_sdk_behavior、SendReliableMessageAsync_rejects_missing_required_fields、SendAsync、Requests、Bodies
// 引用命名空間：System.Net、System.Text、FluentAssertions、Line.Messaging、Newtonsoft.Json.Linq、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Net;
using System.Text;
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LineMessagingProcessor.Tests;

public sealed class LineMessagingProcessorReliableNotificationTests
{
    [Fact]
    public async Task SendReliableMessageAsync_passes_retry_key_to_line_sdk()
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        await processor.SendReliableMessageAsync(
            "U1234567890abcdef",
            "payment received",
            "churchreport:payment:order-1001:paid:payer-line-notice");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("churchreport:payment:order-1001:paid:payer-line-notice");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
        body["messages"]![0]!["text"]!.Value<string>().Should().Be("payment received");
    }

    [Fact]
    public async Task SendReliableMessageAsync_with_blank_retry_key_keeps_non_retry_sdk_behavior()
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        await processor.SendReliableMessageAsync("U1234567890abcdef", "payment received", " ");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "message", "retry-key", "UserId")]
    [InlineData(" ", "message", "retry-key", "UserId")]
    [InlineData("U1234567890abcdef", null, "retry-key", "Message")]
    [InlineData("U1234567890abcdef", " ", "retry-key", "Message")]
    public async Task SendReliableMessageAsync_rejects_missing_required_fields(
        string? userId,
        string? message,
        string? retryKey,
        string expectedParameterName)
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        Func<Task> action = () => processor.SendReliableMessageAsync(userId!, message!, retryKey);

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
