/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 Plugin step 設定的長度、重複屬性、模式與 Shadow secure 邊界。
 * 測試只使用合成 JSON，不輸出或保存秘密；每個案例建立獨立設定物件，沒有共享
 * 可變狀態或資源。
 *
 * 主要功能與流程：以失敗案例注入 JSON parser、模式驗證及 secure 設定邊界，斷言
 * 固定繁中錯誤且不回顯原始設定；以成功案例確認 Shadow／Live 的唯讀結果。
 *
 * 使用邊界與維護：這些是離線設定契約，不代表實機 registration 的完整 schema；
 * 實機切換與 D5-18 關卡由部署工具另驗。
 */
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Infrastructure;

/// <summary>StepConfiguration 的 fail-closed 驗證。</summary>
public sealed class StepConfigurationTests
{
    /// <summary>非法 JSON 只回固定繁中訊息，不回顯合成輸入。</summary>
    [Fact]
    public void StepConfig_InvalidJson_ThrowsChineseMessage()
    {
        var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
            new StepConfiguration("{not-json}", string.Empty));

        Assert.Contains("設定格式錯誤", exception.Message, System.StringComparison.Ordinal);
        Assert.DoesNotContain("not-json", exception.Message, System.StringComparison.Ordinal);
    }

    /// <summary>Shadow secure config 必須拒絕。</summary>
    [Fact]
    public void StepConfig_ShadowWithSecureConfig_Rejected()
    {
        Assert.Throws<InvalidPluginExecutionException>(() =>
            new StepConfiguration(@"{""mode"":""Shadow""}", "synthetic-secret"));
    }
    /// <summary>數字、組合名稱、重複欄位及錯誤選項型別不能繞過模式白名單。</summary>
    [Theory]
    [InlineData("{\"mode\":\"1\"}")]
    [InlineData("{\"mode\":1}")]
    [InlineData("{\"mode\":\"Shadow, Live\"}")]
    [InlineData("{\"mode\":\"Shadow\",\"mode\":\"Live\"}")]
    [InlineData("{\"mode\":\"Shadow\",\"options\":false}")]
    public void StepConfig_UnsupportedShape_Rejected(string json)
    {
        Assert.Throws<InvalidPluginExecutionException>(() => new StepConfiguration(json, string.Empty));
    }
    /// <summary>固定選項不可暴露底層字典；字串值不得包含 JSON 引號。</summary>
    [Fact]
    public void StepConfig_Options_AreDetachedPlainScalars()
    {
        var config = new StepConfiguration("{\"mode\":\"Shadow\",\"options\":{\"name\":\"sample\",\"count\":3}}", string.Empty);
        Assert.Equal("sample", config.Options["name"]);
        Assert.Throws<System.NotSupportedException>(() => ((System.Collections.Generic.IDictionary<string, string>)config.Options).Add("other", "bad"));
    }

    /// <summary>非機密設定限制使用字元數，4096可接受、4097拒絕。</summary>
    [Fact]
    public void StepConfig_LengthBoundary_Enforced()
    {
        const string json = "{\"mode\":\"Shadow\"}";
        Assert.Equal(PluginExecutionMode.Shadow, new StepConfiguration(json.PadRight(4096), string.Empty).Mode);
        Assert.Throws<InvalidPluginExecutionException>(() => new StepConfiguration(json.PadRight(4097), string.Empty));
    }
}
