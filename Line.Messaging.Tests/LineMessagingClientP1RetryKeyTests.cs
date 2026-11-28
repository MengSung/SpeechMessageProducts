/*
 * 檔案整體說明
 *
 * 目的與定位：以離線 handler 保護 push、multicast 重試標頭與既有主體；F5 明確禁止多租戶廣播，原廣播發送案例改驗四個精確禁止符號。
 *
 * 主要功能與流程：
 * 本檔宣告 LineMessagingClientP1RetryKeyTests、CapturingHttpMessageHandler。
 * 測試案例入口包含 Push_message_with_retry_key_sends_line_retry_header、
 * Push_message_existing_overload_does_not_send_retry_header、
 * Multicast_message_with_retry_key_sends_line_retry_header_and_keeps_body、
 * Broadcast_is_prohibited_for_both_overloads_and_types、Empty_retry_key_does_not_send_retry_header、
 * CreateClient、SendAsync。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 所有輸入均合成、無外部 HTTP；案例以 using 擁有 client 與 handler，傳輸完成後依反序 Dispose。禁止清單唯讀且不建立資源；不得偷偷呼叫已禁 API。
 */
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>離線驗證合法推播多載與禁止廣播的新契約；不觸及任何真實 Channel。</summary>
public sealed class LineMessagingClientP1RetryKeyTests
{
    /// <summary>合成單人推播斷言重試標頭及 JSON 主體，資源由案例 using 擁有。</summary>
    [Fact]
    public async Task Push_message_with_retry_key_sends_line_retry_header()
    {
        using var handler = new CapturingHttpMessageHandler();
        using var client = CreateClient(handler);

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

    /// <summary>舊多載不得冒出重試標頭，保持相容；沒有任何外部傳輸。</summary>
    [Fact]
    public async Task Push_message_existing_overload_does_not_send_retry_header()
    {
        using var handler = new CapturingHttpMessageHandler();
        using var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") });

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    /// <summary>多播只把合成名單送入本地 handler，斷言標頭與有序主體保持不變。</summary>
    [Fact]
    public async Task Multicast_message_with_retry_key_sends_line_retry_header_and_keeps_body()
    {
        using var handler = new CapturingHttpMessageHandler();
        using var client = CreateClient(handler);

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

    /// <summary>F5 淘汰跨租戶廣播呼叫測試，改驗正式禁止清單；不以反射或排除測試繞過政策。</summary>
    [Fact]
    public void Broadcast_is_prohibited_for_both_overloads_and_types()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannedSymbols.txt")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var lines = File.ReadAllLines(Path.Combine(directory!.FullName, "BannedSymbols.txt"));
        foreach (var type in new[] { "ILineMessagingClient", "LineMessagingClient" })
        foreach (var args in new[] { "System.Collections.Generic.IList{Line.Messaging.ISendMessage}",
            "System.Collections.Generic.IList{Line.Messaging.ISendMessage},System.String" })
        {
            var id = "M:Line.Messaging." + type + ".Broadcast" + "MessageAsync(" + args + ");";
            Assert.Single(lines.Where(line => line.StartsWith(id, StringComparison.Ordinal)));
        }
    }

    /// <summary>空重試鍵不加標頭；三種合成空值均保留既有推播相容行為。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_retry_key_does_not_send_retry_header(string? retryKey)
    {
        using var handler = new CapturingHttpMessageHandler();
        using var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") },
            retryKey);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    /// <summary>建立當次 SDK client；測試 using 與 handler 分別確定性釋放，不保存到 static。</summary>
    private static LineMessagingClient CreateClient(CapturingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
    }

    /// <summary>單案例 HTTP owner，只保存合成請求及本文以供斷言；Dispose 後不再使用。</summary>
    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly string _mediaType;

        /// <summary>指定純記憶體回應，不建立 socket 或背景工作。</summary>
        public CapturingHttpMessageHandler(string responseBody = "{}", HttpStatusCode statusCode = HttpStatusCode.OK, string mediaType = "application/json")
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _mediaType = mediaType;
        }

        /// <summary>僅供當次斷言的合成請求參照，沒有跨案例共用。</summary>
        public List<HttpRequestMessage> Requests { get; } = new();

        /// <summary>當次合成 JSON，隨案例 owner 一起放棄參照。</summary>
        public List<string> Bodies { get; } = new();

        /// <summary>同步記錄候選後回傳合成回應；回應由 SDK 傳輸 owner Dispose。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync());
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, _mediaType)
            };
        }
    }
}
