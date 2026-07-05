// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs
// 所屬區塊：LINE Messaging SDK 測試專案，驗證 API 端點、序列化與 Client 行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessagingProcessorCredentialTests
// 主要成員：Processor_source_does_not_contain_literal_bearer_tokens、Processor_accepts_channel_access_token_through_constructor、Processor_uses_injected_configuration_line_messaging_token、Processor_uses_standard_configuration_environment_override、Processor_without_token_fails_before_sending_line_request、GetPrivateChannelAccessToken
// 引用命名空間：FluentAssertions、LineMessagingProcessor、Microsoft.Extensions.Configuration、System.Reflection、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using LineMessagingProcessor;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessagingProcessorCredentialTests
{
    [Fact]
    public void Processor_source_does_not_contain_literal_bearer_tokens()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "LineMessagingProcessor",
            "LineMessagingProcessorClass.cs"));

        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("Bearer RvnT/");
        source.Should().NotContain("Bearer zBJV+");
        source.Should().NotContain("Bearer PhC1");
        source.Should().NotContain("dB04t89/1O/w1cDnyilFU=");
    }

    [Fact]
    public void Processor_accepts_channel_access_token_through_constructor()
    {
        using var processor = new LineMessagingProcessorClass("test-token");

        processor.Should().NotBeNull();
    }

    [Fact]
    public void Processor_uses_injected_configuration_line_messaging_token()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LineMessaging:DefaultOrganization"] = "Jesus",
                ["LineMessaging:Jesus:ChannelAccessToken"] = "config-token"
            })
            .Build();

        using var processor = new LineMessagingProcessorClass(configuration);

        GetPrivateChannelAccessToken(processor).Should().Be("Bearer config-token");
    }

    [Fact]
    public void Processor_uses_standard_configuration_environment_override()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LINE_CHANNEL_ACCESS_TOKEN"] = "environment-token",
                ["LineMessaging:DefaultOrganization"] = "Jesus",
                ["LineMessaging:Jesus:ChannelAccessToken"] = "config-token"
            })
            .Build();

        using var processor = new LineMessagingProcessorClass(configuration);

        GetPrivateChannelAccessToken(processor).Should().Be("Bearer environment-token");
    }

    [Fact]
    public async Task Processor_without_token_fails_before_sending_line_request()
    {
        using var processor = new LineMessagingProcessorClass(channelAccessToken: "");

        Func<Task> action = () => processor.SendMessage("user-1", "hello");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LINE channel access token*");
    }

    private static string GetPrivateChannelAccessToken(LineMessagingProcessorClass processor)
    {
        var field = typeof(LineMessagingProcessorClass).GetField(
            "_channelAccessToken",
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        return (string)field!.GetValue(processor)!;
    }
}
