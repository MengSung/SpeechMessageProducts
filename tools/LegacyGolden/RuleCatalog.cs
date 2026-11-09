/*
檔案整體說明
目的與定位：從實際 C# 語法及常值語意列出舊 matcher 正在使用的判斷項，避免把說明清單、註解或另一副本當成規則。
主要功能與流程：Roslyn 解析 Exact、Prefix、Contains 的實際欄位初始化器，並按 GetHandlerType 呼叫順序讀六個 helper 的比較運算；常數經 SemanticModel 解參照，每列保存原始行號與獨立 rule id。它只建立輸入候選與診斷索引，不決定實際路由。
使用邊界與維護：真正意圖由原始來源編譯後執行取得；本檔不模擬比較器、不修正大小寫、不將重複字面詞合併成判斷項。所有語法樹、清單由單次產生呼叫擁有，上限五百一十二項；遇到不支援的來源形狀即拒絕，不能靜默產出缺詞黃金。
*/
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyGolden;

/// <summary>原始碼中的單一比較判斷身分；相同文字在不同位置仍分列，不以顯示內容去重。</summary>
/// <param name="Id">來源相對路徑及原始語法位置形成的穩定規則身分。</param>
/// <param name="Stage">原 matcher 的 Exact、Prefix、Helper 或 Contains 階段。</param>
/// <param name="Method">helper 名稱或實際表名稱。</param>
/// <param name="Keyword">實際常值；不包含僅供說明的 KeywordsList 非使用詞。</param>
/// <param name="DeclaredIntent">原判斷宣告意圖；實際輸入可能被較早規則攔截。</param>
/// <param name="Path">被讀取的白名單相對來源。</param>
/// <param name="Line">原檔一基底起始行。</param>
/// <param name="EndLine">原檔一基底結束行。</param>
/// <param name="Position">供同一原語法樹插樁比對的字元位置，不作為使用者輸入。</param>
internal sealed record LegacyRule(string Id, string Stage, string Method, string Keyword, string DeclaredIntent,
    string Path, int Line, int EndLine, int Position);

/// <summary>只辨識已審核的舊來源結構；任何無法解釋的常值或表項都拒絕發布。</summary>
internal static class RuleCatalog
{
    /// <summary>從原始 compilation 取得表與 helper 判斷；不執行 handler、不接觸外部服務。</summary>
    internal static List<LegacyRule> Extract(Compilation compilation)
    {
        var routingTree = compilation.SyntaxTrees.Single(tree => tree.FilePath == LegacySourceReader.RoutingPath);
        var matcherTree = compilation.SyntaxTrees.Single(tree => tree.FilePath == LegacySourceReader.MatcherPath);
        var routing = routingTree.GetRoot();
        var semantic = compilation.GetSemanticModel(routingTree);
        var result = new List<LegacyRule>();
        AddTable("ExactMatchTable", "Exact");
        AddTable("PrefixMatchTable", "Prefix");
        var route = routing.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "GetHandlerType" && method.ParameterList.Parameters[0].Type is GenericNameSyntax);
        foreach (var condition in route.DescendantNodes().OfType<IfStatementSyntax>())
        {
            if (condition.Condition is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } ||
                member.Expression.ToString() != "MessageKeywordMatcher") continue;
            var methodName = member.Name.Identifier.ValueText;
            var intent = condition.Statement.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Single().Expression!;
            var method = matcherTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(item => item.Identifier.ValueText == methodName);
            var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            if (calls.Length == 0) throw new InvalidDataException("Helper has no supported predicates.");
            foreach (var call in calls)
            {
                if (call.ArgumentList.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax literal || literal.Token.Value is not string keyword)
                    throw new InvalidDataException("Unsupported helper predicate.");
                result.Add(Create(call, "Helper", methodName, keyword, Intent(intent)));
            }
        }
        AddTable("ContainsMatchTable", "Contains");
        if (result.Count is 0 or > 512 || result.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidDataException("Invalid or excessive rule identities.");
        return result;

        // 表內的詞可能是 const 參照；必須由 Roslyn 求值而非字串替換，防止宣告清單誤入。
        void AddTable(string name, string stage)
        {
            var variable = routing.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(item => item.Identifier.ValueText == name);
            var entries = stage == "Exact"
                ? variable.DescendantNodes().OfType<InitializerExpressionSyntax>().Where(item => item.Expressions.Count == 2 && item.Expressions[1] is MemberAccessExpressionSyntax)
                    .Select(item => (Node: (SyntaxNode)item, First: item.Expressions[0], Second: item.Expressions[1]))
                : variable.DescendantNodes().OfType<TupleExpressionSyntax>()
                    .Select(item => (Node: (SyntaxNode)item, First: item.Arguments[0].Expression, Second: item.Arguments[1].Expression));
            var count = 0;
            foreach (var entry in entries)
            {
                if (semantic.GetConstantValue(entry.First).Value is not string keyword) throw new InvalidDataException("Keyword is not a constant.");
                result.Add(Create(entry.Node, stage, name, keyword, Intent(entry.Second)));
                count++;
            }
            if (count == 0) throw new InvalidDataException("Required keyword table is empty.");
        }
    }

    /// <summary>保留 enum 成員原名；來源結構不符時拒絕，避免猜意圖。</summary>
    private static string Intent(ExpressionSyntax expression) => expression is MemberAccessExpressionSyntax member
        ? member.Name.Identifier.ValueText : throw new InvalidDataException("Expected an intent enum member.");

    /// <summary>原始 SyntaxTree 的位置未經換行漂移；同一行多判斷仍由 column 區別。</summary>
    private static LegacyRule Create(SyntaxNode node, string stage, string method, string keyword, string intent)
    {
        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        return new LegacyRule(FormattableString.Invariant($"{node.SyntaxTree.FilePath}:{line}:{span.StartLinePosition.Character + 1}"),
            stage, method, keyword, intent, node.SyntaxTree.FilePath, line, span.EndLinePosition.Line + 1, node.SpanStart);
    }
}
