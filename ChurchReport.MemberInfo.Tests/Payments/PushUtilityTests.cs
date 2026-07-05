// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PushUtilityTests、class RecordingLineHandler
// 主要成員：SendMessageOrThrowAsync_posts_text_message_to_line_push_endpoint、SendMessageOrThrowAsync_throws_when_line_rejects_push_message、SendMessageOrThrowAsync_rejects_empty_line_user_id_before_calling_line、SendAsync、RequestUri、AuthorizationHeader、RequestBody
// 引用命名空間：System.Net、ChurchReport.Tools、FluentAssertions、Line.Messaging、LineMessagingProcessor.Workflows、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Net;
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
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

        await act.Should().ThrowAsync<LineNotificationException>()
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
