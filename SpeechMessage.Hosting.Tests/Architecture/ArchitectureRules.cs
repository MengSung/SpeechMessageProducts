/*
 * 檔案整體說明
 *
 * 目的與定位：一個專案在一個實際目標上的唯讀評估資料；不載入產品 DLL，不含使用者或租戶狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 ArchitectureProject、ArchitectureViolation、ArchitectureRules。
 * - 黃金資料與部署工具各以精確名稱分類；產品不得參考工具，工具不得直接參考沙箱，未知名稱仍拒絕。
 * - 所有非測試節點都檢查目標框架，僅精確 PluginHarness 的 net48 具有額外相容例外。
 * - P6-06 對所有專案的直接／已還原相依拒絕九個舊 ASP.NET Core 套件，
 *   精確識別套件而非拒絕全部 AspNetCore 前綴，保留目前合法的 NewtonsoftJson 整合套件。
 * - 驗證完整評估圖；呼叫端擁有輸入，結果只含固定的架構診斷。
 * - F1 以既有 DAG 的 Layer 精確辨識產品元件；共用與未知層級禁止參考 SpeechMessageProducts.*，
 *   產品層與部署工具不被誤判為共用來源，未知專案仍 fail closed。
 * - 檢查 NuGet 已選出的 Framework 相依，而不是只相信 csproj 宣告能提供 net462。
 * - 部署端 Plugin 與 Workflow 都受同一個 v4.6.2 沙箱契約約束。
 * - 以來源快照執行 R2 主機／Plugin 公開邊界、R3 組織字面值白名單及 R9 傳輸／Plugin 參考守門；
 *   ChurchReport以框架入口、MVC繼承及公開簽章閉包判定；明列例外必附理由與證據，其他主機規則不變。
 *   GoodShepherd2.Web 精確列為產品主機，沿用公開外殼與主機互參限制，相似名稱仍拒絕分類。
 *   F2 的產品識別與啟動守門僅在精確專案、完整命名空間及型別名稱吻合，且為頂層非泛型
 *   static class 時允許公開；不擴張公開簽章閉包，也不授權其他主機或一般服務。
 *   公開表面統計沿用同次分類並同步交給可選接收器，不保存回呼或跨掃描狀態。
 * - 以實際 DAG 而非示意圖的位置標記層級；共用核心內Localization為取字底層，其餘同層彼此不可參考。未知專案 fail closed， 加入新產品時必須明確分類並補正反例，不能因名稱相似自動取得高層權限。
 * 測試案例入口包含 ArchitectureProject、ArchitectureSource、ArchitectureViolation、Validate、
 * ValidateSource、ValidateFrameworkAssets、IsSandbox、IsHost、Layer。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 SpeechMessage.Hosting.Tests/Architecture/ArchitectureProjectReader.cs、
 * SpeechMessage.Hosting.Tests/Architecture/ArchitectureRuleTests.cs、SpeechMessage.Hosting.Tests/Architecture/ArchitectureXmlTests.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpeechMessage.Architecture.Tests;

/// <summary>一個專案在一個實際目標上的唯讀評估資料；不載入產品 DLL，不含使用者或租戶狀態。</summary>
/// <param name="Name">唯一專案名稱。</param><param name="Target">本節點目標。</param><param name="Targets">專案全部產品目標。</param><param name="References">此目標啟用的專案參考名稱。</param><param name="Packages">此目標啟用的直接套件名稱。</param><param name="IsTest">是否測試宿主。</param>
internal sealed record ArchitectureProject(string Name, string Target, IReadOnlyList<string> Targets, IReadOnlyList<string> References, IReadOnlyList<string> Packages, bool IsTest)
{
    /// <summary>本專案此目標的 C# 來源快照；缺省為空，供純相依圖合成案例使用。</summary>
    internal IReadOnlyList<ArchitectureSource> Sources { get; init; } = Array.Empty<ArchitectureSource>();

    /// <summary>本目標還原的傳遞相依身分；R9 即使沒有 Compile 來源也必須檢查。</summary>
    internal IReadOnlyList<string> RestoredDependencies { get; init; } = Array.Empty<string>();

    /// <summary>普通組件／FrameworkReference 身分，不以 PackageReference 存在與否取代檢查。</summary>
    internal IReadOnlyList<string> AssemblyReferences { get; init; } = Array.Empty<string>();

    /// <summary>MSBuild 實際 DefineConstants，讓語法樹與當次 Debug／Release 相同。</summary>
    internal IReadOnlyList<string> PreprocessorSymbols { get; init; } = Array.Empty<string>();

    /// <summary>依已評估 Using 項目組成的 global using 語法，補足未執行生成 target 的唯讀評估。</summary>
    internal IReadOnlyList<string> GlobalUsingDirectives { get; init; } = Array.Empty<string>();

    /// <summary>經本工作樹已還原資產解析的 SDK 契約中繼資料路徑；不載入或執行組件。</summary>
    internal IReadOnlyList<string> ContractMetadataPaths { get; init; } = Array.Empty<string>();

    /// <summary>當次ChurchReport明列例外；只能由受版控清單或合成案例提供，不跨掃描保存。</summary>
    internal IReadOnlyList<ArchitectureR2Exception> ChurchReportExceptions { get; init; } = Array.Empty<ArchitectureR2Exception>();

    /// <summary>本節點實際評估組態；不同組態來源及參考不能互相掩蓋。</summary>
    internal string Configuration { get; init; } = "Release";
}

/// <summary>
/// 架構來源掃描所需的單一 C# 檔案快照；文字只在本次同步驗證期間持有，
/// 不保存使用者、租戶、憑證或跨測試可變狀態。
/// </summary>
/// <param name="Path">相對於目前方案根目錄的來源路徑或合成測試路徑。</param>
/// <param name="Text">來源文字快照；呼叫端負責提供並擁有其生命週期。</param>
internal sealed record ArchitectureSource(string Path, string Text);

/// <summary>不含來源程式或秘密值的固定規則違規，供 CI 精確診斷參考邊界。</summary>
/// <param name="Rule">穩定規則代碼。</param><param name="Project">違規專案。</param><param name="Target">當次評估目標。</param><param name="Dependency">造成違規的參考或套件。</param>
internal sealed record ArchitectureViolation(string Rule, string Project, string Target, string Dependency)
{
    /// <summary>來源違規的方案相對路徑；參考違規為空，便於完整身分追蹤及遷移清冊比對。</summary>
    internal string SourcePath { get; init; } = string.Empty;

    /// <summary>發現違規的實際組態，不能以另一組態綠燈遮蔽。</summary>
    internal string Configuration { get; init; } = "Release";
}

/// <summary>可見性調整的實證例外，只含來源型別身分、失敗理由與可重現證據，不授予其他專案權限。</summary>
/// <param name="Symbol">精確完整型別名稱。</param>
/// <param name="Reason">已重現不能internal的失敗現象。</param>
/// <param name="Evidence">測試名稱或頁面驗收情境。</param>
internal sealed record ArchitectureR2Exception(string Symbol, string Reason, string Evidence);

/// <summary>純函式 R2／R3／R4／R9 規則；每次驗證獨立建立結果，不保存跨測試或跨目標的可變靜態圖。</summary>
internal static class ArchitectureRules
{
    /// <summary>
    /// 共用框架已提供的九個舊套件 ID；唯讀固定清單不含租戶、使用者或當次評估狀態，
    /// 由 Validate 同時比對直接參考及還原相依，套件身分以 NuGet 的不分大小寫語意判定。
    /// </summary>
    private static readonly IReadOnlyList<string> LegacyAspNetCorePackages = Array.AsReadOnly(new[]
    {
        "Microsoft.AspNetCore",
        "Microsoft.AspNetCore.Authentication.Cookies",
        "Microsoft.AspNetCore.Mvc",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Server.IIS",
        "Microsoft.AspNetCore.Server.IISIntegration",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.AspNetCore.Session",
        "Microsoft.AspNetCore.StaticFiles"
    });

    /// <summary>驗證完整評估圖；呼叫端擁有輸入，結果只含固定的架構診斷。</summary>
    /// <param name="projects">每目標的獨立節點。</param><returns>違規清單。</returns>
    internal static IReadOnlyList<ArchitectureViolation> Validate(IReadOnlyList<ArchitectureProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var result = new List<ArchitectureViolation>();
        var byName = projects.GroupBy(project => project.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var project in projects)
        {
            // 套件禁用規則適用於任何專案，必須先於測試宿主的產品架構豁免。
            // 同一套件可能同時出現在直接及還原清單，僅產生一筆診斷，不保留來源圖。
            foreach (var package in project.Packages.Concat(project.RestoredDependencies)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (LegacyAspNetCorePackages.Contains(package, StringComparer.OrdinalIgnoreCase))
                {
                    Add("LegacyAspNetCorePackage", package);
                }
            }

            if (!project.IsTest && IsSharedProject(project.Name))
            {
                foreach (var reference in project.References.Where(reference =>
                    reference.StartsWith("SpeechMessageProducts.", StringComparison.Ordinal)))
                {
                    Add("SharedToProduct", reference);
                }
            }

            if (project.IsTest)
            {
                continue;
            }

            var layer = Layer(project.Name);
            if (layer < 0)
            {
                Add("UnknownProject", project.Name);
            }

            // 直接保護產品輸出，不依賴是否已有 Plugin／其他 core 消費這個專案。
            // net48 額外例外只授予精確的舊程式離線宿主；工具層級本身不授予目標豁免，
            // 不能藉孤立工具節點放行其他 Framework，也不影響沙箱自己的 net462 上限。
            if (project.Target != "net462" && project.Target != "net10.0" &&
                !(project.Name == "PluginHarness" && project.Target == "net48"))
            {
                Add("ProductTarget", project.Target);
            }

            var dual = project.Targets.Contains("net462", StringComparer.Ordinal) && project.Targets.Count > 1;
            var sandbox = IsSandbox(project.Name);
            if (sandbox && project.Target != "net462")
            {
                Add("SandboxTarget", project.Target);
            }

            foreach (var package in project.Packages)
            {
                var hostOnly = package.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
                    package.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) ||
                    package == "Microsoft.PowerPlatform.Dataverse.Client";
                var exception = (project.Name is "ToolUtility.Core" or "SpeechMessageProducts.GoodShepherd.Core") &&
                    project.Target == "net10.0" &&
                    package == "Microsoft.PowerPlatform.Dataverse.Client";
                if (hostOnly && (dual || sandbox) && !exception)
                {
                    Add("DualTargetPackage", package);
                }
            }

            foreach (var reference in project.References)
            {
                if (!byName.TryGetValue(reference, out var dependency))
                {
                    Add("MissingProject", reference);
                    continue;
                }

                var frameworkCore = dependency.Targets.Contains("net462", StringComparer.Ordinal) &&
                    dependency.Targets.Contains("net10.0", StringComparer.Ordinal);
                if (dual && !frameworkCore)
                {
                    Add("DualTargetReference", reference);
                }

                if (sandbox && (!frameworkCore || IsSandbox(reference)))
                {
                    Add("SandboxReference", reference);
                }

                if (IsHost(project.Name) && IsHost(reference))
                {
                    Add("HostReference", reference);
                }

                if (IsTool(reference) || (IsTool(project.Name) && IsSandbox(reference)))
                {
                    Add("DeploymentIsolation", reference);
                }

                if (layer >= 0 && Layer(reference) >= layer)
                {
                    Add("LayerDirection", reference);
                }
            }

            // 區域函式只在當次節點的同步驗證期間借用結果，不保存輸入圖或建立背景工作。
            void Add(string rule, string dependency) => result.Add(new ArchitectureViolation(rule, project.Name, project.Target, dependency));
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// 驗證來源層級的 R2、R3、R9 邊界。此掃描只使用呼叫端提供的 detached 文字，
    /// 不載入產品組件、不執行來源、不保存跨操作快取；任何無法辨識的公開宣告或禁止
    /// 參考均採拒絕策略，讓新產品不能藉由未分類語法靜默繞過守門。
    /// </summary>
    /// <param name="projects">每個實際目標或合成案例的來源快照。</param>
    /// <param name="observePublicType">可選的當次公開表面統計接收器，依序接收組態、來源、型別及四類分類；不保存回呼或來源模型。</param>
    /// <returns>不含來源本文的固定架構違規。</returns>
    internal static IReadOnlyList<ArchitectureViolation> ValidateSource(IReadOnlyList<ArchitectureProject> projects,
        Action<string, string, string, string>? observePublicType = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var violations = new List<ArchitectureViolation>();
        // metadata 只由這次同步掃描擁有；所有 compilation 使用結束後按反向順序 Dispose。
        var metadataOwners = new List<AssemblyMetadata>();
        try
        {
            var paths = PlatformContractPaths().Concat(projects.SelectMany(item => item.ContractMetadataPaths))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var references = new List<MetadataReference>();
            foreach (var path in paths)
            {
                var metadata = AssemblyMetadata.CreateFromFile(path);
                metadataOwners.Add(metadata);
                references.Add(metadata.GetReference());
            }

            foreach (var project in projects.Where(item => !item.IsTest))
            {
                if (IsSandbox(project.Name))
                {
                    var dependencies = ReachableDependencies(project, projects);
                    if (dependencies.Any(item => IsAssemblyFamily(item, "SpeechMessage.Payments")))
                    {
                        Add("R9PluginPaymentsReference", project, "SpeechMessage.Payments");
                    }

                    if (dependencies.Any(item => IsAssemblyFamily(item, "System.Drawing")))
                    {
                        Add("R9PluginSystemDrawingReference", project, "System.Drawing");
                    }
                }

                var parseOptions = new CSharpParseOptions(LanguageVersion.Preview,
                    preprocessorSymbols: project.PreprocessorSymbols);
                var trees = project.Sources.Select(source => CSharpSyntaxTree.ParseText(
                    source.Text, parseOptions, source.Path.Replace('\\', '/')))
                    .Concat(project.GlobalUsingDirectives.Count == 0 ? Array.Empty<SyntaxTree>() :
                        new SyntaxTree[] { CSharpSyntaxTree.ParseText(string.Join(Environment.NewLine,
                            project.GlobalUsingDirectives), parseOptions, "<evaluated-global-usings>") }).ToArray();
                var compilation = CSharpCompilation.Create(project.Name, trees, references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var churchReportAllowed = project.Name == "SpeechMessageProducts.ChurchReport"
                    ? ChurchReportPublicSurface(compilation, project.ChurchReportExceptions) : null;
                var inspectedTypes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                foreach (var tree in trees)
                {
                    var path = tree.FilePath;
                    if (tree.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error))
                    {
                        Add("SourceSyntax", project, path, path);
                        continue;
                    }

                    var root = tree.GetRoot();
                    var model = compilation.GetSemanticModel(tree);
                    foreach (var declaration in root.DescendantNodes().Where(node =>
                        node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
                    {
                        if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol ||
                            !IsExternallyVisible(symbol) || !inspectedTypes.Add(symbol))
                        {
                            continue;
                        }

                        var identity = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                        if (churchReportAllowed is not null && observePublicType is not null)
                        {
                            // 與拒絕規則使用同一語意模型及閉包；分類互斥，入口及MVC優先，精確例外其次。
                            var category = symbol.Name is "Program" or "Startup" ? "entry"
                                : IsMvcEntry(symbol) ? "controller"
                                : project.ChurchReportExceptions.Any(item => item.Symbol == identity) ? "exception"
                                : churchReportAllowed.Contains(symbol.OriginalDefinition) ? "signature" : "violation";
                            observePublicType(project.Configuration, path, identity, category);
                        }
                        if (IsHost(project.Name) && (churchReportAllowed is not null
                            ? !churchReportAllowed.Contains(symbol.OriginalDefinition)
                            : symbol.Name != "Program" &&
                              !symbol.Name.EndsWith("Registration", StringComparison.Ordinal) &&
                              !symbol.Name.EndsWith("Endpoints", StringComparison.Ordinal) &&
                              !IsGoodShepherd2PublicContract(project.Name, symbol)))
                        {
                            Add("R2HostPublicType", project, identity, path);
                        }

                        if (IsSandbox(project.Name) && !IsPluginEntry(symbol, compilation))
                        {
                            Add("R2PluginPublicType", project, identity, path);
                        }
                    }

                    // Token.ValueText 由 C# parser 處理 Unicode、verbatim、raw 與插值文字。
                    // 不自行刪註解／字串，避免字串內的偽宣告污染公開 API 與傳輸判定。
                    var literalValues = root.DescendantNodes().OfType<LiteralExpressionSyntax>()
                        .Where(item => item.IsKind(SyntaxKind.StringLiteralExpression))
                        .Select(item => item.Token.ValueText)
                        .Concat(root.DescendantNodes().OfType<InterpolatedStringTextSyntax>()
                            .Select(item => item.TextToken.ValueText));
                    foreach (var value in literalValues.Where(IsOrganizationCode).Distinct(StringComparer.Ordinal))
                    {
                        if (!IsAllowlistedOrganizationLiteral(project.Name, path, value))
                        {
                            Add("R3OrganizationLiteral", project, value, path);
                        }
                    }

                    if (!IsHttpClientProjectAllowed(project) && UsesFrameworkType(root, model,
                        "System.Net.Http.HttpClient", "System.Net.Http"))
                    {
                        Add("R9HttpClientProject", project, path, path);
                    }

                    if (IsSandbox(project.Name) && root.DescendantNodes().OfType<NameSyntax>()
                        .Any(name => IsDrawingName(name, model)))
                    {
                        // 同一目標的參考與來源只報一個禁止家族，避免逐來源重複診斷。
                        if (!violations.Any(item => item.Rule == "R9PluginSystemDrawingReference" &&
                            item.Project == project.Name && item.Target == project.Target &&
                            item.Configuration == project.Configuration))
                        {
                            Add("R9PluginSystemDrawingReference", project, "System.Drawing", path);
                        }
                    }
                }
            }
        }
        finally
        {
            for (var index = metadataOwners.Count - 1; index >= 0; index--)
            {
                metadataOwners[index].Dispose();
            }
        }

        return violations.AsReadOnly();

        void Add(string rule, ArchitectureProject project, string dependency, string path = "") =>
            violations.Add(new ArchitectureViolation(rule, project.Name, project.Target, dependency)
            {
                SourcePath = path,
                Configuration = project.Configuration
            });
    }

    /// <summary>檢查 NuGet 已選出的 Framework 相依，而不是只相信 csproj 宣告能提供 net462。</summary>
    /// <param name="target">已還原的單一 Framework target 節點。</param>
    /// <param name="targetFramework">目前被驗證的目標框架；只有 net48 測試宿主可保留 PluginHarness net48 相依。</param>
    /// <returns>沙箱不可接受的產品名稱，不回傳完整本機路徑。</returns>
    internal static IReadOnlyList<string> ValidateFrameworkAssets(JsonElement target, string targetFramework = "net462")
    {
        var violations = new List<string>();
        foreach (var dependency in target.EnumerateObject())
        {
            if (dependency.Value.GetProperty("type").GetString() == "project" &&
                (!dependency.Value.TryGetProperty("framework", out var framework) ||
                 (framework.GetString() != ".NETFramework,Version=v4.6.2"
                  && !(targetFramework == "net48"
                       && dependency.Name.StartsWith("LegacyGolden.PluginHarness/", StringComparison.Ordinal)))))
            {
                violations.Add(dependency.Name.Split('/')[0]);
            }
        }

        return violations.AsReadOnly();
    }

    /// <summary>借用目前runtime的基礎與MVC契約metadata，供語意繼承辨識；不載入產品DLL或列舉任意磁碟。</summary>
    private static IEnumerable<string> PlatformContractPaths()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Private.CoreLib.dll", "System.Runtime.dll", "System.Net.Http.dll",
            "System.Collections.dll", "System.Linq.dll", "netstandard.dll",
            "Microsoft.AspNetCore.Mvc.Core.dll", "Microsoft.AspNetCore.Mvc.ViewFeatures.dll",
            "Microsoft.AspNetCore.Mvc.Abstractions.dll"
        };
        var paths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ??
            throw new InvalidDataException("Runtime contract metadata is unavailable.");
        return paths.Split(Path.PathSeparator).Where(path => allowed.Contains(Path.GetFileName(path)));
    }

    /// <summary>
    /// 由Program／Startup、真正MVC控制器與ViewComponent展開本專案公開簽章閉包；
    /// 泛型引數、陣列、ref型別、基底與介面均用Roslyn符號，不以Controller等名稱猜測。
    /// 集合只活在當次compilation，visited保證循環及自參考有界；不保存metadata或來源到static。
    /// </summary>
    /// <param name="compilation">本目標與組態的來源編譯模型，不執行產品。</param>
    /// <param name="exceptions">有失敗實證的精確例外，缺理由／證據或重複一律拒絕。</param>
    /// <returns>本次允許公開的型別定義，供同次掃描比對。</returns>
    private static HashSet<INamedTypeSymbol> ChurchReportPublicSurface(CSharpCompilation compilation,
        IReadOnlyList<ArchitectureR2Exception> exceptions)
    {
        var declared = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().Where(node =>
                node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type) declared.Add(type.OriginalDefinition);
        }
        var allowed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<INamedTypeSymbol>();
        foreach (var type in declared.Where(IsExternallyVisible))
            if (type.Name is "Program" or "Startup" || IsMvcEntry(type)) Include(type);
        var exceptionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exception in exceptions)
        {
            if (string.IsNullOrWhiteSpace(exception.Symbol) || string.IsNullOrWhiteSpace(exception.Reason)
                || string.IsNullOrWhiteSpace(exception.Evidence) || !exceptionNames.Add(exception.Symbol))
                throw new InvalidDataException("R2 exception requires unique symbol, reason and evidence.");
            var type = declared.SingleOrDefault(item =>
                item.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == exception.Symbol);
            if (type is null || !IsExternallyVisible(type))
                throw new InvalidDataException("R2 exception does not identify a public source type.");
            Include(type);
        }
        while (pending.TryDequeue(out var type))
        {
            Include(type.BaseType);
            foreach (var contract in type.Interfaces) Include(contract);
            foreach (var parameter in type.TypeParameters)
                foreach (var constraint in parameter.ConstraintTypes) Include(constraint);
            foreach (var member in type.GetMembers().Where(member => member.DeclaredAccessibility is
                Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            {
                switch (member)
                {
                    case IMethodSymbol method:
                        Include(method.ReturnType);
                        foreach (var parameter in method.Parameters) Include(parameter.Type);
                        foreach (var parameter in method.TypeParameters)
                            foreach (var constraint in parameter.ConstraintTypes) Include(constraint);
                        break;
                    case IPropertySymbol property:
                        Include(property.Type);
                        foreach (var parameter in property.Parameters) Include(parameter.Type);
                        break;
                    case IFieldSymbol field: Include(field.Type); break;
                    case IEventSymbol eventSymbol: Include(eventSymbol.Type); break;
                }
            }
        }
        return allowed;

        // 外部泛型的本地引數仍需公開；只將本地宣告入列，外部framework不可能由此增加本地來源。
        void Include(ITypeSymbol? type)
        {
            switch (type)
            {
                case IArrayTypeSymbol array: Include(array.ElementType); break;
                case IPointerTypeSymbol pointer: Include(pointer.PointedAtType); break;
                case INamedTypeSymbol named:
                    foreach (var argument in named.TypeArguments) Include(argument);
                    if (named.ContainingType is not null) Include(named.ContainingType);
                    if (declared.Contains(named.OriginalDefinition) && allowed.Add(named.OriginalDefinition))
                        pending.Enqueue(named.OriginalDefinition);
                    break;
            }
        }
    }

    /// <summary>完整基底鏈與官方MVC組件身分共同辨識；來源偽造同名ControllerBase不取得框架權限。</summary>
    private static bool IsMvcEntry(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var name = current.ToDisplayString();
            if ((name == "Microsoft.AspNetCore.Mvc.ControllerBase" &&
                 current.ContainingAssembly?.Name == "Microsoft.AspNetCore.Mvc.Core") ||
                (name == "Microsoft.AspNetCore.Mvc.ViewComponent" &&
                 current.ContainingAssembly?.Name == "Microsoft.AspNetCore.Mvc.ViewFeatures")) return true;
        }
        return false;
    }

    /// <summary>
    /// 僅辨識 F2 計畫明定的兩個公開組合根契約；專案、命名空間、短名與頂層非泛型
    /// static class 形狀必須全部吻合，避免同名服務、巢狀型別或其他主機取得豁免。
    /// 公開可見性由呼叫端先行檢查；本方法只借用當次符號，不保存模型或建立資源。
    /// </summary>
    /// <param name="project">目前掃描節點的精確專案名稱。</param>
    /// <param name="symbol">當次編譯模型內已確認對外可見的來源型別。</param>
    /// <returns>只在精確契約身分與外殼形狀全部符合時允許公開。</returns>
    private static bool IsGoodShepherd2PublicContract(string project, INamedTypeSymbol symbol) =>
        project == "SpeechMessageProducts.GoodShepherd2.Web" &&
        symbol.TypeKind == TypeKind.Class && symbol.IsStatic && symbol.Arity == 0 &&
        symbol.ContainingType is null &&
        (symbol.ContainingNamespace.ToDisplayString(), symbol.Name) is
            ("SpeechMessageProducts.GoodShepherd2.Web", "GoodShepherd2Product") or
            ("SpeechMessageProducts.GoodShepherd2.Web.Startup", "GoodShepherd2StartupGuard");

    /// <summary>所有包含型別均須公開；internal 容器、私有或 protected 巢狀型別不構成直接公開 API。</summary>
    private static bool IsExternallyVisible(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 使用真正 SDK 組件身分及 Roslyn 的完整繼承圖辨識入口；別名與間接繼承有效，
    /// 名稱相似或在產品來源偽造同命名空間的介面均無權冒充官方契約。
    /// </summary>
    private static bool IsPluginEntry(INamedTypeSymbol symbol, CSharpCompilation compilation)
    {
        if (symbol.TypeKind != TypeKind.Class)
        {
            return false;
        }

        var plugin = compilation.GetTypeByMetadataName("Microsoft.Xrm.Sdk.IPlugin");
        if (plugin?.ContainingAssembly.Name == "Microsoft.Xrm.Sdk" &&
            symbol.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item, plugin)))
        {
            return true;
        }

        var activity = compilation.GetTypeByMetadataName("System.Activities.CodeActivity");
        if (activity?.ContainingAssembly.Name == "System.Activities")
        {
            for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, activity))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>檢查語意型別而非字串；using 別名、完整名稱與 typeof 皆依真正 framework 組件身分辨識。</summary>
    private static bool UsesFrameworkType(SyntaxNode root, SemanticModel model, string fullName, string assemblyName) =>
        root.DescendantNodes().OfType<NameSyntax>().Any(name =>
        {
            var symbol = model.GetTypeInfo(name).Type ?? model.GetSymbolInfo(name).Symbol as ITypeSymbol;
            return symbol?.ToDisplayString() == fullName && symbol.ContainingAssembly.Name == assemblyName;
        });

    /// <summary>禁止 System.Drawing 命名空間／型別與明確 using，不把相似前綴視為同一家族。</summary>
    private static bool IsDrawingName(NameSyntax name, SemanticModel model)
    {
        var text = name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal);
        var symbol = model.GetSymbolInfo(name).Symbol;
        return IsAssemblyFamily(text, "System.Drawing") ||
            (symbol is not null && IsAssemblyFamily(symbol.ContainingNamespace?.ToDisplayString() ??
                symbol.ToDisplayString(), "System.Drawing"));
    }

    /// <summary>
    /// 正式產品的傳輸雙目標例外只有兩個核心；P5-02 的 net48 PluginHarness
    /// 只允許自己的記錄替身使用 HttpClient，不能把例外擴到其他工具或產品。
    /// </summary>
    private static bool IsHttpClientProjectAllowed(ArchitectureProject project) =>
        project.Name is "Line.Messaging" or "LineMessagingProcessor.Core" ||
        (project.Name == "PluginHarness" && project.Target == "net48") ||
        (project.Target == "net10.0" && project.Targets.All(target => target == "net10.0") &&
         Layer(project.Name) >= 0 && !IsSandbox(project.Name) && !IsTool(project.Name));

    /// <summary>僅辨識確定的組織代碼，固定 allowlist 仍須逐精確專案／路徑／值判斷。</summary>
    private static bool IsOrganizationCode(string value) => value is "Jesus" or "JesusBack" or "Sunny" or "SunnyBack" or "Ymllc";

    /// <summary>
    /// 遷移相容例外只保留 Tenancy 的既有缺省組織；P6-03 已刪 ChurchReport 舊送訊殼及其例外。
    /// 只限下列精確檔案，不可擴為產品豁免。
    /// </summary>
    private static bool IsAllowlistedOrganizationLiteral(string project, string path, string value) =>
        project == "SpeechMessage.Tenancy" &&
        path == "SpeechMessage.Tenancy/ConfigurationLineChannelResolver.cs" && value == "Jesus";

    /// <summary>完整組件家族比對，不以 PaymentsPretender 或 DrawingHelper 取得或觸發另一組件的權限。</summary>
    private static bool IsAssemblyFamily(string name, string family) =>
        name == family || name.StartsWith(family + ".", StringComparison.Ordinal);

    /// <summary>
    /// 在目前目標與組態的圖上走訪傳遞參考；visited 有界於輸入圖，循環不會無限遞迴。
    /// 同時納入已還原相依、普通 Reference 與 FrameworkReference，空 Compile 不能豁免。
    /// </summary>
    private static HashSet<string> ReachableDependencies(ArchitectureProject project,
        IReadOnlyList<ArchitectureProject> projects)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<ArchitectureProject>();
        pending.Push(project);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current.Name))
            {
                continue;
            }

            foreach (var dependency in current.References.Concat(current.Packages)
                .Concat(current.RestoredDependencies).Concat(current.AssemblyReferences))
            {
                found.Add(dependency);
                var next = projects.FirstOrDefault(item => item.Name == dependency &&
                    item.Target == project.Target && item.Configuration == project.Configuration);
                if (next is not null)
                {
                    pending.Push(next);
                }
            }
        }

        return found;
    }

    /// <summary>只有明列的三個工具可取得工具層級；產品不得反向依賴，也不授予目標、傳輸或沙箱豁免。</summary>
    private static bool IsTool(string name) => name is "SpeechMessage.Dynamics.Deployment" or "LegacyGolden" or "PluginHarness";

    /// <summary>部署端 Plugin 與 Workflow 都受同一個 v4.6.2 沙箱契約約束。</summary>
    private static bool IsSandbox(string name) => name is "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows";

    /// <summary>三個精確主機具有獨立啟動與授權邊界，不得彼此參考產品外殼；名稱前綴不授予主機權限。</summary>
    private static bool IsHost(string name) => name is "SpeechMessageProducts.ChurchReport"
        or "SpeechMessageProducts.LineWebhook" or "SpeechMessageProducts.GoodShepherd2.Web";

    /// <summary>
    /// 已知產品來源依明確 DAG 分類排除，共用及未知來源則受 SharedToProduct 守門；
    /// 未分類的產品前綴不能自動取得豁免，避免以名稱偽裝產品邊界。
    /// </summary>
    private static bool IsSharedProject(string name) =>
        !(name.StartsWith("SpeechMessageProducts.", StringComparison.Ordinal) && Layer(name) >= 0)
        && !IsSandbox(name)
        && !IsTool(name);

    /// <summary>
    /// 以實際 DAG 而非示意圖的位置標記層級；共用核心內Localization為取字底層，其餘同層彼此不可參考。未知專案 fail closed，
    /// 加入新產品時必須明確分類並補正反例，不能因名稱相似自動取得高層權限。
    /// </summary>
    private static int Layer(string name) => name switch
    {
        "Line.Messaging" or "Line.Messaging.PluginSubset" or "PowerPlatform.Dataverse.Client" => 0,
        // P4-02 付款核心向下參考 Localization 的 Money；反向仍由嚴格層序拒絕。
        "SpeechMessage.Payments" => 8,
        // P2-05明定Localization是共用核心取字底層；反向仍由嚴格層序拒絕。
        "SpeechMessage.Localization" => 5,
        "LineMessagingProcessor.Core" => 10,
        "ToolUtility.Core" or "ToolUtility.Core.PluginSubset" => 20,
        "SpeechMessage.Tenancy" or "SpeechMessageProducts.GoodShepherd.Core" => 30,
        // P2診斷owner抽取後Hosting不再依賴ToolUtility；舊net10消費者向此基礎層委派。
        // 正反圖及實際方案共同禁止Hosting反向引用舊工具，不能讓雙目標或Plugin取得主機資源。
        "SpeechMessage.Hosting" => 32,
        "LineMessagingProcessor" or "SpeechMessage.Payments.Workflows" => 35,
        "ToolUtility" or "LineMessagingProcessor.Workflows" or "LineMessagingProcessor.RichMenus" => 40,
        "LineMessagingProcessor.AspNetCore" or "SpeechMessage.Payments.AspNetCore" => 50,
        "SpeechMessageProducts.GoodShepherd.Hosting" => 60,
        "SpeechMessageProducts.ChurchReport" or "SpeechMessageProducts.LineWebhook"
            or "SpeechMessageProducts.GoodShepherd2.Web" or "SpeechMessage.Dynamics.Plugins"
            or "SpeechMessage.Dynamics.Workflows" => 100,
        "SpeechMessage.Dynamics.Deployment" or "LegacyGolden" or "PluginHarness" => 110,
        _ => -1
    };
}
