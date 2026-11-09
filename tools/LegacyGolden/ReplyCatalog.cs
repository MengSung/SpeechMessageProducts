/*
檔案整體說明
目的與定位：以 Roslyn 靜態語法建立全部路由意圖的回覆來源、固定文字、條件與資料處理委派，不把沒有執行的 CRM 流程當作測試結果。
主要功能與流程：從 RouteToHandlerAsync 的實際 switch 逐一連到 IncomingMessageProcessor partial 方法；記錄 reply 呼叫的完整固定文案或插值占位、建構型別、委派方法與所有參數占位，連同原行號與來源身分。另明列 SendReplyAsync 的兩種宿主輸出及 Webhook 四種媒體空方法。
使用邊界與維護：只讀本次白名單轉碼來源，不實例化任何 handler、不查詢資料或傳送 LINE。文字求值僅處理 C# 常見字串組合，未知表達式明標占位，不猜結果。所有集合由本次呼叫擁有；未找到路由對應方法、重複意圖或超出預期結構直接拒絕，不發布部分清單。
*/
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyGolden;

/// <summary>靜態契約抽取器；輸出明確區分固定文字、資料委派與未實作空方法。</summary>
internal static class ReplyCatalog
{
    /// <summary>逐 switch label 抽取 handler；Unknown 沒有正式呼叫，保留 no-op 而不採註解內的回覆。</summary>
    internal static JsonObject Extract(IReadOnlyList<SourceDocument> sources)
    {
        var methods = sources.Where(source => source.Path != LegacySourceReader.RoutingPath && source.Path != LegacySourceReader.MatcherPath)
            .SelectMany(source => CSharpSyntaxTree.ParseText(source.Text, path: source.Path).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();
        MethodDeclarationSyntax Incoming(string name) => methods.Single(method => method.Identifier.ValueText == name &&
            method.Ancestors().OfType<ClassDeclarationSyntax>().First().Identifier.ValueText == "IncomingMessageProcessor");
        var route = Incoming("RouteToHandlerAsync");
        var intents = new JsonObject();
        foreach (var section in route.DescendantNodes().OfType<SwitchSectionSyntax>())
        {
            var label = section.Labels.OfType<CaseSwitchLabelSyntax>().Single();
            var intent = ((MemberAccessExpressionSyntax)label.Value).Name.Identifier.ValueText;
            var call = section.DescendantNodes().OfType<InvocationExpressionSyntax>().SingleOrDefault(item => item.Expression is IdentifierNameSyntax);
            var entry = call is null ? new JsonObject { ["kind"] = "no-op", ["executed"] = false }
                : Describe(Incoming(((IdentifierNameSyntax)call.Expression).Identifier.ValueText));
            entry["routeSource"] = Location(section);
            entry["routeArguments"] = call is null ? new JsonArray() : Arguments(call.ArgumentList);
            intents.Add(intent, entry);
        }
        var media = new JsonObject();
        foreach (var kind in new[] { "Image", "Audio", "Location", "Sticker" })
        {
            var method = methods.Single(item => item.Identifier.ValueText == "Handle" + kind + "MessageAsync");
            media.Add(kind.ToLowerInvariant(), new JsonObject
            {
                ["kind"] = method.Body?.Statements.Count == 0 ? "no-op-comment-only" : "implemented-static-contract",
                ["source"] = Location(method), ["externalExecution"] = false
            });
        }
        return new JsonObject
        {
            ["evidenceKind"] = "static-source-contract-not-crm-execution",
            ["intents"] = intents,
            ["emptyInput"] = Describe(Incoming("RouteMessageAsync")),
            ["replyTransport"] = Describe(Incoming("SendReplyAsync")),
            ["webhookMessages"] = media,
            ["webhookTextEntry"] = Describe(methods.Single(method => method.Identifier.ValueText == "HandleTextMessageAsync")),
            ["webhookDelegation"] = Describe(methods.Single(method => method.Identifier.ValueText == "RouteMessageAsync" &&
                method.Ancestors().OfType<ClassDeclarationSyntax>().First().Identifier.ValueText == "WebhookMessageProcessor"))
        };
    }

    /// <summary>保留每個執行呼叫及分支上下文，註解內的假呼叫不會進入語法節點集合。</summary>
    private static JsonObject Describe(MethodDeclarationSyntax method)
    {
        var variables = method.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToArray();
        var replies = new JsonArray();
        var delegates = new JsonArray();
        var constructors = new JsonArray();
        foreach (var creation in method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            constructors.Add(new JsonObject
            {
                ["type"] = creation.Type.ToString(), ["arguments"] = creation.ArgumentList is null ? new JsonArray() : Arguments(creation.ArgumentList),
                ["source"] = Location(creation), ["condition"] = Conditions(creation),
                ["usingOwned"] = creation.Ancestors().OfType<UsingStatementSyntax>().Any()
            });
        foreach (var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = call.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : call.Expression.ToString();
            if (name is "ConfigureAwait" or "WriteLine" or "LogError" or "AsSpan" or "Trim" or "IsNullOrWhiteSpace" or "IsNullOrEmpty" or "Split") continue;
            var receiver = call.Expression is MemberAccessExpressionSyntax access ? access.Expression.ToString() : "IncomingMessageProcessor";
            var constructed = variables.Where(variable => variable.Identifier.ValueText == receiver && variable.Initializer?.Value is ObjectCreationExpressionSyntax)
                .Select(variable => ((ObjectCreationExpressionSyntax)variable.Initializer!.Value).Type.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            var entry = new JsonObject
            {
                ["type"] = constructed.Length == 1 ? constructed[0] : receiver,
                ["method"] = name, ["arguments"] = Arguments(call.ArgumentList),
                ["condition"] = Conditions(call), ["source"] = Location(call)
            };
            if (name is "ReplyMessageAsync" or "SendMessage" or "SendReplyAsync" or "ReplyTextMessage")
            {
                entry["textTemplate"] = Text(call.ArgumentList.Arguments[^1].Expression, variables);
                replies.Add(entry);
            }
            else delegates.Add(entry);
        }
        return new JsonObject
        {
            ["handler"] = method.Identifier.ValueText,
            ["kind"] = delegates.Count > 0 ? "data-or-method-delegation-with-static-fallbacks" : replies.Count > 0 ? "text-or-template" : "no-op",
            ["executed"] = false, ["source"] = Location(method),
            ["fixedReplies"] = replies, ["delegations"] = delegates, ["constructions"] = constructors
        };
    }

    /// <summary>參數以原表達式占位表示，永不求值識別、token、entity 或服務物件。</summary>
    private static JsonArray Arguments(ArgumentListSyntax arguments) => new(arguments.Arguments
        .Select(argument => (JsonNode?)JsonValue.Create("{" + argument.Expression.WithoutTrivia().ToString() + "}")).ToArray());

    /// <summary>分支及 catch 範圍是靜態條件，不宣稱在本次 matcher 輸入下已選到某個 CRM 分支。</summary>
    private static JsonArray Conditions(SyntaxNode node) => new(node.Ancestors().Reverse().Select(ancestor => ancestor switch
    {
        IfStatementSyntax condition => (condition.Statement.Span.Contains(node.Span) ? "true (" : "false (") + condition.Condition.WithoutTrivia() + ")",
        CatchClauseSyntax => "catch",
        _ => null
    }).Where(value => value is not null).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    /// <summary>只展開字串常值、插值、串接與本地單一 initializer；其餘用明確占位，避免以新執行語意猜回覆。</summary>
    private static string Text(ExpressionSyntax expression, VariableDeclaratorSyntax[] variables, int depth = 0)
    {
        if (depth > 64) throw new InvalidDataException("Reply expression exceeds supported depth.");
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.Token.Value is string text => text,
            InterpolatedStringExpressionSyntax interpolation => string.Concat(interpolation.Contents.Select(part => part switch
            {
                InterpolatedStringTextSyntax segment => segment.TextToken.ValueText,
                InterpolationSyntax value => "{" + value.Expression.WithoutTrivia() + "}",
                _ => throw new InvalidDataException("Unsupported interpolation.")
            })),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) => Text(binary.Left, variables, depth + 1) + Text(binary.Right, variables, depth + 1),
            MemberAccessExpressionSyntax member when member.ToString() == "Environment.NewLine" => "\r\n",
            IdentifierNameSyntax identifier when variables.Count(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText) == 1 =>
                Text(variables.Single(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText).Initializer!.Value, variables, depth + 1),
            _ => "{" + expression.WithoutTrivia() + "}"
        };
    }

    /// <summary>來源行範圍引用 routing.sources 的 hash；不保存絕對機器路徑。</summary>
    internal static JsonObject Location(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return new JsonObject { ["path"] = node.SyntaxTree.FilePath, ["line"] = span.StartLinePosition.Line + 1, ["endLine"] = span.EndLinePosition.Line + 1 };
    }
}
