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
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Pattern.Matches(template)) required.Add(match.Groups["name"].Value);
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
}
