using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    /// 由既有 Roslyn 詞法器略過一組括號，讓一般、逐字、原始及插值字串與註解
    /// 各自遵循 C# 語法；不可把逐字字串的反斜線當成逸出而吞掉後續 HTML。
    /// </summary>
    /// <param name="source">本次 Razor 文字。</param>
    /// <param name="start">左括號位置。</param>
    /// <returns>匹配右括號後的絕對位置；只掃描到此邊界，不解析或執行模板。</returns>
    /// <exception cref="InvalidDataException">程式片段不完整，不能安全判定顯示文字邊界。</exception>
    private static int SkipBalanced(string source, int start)
    {
        var open = source[start] == '(' ? SyntaxKind.OpenParenToken : SyntaxKind.OpenBraceToken;
        var close = open == SyntaxKind.OpenParenToken ? SyntaxKind.CloseParenToken : SyntaxKind.CloseBraceToken;
        var depth = 0;
        // 逐 token 列舉只在本次呼叫持有來源；foreach 的確定性清理也涵蓋提前 return。
        // offset 與 initialTokenPosition 同為 start，Span.End 才能直接用於原模板的位置。
        foreach (var token in SyntaxFactory.ParseTokens(source, offset: start, initialTokenPosition: start))
        {
            if (token.ContainsDiagnostics)
                throw new InvalidDataException("I18N415: Razor code boundary is invalid.");
            if (token.IsKind(open)) depth++;
            if (token.IsKind(close) && --depth == 0) return token.Span.End;
        }
        // 無法判定邊界即拒絕，不得把整個檔案餘段靜默當成程式碼而漏算顯示文字。
        throw new InvalidDataException("I18N415: Razor code boundary is incomplete.");
    }

}
