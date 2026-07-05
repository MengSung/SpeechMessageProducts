// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Tests/LineMessagingProcessorGroupRoomProfileTests.cs
// 所屬區塊：LINE 訊息處理核心測試專案。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessagingProcessorGroupRoomProfileTests、class CapturingHttpMessageHandler
// 主要成員：GetGroupMemberProfileAsync_delegates_to_line_sdk_group_member_profile_endpoint、GetRoomMemberProfileAsync_delegates_to_line_sdk_room_member_profile_endpoint、GetGroupMemberProfileAsync_rejects_blank_identifiers_before_http_call、GetRoomMemberProfileAsync_rejects_blank_identifiers_before_http_call、SendAsync、Requests
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

public sealed class LineMessagingProcessorGroupRoomProfileTests
{
    [Fact]
    public async Task GetGroupMemberProfileAsync_delegates_to_line_sdk_group_member_profile_endpoint()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Group User","userId":"U123","pictureUrl":"https://example.com/group.png","statusMessage":"group hello"}""");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        var profile = await processor.GetGroupMemberProfileAsync("G123", "U123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/group/G123/member/U123");
        profile.DisplayName.Should().Be("Group User");
        profile.UserId.Should().Be("U123");
        profile.PictureUrl.Should().Be("https://example.com/group.png");
        profile.StatusMessage.Should().Be("group hello");
    }

    [Fact]
    public async Task GetRoomMemberProfileAsync_delegates_to_line_sdk_room_member_profile_endpoint()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Room User","userId":"U456","pictureUrl":"https://example.com/room.png","statusMessage":"room hello"}""");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        var profile = await processor.GetRoomMemberProfileAsync("R123", "U456");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/room/R123/member/U456");
        profile.DisplayName.Should().Be("Room User");
        profile.UserId.Should().Be("U456");
        profile.PictureUrl.Should().Be("https://example.com/room.png");
        profile.StatusMessage.Should().Be("room hello");
    }

    [Theory]
    [InlineData(null, "U123", "groupId")]
    [InlineData("", "U123", "groupId")]
    [InlineData(" ", "U123", "groupId")]
    [InlineData("G123", null, "userId")]
    [InlineData("G123", "", "userId")]
    [InlineData("G123", " ", "userId")]
    public async Task GetGroupMemberProfileAsync_rejects_blank_identifiers_before_http_call(
        string? groupId,
        string? userId,
        string expectedParameterName)
    {
        var handler = new CapturingHttpMessageHandler("{}");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        Func<Task> action = () => processor.GetGroupMemberProfileAsync(groupId!, userId!);

        var exception = await action.Should().ThrowAsync<ArgumentException>();
        exception.Which.ParamName.Should().Be(expectedParameterName);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "U123", "roomId")]
    [InlineData("", "U123", "roomId")]
    [InlineData(" ", "U123", "roomId")]
    [InlineData("R123", null, "userId")]
    [InlineData("R123", "", "userId")]
    [InlineData("R123", " ", "userId")]
    public async Task GetRoomMemberProfileAsync_rejects_blank_identifiers_before_http_call(
        string? roomId,
        string? userId,
        string expectedParameterName)
    {
        var handler = new CapturingHttpMessageHandler("{}");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        Func<Task> action = () => processor.GetRoomMemberProfileAsync(roomId!, userId!);

        var exception = await action.Should().ThrowAsync<ArgumentException>();
        exception.Which.ParamName.Should().Be(expectedParameterName);
        handler.Requests.Should().BeEmpty();
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
