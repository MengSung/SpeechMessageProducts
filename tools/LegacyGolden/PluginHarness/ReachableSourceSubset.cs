/*
檔案整體說明

目的與定位：提供 P5-02 真實舊專案在安全 guard 之前使用的保守 Roslyn 來源切片；依操作者指定的公開 IPlugin／CodeActivity 入口追蹤來源符號，移除可證明未使用的型別與普通方法，不以專案或工具名稱建立替身。
主要功能與流程：所有入口成員作為根；相依型別保留欄位、初始化、全部建構子、屬性、事件、運算子、終結器及帶 attribute 的回呼。來源參考、泛型引數、base、介面、方法群組、lambda、foreach 與 await 的隱含呼叫形成單次閉包；虛擬／介面呼叫保守保留所有來源同名方法。反射、dynamic、無法解析的可達操作及未支援語法退回完整來源，讓既有 guard 繼續拒絕，絕不改寫危險 API 或方法本體。
使用邊界與維護：本檔只處理呼叫端已完成容量驗證的記憶體 compilation，不讀舊檔、不輸出來源或診斷原文、不載入／執行舊組件、不建立 task、連線或持久快取。閉包、semantic model 與來源參考只由一次 Create 擁有，回傳後沒有 static 保留；回傳 compilation 的生命週期由呼叫端限制。重建後強制重新繫結，缺型別／成員的錯誤留在結果，呼叫端仍須跑原有 guard 並成功 emit 才可執行；不能以切片成功取代安全驗證。合成測試覆蓋未使用危險 helper、各種間接可達性及 fail-closed 邊界，不代表舊程式或實機驗收。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyGolden.PluginHarness;

/// <summary>
/// 只刪除來源宣告的離線可達性分析器。保留的宣告保持原始 token／body；
/// 分析不能證明安全時交回完整輸入，讓既有 guard 與 compiler fail closed。
/// </summary>
public static class ReachableSourceSubset
{
    /// <summary>
    /// 以 metadata 完整名稱指定公開、非抽象的 IPlugin／CodeActivity 來源入口，建立保守來源閉包。
    /// 空根、無效根、反射、dynamic 或可達未解析操作均回傳原 compilation；不代表通過 guard。
    /// 結果已重新繫結，仍可能包含缺成員／using 等編譯錯誤，呼叫端必須檢查原有 guard 及 emit 結果，
    /// 不得降低檢查、執行失敗產物，亦不得保留 compilation 超過本次編譯的生命週期。
    /// </summary>
    /// <param name="compilation">呼叫端擁有且已限制來源容量的原始編譯，不含已載入舊產物。</param>
    /// <param name="entryTypeNames">精確 metadata 型別名稱；巢狀型別使用 Roslyn 的加號名稱，不以專案名猜測。</param>
    /// <param name="cancellationToken">呼叫端取消時立即中止分析並傳遞取消，不發布部分來源。</param>
    /// <returns>原始完整 compilation，或僅刪除宣告且已重新繫結的 compilation；絕非安全執行許可。</returns>
    public static CSharpCompilation Create(CSharpCompilation compilation, IEnumerable<string> entryTypeNames,
        CancellationToken cancellationToken = default)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (entryTypeNames == null)
        {
            throw new ArgumentNullException(nameof(entryTypeNames));
        }

        return new Closure(compilation, cancellationToken).Build(entryTypeNames);
    }

    /// <summary>
    /// 一次分析的唯一可變 owner。集合大小受輸入來源的宣告／節點數限制，
    /// 不共用跨入口、跨專案或跨案例的符號、來源與 semantic model。
    /// </summary>
    private sealed class Closure
    {
        private readonly CSharpCompilation compilation;
        private readonly CancellationToken cancellationToken;
        private readonly Dictionary<SyntaxTree, SemanticModel> models = new();
        private readonly HashSet<ISymbol> types = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ISymbol> typeShapes = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ISymbol> methods = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ISymbol> completeTypes = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<string, List<ISymbol>> membersByName = new(StringComparer.Ordinal);
        private readonly HashSet<string> dispatchedNames = new(StringComparer.Ordinal);
        private readonly Queue<SyntaxNode> pending = new();
        private readonly HashSet<SyntaxNode> queued = new();
        private bool uncertain;

        /// <summary>僅借用呼叫端 compilation；所有集合均為本次同步分析私有，不取得任何外部資源。</summary>
        internal Closure(CSharpCompilation compilation, CancellationToken cancellationToken)
        {
            this.compilation = compilation;
            this.cancellationToken = cancellationToken;
        }

        /// <summary>
        /// 先建派送索引再傳播閉包，避免掃描順序漏掉後方來源實作。
        /// 整份候選完成後才重建；取消、無法證明及編譯錯誤都不冒充可執行結果。
        /// </summary>
        internal CSharpCompilation Build(IEnumerable<string> entryTypeNames)
        {
            var roots = new List<INamedTypeSymbol>();
            foreach (var name in entryTypeNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = string.IsNullOrWhiteSpace(name) ? null : compilation.GetTypeByMetadataName(name);
                if (type == null || !IsEntry(type))
                {
                    return compilation;
                }

                roots.Add(type);
            }

            if (roots.Count == 0)
            {
                return compilation;
            }

            IndexMethodsAndGlobalSyntax();
            foreach (var root in roots)
            {
                KeepCompleteType(root);
                for (var parent = root.BaseType; parent != null; parent = parent.BaseType)
                {
                    KeepCompleteType(parent);
                }
            }

            while (pending.Count != 0 && !uncertain)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Scan(pending.Dequeue());
            }

            if (uncertain)
            {
                return compilation;
            }

            var rewritten = new List<SyntaxTree>();
            var pruner = new Pruner(this);
            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = pruner.Visit(tree.GetRoot(cancellationToken))!;
                rewritten.Add(tree.WithRootAndOptions(root, tree.Options));
            }

            var result = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(rewritten);
            // 強制使用新的語法樹重新繫結；錯誤保留於 compilation，不能自動補碼或放寬 guard。
            result.GetDiagnostics(cancellationToken);
            return result;
        }

        /// <summary>只接受來源公開具體入口，沿繼承鏈辨識 CodeActivity，避免套件或一般類別被誤作根。</summary>
        private static bool IsEntry(INamedTypeSymbol type)
        {
            if (type.DeclaringSyntaxReferences.Length == 0 || type.IsAbstract
                || type.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }

            if (type.AllInterfaces.Any(item => item.ToDisplayString() == "Microsoft.Xrm.Sdk.IPlugin"))
            {
                return true;
            }

            for (var parent = type.BaseType; parent != null; parent = parent.BaseType)
            {
                if (parent.ToDisplayString() == "System.Activities.CodeActivity")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 建立全部來源普通方法名稱索引，供無 body 的 metadata 介面與虛擬派送使用；
        /// assembly／module attributes 與 using 亦進閉包，global statement 不支援時保留完整輸入。
        /// </summary>
        private void IndexMethodsAndGlobalSyntax()
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = Model(tree);
                foreach (var node in tree.GetRoot(cancellationToken).DescendantNodesAndSelf())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (node is GlobalStatementSyntax)
                    {
                        uncertain = true;
                    }
                    else if (node is UsingDirectiveSyntax || node is AttributeListSyntax attributes
                        && attributes.Target != null)
                    {
                        Enqueue(node);
                    }
                    else if (node is MethodDeclarationSyntax || node is PropertyDeclarationSyntax
                        || node is IndexerDeclarationSyntax || node is EventDeclarationSyntax
                        || node is VariableDeclaratorSyntax && node.Parent?.Parent is EventFieldDeclarationSyntax)
                    {
                        var symbol = model.GetDeclaredSymbol(node, cancellationToken);
                        if (symbol == null)
                        {
                            continue;
                        }

                        var name = SimpleName(symbol);
                        if (!membersByName.TryGetValue(name, out var members))
                        {
                            members = new List<ISymbol>();
                            membersByName.Add(name, members);
                        }

                        members.Add(symbol);
                    }
                }
            }
        }

        /// <summary>單次分析內每棵來源樹只借用一個 semantic model，回傳結果不持有本索引。</summary>
        private SemanticModel Model(SyntaxTree tree)
        {
            if (!models.TryGetValue(tree, out var model))
            {
                model = compilation.GetSemanticModel(tree);
                models.Add(tree, model);
            }

            return model;
        }

        /// <summary>節點至多排入一次，避免循環參考、partial 宣告或多個根造成重複掃描與無界工作量。</summary>
        private void Enqueue(SyntaxNode node)
        {
            if (queued.Add(node))
            {
                pending.Enqueue(node);
            }
        }

        /// <summary>
        /// 根與來源基底保留所有成員，包含所有 nested 宣告；
        /// 非根型別仍由 KeepType 保留初始化及派送必需成員，普通方法才依符號裁剪。
        /// </summary>
        private void KeepCompleteType(INamedTypeSymbol type)
        {
            KeepType(type);
            type = type.OriginalDefinition;
            if (type.DeclaringSyntaxReferences.Length == 0 || !completeTypes.Add(type))
            {
                return;
            }

            foreach (var member in type.GetMembers())
            {
                if (member is INamedTypeSymbol nested)
                {
                    KeepCompleteType(nested);
                }
                else
                {
                    KeepSymbol(member);
                }
            }
        }

        /// <summary>
        /// 型別提及本身就保守啟用其全部初始化、ctor、base、非方法成員與多型契約；
        /// 追蹤 constructed 泛型引數及約束，避免只看 OriginalDefinition 漏掉來源實作。
        /// </summary>
        private void KeepType(ITypeSymbol? type)
        {
            if (type == null || !typeShapes.Add(type))
            {
                return;
            }

            if (type.TypeKind == TypeKind.Dynamic || type.TypeKind == TypeKind.Error
                || type.TypeKind == TypeKind.FunctionPointer || type.TypeKind == TypeKind.Pointer)
            {
                uncertain = true;
                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                KeepType(array.ElementType);
                return;
            }

            if (type is ITypeParameterSymbol parameter)
            {
                foreach (var constraint in parameter.ConstraintTypes)
                {
                    KeepType(constraint);
                }

                return;
            }

            if (!(type is INamedTypeSymbol named))
            {
                return;
            }

            foreach (var argument in named.TypeArguments)
            {
                KeepType(argument);
            }

            if (IsReflectionType(named))
            {
                uncertain = true;
            }

            if (named.IsRefLikeType || named.GetAttributes().Any(item =>
                item.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute"))
            {
                uncertain = true;
            }

            named = named.OriginalDefinition;
            if (named.DeclaringSyntaxReferences.Length == 0 || !types.Add(named))
            {
                return;
            }

            KeepType(named.ContainingType);
            KeepType(named.BaseType);
            foreach (var contract in named.Interfaces)
            {
                KeepType(contract);
            }

            foreach (var syntax in named.DeclaringSyntaxReferences)
            {
                Enqueue(syntax.GetSyntax(cancellationToken));
            }

            foreach (var member in named.GetMembers())
            {
                if (member is INamedTypeSymbol)
                {
                    continue;
                }

                if (!(member is IMethodSymbol method) || method.MethodKind != MethodKind.Ordinary
                    || method.IsVirtual || method.IsOverride || method.IsAbstract
                    || method.ExplicitInterfaceImplementations.Length != 0
                    || method.GetAttributes().Length != 0 || ImplementsInterface(named, method))
                {
                    KeepSymbol(member);
                }
            }
        }

        /// <summary>保留介面契約的實作，亦涵蓋 using／框架內部的 IDisposable 等隱含呼叫。</summary>
        private static bool ImplementsInterface(INamedTypeSymbol type, IMethodSymbol method) =>
            type.AllInterfaces.SelectMany(item => item.GetMembers()).OfType<IMethodSymbol>()
                .Any(item => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(item)?.OriginalDefinition,
                    method.OriginalDefinition));

        /// <summary>
        /// 追蹤普通方法及方法群組，將 accessor 轉回屬性／事件 owner，partial 兩端一併保留。
        /// metadata symbol 雖無來源 body，仍先處理派送與泛型引數，不得因此提早返回。
        /// </summary>
        private void KeepSymbol(ISymbol? symbol)
        {
            if (symbol == null)
            {
                return;
            }

            if (symbol is IAliasSymbol alias)
            {
                KeepSymbol(alias.Target);
                return;
            }

            if (symbol is ITypeSymbol type)
            {
                KeepType(type);
                return;
            }

            KeepType(symbol.ContainingType);
            CheckReflectionMember(symbol);
            if (symbol is IMethodSymbol method)
            {
                foreach (var argument in method.TypeArguments)
                {
                    KeepType(argument);
                }

                KeepType(method.ReturnType);
                foreach (var parameter in method.Parameters)
                {
                    KeepType(parameter.Type);
                }

                if (method.IsVirtual || method.IsOverride || method.IsAbstract
                    || method.ContainingType.TypeKind == TypeKind.Interface)
                {
                    KeepDispatch(SimpleName(method));
                }

                method = (method.ReducedFrom ?? method).OriginalDefinition;
                if (!methods.Add(method))
                {
                    return;
                }

                if (method.AssociatedSymbol != null)
                {
                    KeepSymbol(method.AssociatedSymbol);
                }

                if (method.PartialDefinitionPart != null)
                {
                    KeepSymbol(method.PartialDefinitionPart);
                }

                if (method.PartialImplementationPart != null)
                {
                    KeepSymbol(method.PartialImplementationPart);
                }
            }
            else if (symbol is IPropertySymbol property)
            {
                if (property.IsVirtual || property.IsOverride || property.IsAbstract
                    || property.ContainingType.TypeKind == TypeKind.Interface)
                {
                    KeepDispatch(SimpleName(property));
                }

                KeepType(property.Type);
                foreach (var parameter in property.Parameters)
                {
                    KeepType(parameter.Type);
                }
            }
            else if (symbol is IFieldSymbol field)
            {
                KeepType(field.Type);
            }
            else if (symbol is IEventSymbol eventSymbol)
            {
                if (eventSymbol.IsVirtual || eventSymbol.IsOverride || eventSymbol.IsAbstract
                    || eventSymbol.ContainingType.TypeKind == TypeKind.Interface)
                {
                    KeepDispatch(SimpleName(eventSymbol));
                }

                KeepType(eventSymbol.Type);
            }
            else if (symbol is ILocalSymbol local)
            {
                KeepType(local.Type);
            }
            else if (symbol is IParameterSymbol parameter)
            {
                KeepType(parameter.Type);
            }

            foreach (var syntax in symbol.DeclaringSyntaxReferences)
            {
                var node = syntax.GetSyntax(cancellationToken);
                // 欄位 symbol 指向 VariableDeclarator；追蹤包含型別、attribute 與所有初始化的整個宣告。
                Enqueue(node is VariableDeclaratorSyntax && node.Parent?.Parent is BaseFieldDeclarationSyntax field
                    ? field : node);
            }
        }

        /// <summary>不依執行期型別推測派送目標；所有來源同名實作均啟用，其相依及危險 API 留給原 guard。</summary>
        private void KeepDispatch(string name)
        {
            if (dispatchedNames.Add(name) && membersByName.TryGetValue(name, out var candidates))
            {
                foreach (var candidate in candidates)
                {
                    KeepSymbol(candidate);
                }
            }
        }

        /// <summary>顯式介面實作的 Roslyn 名稱可能帶介面前綴，只比較最後的方法名稱以保守涵蓋。</summary>
        private static string SimpleName(ISymbol member) =>
            member.Name.Substring(member.Name.LastIndexOf('.') + 1);

        /// <summary>
        /// 只掃描保留型別的宣告頭，避免未使用普通方法的 body 提早污染閉包；
        /// 其他保留成員完整掃描，lambda 與 local function 均不另行裁剪。
        /// </summary>
        private void Scan(SyntaxNode node)
        {
            if (node is TypeDeclarationSyntax declaration)
            {
                if (declaration is RecordDeclarationSyntax || declaration.ParameterList != null)
                {
                    uncertain = true;
                    return;
                }

                foreach (var child in node.ChildNodes().Where(item => !(item is MemberDeclarationSyntax)))
                {
                    ScanNodes(child);
                }
            }
            else
            {
                ScanNodes(node);
            }
        }

        /// <summary>
        /// 掃描顯式符號、轉換、foreach／await 編譯器模式。
        /// 不能解析的可達呼叫不猜目標；回退完整來源，確保裁剪不掩蓋危險 API。
        /// </summary>
        private void ScanNodes(SyntaxNode root)
        {
            var model = Model(root.SyntaxTree);
            foreach (var node in root.DescendantNodesAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is UnsafeStatementSyntax || node is PointerTypeSyntax || node is FunctionPointerTypeSyntax
                    || node is QueryExpressionSyntax || node is ParenthesizedVariableDesignationSyntax
                    || node is ForEachVariableStatementSyntax || node is CollectionExpressionSyntax
                    || node is AssignmentExpressionSyntax assignment && assignment.Left is TupleExpressionSyntax)
                {
                    uncertain = true;
                    return;
                }

                if (node is ExpressionSyntax expression)
                {
                    var info = model.GetTypeInfo(expression, cancellationToken);
                    KeepType(info.Type);
                    KeepType(info.ConvertedType);
                    KeepSymbol(model.GetConversion(expression, cancellationToken).MethodSymbol);
                    TrackSymbol(model.GetSymbolInfo(expression, cancellationToken),
                        node is InvocationExpressionSyntax || node is ObjectCreationExpressionSyntax
                        || node is ImplicitObjectCreationExpressionSyntax);
                }
                else if (node is AttributeSyntax || node is ConstructorInitializerSyntax)
                {
                    TrackSymbol(model.GetSymbolInfo(node, cancellationToken), required: true);
                }

                if (node is InitializerExpressionSyntax initializer && initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
                {
                    foreach (var item in initializer.Expressions)
                    {
                        TrackSymbol(model.GetCollectionInitializerSymbolInfo(item, cancellationToken), required: true);
                    }
                }

                if (node is CommonForEachStatementSyntax loop)
                {
                    var info = model.GetForEachStatementInfo(loop);
                    if (info.GetEnumeratorMethod == null || info.MoveNextMethod == null || info.CurrentProperty == null)
                    {
                        uncertain = true;
                    }

                    KeepSymbol(info.GetEnumeratorMethod);
                    KeepSymbol(info.MoveNextMethod);
                    KeepSymbol(info.CurrentProperty);
                    KeepSymbol(info.DisposeMethod);
                    KeepSymbol(info.ElementConversion.MethodSymbol);
                    KeepSymbol(info.CurrentConversion.MethodSymbol);
                }
                else if (node is AwaitExpressionSyntax awaitExpression)
                {
                    var info = model.GetAwaitExpressionInfo(awaitExpression);
                    if (info.GetAwaiterMethod == null || info.GetResultMethod == null || info.IsCompletedProperty == null)
                    {
                        uncertain = true;
                    }

                    KeepSymbol(info.GetAwaiterMethod);
                    KeepSymbol(info.GetResultMethod);
                    KeepSymbol(info.IsCompletedProperty);
                }
            }
        }

        /// <summary>
        /// 有歧義時保留所有候選，若整個呼叫無 symbol／候選就不裁剪；
        /// 多載解析錯誤仍會由重新編譯拒絕，不能自動修成另一個危險／不同方法。
        /// </summary>
        private void TrackSymbol(SymbolInfo info, bool required)
        {
            KeepSymbol(info.Symbol);
            foreach (var candidate in info.CandidateSymbols)
            {
                KeepSymbol(candidate);
            }

            if (required && info.Symbol == null && info.CandidateSymbols.Length == 0)
            {
                uncertain = true;
            }
        }

        /// <summary>反射、動態建立、remoting／interop 或 expression tree 可繞過靜態來源閉包，保留完整輸入。</summary>
        private static bool IsReflectionType(INamedTypeSymbol type)
        {
            var ns = type.ContainingNamespace.ToDisplayString();
            var full = type.ToDisplayString();
            return ns == "System.Reflection" || ns.StartsWith("System.Reflection.", StringComparison.Ordinal)
                || ns.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal)
                || ns.StartsWith("System.Runtime.Remoting", StringComparison.Ordinal)
                || ns.StartsWith("System.Linq.Expressions", StringComparison.Ordinal)
                || full == "System.Activator" || full == "System.AppDomain";
        }

        /// <summary>
        /// typeof／GetType 單純取得型別不代表反射執行；Type 的列舉／查找／Invoke 等操作與
        /// Delegate.CreateDelegate 使未參考方法仍可能執行，必須保留完整來源供既有 guard 拒絕。
        /// </summary>
        private void CheckReflectionMember(ISymbol symbol)
        {
            var owner = symbol.ContainingType?.ToDisplayString();
            if (owner == "System.Type" && symbol.Name != "Name" && symbol.Name != "FullName" && symbol.Name != "GetTypeCode"
                || owner == "System.Delegate" && symbol.Name == "CreateDelegate")
            {
                uncertain = true;
            }
        }

        /// <summary>
        /// 只移除整個不可達型別或普通方法；其餘宣告原樣遞迴保留，
        /// 不重寫 body、常值、guard 或任何 side effect，缺成員由重建 compilation 診斷。
        /// </summary>
        private sealed class Pruner : CSharpSyntaxRewriter
        {
            private readonly Closure owner;

            /// <summary>借用目前閉包，只在完整分析完成後同步重建來源樹。</summary>
            internal Pruner(Closure owner)
            {
                this.owner = owner;
            }

            /// <summary>每次只讀取原樹 semantic model；移除判斷依原始符號，不對已修改的樹猜測名稱。</summary>
            public override SyntaxNode? Visit(SyntaxNode? node)
            {
                if (node is BaseTypeDeclarationSyntax || node is DelegateDeclarationSyntax)
                {
                    var symbol = owner.Model(node.SyntaxTree).GetDeclaredSymbol(node, owner.cancellationToken);
                    if (symbol == null || !owner.types.Contains(symbol.OriginalDefinition))
                    {
                        return null;
                    }
                }
                else if (node is MethodDeclarationSyntax method)
                {
                    var symbol = owner.Model(node.SyntaxTree).GetDeclaredSymbol(method, owner.cancellationToken);
                    if (symbol == null || !owner.methods.Contains(symbol.OriginalDefinition))
                    {
                        return null;
                    }
                }

                return base.Visit(node);
            }
        }
    }
}
