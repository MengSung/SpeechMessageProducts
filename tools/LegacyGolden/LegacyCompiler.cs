/*
檔案整體說明
目的與定位：真正編譯並執行轉碼後的兩個舊 matcher 檔，保證黃金意圖不是由新寫的路由猜測。
主要功能與流程：原碼 compilation 保持語意與 token 不變，僅加呼叫橋接。另建立只加觀察副作用的追蹤版，在原 return 與 helper 比較處記錄階段／rule id；每筆輸入都先執行原版，再核對追蹤版意圖完全相同。語法、編譯或結果不一致即失敗，不發布候選。
使用邊界與維護：只接受操作者已審核的可信來源，Roslyn 與 collectible AssemblyLoadContext 並非不可信程式沙箱。每次呼叫分別擁有兩個載入 context，委派只在 using 期間有效，Dispose 清除參照並 Unload；沒有跨呼叫可變 static 或服務連線。metadata 以有限 framework byte 快照建立，不保留檔案 handle，編譯 stream 立即釋放。原檔與 stub 清單由產物明記，本版沒有相依 stub。
*/
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyGolden;

/// <summary>單次原碼及追蹤碼 compilation；不快取產品組件或使用者輸入。</summary>
internal static class LegacyCompiler
{
    /// <summary>只借用 net10 framework 的必要中繼資料，不將目前 Host 或 CRM 組件加進舊 matcher 相依。</summary>
    internal static CSharpCompilation Compile(IReadOnlyList<SourceDocument> sources)
    {
        var trees = new[] { LegacySourceReader.RoutingPath, LegacySourceReader.MatcherPath }
            .Select(path => CSharpSyntaxTree.ParseText(sources.Single(source => source.Path == path).Text, path: path)).ToArray();
        if (trees.Any(tree => tree.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error)))
            throw new InvalidDataException("Legacy source has syntax errors.");
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var names = new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "System.Console.dll", "System.Collections.dll", "System.Collections.Immutable.dll", "System.Linq.dll", "System.Memory.dll" };
        var references = names.Select(name => MetadataReference.CreateFromImage(File.ReadAllBytes(Path.Combine(runtime, name))));
        return CSharpCompilation.Create("LegacyGoldenOriginal", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, deterministic: true));
    }

    /// <summary>建立原碼執行 owner；只加入字串至公開 GetHandlerType 的橋接，不改原來源任何語法。</summary>
    internal static Harness OpenOriginal(CSharpCompilation original) => new(original.AddSyntaxTrees(CSharpSyntaxTree.ParseText(ReadBridge(), path: "Bridge.cs")));

    /// <summary>原碼控制流程只加觀察，不重新實作條件；trace 與原版逐筆比對提供語意保護。</summary>
    internal static Harness OpenTraced(CSharpCompilation original, IReadOnlyList<LegacyRule> rules)
    {
        var replacements = original.SyntaxTrees.Select(tree => CSharpSyntaxTree.Create(
            (CSharpSyntaxNode)new Observer(rules).Visit(tree.GetRoot())!, path: tree.FilePath));
        return new Harness(original.RemoveAllSyntaxTrees().AddSyntaxTrees(replacements)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(ReadBridge(), path: "Bridge.cs")));
    }

    /// <summary>讀取內嵌且完整記錄繁中契約的橋接範本；stream 只由本方法持有，立即 Dispose，不把生成碼混成產品顯示文案。</summary>
    private static string ReadBridge()
    {
        using var stream = typeof(LegacyCompiler).Assembly.GetManifestResourceStream("LegacyGolden.HarnessBridge")
            ?? throw new InvalidDataException("Harness bridge resource is missing.");
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }

    /// <summary>只改觀察副作用的語法 visitor；不新增任何比較邏輯，也不替換舊 comparer。</summary>
    private sealed class Observer(IReadOnlyList<LegacyRule> rules) : CSharpSyntaxRewriter
    {
        /// <summary>包住原 helper 比較，先執行原呼叫，再保存已命中的真正 rule id。</summary>
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var rule = rules.SingleOrDefault(item => item.Stage == "Helper" && item.Path == node.SyntaxTree.FilePath && item.Position == node.SpanStart);
            if (rule is null) return base.VisitInvocationExpression(node);
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("GoldenBridge.Observe"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Argument(node.WithoutTrivia()),
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(rule.Id))) }))).WithTriviaFrom(node);
        }

        /// <summary>只在 span 主路由的實際 return 前保存階段，所有原 if／foreach／return 表達式照舊執行。</summary>
        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (node.SyntaxTree.FilePath != LegacySourceReader.RoutingPath || method?.Identifier.ValueText != "GetHandlerType" ||
                method.ParameterList.Parameters[0].Type is not GenericNameSyntax) return base.VisitReturnStatement(node);
            var expression = node.Expression?.ToString();
            string? observer = expression switch
            {
                "exactMatch" => "GoldenBridge.Stage = \"Exact\"; GoldenBridge.Rule = messageStr;",
                "handlerType" when node.Ancestors().OfType<ForEachVariableStatementSyntax>().Any(loop => loop.Expression.ToString() == "PrefixMatchTable") =>
                    "GoldenBridge.Stage = \"Prefix\"; GoldenBridge.Rule = prefix;",
                "handlerType" => "GoldenBridge.Stage = \"Contains\"; GoldenBridge.Rule = keyword;",
                _ when expression?.StartsWith("MessageHandlerType.", StringComparison.Ordinal) == true && expression != "MessageHandlerType.Unknown" =>
                    "GoldenBridge.Stage = \"Helper\";",
                _ => null
            };
            return observer is null ? node : SyntaxFactory.ParseStatement("{ " + observer + " " + node.WithoutTrivia() + " }").WithTriviaFrom(node);
        }
    }

    /// <summary>載入 context 與執行委派的唯一 owner；Dispose 後禁止再執行，重複釋放安全。</summary>
    internal sealed class Harness : IDisposable
    {
        private AssemblyLoadContext? context;
        private Func<string, string[]>? run;

        /// <summary>編譯成功後才載入；編譯診斷只回代碼、不包含來源文字，任一錯誤不留下半成品。</summary>
        internal Harness(CSharpCompilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            if (!result.Success) throw new InvalidDataException("Legacy compilation failed: " + string.Join(",", result.Diagnostics
                .Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Id).Distinct(StringComparer.Ordinal)));
            context = new AssemblyLoadContext("LegacyGolden", isCollectible: true);
            Lifetime = new WeakReference(context);
            try
            {
                stream.Position = 0;
                var assembly = context.LoadFromStream(stream);
                run = assembly.GetType("GoldenBridge", throwOnError: true)!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
                    .CreateDelegate<Func<string, string[]>>();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>弱參照只供測試驗證真正卸載，不延長 context 壽命或允許執行舊組件。</summary>
        internal WeakReference Lifetime { get; }
        /// <summary>回傳與 context 分離的字串陣列；owner 內串行呼叫，不能跨執行緒共用同一 harness。</summary>
        internal string[] Run(string input) => (run ?? throw new ObjectDisposedException(nameof(Harness)))(input);
        /// <summary>先解除唯一強委派，再發起 context 卸載；GC 後弱參照必須回到空基線。</summary>
        public void Dispose()
        {
            run = null;
            context?.Unload();
            context = null;
        }
    }
}
