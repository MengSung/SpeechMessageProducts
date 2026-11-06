/*
 * 檔案整體說明
 *
 * 目的與定位：檢查processor透過建構式／組態取得頻道token而不含字面Bearer，缺設定在送出請求前失敗。
 *
 * 主要功能與流程：
 * 本檔宣告 LineMessagingProcessorCredentialTests。
 * 測試案例入口包含 Processor_source_does_not_contain_literal_bearer_tokens、
 * Processor_accepts_channel_access_token_through_constructor、
 * Processor_uses_injected_configuration_line_messaging_token、
 * Processor_uses_standard_configuration_environment_override、
 * Processor_without_token_fails_before_sending_line_request、GetPrivateChannelAccessToken。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
