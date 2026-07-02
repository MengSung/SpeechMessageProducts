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
