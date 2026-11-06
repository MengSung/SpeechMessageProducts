/*
 * 檔案整體說明
 *
 * 目的與定位：以假HTTP handler確認使用者profile查詢委派SDK、空ID先拒絕且舊同步入口保持相容。
 *
 * 主要功能與流程：
 * 本檔宣告 LineMessagingProcessorIdentityProfileTests、CapturingHttpMessageHandler。
 * 測試案例入口包含 GetUserProfileAsync_delegates_to_line_sdk_profile_endpoint、
 * GetUserProfileAsync_rejects_blank_user_id_before_http_call、
 * GetUserProfile_keeps_legacy_signature_by_using_sdk_backed_adapter、SendAsync。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Net;
using FluentAssertions;
using Line.Messaging;
using Xunit;

namespace LineMessagingProcessor.Tests;

public sealed class LineMessagingProcessorIdentityProfileTests
{
    [Fact]
    public async Task GetUserProfileAsync_delegates_to_line_sdk_profile_endpoint()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Test User","userId":"U1234567890abcdef","pictureUrl":"https://example.com/u.png","statusMessage":"hello"}""");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        var profile = await processor.GetUserProfileAsync("U1234567890abcdef");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/profile/U1234567890abcdef");
        profile.DisplayName.Should().Be("Test User");
        profile.UserId.Should().Be("U1234567890abcdef");
        profile.PictureUrl.Should().Be("https://example.com/u.png");
        profile.StatusMessage.Should().Be("hello");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetUserProfileAsync_rejects_blank_user_id_before_http_call(string? userId)
    {
        var handler = new CapturingHttpMessageHandler("{}");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        Func<Task> action = () => processor.GetUserProfileAsync(userId!);

        var exception = await action.Should().ThrowAsync<ArgumentException>();
        exception.Which.ParamName.Should().Be("UserId");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserProfile_keeps_legacy_signature_by_using_sdk_backed_adapter()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Legacy User","userId":"Ulegacy","pictureUrl":"https://example.com/legacy.png","statusMessage":"legacy"}""");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        var profile = await processor.GetUserProfile("Ulegacy");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/profile/Ulegacy");
        profile.DisplayName.Should().Be("Legacy User");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            });
        }
    }
}
