// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Tests/LineMessagingProcessorIdentityProfileTests.cs
// 所屬區塊：LINE 訊息處理核心測試專案。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessagingProcessorIdentityProfileTests、class CapturingHttpMessageHandler
// 主要成員：GetUserProfileAsync_delegates_to_line_sdk_profile_endpoint、GetUserProfileAsync_rejects_blank_user_id_before_http_call、GetUserProfile_keeps_legacy_signature_by_using_sdk_backed_adapter、SendAsync、Requests
// 引用命名空間：System.Net、FluentAssertions、Line.Messaging、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
