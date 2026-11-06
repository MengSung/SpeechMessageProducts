using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SpeechMessage.Localization;

/// <summary>
/// 以與 I18nCheck 相同的非巢狀大括號文法處理具名參數。靜態正則只保存固定文法，
/// 所有值、名稱及 MatchEvaluator 僅存在於一次呼叫，不快取模板、context 或使用者參數。
/// </summary>
internal static class NamedTextFormatter
{
    /// <summary>固定文法附一秒逾時；不使用 net10 專屬正則功能，保持 net462 結果一致。</summary>
    private static readonly Regex Pattern = new(@"\{(?<name>[^{}]+)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>先驗證全部參數集合，再以單次 Replace 插入已格式化值；插入值不再次解析。</summary>
    /// <param name="template">已選定的原文，最大三萬二千七百六十八字元。</param>
    /// <param name="culture">明確且不可變的格式文化。</param>
    /// <param name="args">本次借用的參數，最多二百五十六個不同名稱。</param>
    /// <returns>本次操作擁有的完整字串。</returns>
    internal static string Format(string template, CultureInfo culture, TextArg[] args)
    {
        if (template.Length > 32_768 || args.Length > 256) throw new FormatException("Text formatting exceeds its bounded input budget.");
        var required = ParameterNames(template);
        var supplied = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var arg in args)
        {
            if (string.IsNullOrEmpty(arg.Name) || !required.Contains(arg.Name) || supplied.ContainsKey(arg.Name))
                throw new FormatException("Text parameter names must exactly match the template.");
            supplied.Add(arg.Name, string.Empty);
        }
        if (supplied.Count != required.Count) throw new FormatException("Text parameter names must exactly match the template.");
        if (required.Count == 0) return template;
        // 先完成完整集合驗證，才允許參數值執行自訂格式化；錯誤呼叫不能引發不必要的外部副作用。
        foreach (var arg in args)
        {
            var value = arg.Value is IFormattable formattable ? formattable.ToString(null, culture) : arg.Value?.ToString();
            supplied[arg.Name] = value ?? string.Empty;
        }
        return Pattern.Replace(template, match => supplied[match.Groups["name"].Value]);
    }

    /// <summary>供覆寫候選與實際格式化共用同一份非巢狀參數文法，不保留任何模板或 Match。</summary>
    /// <param name="template">已限制長度的單次原文。</param>
    /// <returns>本次操作擁有、Ordinal 比較的名稱集合。</returns>
    internal static HashSet<string> ParameterNames(string template)
    {
        if (template.Length > 32_768) throw new FormatException("Text template exceeds its bounded input budget.");
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Pattern.Matches(template))
            if (required.Add(match.Groups["name"].Value) && required.Count > 256)
                throw new FormatException("Text parameter count exceeds its bounded input budget.");
        return required;
    }

    /// <summary>只計算受限模板的最長 UTF-16 長度，不配置展開後字串，也不重解析插入文字。</summary>
    /// <param name="template">待檢查的受限模板。</param>
    /// <param name="churchNameLength">已核定完整最長名稱的 UTF-16 長度。</param>
    /// <returns>替換每個 churchName 後的長度；未知參數沒有可證明上限，直接拒絕。</returns>
    internal static long MaximumLength(string template, int churchNameLength)
    {
        long length = template.Length;
        foreach (Match match in Pattern.Matches(template))
        {
            if (!string.Equals(match.Groups["name"].Value, "churchName", StringComparison.Ordinal))
                throw new FormatException("A constrained template has no declared parameter length.");
            length += churchNameLength - match.Length;
        }
        return length;
    }
}
