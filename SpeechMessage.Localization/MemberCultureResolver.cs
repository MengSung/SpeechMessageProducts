using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpeechMessage.Localization;

/// <summary>啟動時建立的不可變發布選項，只含語系政策，不含任何使用者或教會資料。</summary>
public sealed class LocalizationOptions
{
    /// <summary>固定繁中預設來源只供複製，不對外暴露或修改，避免每次建構配置同一常數陣列。</summary>
    private static readonly string[] DefaultReleasedCultures = { "zh-TW" };
    /// <summary>已開放語系的固定唯讀清單，預設只有繁中。</summary>
    public IReadOnlyList<string> ReleasedCultures { get; } = Array.Empty<string>();
    /// <summary>只供非正式環境明確 opt-in 的偽語系開關。</summary>
    public bool AllowPseudoLocale { get; }
    /// <summary>複製啟動設定，後續來源修改不得改變已發布政策。</summary>
    /// <param name="releasedCultures">已開放語系；缺省只有 zh-TW。</param>
    /// <param name="allowPseudoLocale">是否允許非正式偽語系。</param>
    public LocalizationOptions(IReadOnlyList<string>? releasedCultures = null, bool allowPseudoLocale = false)
    {
        ReleasedCultures = CultureNames.Copy(releasedCultures ?? DefaultReleasedCultures);
        AllowPseudoLocale = allowPseudoLocale;
    }
    /// <summary>由 host 啟動時呼叫；正式環境出現任何偽語系配置即拒絕，不執行 I/O 或告警傳送。</summary>
    /// <param name="isProduction">由 host 確認的正式環境狀態。</param>
    /// <param name="configuredCultures">host 已載入的教會／預設語系集合。</param>
    public void Validate(bool isProduction, IReadOnlyList<string>? configuredCultures = null)
    {
        if (isProduction && (AllowPseudoLocale || CultureNames.Contains(ReleasedCultures, "qps-ploc")
                || (configuredCultures is not null && CultureNames.Contains(configuredCultures, "qps-ploc"))))
            throw new InvalidOperationException("Pseudo locale configuration is forbidden in Production.");
    }
}

/// <summary>單一成員語言偏好純值，不攜帶身分、授權或任何可變 Session。</summary>
public sealed class MemberLanguagePreference
{
    /// <summary>可能缺少或未開放的文字偏好。</summary>
    public string? Culture { get; }
    /// <summary>只保存不可變偏好字串，是否可用由純 resolver 決定。</summary>
    /// <param name="culture">語言偏好。</param>
    public MemberLanguagePreference(string? culture) => Culture = culture;
}

/// <summary>伺服器提供的不可變教會語系設定；這個值物件本身不授予資料存取權。</summary>
public sealed class ChurchLocaleSettings
{
    /// <summary>教會預設文字語系。</summary>
    public string DefaultCulture { get; } = string.Empty;
    /// <summary>教會已啟用語系的來源隔離唯讀副本。</summary>
    public IReadOnlyList<string> EnabledCultures { get; } = Array.Empty<string>();
    /// <summary>獨立於文字語言的格式文化。</summary>
    public string FormatCulture { get; } = string.Empty;
    /// <summary>複製完整設定，不保留外部可變陣列。</summary>
    /// <param name="defaultCulture">預設文字語系。</param>
    /// <param name="enabledCultures">已啟用語系。</param>
    /// <param name="formatCulture">格式文化。</param>
    public ChurchLocaleSettings(string defaultCulture, IReadOnlyList<string> enabledCultures, string formatCulture = "zh-TW")
    {
        DefaultCulture = CultureNames.Normalize(defaultCulture);
        EnabledCultures = CultureNames.Copy(enabledCultures);
        FormatCulture = CultureNames.Normalize(formatCulture);
    }
}

/// <summary>純語言決定結果，不含教會識別且不可用來更換授權範圍。</summary>
public sealed class ResolvedCulture
{
    /// <summary>已決定的文字語系。</summary>
    public string TextCulture { get; }
    /// <summary>明確格式文化；偽語系固定 en-US。</summary>
    public string FormatCulture { get; }
    /// <summary>只由 resolver 發布完整結果。</summary>
    /// <param name="textCulture">文字語系。</param>
    /// <param name="formatCulture">格式文化。</param>
    internal ResolvedCulture(string textCulture, string formatCulture) { TextCulture = textCulture; FormatCulture = formatCulture; }
}

