using System.Collections.Frozen;
using Microsoft.CodeAnalysis.CSharp;

namespace I18nCheck;

/// <summary>
/// 在離線文字掃描中分離 C# token 與 Razor 標記；只回傳程式區塊終點，不執行模板。
/// 每次呼叫獨占來源、括號與標籤堆疊，沒有跨檔快取；混合遞迴最多九十六層，
/// 單一堆疊最多五百一十二層，無法確定邊界即以固定代碼拒絕，避免漏算及堆疊耗盡。
/// </summary>
internal static class RazorCodeBoundary
{
    /// <summary>固定 HTML void 元素不需要結束標籤；凍結集合不承載來源或使用者狀態。</summary>
    private static readonly FrozenSet<string> VoidElements = new[]
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link",
        "meta", "param", "source", "track", "wbr"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 由已確認的左括號開始定位匹配終點；C# 字串的反斜線、雙引號、
    /// 插值與原始字串交給 Roslyn，遇到 Razor 轉換再切回專用標記掃描。
    /// </summary>
    /// <param name="source">生命週期僅限本次計數的完整來源。</param>
    /// <param name="start">左圓括號或左大括號的絕對位置。</param>
    /// <returns>匹配右括號之後的位置，不吞掉其後 HTML。</returns>
    internal static int Skip(string source, int start) => SkipCode(source, start, 0);

    /// <summary>
    /// 逐 token 推進，只有語句起點的直接 HTML 與明確 @ 轉換走標記規則，
    /// 避免把 C# 比較運算子或泛型當成標籤。單一呼叫的堆疊不跨來源保存。
    /// </summary>
    /// <param name="source">本次來源。</param>
    /// <param name="start">起始括號。</param>
    /// <param name="recursion">目前混合語法遞迴深度。</param>
    /// <returns>程式區塊終點。</returns>
    private static int SkipCode(string source, int start, int recursion)
    {
        CheckDepth(recursion);
        var closers = new Stack<SyntaxKind>();
        var statementStart = source[start] == '{';
        for (var index = start; index < source.Length;)
        {
            // 單 token 的 Span 相對本次 offset；leading trivia 包含 C# 註解，
            // trailing trivia 也只由此區域 token 短暫持有，不把詞法器延伸進 Razor 標記。
            var token = SyntaxFactory.ParseToken(source, offset: index);
            var tokenStart = index + token.SpanStart;
            if (tokenStart >= source.Length) break;
            if (source.AsSpan(tokenStart).StartsWith("@<", StringComparison.Ordinal))
            {
                index = SkipMarkup(source, tokenStart + 1, recursion + 1);
                statementStart = true;
                continue;
            }
            if (source.AsSpan(tokenStart).StartsWith("@:", StringComparison.Ordinal))
            {
                index = SkipLine(source, tokenStart + 2);
                statementStart = true;
                continue;
            }
            if (statementStart && IsTagStart(source, tokenStart))
            {
                index = SkipMarkup(source, tokenStart, recursion + 1);
                continue;
            }
            if (token.ContainsDiagnostics || token.Span.Length == 0) throw InvalidBoundary();
            var kind = token.Kind();
            var closer = kind switch
            {
                SyntaxKind.OpenParenToken => SyntaxKind.CloseParenToken,
                SyntaxKind.OpenBraceToken => SyntaxKind.CloseBraceToken,
                SyntaxKind.OpenBracketToken => SyntaxKind.CloseBracketToken,
                _ => SyntaxKind.None
            };
            if (closer != SyntaxKind.None)
            {
                if (closers.Count == 512) throw InvalidBoundary();
                closers.Push(closer);
            }
            else if (kind is SyntaxKind.CloseParenToken or SyntaxKind.CloseBraceToken or SyntaxKind.CloseBracketToken)
            {
                if (closers.Count == 0 || closers.Pop() != kind) throw InvalidBoundary();
                if (closers.Count == 0) return index + token.Span.End;
            }
            statementStart = kind is SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken or SyntaxKind.SemicolonToken or SyntaxKind.ColonToken;
            index += token.FullSpan.Length;
        }
        throw InvalidBoundary();
    }

    /// <summary>
    /// 完整配對範本根標籤及同名巢狀標籤；標記文字中的撇號、引號、括號不是 C#。
    /// void 與自閉合標籤立即結束，模板內 @ 轉換遞迴處理；整段仍屬排除計數範圍。
    /// </summary>
    /// <param name="source">本次來源。</param>
    /// <param name="start">已確認標籤起點。</param>
    /// <param name="recursion">混合語法深度。</param>
    /// <returns>根標籤結束位置。</returns>
    private static int SkipMarkup(string source, int start, int recursion)
    {
        CheckDepth(recursion);
        var root = ReadTag(source, start, recursion);
        if (root.Closing) throw InvalidBoundary();
        if (root.Empty) return root.End;
        var tags = new Stack<string>();
        tags.Push(root.Name);
        for (var index = root.End; index < source.Length;)
        {
            if (source[index] == '@') { index = SkipTransition(source, index, recursion + 1); continue; }
            // DevExtreme 的用戶端樣板區段不是 HTML 標籤；其中字串不能變成標籤配對。
            if (source.AsSpan(index).StartsWith("<%", StringComparison.Ordinal))
            {
                var end = source.IndexOf("%>", index + 2, StringComparison.Ordinal);
                if (end < 0) throw InvalidBoundary();
                index = end + 2;
                continue;
            }
            if (!IsTagStart(source, index)) { index++; continue; }
            var tag = ReadTag(source, index, recursion);
            if (tag.Closing)
            {
                if (tags.Count == 0 || !string.Equals(tags.Pop(), tag.Name, StringComparison.OrdinalIgnoreCase)) throw InvalidBoundary();
                if (tags.Count == 0) return tag.End;
            }
            else if (!tag.Empty)
            {
                if (tags.Count == 512) throw InvalidBoundary();
                tags.Push(tag.Name);
            }
            index = tag.End;
        }
        throw InvalidBoundary();
    }

