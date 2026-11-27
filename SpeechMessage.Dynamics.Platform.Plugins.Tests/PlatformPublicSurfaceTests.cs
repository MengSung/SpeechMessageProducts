/*
檔案整體說明
目的與定位：驗證 F3 真正 net462 平台組件的公開面，只允許計畫核定的兩個 IPlugin 入口，不把組織映射 helper 暴露成沙箱 API。
主要功能與流程：測試宿主以反射讀取已建置產品組件的 exported types，按完整名稱排序後與兩個明確入口比較；公開 helper 或第三個入口會使集合斷言失敗。
使用邊界與維護：只讀本案已參考的組件中繼資料，不建立 Plugin、SDK、D365 連線或 LINE 呼叫，不讀秘密或使用者資料。沒有 timer、背景工作或外部 owner；結果僅證明離線公開邊界，不能代替最終部署包與實機註冊驗收。
*/
using System;
using System.Linq;
using SpeechMessage.Dynamics.Platform.Plugins.ChannelPool;
using Xunit;

namespace SpeechMessage.Dynamics.Platform.Plugins.Tests;

/// <summary>對真正產品輸出做公開型別集合驗證，防止合成來源測試漏掉實作 helper。</summary>
public sealed class PlatformPublicSurfaceTests
{
    /// <summary>唯一核定公開名稱；私有純值集合從不修改或傳給產品，不含租戶或使用者狀態。</summary>
    private static readonly string[] ExpectedEntries =
    {
        "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.ChannelCreatedPlugin",
        "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.SlotRequestPlugin"
    };
    /// <summary>只允許兩個核定完整型別；任何 helper 公開或核定入口缺漏皆明確失敗。</summary>
    [Fact]
    public void PlatformAssembly_ExportsOnlyTwoApprovedPluginEntries()
    {
        var actual = typeof(SlotRequestPlugin).Assembly.GetExportedTypes()
            .Select(type => type.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(ExpectedEntries, actual);
    }
}
