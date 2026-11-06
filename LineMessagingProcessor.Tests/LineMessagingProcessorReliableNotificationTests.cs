/*
 * 檔案整體說明
 *
 * 目的與定位：驗證可靠通知把穩定retry key交LINE SDK、空鍵維持舊行為並拒絕缺必要欄位。
 *
 * 主要功能與流程：
 * 本檔宣告 LineMessagingProcessorReliableNotificationTests、CapturingHttpMessageHandler。
 * 測試案例入口包含 SendReliableMessageAsync_passes_retry_key_to_line_sdk、
 * SendReliableMessageAsync_with_blank_retry_key_keeps_non_retry_sdk_behavior、
 * SendReliableMessageAsync_rejects_missing_required_fields、SendAsync。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
