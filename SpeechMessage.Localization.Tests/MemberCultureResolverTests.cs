/*
 * 檔案整體說明
 *
 * 目的與定位：以純設定測試已啟用／已開放交集、英文空殼及正式偽語系守門；不讀取任何環境、時鐘或使用者狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberCultureResolverTests。
 * - 偏好只有同時啟用及開放才勝出；語言純函式不接受教會身分或改寫授權。
 * - 英文雖然啟用但仍是未開放空殼，不能因存在資源或會友偏好就突破發布閘門。
 * - 已開放但本教會未啟用的偏好與預設皆不能選用，最後明確回繁中。
 * - 偽語系需要明確 opt-in 與教會啟用；啟用後固定 en-US 格式，未允許時回繁中。
 * 測試案例入口包含 Resolve_PreferenceEnabledAndReleased_UsesPreference、Resolve_EnNotReleased_FallsBackToChurchDefault、
 * Resolve_NoEligiblePreferenceOrDefault_UsesZhTw、Resolve_PseudoWhenNotAllowed_FallsBack、
 * Validate_ProductionPseudoConfiguration_Throws、Resolve_SettingsSourceMutation_DoesNotChangeResult、
 * Resolve_UnknownPreference_UsesEligibleDefault。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using Xunit;

namespace SpeechMessage.Localization.Tests;

/// <summary>以純設定測試已啟用／已開放交集、英文空殼及正式偽語系守門；不讀取任何環境、時鐘或使用者狀態。</summary>
public sealed class MemberCultureResolverTests
{
    /// <summary>偏好只有同時啟用及開放才勝出；語言純函式不接受教會身分或改寫授權。</summary>
    [Fact(DisplayName = "偏好同時啟用與開放才可選用")]
    public void Resolve_PreferenceEnabledAndReleased_UsesPreference()
    {
        var resolver = new MemberCultureResolver(new LocalizationOptions(["zh-TW", "en"]));
        var result = resolver.Resolve(new MemberLanguagePreference("EN"), new ChurchLocaleSettings("zh-TW", ["zh-TW", "en"], "de-DE"));
        Assert.Equal("en", result.TextCulture);
        Assert.Equal("de-DE", result.FormatCulture);
    }

    /// <summary>英文雖然啟用但仍是未開放空殼，不能因存在資源或會友偏好就突破發布閘門。</summary>
    [Fact(DisplayName = "英文未開放回教會預設")]
    public void Resolve_EnNotReleased_FallsBackToChurchDefault()
    {
        var result = new MemberCultureResolver(new LocalizationOptions()).Resolve(new MemberLanguagePreference("en"),
            new ChurchLocaleSettings("zh-TW", ["zh-TW", "en"]));
        Assert.Equal("zh-TW", result.TextCulture);
    }

    /// <summary>已開放但本教會未啟用的偏好與預設皆不能選用，最後明確回繁中。</summary>
    [Fact(DisplayName = "偏好及預設不可用時回繁中")]
    public void Resolve_NoEligiblePreferenceOrDefault_UsesZhTw()
    {
        var resolver = new MemberCultureResolver(new LocalizationOptions(["zh-TW", "en"]));
        var result = resolver.Resolve(new MemberLanguagePreference("en"), new ChurchLocaleSettings("en", []));
        Assert.Equal("zh-TW", result.TextCulture);
    }

    /// <summary>偽語系需要明確 opt-in 與教會啟用；啟用後固定 en-US 格式，未允許時回繁中。</summary>
    /// <param name="allowed">是否允許偽語系。</param>
    /// <param name="expected">預期文字語系。</param>
    [Theory(DisplayName = "偽語系必須明確允許且使用固定格式")]
    [InlineData(false, "zh-TW")]
    [InlineData(true, "qps-ploc")]
    public void Resolve_PseudoWhenNotAllowed_FallsBack(bool allowed, string expected)
    {
        var result = new MemberCultureResolver(new LocalizationOptions(allowPseudoLocale: allowed))
            .Resolve(new MemberLanguagePreference("qps-ploc"), new ChurchLocaleSettings("zh-TW", ["zh-TW", "qps-ploc"]));
        Assert.Equal(expected, result.TextCulture);
        Assert.Equal(allowed ? "en-US" : "zh-TW", result.FormatCulture);
    }

    /// <summary>正式環境即使只在已開放或教會設定出現偽語系，也必須拒絕啟動；開發 opt-in 保留。</summary>
    [Fact(DisplayName = "正式環境任何偽語系設定拒絕啟動")]
    public void Validate_ProductionPseudoConfiguration_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new LocalizationOptions(allowPseudoLocale: true).Validate(true));
        Assert.Throws<InvalidOperationException>(() => new LocalizationOptions(["zh-TW", "qps-ploc"]).Validate(true));
        Assert.Throws<InvalidOperationException>(() => new LocalizationOptions().Validate(true, ["zh-TW", "QPS-PLOC"]));
        new LocalizationOptions().Validate(true);
        new LocalizationOptions(allowPseudoLocale: true).Validate(false, ["qps-ploc"]);
    }

    /// <summary>建構後外部修改 options 或教會語系來源陣列，既有 resolver 結果不能改變。</summary>
    [Fact(DisplayName = "語系選項與教會設定複製來源且不可變")]
    public void Resolve_SettingsSourceMutation_DoesNotChangeResult()
    {
        string[] released = ["zh-TW", "en"];
        string[] enabled = ["zh-TW", "en"];
        var options = new LocalizationOptions(released);
        var church = new ChurchLocaleSettings("zh-TW", enabled);
        released[1] = "fr";
        enabled[1] = "fr";
        Assert.Equal("en", new MemberCultureResolver(options).Resolve(new MemberLanguagePreference("en"), church).TextCulture);
        if (options.ReleasedCultures is IList<string> mutable) Assert.Throws<NotSupportedException>(() => mutable[0] = "fr");
        if (church.EnabledCultures is IList<string> mutableChurch) Assert.Throws<NotSupportedException>(() => mutableChurch[0] = "fr");
    }

    /// <summary>無偏好或未知偏好不拋出格式化例外，而是依確定預設回退；不新增動態語系快取。</summary>
    /// <param name="preference">缺少或不支援的偏好。</param>
    [Theory(DisplayName = "缺少與未知偏好使用已開放教會預設")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr-FR")]
    public void Resolve_UnknownPreference_UsesEligibleDefault(string? preference)
    {
        Assert.Equal("zh-TW", new MemberCultureResolver(new LocalizationOptions())
            .Resolve(new MemberLanguagePreference(preference), new ChurchLocaleSettings("zh-TW", ["zh-TW"])).TextCulture);
    }
}
