// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Tests/LineMessagingProcessorSendMessageTests.cs
// 所屬區塊：LINE 訊息處理核心測試專案。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessagingProcessorSendMessageTests、class CapturingHttpMessageHandler
// 主要成員：SendMessage_delegates_normal_text_push_to_line_sdk、SendMessage_preserves_legacy_confirmation_code_message_text、SendMessage_rejects_blank_required_fields_before_http_call、SendAsync、Requests、Bodies
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

    [Fact]
    public void Dispose_CanBeCalledRepeatedly_WhenProcessorOwnsCompatibilityClient()
    {
        using var processor = new LineMessagingProcessorClass("test-token");

        processor.Dispose();
        processor.Dispose();
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
