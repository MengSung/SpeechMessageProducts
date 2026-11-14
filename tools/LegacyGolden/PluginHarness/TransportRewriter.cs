/*
檔案整體說明

目的與定位：把舊 Plugin／Workflow 的 HTTP、RestSharp 與檔案建構改寫成 harness 記錄或 scratch 入口，並拒絕不能證明安全的傳輸、程序、反射及實體檔案操作。

主要功能與流程：使用改寫前的 Roslyn semantic model 識別 alias 與完整型別名稱，逐樹轉換 HttpClient、RestClient、FileStream、File／Directory 與文字 reader／writer 的路徑建構。URL 常值本身不構成拒絕條件；只有無法替換的 WebRequest/WebClient、Socket/TcpClient 等明確傳輸型別會分類為網路阻擋。純資料型別不因位於 System.Net namespace 而整批拒絕。工具組件內只明列容許既有六個純離線 Rest 相容型別，其他工具 API 仍不可由來源直接使用。改寫後重新建立 semantic model 驗證，不依註解或字串裡的 API 名稱判定；IOrganizationService 是假服務契約，不視為連外。其他檔案、反射或執行逃逸限制仍拒絕，但由編譯失敗分類承載。錯誤只回固定代碼與檔名，不回原始碼、URL、token 或例外本文。

使用邊界與維護：本檔不建立傳輸、不開啟檔案、不執行改寫產物，僅保存單次編譯的語法樹與有界診斷。ScratchFiles 的路徑映射／容量及釋放由 runtime owner 負責，HttpClient 的固定 handler 由記錄替身擁有；來源不得覆寫 handler、透過原始網路型別或反射逃逸。源碼驗證不能替代 runner 的隔離 AppDomain、不可用 proxy 與 finally 卸載。新增 API 必須有精確攔截與負向測試，不得因依賴出現未知 API 就放寬邊界。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyGolden.PluginHarness;

/// <summary>改寫後的單次編譯候選；診斷非空即拒絕 emit，不能交給 runner。</summary>
internal sealed class TransportRewriteResult
{
    /// <summary>保存區域 Roslyn compilation 與固定診斷，不保存執行 owner 或網路資源。</summary>
    internal TransportRewriteResult(CSharpCompilation compilation, IEnumerable<string> diagnostics)
    {
        Compilation = compilation;
        Diagnostics = diagnostics.Take(20).ToArray();
    }

    /// <summary>只有安全診斷為空時才可繼續 emit 的改寫候選。</summary>
    internal CSharpCompilation Compilation { get; }

    /// <summary>有界固定代碼與檔名；不包含原始碼、URL 或秘密。</summary>
    internal IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>以真實 symbol 進行受限替換；不把相同文字的自訂類別或註解當成框架 API。</summary>
internal sealed class TransportRewriter : CSharpSyntaxRewriter
{
    private const string Runtime = "global::LegacyGolden.PluginHarness.";
    private readonly SemanticModel model;
    private readonly List<string> diagnostics;
    private readonly bool rewrittenPass;
    private static readonly string[] FileMethods = { "ReadAllBytes", "ReadAllText", "ReadAllLines", "WriteAllBytes", "WriteAllText", "WriteAllLines", "AppendAllText", "AppendAllLines", "Exists", "Delete" };
    private static readonly string[] OfflineRestTypes = { "RestSharp.RestClient", "RestSharp.RestRequest", "RestSharp.IRestResponse", "RestSharp.RestResponse", "RestSharp.Method", "RestSharp.ParameterType" };
    private static readonly string[] PureIoTypes = { "Stream", "MemoryStream", "TextReader", "TextWriter", "StringReader", "StringWriter", "BinaryReader", "BinaryWriter",
        "IOException", "InvalidDataException", "FileNotFoundException", "EndOfStreamException", "FileMode", "FileAccess", "FileShare", "SeekOrigin", "Path", "StreamReader", "StreamWriter", "FileStream" };
    private static readonly string[] HttpDataTypes = { "HttpClient", "HttpMessageInvoker", "HttpRequestMessage", "HttpResponseMessage", "HttpContent", "StringContent", "ByteArrayContent", "StreamContent", "MultipartContent", "MultipartFormDataContent", "FormUrlEncodedContent", "HttpMethod", "HttpCompletionOption", "HttpRequestException", "HttpResponseHeaders", "HttpRequestHeaders", "HttpContentHeaders", "MediaTypeHeaderValue", "AuthenticationHeaderValue", "HttpHeaders", "NameValueHeaderValue", "ContentDispositionHeaderValue", "HttpResponseMessageExtensions", "HttpClientExtensions", "HttpContentExtensions" };
    private static readonly string[] UninterceptedNetworkTypes =
    {
        "System.Net.WebRequest", "System.Net.HttpWebRequest", "System.Net.WebClient",
        "System.Net.Sockets.Socket", "System.Net.Sockets.TcpClient", "System.Net.Sockets.TcpListener",
        "System.Net.Sockets.UdpClient", "System.Net.Sockets.NetworkStream",
        "System.Net.Dns", "System.Net.HttpListener", "System.Net.Mail.SmtpClient",
        "System.Net.WebSockets.WebSocket", "System.Net.WebSockets.ClientWebSocket",
        "System.Net.NetworkInformation.Ping"
    };

    /// <summary>每棵樹共用其原始 semantic model，診斷 owner 是此次 Rewrite 呼叫。</summary>
    private TransportRewriter(SemanticModel model, List<string> diagnostics, bool rewrittenPass)
    {
        this.model = model;
        this.diagnostics = diagnostics;
        this.rewrittenPass = rewrittenPass;
    }

    /// <summary>改寫前後各掃描一次；原始未知呼叫不可因語法轉換消失，改寫結果也不可引入未攔截操作。</summary>
    internal static TransportRewriteResult Rewrite(CSharpCompilation compilation)
    {
        var diagnostics = new List<string>();
        var original = compilation;
        foreach (var tree in original.SyntaxTrees)
        {
            var rewriter = new TransportRewriter(original.GetSemanticModel(tree), diagnostics, false);
            var root = rewriter.Visit(tree.GetRoot())!;
            compilation = compilation.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(root, tree.Options));
        }

        if (diagnostics.Count == 0)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var validator = new TransportRewriter(compilation.GetSemanticModel(tree), diagnostics, true);
                validator.Visit(tree.GetRoot());
            }
        }

        return new TransportRewriteResult(compilation, diagnostics.Distinct(StringComparer.Ordinal));
    }

    /// <summary>拒絕 dynamic、unsafe、extern 與 P/Invoke；這些機制會使靜態傳輸／檔案邊界不可證明。</summary>
    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (node == null)
        {
            return null;
        }

        if (node is UnsafeStatementSyntax || node is PointerTypeSyntax || node is FunctionPointerTypeSyntax
            || node is MethodDeclarationSyntax method && method.Modifiers.Any(SyntaxKind.ExternKeyword))
        {
            Block(node, "UnsafeExecutionApiRefused");
        }

        if (node is ExpressionSyntax expression && model.GetTypeInfo(expression).Type?.TypeKind == TypeKind.Dynamic)
        {
            Block(node, "DynamicExecutionRefused");
        }

        return base.Visit(node);
    }

    /// <summary>只攔截可解析的指定建構；handler 參數、未知衍生傳輸或未支援的路徑建構拒絕。</summary>
    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var type = model.GetTypeInfo(node).Type?.ToDisplayString();
        if (!rewrittenPass && type == "System.Net.Http.HttpClient")
        {
            if (node.ArgumentList != null && node.ArgumentList.Arguments.Count != 0)
            {
                Block(node, "CustomHttpHandlerRefused");
                return node;
            }

            return node.WithType(SyntaxFactory.ParseTypeName(Runtime + "RecordingHttpClient"))
                .WithInitializer((InitializerExpressionSyntax?)Visit(node.Initializer));
        }

        if (!rewrittenPass && type == "RestSharp.RestClient")
        {
            return node.WithType(SyntaxFactory.ParseTypeName(Runtime + "RecordingRestClient"))
                .WithArgumentList((ArgumentListSyntax?)Visit(node.ArgumentList))
                .WithInitializer((InitializerExpressionSyntax?)Visit(node.Initializer));
        }

        if (!rewrittenPass && type == "System.IO.FileStream")
        {
            if (node.Initializer != null || node.ArgumentList == null || node.ArgumentList.Arguments.Count < 2 || node.ArgumentList.Arguments.Count > 4
                || model.GetTypeInfo(node.ArgumentList.Arguments[0].Expression).Type?.SpecialType != SpecialType.System_String)
            {
                Block(node, "FileStreamConstructorRefused");
                return node;
            }

            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(Runtime + "ScratchFiles.Open"),
                (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);
        }

        if (!rewrittenPass && (type == "System.IO.StreamWriter" || type == "System.IO.StreamReader")
            && node.ArgumentList != null && node.ArgumentList.Arguments.Count > 0
            && model.GetTypeInfo(node.ArgumentList.Arguments[0].Expression).Type?.SpecialType == SpecialType.System_String)
        {
            return RewriteTextStream(node, type == "System.IO.StreamWriter");
        }

        CheckType(model.GetTypeInfo(node).Type, node, construction: true);
        return base.VisitObjectCreationExpression(node);
    }

    /// <summary>target-typed new 不得繞過建構改寫；明確轉為型別後使用同一攔截規則。</summary>
    public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        var type = model.GetTypeInfo(node).Type;
        if (type?.ToDisplayString() == "System.Net.Http.HttpClient" || type?.ToDisplayString() == "RestSharp.RestClient"
            || type?.ToDisplayString() == "System.IO.FileStream" || type?.ToDisplayString() == "System.IO.StreamWriter" || type?.ToDisplayString() == "System.IO.StreamReader")
        {
            Block(node, "ImplicitTransportConstructorRefused");
        }

        CheckType(type, node, construction: true);
        return base.VisitImplicitObjectCreationExpression(node);
    }

    /// <summary>所有實體 File／Directory 呼叫都轉固定 scratch 入口；未知方法不允許落回原始框架。</summary>
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var symbol = model.GetSymbolInfo(node).Symbol as IMethodSymbol;
        var type = symbol?.ContainingType.ToDisplayString();
        var name = symbol?.Name;
        if (!rewrittenPass && (type == "System.IO.File" || type == "System.IO.Directory"))
        {
            if (name != null && (type == "System.IO.File" && FileMethods.Contains(name, StringComparer.Ordinal)
                || type == "System.IO.Directory" && (name == "CreateDirectory" || name == "Exists")))
            {
                var mappedName = type == "System.IO.Directory" && name == "Exists" ? "DirectoryExists" : name;
                return node.WithExpression(SyntaxFactory.ParseExpression(Runtime + "ScratchFiles." + mappedName))
                    .WithArgumentList((ArgumentListSyntax)Visit(node.ArgumentList)!);
            }

            if (type == "System.IO.File" && (name == "OpenRead" || name == "OpenWrite") && node.ArgumentList.Arguments.Count == 1)
            {
                var args = SyntaxFactory.SeparatedList(new[]
                {
                    (ArgumentSyntax)Visit(node.ArgumentList.Arguments[0])!,
                    SyntaxFactory.Argument(SyntaxFactory.ParseExpression("global::System.IO.FileMode." + (name == "OpenRead" ? "Open" : "OpenOrCreate"))),
                    SyntaxFactory.Argument(SyntaxFactory.ParseExpression("global::System.IO.FileAccess." + (name == "OpenRead" ? "Read" : "Write"))),
                    SyntaxFactory.Argument(SyntaxFactory.ParseExpression("global::System.IO.FileShare.Read"))
                });
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(Runtime + "ScratchFiles.Open"), SyntaxFactory.ArgumentList(args)).WithTriviaFrom(node);
            }

            if (type == "System.IO.File" && name == "Open" && node.ArgumentList.Arguments.Count >= 2 && node.ArgumentList.Arguments.Count <= 4)
            {
                return node.WithExpression(SyntaxFactory.ParseExpression(Runtime + "ScratchFiles.Open"))
                    .WithArgumentList((ArgumentListSyntax)Visit(node.ArgumentList)!);
            }

            Block(node, "FileApiRefused");
            return node;
        }

        if (type == "System.IO.Path" && name == "GetTempFileName"
            || type == "System.Environment" && name != "get_NewLine"
            || type == "System.Type" && name != "GetTypeCode" && name != "get_Name" && name != "get_FullName"
            || type == "System.Delegate" && name == "CreateDelegate")
        {
            Block(node, "ExecutionEscapeRefused");
        }

        if (type == "System.Threading.Tasks.Task" && (name == "Run" || name == "Delay")
            || type?.StartsWith("System.Threading.Tasks.TaskFactory", StringComparison.Ordinal) == true
            || type?.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) == true && name == "ContinueWith"
            || type == "System.Threading.ThreadPool" || type == "System.Threading.Thread" || type == "System.Threading.Timer"
            || type == "System.Timers.Timer")
        {
            Block(node, "BackgroundExecutionRefused");
        }

        CheckType(symbol?.ContainingType, node, construction: false);
        return base.VisitInvocationExpression(node);
    }

    /// <summary>方法群組也必須檢查，避免 File.WriteAllText 指派成 delegate 後逃過 invocation 攔截。</summary>
    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var symbol = model.GetSymbolInfo(node).Symbol;
        var type = symbol?.ContainingType;
        if (symbol is IMethodSymbol && (type?.ToDisplayString() == "System.IO.File" || type?.ToDisplayString() == "System.IO.Directory")
            && !(node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node))
        {
            Block(node, "FileMethodGroupRefused");
        }

        if (type?.ToDisplayString() == "System.Environment" && symbol?.Name != "NewLine"
            || type?.ToDisplayString() == "System.AppDomain"
            || type?.ToDisplayString() == "System.Type" && symbol?.Name != "Name" && symbol?.Name != "FullName" && symbol?.Name != "GetTypeCode")
        {
            Block(node, "ExecutionEscapeRefused");
        }

        CheckType(type, node, construction: false);
        return base.VisitMemberAccessExpression(node);
    }

    /// <summary>別名、using static 與完整型別的 symbol 都接受相同驗證；RestClient 型別改成記錄型別防止靜態 dispatch 逃逸。</summary>
    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var symbol = model.GetSymbolInfo(node).Symbol;
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }

        if (symbol is INamedTypeSymbol type)
        {
            if (!rewrittenPass && type.ToDisplayString() == "RestSharp.RestClient")
            {
                // 完整 qualified name 由 VisitQualifiedName 處理，避免產生 RestSharp.global::...。
                if (!(node.Parent is QualifiedNameSyntax) && !(node.Parent is AliasQualifiedNameSyntax))
                {
                    return SyntaxFactory.ParseName(Runtime + "RecordingRestClient").WithTriviaFrom(node);
                }
            }

            CheckType(type, node, construction: false);
        }
        else if (symbol is IMethodSymbol method)
        {
            CheckType(method.ContainingType, node, construction: false);
            if (method.ContainingType.ToDisplayString() == "System.IO.File" || method.ContainingType.ToDisplayString() == "System.IO.Directory")
            {
                Block(node, "FileMethodGroupRefused");
            }
        }

        return base.VisitIdentifierName(node);
    }

    /// <summary>完整 RestSharp.RestClient 及 namespace alias 也替換整個型別，不依文字搜尋。</summary>
    public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
    {
        if (!rewrittenPass && model.GetSymbolInfo(node).Symbol is INamedTypeSymbol type && type.ToDisplayString() == "RestSharp.RestClient")
        {
            return SyntaxFactory.ParseName(Runtime + "RecordingRestClient").WithTriviaFrom(node);
        }

        return base.VisitQualifiedName(node);
    }

    /// <summary>禁止自訂 HttpClient／handler／RestClient 衍生類利用 base 建構逃離記錄替身。</summary>
    public override SyntaxNode? VisitSimpleBaseType(SimpleBaseTypeSyntax node)
    {
        var type = model.GetTypeInfo(node.Type).Type;
        if (type?.ToDisplayString() == "System.Net.Http.HttpClient" || type?.ToDisplayString() == "RestSharp.RestClient"
            || type?.ToDisplayString() == "LegacyGolden.PluginHarness.RecordingHttpClient" || type?.ToDisplayString() == "LegacyGolden.PluginHarness.RecordingRestClient")
        {
            Block(node, "TransportSubclassRefused");
        }

        CheckType(type, node, construction: false);
        return base.VisitSimpleBaseType(node);
    }

    /// <summary>文字檔路徑先導向 scratch stream；append 的判斷保留原運算式與單次求值，不修改常值。</summary>
    private ObjectCreationExpressionSyntax RewriteTextStream(ObjectCreationExpressionSyntax node, bool writer)
    {
        var args = node.ArgumentList!.Arguments;
        if (args.Any(item => item.NameColon != null))
        {
            Block(node, "NamedTextFileConstructorRefused");
            return node;
        }

        var skipAppend = writer && args.Count >= 2 && model.GetTypeInfo(args[1].Expression).Type?.SpecialType == SpecialType.System_Boolean;
        var mode = !writer ? "global::System.IO.FileMode.Open" : "global::System.IO.FileMode.Create";
        if (skipAppend)
        {
            mode = "(" + args[1].Expression.ToFullString() + ") ? global::System.IO.FileMode.Append : global::System.IO.FileMode.Create";
            Visit(args[1]);
        }

        var openArgs = SyntaxFactory.SeparatedList(new[]
        {
            (ArgumentSyntax)Visit(args[0])!,
            SyntaxFactory.Argument(SyntaxFactory.ParseExpression(mode)),
            SyntaxFactory.Argument(SyntaxFactory.ParseExpression("global::System.IO.FileAccess." + (writer ? "Write" : "Read"))),
            SyntaxFactory.Argument(SyntaxFactory.ParseExpression("global::System.IO.FileShare.Read"))
        });
        var stream = SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(Runtime + "ScratchFiles.Open"), SyntaxFactory.ArgumentList(openArgs));
        var newArgs = new List<ArgumentSyntax> { SyntaxFactory.Argument(stream) };
        newArgs.AddRange(args.Skip(skipAppend ? 2 : 1).Select(item => (ArgumentSyntax)Visit(item)!));
        return node.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(newArgs)));
    }

    /// <summary>集中定義來源可使用的傳輸／檔案型別；CRM 注入介面不列入拒絕清單。</summary>
    private void CheckType(ITypeSymbol? type, SyntaxNode node, bool construction)
    {
        if (type == null)
        {
            return;
        }

        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var full = type.ToDisplayString();
        // Rest 相容面和工具共用組件，但六個明列型別只記錄、不開網路；不能按組件整批誤擋。
        // 其餘工具公開 API 包含發布與讀取功能，不是舊來源可借用的安全能力；不可放行整個 namespace。
        // 第二遍才容許由本rewriter插入的記錄替身與scratch呼叫，不能讓來源自行取得它們。
        if (!rewrittenPass && type.ContainingAssembly?.Name == typeof(TransportRewriter).Assembly.GetName().Name
            && !OfflineRestTypes.Contains(full, StringComparer.Ordinal))
        {
            Block(node, "HarnessApiRefused");
        }

        if (construction && (full == "System.Threading.Thread" || full == "System.Threading.Timer"
            || full == "System.Timers.Timer" || full.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)))
        {
            Block(node, "BackgroundExecutionRefused");
        }

        if (ns == "System.IO" && !PureIoTypes.Contains(type.Name, StringComparer.Ordinal))
        {
            Block(node, "FileApiRefused");
        }

        if (rewrittenPass && construction && (full == "System.Net.Http.HttpClient" || full == "System.IO.FileStream" || full == "RestSharp.RestClient"))
        {
            Block(node, "UninterceptedConstructorRefused");
        }

        if (UninterceptedNetworkTypes.Contains(full, StringComparer.Ordinal))
        {
            Block(node, "NetworkApiRefused");
        }

        if ((ns == "System.Net.Http" || ns.StartsWith("System.Net.Http.", StringComparison.Ordinal))
            && !HttpDataTypes.Contains(type.Name, StringComparer.Ordinal) && ns != "System.Net.Http.Formatting")
        {
            Block(node, "HttpTransportApiRefused");
        }

        if (full == "Microsoft.Xrm.Sdk.Client.OrganizationServiceProxy" || full == "Microsoft.Xrm.Tooling.Connector.CrmServiceClient"
            || full == "Microsoft.PowerPlatform.Dataverse.Client.ServiceClient" || full == "System.Activator" || full == "System.AppDomain"
            || ns.StartsWith("System.Reflection", StringComparison.Ordinal) || ns.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal)
            || ns.StartsWith("System.Runtime.Remoting", StringComparison.Ordinal) || ns.StartsWith("System.Linq.Expressions", StringComparison.Ordinal)
            || ns.StartsWith("System.IO.IsolatedStorage", StringComparison.Ordinal) || ns.StartsWith("System.IO.Pipes", StringComparison.Ordinal)
            || ns.StartsWith("System.IO.MemoryMappedFiles", StringComparison.Ordinal) || ns.StartsWith("System.Data.SqlClient", StringComparison.Ordinal)
            || ns.StartsWith("System.Data.Odbc", StringComparison.Ordinal) || ns.StartsWith("System.Data.OleDb", StringComparison.Ordinal)
            || ns.StartsWith("System.Configuration", StringComparison.Ordinal) || ns.StartsWith("Microsoft.Win32", StringComparison.Ordinal)
            || ns.StartsWith("System.Management", StringComparison.Ordinal)
            || ns.StartsWith("System.ServiceModel", StringComparison.Ordinal) || ns == "System.Web" && type.Name != "HttpUtility"
            || ns == "System.Diagnostics" && (type.Name == "Process" || type.Name == "ProcessStartInfo" || type.Name == "EventLog" || type.Name == "EventLogTraceListener"))
        {
            Block(node, "ExternalExecutionApiRefused");
        }

        if (ns == "RestSharp" && type.Name == "IRestClient")
        {
            Block(node, "RestClientInterfaceRefused");
        }
    }

    /// <summary>診斷容量固定二十筆；重複節點不使輸出無界成長，也不保存 symbol 或來源。</summary>
    private void Block(SyntaxNode node, string code)
    {
        if (diagnostics.Count < 20)
        {
            var diagnostic = code + ":" + Path.GetFileName(node.SyntaxTree.FilePath);
            if (!diagnostics.Contains(diagnostic, StringComparer.Ordinal))
            {
                diagnostics.Add(diagnostic);
            }
        }
    }
}
