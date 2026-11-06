/*
 * 檔案整體說明
 *
 * 目的與定位：離線詞法掃描 Razor 的 HTML 文字節點及四種顯示屬性；不建置、執行或解譯產品模板。 固定正則無回溯，來源與候選字串只在單次掃描持有，沒有靜態內容快取。
 *
 * 主要功能與流程：
 * 本檔宣告 RazorScanner。
 * - HTML／Razor 註解與 script／style 內容均不屬於此階段定義的 HTML 文字節點。
 * - 僅計畫指定四種屬性，可使用單引號、雙引號或未加引號的值。
 * - 先去除非顯示區與 Razor 運算式，再按 HTML 節點邊界計算，不輸出來源片段。
 * - 移除 @ 運算式及純程式區塊；控制流程只跳過條件，保留其內實際 HTML。
 * 主要操作入口包含 Count、StripExpressions、SkipBalanced。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 tools/I18nCheck/SourceScanner.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace I18nCheck;

/// <summary>
/// 離線詞法掃描 Razor 的 HTML 文字節點及四種顯示屬性；不建置、執行或解譯產品模板。
/// 固定正則無回溯，來源與候選字串只在單次掃描持有，沒有靜態內容快取。
/// </summary>
internal static class RazorScanner
{
    /// <summary>HTML／Razor 註解與 script／style 內容均不屬於此階段定義的 HTML 文字節點。</summary>
    private static readonly Regex Hidden = new(@"<!--[\s\S]*?-->|@\*[\s\S]*?\*@|<script\b[^>]*>[\s\S]*?</script\s*>|<style\b[^>]*>[\s\S]*?</style\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>標籤掃描保留引號內的大於符號，避免把屬性誤拆成文字。</summary>
    private static readonly Regex Tags = new("<(?:\"[^\"]*\"|'[^']*'|[^'\">])*>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>僅計畫指定四種屬性，可使用單引號、雙引號或未加引號的值。</summary>
    private static readonly Regex Attributes = new("(?:^|\\s)(?:title|placeholder|alt|value)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>先去除非顯示區與 Razor 運算式，再按 HTML 節點邊界計算，不輸出來源片段。</summary>
    /// <param name="source">大小已受檔案層限制的 Razor 文字。</param>
    /// <returns>含 CJK 的文字節點與指定屬性個數。</returns>
    internal static int Count(string source)
    {
        var html = StripExpressions(Hidden.Replace(source, string.Empty));
        var previous = 0;
        var count = 0;
        foreach (Match tag in Tags.Matches(html))
        {
            if (SourceScanner.ContainsCjk(WebUtility.HtmlDecode(html[previous..tag.Index]))) count++;
            count += Attributes.Matches(tag.Value).Count(attribute => SourceScanner.ContainsCjk(WebUtility.HtmlDecode(attribute.Groups["value"].Value)));
            previous = tag.Index + tag.Length;
        }
        if (SourceScanner.ContainsCjk(WebUtility.HtmlDecode(html[previous..]))) count++;
        return count;
    }

    /// <summary>移除 @ 運算式及純程式區塊；控制流程只跳過條件，保留其內實際 HTML。</summary>
    /// <param name="source">已移除註解與隱藏元素的來源。</param>
    /// <returns>僅供此呼叫計數的 HTML 候選。</returns>
    private static string StripExpressions(string source)
    {
        var result = new StringBuilder(source.Length);
        for (var index = 0; index < source.Length;)
        {
            if (source[index] != '@') { result.Append(source[index++]); continue; }
            index++;
            if (index == source.Length) break;
            if (source[index] == '@') { result.Append('@'); index++; continue; }
            if (source[index] is '(' or '{') { index = SkipBalanced(source, index); continue; }
            var start = index;
            while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '.')) index++;
            var identifier = source[start..index];
            if (identifier.Length == 0) continue;
            while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
            if (index < source.Length && source[index] == '(') index = SkipBalanced(source, index);
            if (identifier is "functions" or "code")
            {
                while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
                if (index < source.Length && source[index] == '{') index = SkipBalanced(source, index);
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// 分別辨識 C# 與 Razor 標記邊界；程式內的範本仍整段排除，不擴大顯示計數。
    /// C# 字串與註解由 Roslyn 處理，不能把 Razor 範本委派當成純 C# 語法錯誤。
    /// </summary>
    /// <param name="source">本次 Razor 文字。</param>
    /// <param name="start">左括號位置。</param>
    /// <returns>匹配右括號後的絕對位置；只掃描到此邊界，不解析或執行模板。</returns>
    /// <exception cref="InvalidDataException">程式片段不完整，不能安全判定顯示文字邊界。</exception>
    private static int SkipBalanced(string source, int start) => RazorCodeBoundary.Skip(source, start);
}
