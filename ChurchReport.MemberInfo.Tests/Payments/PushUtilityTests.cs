/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承接以下操作邊界：保護付款推播失敗仍拋標準例外且不洩漏遠端本文。注入含合成私密標記的 HTTP 400； 嚴格檢查失敗分類、安全訊息及例外文字，不能以回復原始 provider message 換取相容。
 * HTTP 替身及 transport 由本案 using 擁有並在 await 排空後確定性釋放。
 *
 * 主要功能與流程：
 * 本檔宣告 PushUtilityTests、RecordingLineHandler。
 * - 保護付款推播失敗仍拋標準例外且不洩漏遠端本文。注入含合成私密標記的 HTTP 400； 嚴格檢查失敗分類、安全訊息及例外文字，不能以回復原始 provider message 換取相容。 HTTP 替身及
 * transport 由本案 using 擁有並在 await 排空後確定性釋放。
 * 測試案例入口包含 SendMessageOrThrowAsync_posts_text_message_to_line_push_endpoint、
 * SendMessageOrThrowAsync_throws_when_line_rejects_push_message、
 * SendMessageOrThrowAsync_rejects_empty_line_user_id_before_calling_line、SendAsync。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs 追查目前工作樹中的合作／使用位置。
 */
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

    /// <summary>
    /// 保護付款推播失敗仍拋標準例外且不洩漏遠端本文。注入含合成私密標記的 HTTP 400；
    /// 嚴格檢查失敗分類、安全訊息及例外文字，不能以回復原始 provider message 換取相容。
    /// HTTP 替身及 transport 由本案 using 擁有並在 await 排空後確定性釋放。
    /// </summary>
    [Fact]
    public async Task SendMessageOrThrowAsync_throws_when_line_rejects_push_message()
    {
        var handler = new RecordingLineHandler(
            HttpStatusCode.BadRequest,
            "{\"message\":\"private-synthetic-payment-detail\",\"details\":[]}");
        using var httpClient = new HttpClient(handler);
        var pushUtility = new PushUtility(new LineMessagingClient(
            httpClient,
            "line-token",
            "https://line.example.test/v2"));

        var act = () => pushUtility.SendMessageOrThrowAsync("bad-user", "ATM instructions");

        var exception = await act.Should().ThrowAsync<LineNotificationException>()
            .WithMessage("LINE API request failed. HTTP 400.");
        exception.Which.Result.Status.Should().Be(LineNotificationStatus.ProviderRejected);
        exception.Which.Result.ErrorCode.Should().Be("line-provider-rejected");
        exception.Which.Result.ErrorMessage.Should().Be("LINE API request failed. HTTP 400.");
        exception.Which.ToString().Should().NotContain("private-synthetic-payment-detail");
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
