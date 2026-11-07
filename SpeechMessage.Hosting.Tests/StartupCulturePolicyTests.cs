/*
檔案整體說明

目的與定位：保護 P2-01 真正使用生成語系完成度的啟動政策，避免 Development 把英文
空殼宣稱已發布，或教會啟用未發布語系。只呼叫純守門、使用原始生成產物及合成組態，
不建立 Host、載入秘密、修改環境變數或連線外部服務。

主要功能與流程：以正式與開發環境的正反例區分發布政策及明確 opt-in 的偽語系，
並核對編譯組件中存在真實生成完成度型別。缺少資料、空白翻譯或靜態狀態假代替產物
都不能讓測試通過；後續組態案例驗證不可變啟動快照與固定安全錯誤訊息。

使用邊界與維護：每案資料屬於本次呼叫，組態 root 由案例確定 Dispose，沒有共用
可變狀態、背景工作或待清理網路資源。這些結果只屬本機離線守門驗證，不能代替
實際 ICU 子程序或 D365／LINE 安全前置；產品 Main 的接入由組合根契約另行保護。
*/
using Microsoft.Extensions.Configuration;
using SpeechMessage.Hosting;
using SpeechMessage.Localization;
using Xunit;

namespace SpeechMessage.Hosting.Tests;

/// <summary>驗證所有環境的完成度限制及偽語系明確 opt-in，不依賴產品設定或程序文化。</summary>
public sealed class StartupCulturePolicyTests
{
    /// <summary>英文只有部分原始翻譯，所有環境皆不得把它列為已發布。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteReleasedCulture_IsRejectedInEveryEnvironment(bool production)
    {
        Assert.Throws<InvalidOperationException>(() => StartupGuards.ValidateLocalization(
            new LocalizationOptions(["zh-TW", "en"]), production));
    }

    /// <summary>偽語系只在非正式環境明確 opt-in 時可以啟用；缺 opt-in 必須拒絕。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PseudoConfiguredCulture_RequiresExplicitDevelopmentOptIn(bool allowPseudo)
    {
        var options = new LocalizationOptions(allowPseudoLocale: allowPseudo);
        if (allowPseudo) StartupGuards.ValidateLocalization(options, false, ["zh-TW", "qps-ploc"]);
        else Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateLocalization(options, false, ["zh-TW", "qps-ploc"]));
    }

    /// <summary>正式編譯組件必須含生成完成度，不能只憑固定 SupportedCultures 狀態宣稱完成。</summary>
    [Fact]
    public void LocalizationAssembly_ContainsExecutableGeneratedCompleteness()
    {
        var type = typeof(LocalizationOptions).Assembly.GetType("SpeechMessage.Localization.CultureCompleteness");
        Assert.NotNull(type);
        Assert.Equal(true, type.GetMethod("IsComplete")!.Invoke(null, ["zh-TW"]));
        Assert.Equal(false, type.GetMethod("IsComplete")!.Invoke(null, ["en"]));
        Assert.Equal(false, type.GetMethod("IsComplete")!.Invoke(null, ["qps-ploc"]));
    }

    /// <summary>未設定時使用繁中；讀取後的政策不受 root 後續變更影響，也不持有 root。</summary>
    [Fact]
    public void Configuration_DefaultPolicy_IsDetachedFromMutableRoot()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection().Build();
        var options = StartupGuards.ValidateLocalizationConfiguration(configuration, true);
        configuration["Localization:ReleasedCultures:0"] = "en";
        Assert.Equal(["zh-TW"], options.ReleasedCultures);
        Assert.False(options.AllowPseudoLocale);
        Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateLocalizationConfiguration(configuration, true));
    }

    /// <summary>錯誤布林、空白／重複文化、scalar／稀疏／巢狀陣列均拒絕，訊息不能回顯設定值。</summary>
    [Theory]
    [InlineData("Localization:AllowPseudoLocale", "private-invalid-value")]
    [InlineData("Localization:ReleasedCultures", "private-invalid-value")]
    [InlineData("Localization:ReleasedCultures:0", "private-invalid-value")]
    [InlineData("Localization:ReleasedCultures:1", "zh-TW")]
    [InlineData("Localization:ReleasedCultures:0:Nested", "private-invalid-value")]
    [InlineData("Localization:ReleasedCultures:0", " ")]
    public void Configuration_InvalidShapeOrValue_FailsWithoutEcho(string key, string value)
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        var failure = Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateLocalizationConfiguration(configuration, false));
        Assert.DoesNotContain("private-invalid-value", failure.ToString(), StringComparison.Ordinal);
    }

    /// <summary>開發環境明確 opt-in 可用偽語系；相同快照在正式環境必須拒絕。</summary>
    [Fact]
    public void Configuration_PseudoOptIn_IsDevelopmentOnly()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Localization:AllowPseudoLocale"] = "true",
                ["Localization:ReleasedCultures:0"] = "zh-TW",
                ["Localization:ReleasedCultures:1"] = "qps-ploc"
            }).Build();
        Assert.True(StartupGuards.ValidateLocalizationConfiguration(configuration, false).AllowPseudoLocale);
        Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateLocalizationConfiguration(configuration, true));
    }

    /// <summary>超過六十四文化或重複正規化文化，必須停止建表，不能截短或靜默去重。</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(65)]
    public void Configuration_DuplicateOrOversizeCultures_AreRejected(int count)
    {
        var values = Enumerable.Range(0, count).ToDictionary(
            index => "Localization:ReleasedCultures:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => (string?)"zh-TW", StringComparer.Ordinal);
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateLocalizationConfiguration(configuration, false));
    }
}