    /// <summary>
    /// 讀取一個標籤並尊重屬性引號及其中 Razor 運算式；只有真正未被引號包住的
    /// 大於符號才結束標籤。回傳值只保留標籤名稱與位置，不保存屬性或顯示內容。
    /// </summary>
    /// <param name="source">本次來源。</param>
    /// <param name="start">小於符號位置。</param>
    /// <param name="recursion">遞迴深度。</param>
    /// <returns>本次標籤的不可變中繼資料。</returns>
    private static Tag ReadTag(string source, int start, int recursion)
    {
        var index = start + 1;
        var closing = source[index] == '/';
        if (closing) index++;
        var nameStart = index;
        while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '-' or ':' or '_')) index++;
        if (index == nameStart) throw InvalidBoundary();
        var name = source[nameStart..index];
        var quote = '\0';
        for (; index < source.Length; index++)
        {
            if (source[index] == '@') { index = SkipTransition(source, index, recursion + 1) - 1; continue; }
            var character = source[index];
            if (quote != '\0') { if (character == quote) quote = '\0'; continue; }
            if (character is '\'' or '"') { quote = character; continue; }
            if (character != '>') continue;
            var previous = index - 1;
            while (previous > start && char.IsWhiteSpace(source[previous])) previous--;
            return new Tag(name, closing, source[previous] == '/' || VoidElements.Contains(name), index + 1);
        }
        throw InvalidBoundary();
    }

    /// <summary>
    /// 模板標記內的 Razor 轉換獨立判定，支援明確區塊、隱含成員呼叫、
    /// 範本委派及單行文字。以來源位置推進，所有遞迴由共同上限限制。
    /// </summary>
    /// <param name="source">本次來源。</param>
    /// <param name="start">@ 的絕對位置。</param>
    /// <param name="recursion">遞迴深度。</param>
    /// <returns>轉換後位置。</returns>
    private static int SkipTransition(string source, int start, int recursion)
    {
        CheckDepth(recursion);
        var index = start + 1;
        if (index >= source.Length) return index;
        if (source[index] == '@') return index + 1;
        if (source[index] == ':') return SkipLine(source, index + 1);
        if (source[index] is '(' or '{') return SkipCode(source, index, recursion + 1);
        if (IsTagStart(source, index)) return SkipMarkup(source, index, recursion + 1);
        var identifierStart = index;
        while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '.')) index++;
        var identifier = source[identifierStart..index];
        while (index < source.Length)
        {
            if (source[index] is '(' or '[') { index = SkipCode(source, index, recursion + 1); continue; }
            if (source[index] == '.' || source[index] == '?') { index++; continue; }
            if (char.IsLetterOrDigit(source[index]) || source[index] == '_') { index++; continue; }
            break;
        }
        if (identifier is "if" or "for" or "foreach" or "while" or "switch" or "using" or "lock")
        {
            while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
            if (index < source.Length && source[index] == '(') index = SkipCode(source, index, recursion + 1);
            while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
            if (index < source.Length && source[index] == '{') index = SkipCode(source, index, recursion + 1);
        }
        return index;
    }

    /// <summary>只有標籤名稱開頭可進入配對；一般文字的小於符號維持文字語義。</summary>
    /// <param name="source">本次來源。</param>
    /// <param name="index">待檢查位置。</param>
    /// <returns>是否是開啟或關閉標籤的起點。</returns>
    private static bool IsTagStart(string source, int index)
    {
        if (index + 1 >= source.Length || source[index] != '<') return false;
        var name = source[index + 1] == '/' ? index + 2 : index + 1;
        return name < source.Length && char.IsAsciiLetter(source[name]);
    }

    /// <summary>Razor 單行文字只到換行；其內假括號及引號不參與外層 C# 配對。</summary>
    /// <param name="source">本次來源。</param>
    /// <param name="start">行內容起點。</param>
    /// <returns>換行位置或來源終點。</returns>
    private static int SkipLine(string source, int start)
    {
        var end = source.IndexOf('\n', start);
        return end < 0 ? source.Length : end + 1;
    }

    /// <summary>來源可由 repo 提供；超深混合語法須固定拒絕，不讓遞迴耗盡程序堆疊。</summary>
    /// <param name="depth">本次遞迴深度。</param>
    private static void CheckDepth(int depth)
    {
        if (depth >= 96) throw InvalidBoundary();
    }

    /// <summary>拒絕只帶固定代碼，不把來源、路徑、屬性或使用者文字帶入診斷。</summary>
    /// <returns>由命令邊界轉為非零結束碼的輸入例外。</returns>
    private static InvalidDataException InvalidBoundary() => new("I18N415: Razor code boundary is incomplete or invalid.");

    /// <summary>只在本次標記掃描中使用的值，不持有來源或屬性內容。</summary>
    /// <param name="Name">標籤名稱。</param>
    /// <param name="Closing">是否為結束標籤。</param>
    /// <param name="Empty">是否為自閉合或 void 元素。</param>
    /// <param name="End">標籤之後的絕對位置。</param>
    private readonly record struct Tag(string Name, bool Closing, bool Empty, int End);
}
