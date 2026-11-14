/*
檔案整體說明

目的與定位：實作 P5-02 逐入口舊專案的 Roslyn 記憶體編譯介面，將來源閉包交給 TransportRewriter 驗證與改寫，供隔離 AppDomain runner 執行已成功產生的位元組。

主要功能與流程：先完成有界來源讀取，解析 net48 reference assemblies、harness、CRM／Workflow／JSON 及核定舊套件參考，建立語意模型後改寫；只有真正不可攔截的網路 API 診斷分類為 BlockedByNetworkApi，其他安全邊界拒絕分類為 CompileFailed，成功 emit 才交付 Generated。任何失敗僅回固定狀態與前二十筆錯誤代碼、檔名，不輸出診斷原文、機密或絕對路徑；來源含硬寫 token 不構成拒編條件。

使用邊界與維護：本檔不 Assembly.Load、不執行舊型別、不建立背景 task 或外部連線。來源、Roslyn 物件、參考位元組均屬單次編譯，metadata 由記憶體建立避免映射來源檔的長期 handle；emit stream 由 using 釋放。CompilationResult 擁有輸出 byte[]，Dispose 清零並移除參考；呼叫端須在隔離執行後卸載 domain 再釋放結果。安全拒絕優先於語法錯誤，避免把未攔截 API 誤報可執行。維護時驗證真舊來源與合成失敗／阻擋案例。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;

namespace LegacyGolden.PluginHarness;

/// <summary>
/// 單次編譯結果與輸出位元組的唯一 owner。Generated 只代表可交給離線 runner，
/// 不代表實機驗收；呼叫端必須以 using 限制來源編譯產物的保留期間。
/// </summary>
public sealed class CompilationResult : IDisposable
{
    /// <summary>建立區域結果；錯誤診斷凍結為唯讀集合，不保留 Roslyn 診斷或來源樹。</summary>
    internal CompilationResult(string status, IEnumerable<string> diagnostics, byte[]? assemblyBytes = null, string? entryType = null)
    {
        Status = status;
        Diagnostics = Array.AsReadOnly(diagnostics.Take(20).ToArray());
        AssemblyBytes = assemblyBytes;
        EntryType = entryType;
    }

    /// <summary>固定三態之一：Generated、CompileFailed 或 BlockedByNetworkApi，不包含例外原文。</summary>
    public string Status { get; }

    /// <summary>前二十筆固定代碼與來源檔名；不包含路徑、訊息、原始碼片段或秘密。</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>只在 Generated 有值；本物件擁有此陣列，Dispose 後清零且回傳 null，不能跨執行快取。</summary>
    public byte[]? AssemblyBytes { get; private set; }

    /// <summary>入口專案內第一個實作 IPlugin 或繼承 CodeActivity 的完整型別名，無入口則為 null。</summary>
    public string? EntryType { get; }

    /// <summary>冪等清除由本結果擁有的編譯位元組；不負責卸載 runner 所擁有的 AppDomain。</summary>
    public void Dispose()
    {
        if (AssemblyBytes != null)
        {
            Array.Clear(AssemblyBytes, 0, AssemblyBytes.Length);
            AssemblyBytes = null;
        }
    }
}

/// <summary>受限舊來源編譯器；公開 API 不載入產物，所有執行生命週期都交給呼叫端。</summary>
public static class LegacyProjectCompiler
{
    private static readonly string[] PackageAssemblyNames = { "Microsoft.Xrm.Sdk.dll", "Microsoft.Crm.Sdk.Proxy.dll", "Microsoft.Xrm.Sdk.Workflow.dll" };
    private static readonly string[] FrameworkNames = { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Data.dll", "System.Data.DataSetExtensions.dll",
        "System.Xml.dll", "System.Xml.Linq.dll", "System.Net.Http.dll", "System.Activities.dll", "System.Runtime.Serialization.dll", "System.Drawing.dll",
        "System.Web.dll", "System.Configuration.dll", "System.ServiceModel.dll", "Microsoft.CSharp.dll", "System.Numerics.dll" };

    /// <summary>
    /// 從核定來源根目錄的單一第一層專案建立閉包並編譯。路徑與讀取失敗回 CompileFailed，
    /// 不執行專案建置事件；呼叫端取得結果後負責 Dispose 與隔離執行。
    /// </summary>
    public static CompilationResult Compile(string legacyRoot, string projectName)
    {
        try
        {
            return CompileInput(BoundedSourceReader.ReadProject(legacyRoot, projectName));
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            return new CompilationResult("CompileFailed", Enumerable.Repeat("SourceRootInvalid", 1));
        }
    }

    /// <summary>
    /// 為不依賴舊方案的合成測試提供相同安全編譯管線。最多 512 段、每段 UTF-8 1 MiB、
    /// 合計 16 MiB；來源只存在本次同步呼叫，例外訊息與來源本文不進結果。
    /// </summary>
    public static CompilationResult CompileSources(IEnumerable<string> sources)
    {
        if (sources == null)
        {
            return new CompilationResult("CompileFailed", Enumerable.Repeat("SourceRequired", 1));
        }

        var files = new List<SourceFile>();
        long total = 0;
        foreach (var source in sources.Take(513))
        {
            if (source == null || source.Length > 1024 * 1024 || files.Count >= 512)
            {
                return new CompilationResult("CompileFailed", Enumerable.Repeat("SourceCapacityExceeded", 1));
            }

            var size = Encoding.UTF8.GetByteCount(source);
            total += size;
            if (size > 1024 * 1024 || total > 16L * 1024 * 1024)
            {
                return new CompilationResult("CompileFailed", Enumerable.Repeat("SourceCapacityExceeded", 1));
            }

            files.Add(new SourceFile("Synthetic/Source" + files.Count.ToString(CultureInfo.InvariantCulture) + ".cs", source, size, string.Empty));
        }

        return CompileInput(new ProjectInput("Synthetic", files, Array.Empty<string>()));
    }

    /// <summary>共用分類與公開編譯 API；先改寫並證明來源邊界，再 emit 真正完整 DLL。</summary>
    internal static CompilationResult CompileInput(ProjectInput input)
    {
        if (input.ReadIssues.Count != 0 || input.Sources.Count == 0)
        {
            return new CompilationResult("CompileFailed", input.ReadIssues.Count == 0 ? Enumerable.Repeat("NoReadableCSharpSource", 1) : input.ReadIssues);
        }

        try
        {
            var trees = input.Sources.Select(source => CSharpSyntaxTree.ParseText(source.Text,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest).WithPreprocessorSymbols("TRACE"), source.RelativePath, Encoding.UTF8)).ToArray();
            var compilation = CSharpCompilation.Create("LegacyGoldenCompiled", trees, FrameworkReferences(input.LegacyRoot),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, deterministic: true,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));
            var rewritten = TransportRewriter.Rewrite(compilation);
            if (rewritten.Diagnostics.Count != 0)
            {
                var status = rewritten.Diagnostics.Any(item => item.StartsWith("NetworkApiRefused:", StringComparison.Ordinal))
                    ? "BlockedByNetworkApi"
                    : "CompileFailed";
                return new CompilationResult(status, rewritten.Diagnostics);
            }

            compilation = rewritten.Compilation;
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                if (!emit.Success)
                {
                    return new CompilationResult("CompileFailed", SafeDiagnostics(emit.Diagnostics));
                }

                return new CompilationResult("Generated", Array.Empty<string>(), stream.ToArray(), FindEntryType(compilation, input.Name));
            }
        }
        catch (Exception exception) when (IsInputFailure(exception) || exception is BadImageFormatException || exception is InvalidOperationException)
        {
            return new CompilationResult("CompileFailed", Enumerable.Repeat("CompilationInfrastructureFailure", 1));
        }
    }

    /// <summary>只回代碼與 basename；禁止 Diagnostic.ToString/GetMessage 把常值、token 或路徑帶入輸出。</summary>
    private static IEnumerable<string> SafeDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)
            .OrderBy(item => item.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Location.SourceSpan.Start).Take(20)
            .Select(item => item.Id + ":" + Path.GetFileName(item.Location.SourceTree?.FilePath ?? "compilation"));
    }

    /// <summary>優先挑入口專案的 public Plugin 或 Activity；相依專案型別不會覆蓋原入口。</summary>
    private static string? FindEntryType(CSharpCompilation compilation, string projectName)
    {
        foreach (var tree in compilation.SyntaxTrees.OrderBy(item => item.FilePath, StringComparer.Ordinal))
        {
            if (!tree.FilePath.StartsWith(projectName + "/", StringComparison.Ordinal))
            {
                continue;
            }

            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!(model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type) || type.IsAbstract || type.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (type.AllInterfaces.Any(item => item.ToDisplayString() == "Microsoft.Xrm.Sdk.IPlugin") || HasActivityBase(type))
                {
                    return type.ToDisplayString();
                }
            }
        }

        return null;
    }

    /// <summary>逐層識別 CodeActivity，不要求活動一定直接繼承，避免自訂中介基底誤判。</summary>
    private static bool HasActivityBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Activities.CodeActivity")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 只以 reference assemblies 與固定受信任套件建立 metadata，不載入舊 DLL。
    /// harness 若已提供 RestSharp shim 則不引用舊 RestSharp，避免實際傳輸實作混入。
    /// </summary>
    private static IEnumerable<MetadataReference> FrameworkReferences(string? legacyRoot)
    {
        var locations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8");
        if (!Directory.Exists(framework))
        {
            var referencePackage = FindPackageDirectory("microsoft.netframework.referenceassemblies.net48");
            framework = Path.Combine(referencePackage ?? string.Empty, "build", ".NETFramework", "v4.8");
        }

        foreach (var name in FrameworkNames)
        {
            var path = Path.Combine(framework, name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("FrameworkReferenceMissing");
            }

            locations[name] = path;
        }

        var facades = Path.Combine(framework, "Facades");
        if (Directory.Exists(facades))
        {
            foreach (var path in Directory.EnumerateFiles(facades, "*.dll"))
            {
                locations[Path.GetFileName(path)] = path;
            }
        }

        var harness = typeof(LegacyProjectCompiler).Assembly;
        locations[Path.GetFileName(harness.Location)] = harness.Location;
        locations["Newtonsoft.Json.dll"] = typeof(JsonConvert).Assembly.Location;
        var runtimeDirectory = Path.GetDirectoryName(harness.Location)!;
        foreach (var name in PackageAssemblyNames)
        {
            var besideHarness = Path.Combine(runtimeDirectory, name);
            var package = name == "Microsoft.Xrm.Sdk.Workflow.dll" ? "microsoft.crmsdk.workflow" : "microsoft.crmsdk.coreassemblies";
            var packageRoot = FindPackageDirectory(package);
            var packagePath = packageRoot == null ? null : FindAssembly(packageRoot, name);
            if (File.Exists(besideHarness))
            {
                locations[name] = besideHarness;
            }
            else if (packagePath != null)
            {
                locations[name] = packagePath;
            }
        }

        if (legacyRoot != null)
        {
            var packages = Path.Combine(legacyRoot, "packages");
            if (Directory.Exists(packages) && (File.GetAttributes(packages) & FileAttributes.ReparsePoint) == 0)
            {
                AddLegacyPackage("Microsoft.AspNet.WebApi.Client.*", "System.Net.Http.Formatting.dll");
                AddLegacyPackage("ZXing*", "zxing.dll");
                if (harness.GetType("RestSharp.RestClient", false) == null)
                {
                    AddLegacyPackage("RestSharp.*", "RestSharp.dll");
                }
            }

            void AddLegacyPackage(string pattern, string assemblyName)
            {
                var packageDirectory = Directory.EnumerateDirectories(packages, pattern).OrderByDescending(item => item, StringComparer.Ordinal).FirstOrDefault();
                var path = packageDirectory == null ? null : FindAssembly(packageDirectory, assemblyName);
                if (path != null)
                {
                    locations[assemblyName] = path;
                }
            }
        }

        // CreateFromImage 使用區域陣列，不在來源 DLL 上留下 Roslyn memory-mapped file handle。
        foreach (var path in locations.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value))
        {
            yield return MetadataReference.CreateFromImage(File.ReadAllBytes(path));
        }
    }

    /// <summary>只搜尋 NuGet 全域套件的指定套件名稱，不遍歷任意工作樹或使用舊輸出 DLL。</summary>
    private static string? FindPackageDirectory(string name)
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        }

        var directory = Path.Combine(packageRoot, name);
        return Directory.Exists(directory) ? Directory.EnumerateDirectories(directory).OrderByDescending(item => item, StringComparer.Ordinal).FirstOrDefault() : null;
    }

    /// <summary>僅在套件 lib 子目錄尋找指定 metadata，拒絕 link 與大檔；不載入組件。</summary>
    private static string? FindAssembly(string packageDirectory, string name)
    {
        var lib = Path.Combine(packageDirectory, "lib");
        if (!Directory.Exists(lib) || (File.GetAttributes(packageDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(lib).OrderBy(item => item, StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, name);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0 && File.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && new FileInfo(path).Length < 32L * 1024 * 1024)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>只將預期讀取／格式失敗轉成固定診斷，不攔截記憶體耗盡等不可恢復錯誤。</summary>
    private static bool IsInputFailure(Exception exception) => exception is IOException || exception is UnauthorizedAccessException
        || exception is ArgumentException || exception is NotSupportedException || exception is System.Xml.XmlException;
}