/// <summary>純函式解析語言政策，不查資料、不記錄成員偏好或更改文化。</summary>
public interface IMemberCultureResolver
{
    /// <summary>偏好、教會預設、繁中依序決定，偏好與預設須同時啟用和開放。</summary>
    /// <param name="preference">本次成員偏好。</param>
    /// <param name="church">已授權教會的設定快照。</param>
    /// <returns>不含任何授權身分的語言結果。</returns>
    ResolvedCulture Resolve(MemberLanguagePreference preference, ChurchLocaleSettings church);
}

/// <summary>只借用不可變啟動選項，每次呼叫獨立解析，不保留任何成員或教會設定。</summary>
public sealed class MemberCultureResolver : IMemberCultureResolver
{
    /// <summary>固定啟動政策，不保存每次解析的成員偏好或教會設定。</summary>
    private readonly LocalizationOptions options;
    /// <summary>借用不可變發布政策。</summary>
    /// <param name="options">啟動選項。</param>
    public MemberCultureResolver(LocalizationOptions options) => this.options = options ?? throw new ArgumentNullException(nameof(options));
    /// <summary>以明確政策決定語言，沒有副作用或資源擁有權。</summary>
    /// <param name="preference">本次偏好。</param>
    /// <param name="church">本次設定。</param>
    /// <returns>完整不可變結果。</returns>
    public ResolvedCulture Resolve(MemberLanguagePreference preference, ChurchLocaleSettings church)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(preference);
        ArgumentNullException.ThrowIfNull(church);
#else
        if (preference is null) throw new ArgumentNullException(nameof(preference));
        if (church is null) throw new ArgumentNullException(nameof(church));
#endif
        var selected = IsEligible(preference.Culture, church) ? CultureNames.Normalize(preference.Culture!)
            : IsEligible(church.DefaultCulture, church) ? church.DefaultCulture : "zh-TW";
        return new ResolvedCulture(selected, string.Equals(selected, "qps-ploc", StringComparison.OrdinalIgnoreCase) ? "en-US" : church.FormatCulture);
    }

    /// <summary>未列入啟用清單的偏好不解析任意文化；偽語系由獨立 opt-in 控制，其他語系須明確已開放。</summary>
    /// <param name="culture">可能缺少或未知的偏好。</param>
    /// <param name="church">唯讀教會設定。</param>
    /// <returns>同時通過啟用及發布政策時為真。</returns>
    private bool IsEligible(string? culture, ChurchLocaleSettings church) => culture is not null
        && CultureNames.Contains(church.EnabledCultures, culture)
        && (string.Equals(culture, "qps-ploc", StringComparison.OrdinalIgnoreCase)
            ? options.AllowPseudoLocale : CultureNames.Contains(options.ReleasedCultures, culture));
}

/// <summary>集中固定設定的語系識別與複製，集合最多六十四個；不建立按外部偏好增長的靜態快取。</summary>
internal static class CultureNames
{
    /// <summary>以 BCL 標準名稱統一大小寫，偽語系使用固定核定名稱；空值／未知格式直接拒絕。</summary>
    /// <param name="culture">啟動設定中的語系。</param>
    /// <returns>標準語系名稱。</returns>
    internal static string Normalize(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture) || culture.Length > 128) throw new ArgumentException("A valid bounded culture is required.", nameof(culture));
        return string.Equals(culture, "qps-ploc", StringComparison.OrdinalIgnoreCase) ? "qps-ploc" : CultureInfo.GetCultureInfo(culture).Name;
    }

    /// <summary>複製並驗證語系設定，大小寫等價的重複項明確拒絕，不靜默覆寫或保留可變來源。</summary>
    /// <param name="cultures">最多六十四個啟動設定值。</param>
    /// <returns>獨佔陣列的唯讀封裝。</returns>
    internal static IReadOnlyList<string> Copy(IReadOnlyList<string> cultures)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(cultures);
#else
        if (cultures is null) throw new ArgumentNullException(nameof(cultures));
#endif
        if (cultures.Count > 64) throw new ArgumentException("Culture settings exceed their bounded count.", nameof(cultures));
        var values = new string[cultures.Count];
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < cultures.Count; index++)
        {
            values[index] = Normalize(cultures[index]);
            if (!identities.Add(values[index])) throw new ArgumentException("Duplicate culture setting.", nameof(cultures));
        }
        return Array.AsReadOnly(values);
    }

    /// <summary>有界且不配置的 OrdinalIgnoreCase 掃描；語系清單上限六十四，不依賴程序文化。</summary>
    /// <param name="cultures">唯讀設定清單。</param>
    /// <param name="culture">本次待查語系。</param>
    /// <returns>明確列入設定時為真。</returns>
    internal static bool Contains(IReadOnlyList<string> cultures, string culture)
    {
        for (var index = 0; index < cultures.Count; index++)
            if (string.Equals(cultures[index], culture, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
