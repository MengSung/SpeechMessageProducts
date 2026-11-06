/*
 * 檔案整體說明
 *
 * 目的與定位：先鎖既有 Processor 建構結果，全部使用合成設定；只建立並立即釋放未送出請求的 client，不連 LINE。
 *
 * 主要功能與流程：
 * 本檔宣告 LineChannelConfigurationCompatibilityTests。
 * - 舊公開設定建構式先環境再部署預設，不增加 CRM 組織層；Bearer 只出現一次，空設定仍維持空供送訊入口拒絕。
 * - 明確指定預設別名時不能硬改回 Jesus；直接呼叫舊入口的 null 設定仍採舊空結果。
 * 測試案例入口包含 Constructor_ExistingPrecedence_RemainsCompatible、
 * Constructor_ExplicitDefaultAndNull_PreserveLegacyResults。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LineMessagingProcessor.Tests;

/// <summary>先鎖既有 Processor 建構結果，全部使用合成設定；只建立並立即釋放未送出請求的 client，不連 LINE。</summary>
public sealed class LineChannelConfigurationCompatibilityTests
{
    /// <summary>舊公開設定建構式先環境再部署預設，不增加 CRM 組織層；Bearer 只出現一次，空設定仍維持空供送訊入口拒絕。</summary>
    /// <param name="environment">合成環境設定值。</param>
    /// <param name="defaultToken">合成預設設定值。</param>
    /// <param name="expected">人工指定的舊正規化建構結果。</param>
    [Theory(DisplayName = "Processor既有設定優先序與Bearer建構結果")]
    [InlineData("synthetic-env", "synthetic-default", "Bearer synthetic-env")]
    [InlineData("Bearer synthetic-env", "synthetic-default", "Bearer synthetic-env")]
    [InlineData("", "synthetic-default", "Bearer synthetic-default")]
    [InlineData(" ", "synthetic-default", "Bearer synthetic-default")]
    [InlineData(null, "synthetic-default", "Bearer synthetic-default")]
    [InlineData(null, "", "")]
    [InlineData(null, null, "")]
    public void Constructor_ExistingPrecedence_RemainsCompatible(string? environment, string? defaultToken, string expected)
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:Organization"] = "Alpha",
            ["LineMessaging:Alpha:ChannelAccessToken"] = "synthetic-never-selected",
            ["LineMessaging:Jesus:ChannelAccessToken"] = defaultToken,
            ["LINE_CHANNEL_ACCESS_TOKEN"] = environment
        }).Build();
        using var processor = new LineMessagingProcessorClass(config);
        Assert.Equal(expected, typeof(LineMessagingProcessorClass).GetField("_channelAccessToken",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(processor));
    }

    /// <summary>明確指定預設別名時不能硬改回 Jesus；直接呼叫舊入口的 null 設定仍採舊空結果。</summary>
    [Fact(DisplayName = "Processor明確預設別名與null設定相容")]
    public void Constructor_ExplicitDefaultAndNull_PreserveLegacyResults()
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LineMessaging:DefaultOrganization"] = "DevTest",
            ["LineMessaging:DevTest:ChannelAccessToken"] = "synthetic-dev",
            ["LineMessaging:Jesus:ChannelAccessToken"] = "synthetic-not-selected"
        }).Build();
        using var processor = new LineMessagingProcessorClass(config);
        Assert.Equal("Bearer synthetic-dev", typeof(LineMessagingProcessorClass).GetField("_channelAccessToken",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(processor));
        var method = typeof(LineMessagingProcessorClass).GetMethod("ResolveChannelAccessToken", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(string.Empty, method.Invoke(null, new object?[] { null }));
    }
}
#endif
