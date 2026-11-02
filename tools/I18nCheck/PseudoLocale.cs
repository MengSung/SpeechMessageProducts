using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace I18nCheck;

/// <summary>
/// 唯一的具名參數解析器；偽語系計數與 resx 集合驗證共用完全相同的辨識規則。
/// 靜態正則只保存固定文法，無回溯、無來源快取，不保留任何使用者或教會資料。
/// </summary>
internal static class Placeholders
{
    /// <summary>辨識一組大括號內的非空名稱，不把參數任意限縮為 ASCII；巢狀括號不合併。</summary>
    private static readonly Regex Pattern = new(@"\{(?<name>[^{}]+)\}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>僅移除識別成功的整個參數，保留標點、空白與其他 Unicode 字元。</summary>
    /// <param name="text">本次資源文字，不存入任何靜態欄位。</param>
    /// <returns>可按 Rune 計數的非參數文字。</returns>
    internal static string Remove(string text) => Pattern.Replace(text, string.Empty);

    /// <summary>產生 Ordinal 唯讀集合，重複參數及順序不影響跨語系契約。</summary>
    /// <param name="text">本次資源文字。</param>
    /// <returns>只包含參數名稱的不可變集合。</returns>
    internal static FrozenSet<string> Names(string text) => Pattern.Matches(text).Select(match => match.Groups["name"].Value).ToFrozenSet(StringComparer.Ordinal);
}

/// <summary>依 2026-11-03 使用者核定公式擴展文字，只在整段外包圍一次，不修改參數。</summary>
internal static class PseudoLocale
{
    /// <summary>以 Rune 計算非參數字數，再用整數 ceil(2n/5) 補字；不使用程序文化或時鐘。</summary>
    /// <param name="text">完整中性資源，包含必須原樣保留的參數。</param>
    /// <returns>原文後接至少一個 ǂ，最後才加入右全形方括號。</returns>
    internal static string Transform(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var count = 0;
        foreach (var rune in Placeholders.Remove(text).EnumerateRunes())
        {
            _ = rune;
            count++;
        }
        var padding = (int)Math.Max(1, ((2L * count) + 4) / 5);
        return "［" + text + new string('ǂ', padding) + "］";
    }
}
