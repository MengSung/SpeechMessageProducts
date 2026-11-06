/*
 * 檔案整體說明
 *
 * 目的與定位：用 Roslyn 辨識常值與插值文字，僅在符號可證明時排除診斷呼叫；解析不完整時保守計入。 語法樹與語意模型由單次掃描持有，完成後不放入靜態快取，避免原始文字無界保留。
 *
 * 主要功能與流程：
 * 本檔宣告 SourceScanner。
 * - 固定框架中繼資料只建立一次，範圍限本工具程序的可信平台清單與 ILogger 組件。 不包含來源、request 或教會資訊；數量受程序相依清單限制，程序結束即由執行環境回收。
 * - 建立單檔語法與語意分析；註解從來不是字串常值，因此不會進入計數。
 * - 以同專案所有來源建立一份語意模型集合，避免每檔重建重複的中繼資料。
 * - 每個常值或整個插值字串各計一次；插值運算式中的獨立常值另行計數。
 * 主要操作入口包含 CountCSharp、CreateCompilation、CountRazor、ContainsCjk、IsIgnoredMember、IsDiagnosticArgument。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 tools/I18nCheck.Tests/BoundaryTests.cs、tools/I18nCheck.Tests/I18nContractsTests.cs、
 * tools/I18nCheck.Tests/InputIntegrityTests.cs、tools/I18nCheck.Tests/RazorLexicalBoundaryTests.cs、
 * tools/I18nCheck/RazorScanner.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Extensions.Logging;

namespace I18nCheck;

/// <summary>
/// 用 Roslyn 辨識常值與插值文字，僅在符號可證明時排除診斷呼叫；解析不完整時保守計入。
/// 語法樹與語意模型由單次掃描持有，完成後不放入靜態快取，避免原始文字無界保留。
/// </summary>
internal static class SourceScanner
{
    /// <summary>
    /// 固定框架中繼資料只建立一次，範圍限本工具程序的可信平台清單與 ILogger 組件。
    /// 不包含來源、request 或教會資訊；數量受程序相依清單限制，程序結束即由執行環境回收。
    /// </summary>
    private static readonly ImmutableArray<MetadataReference> References =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? typeof(object).Assembly.Location)
        .Split(Path.PathSeparator).Append(typeof(ILogger).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    /// <summary>建立單檔語法與語意分析；註解從來不是字串常值，因此不會進入計數。</summary>
    /// <param name="source">只在本次呼叫保存的來源。</param>
    /// <returns>未被明確排除的 CJK 字串數。</returns>
    internal static int CountCSharp(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CreateCompilation([tree]);
        return CountCSharp(tree, compilation.GetSemanticModel(tree));
    }

    /// <summary>以同專案所有來源建立一份語意模型集合，避免每檔重建重複的中繼資料。</summary>
    /// <param name="trees">本專案固定候選語法樹。</param>
    /// <returns>只在本專案掃描期間持有來源的編譯描述，不輸出或執行程式。</returns>
    internal static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create("I18nInventory", trees, References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>每個常值或整個插值字串各計一次；插值運算式中的獨立常值另行計數。</summary>
    /// <param name="tree">單一來源語法樹。</param>
    /// <param name="model">同專案的語意模型，用於證明 ILogger／Trace／Debug 邊界。</param>
    /// <returns>顯示文字常值數量。</returns>
    internal static int CountCSharp(SyntaxTree tree, SemanticModel model)
    {
        var count = 0;
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            var hasCjk = node switch
            {
                LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression) => ContainsCjk(literal.Token.ValueText),
                InterpolatedStringExpressionSyntax interpolation => interpolation.Contents.OfType<InterpolatedStringTextSyntax>().Any(text => ContainsCjk(text.TextToken.ValueText)),
                _ => false
            };
            if (hasCjk && !IsIgnoredMember(node) && !IsDiagnosticArgument(node, model)) count++;
        }
        return count;
    }

    /// <summary>Razor 只盤點 HTML 顯示文字，不啟動網頁或執行任何 Razor 程式。</summary>
    /// <param name="source">單一 Razor 來源。</param>
    /// <returns>文字節點與指定屬性的 CJK 片段數。</returns>
    internal static int CountRazor(string source) => RazorScanner.Count(source);

    /// <summary>只包含計畫指定的 BMP CJK 區間，英文、日文假名與 emoji 不誤算。</summary>
    /// <param name="value">語法常值或 HTML 解碼後的片段。</param>
    /// <returns>含任一 U+3400–U+9FFF 或 U+F900–U+FAFF 字元時為真。</returns>
    internal static bool ContainsCjk(string value) => value.Any(character => character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff');

    /// <summary>標記只對其所在成員與子節點生效，必須有非空固定理由；不按註解猜測忽略。</summary>
    /// <param name="node">待判定常值。</param>
    /// <returns>外層成員存在有效 I18nIgnore 標記時為真。</returns>
    private static bool IsIgnoredMember(SyntaxNode node) => node.Ancestors().OfType<MemberDeclarationSyntax>()
        .SelectMany(member => member.AttributeLists).SelectMany(list => list.Attributes)
        .Any(attribute => attribute.Name.ToString().Split('.').Last() is "I18nIgnore" or "I18nIgnoreAttribute"
            && attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax reason
            && reason.IsKind(SyntaxKind.StringLiteralExpression) && !string.IsNullOrWhiteSpace(reason.Token.ValueText));

    /// <summary>僅排除真正診斷符號；未知型別或同名方法保守保留，ILogger 的其他資料引數仍計入。</summary>
    /// <param name="node">候選字串，可能位於插值或其他運算式內。</param>
    /// <param name="model">單次專案的語意模型。</param>
    /// <returns>在 Trace／Debug 引數或 ILogger message／state 樣板內時為真。</returns>
    private static bool IsDiagnosticArgument(SyntaxNode node, SemanticModel model)
    {
        foreach (var argument in node.Ancestors().OfType<ArgumentSyntax>())
        {
            if (argument.Parent?.Parent is not InvocationExpressionSyntax invocation || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) continue;
            var type = method.ContainingType.ToDisplayString();
            if (type is "System.Diagnostics.Trace" or "System.Diagnostics.Debug") return true;
            if (method.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.Logging") continue;
            if (method.ContainingType.Name is not "LoggerExtensions" and not "ILogger") continue;
            if (model.GetOperation(argument) is IArgumentOperation operation && operation.Parameter?.Name is "message" or "state") return true;
        }
        return false;
    }
}
