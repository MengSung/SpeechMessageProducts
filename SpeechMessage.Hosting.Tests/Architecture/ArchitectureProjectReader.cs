/*
 * 檔案整體說明
 *
 * 目的與定位：從實際方案逐 csproj、逐目標呼叫 MSBuild 的唯讀評估；避免自行猜測 Condition／Import 規則漏掉 Framework 專用參考，
 * 並為 R2／R3／R9 提供有界 C# 來源快照。每個程序最長三十秒，輸入只來自受信任方案，程序、串流與 JSON 均有限持有。
 *
 * 主要功能與流程：
 * 本檔宣告 ArchitectureProjectReader。
 * - 定位當前工作樹的方案並評估每目標；不走父工作樹、不讀 appsettings 或連線資訊。
 * - 評估呼叫者擁有的受信任 csproj 集合；同樣逐目標解析 Condition，不自行模擬 MSBuild。
 * - 只向上尋找目前 checkout 的方案；找到即停，不以另一個工作樹補來源。
 * - 拒絕跨工作樹及 reparse point，確保測試只檢視本次方案宣告的普通檔案；單檔與單目標來源
 *   均有大小上限，避免架構檢查因異常來源無界保留記憶體。
 * 測試案例入口包含 ReadSolution、ReadProjects、FindRoot、ValidateLocalPath、Evaluate。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 SpeechMessage.Hosting.Tests/Architecture/ArchitectureRuleTests.cs、
 * SpeechMessage.Hosting.Tests/Architecture/ArchitectureXmlTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 從實際方案逐 csproj、逐目標呼叫 MSBuild 的唯讀評估；避免自行猜測 Condition／Import 規則漏掉
/// Framework 專用參考。每個程序最長三十秒，輸入只來自受信任方案，程序、串流與 JSON 均有限持有。
/// </summary>
internal static class ArchitectureProjectReader
{
    /// <summary>單一來源最多十六 MiB；讀取前檢查且串流不允許並行寫入。</summary>
    private const long MaximumSourceFileBytes = 16L * 1024 * 1024;
    /// <summary>每目標最多六十四 MiB UTF-16 文字，不計重複來源為跨目標新增配置。</summary>
    private const long MaximumSourceBytesPerTarget = 64L * 1024 * 1024;

    /// <summary>同次方案掃描持有的唯一來源總量最多二百五十六 MiB。</summary>
    private const long MaximumSnapshotBytes = 256L * 1024 * 1024;

    /// <summary>定位當前工作樹的方案並評估每目標；不走父工作樹、不讀 appsettings 或連線資訊。</summary>
    /// <param name="configuration">只接受 Debug 或 Release，分別驗證條件來源與參考。</param>
    /// <returns>只含相依名稱與目標的 detached 評估圖。</returns>
    internal static IReadOnlyList<ArchitectureProject> ReadSolution(string configuration = "Release")
    {
        var root = FindRoot();
        var solution = File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.sln"));
        var matches = Regex.Matches(solution, "(?m)^Project\\([^\\r\\n]+?= \"[^\"]+\", \"(?<path>[^\"]+\\.csproj)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var paths = matches.Select(match => Path.GetFullPath(Path.Combine(root, match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)))).ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidDataException("Solution contains no project entries.");
        }

        return ReadProjects(root, paths, verifyRestoredAssets: true, configuration: configuration);
    }

    /// <summary>評估呼叫者擁有的受信任 csproj 集合；同樣逐目標解析 Condition，不自行模擬 MSBuild。</summary>
    /// <param name="root">限制讀取範圍的絕對根目錄。</param><param name="paths">本次專案路徑。</param>
    /// <param name="verifyRestoredAssets">實際方案必須同時檢查 NuGet 已還原的 Framework 選擇；純 XML 範例不需要套件還原。</param>
    /// <param name="configuration">當次實際建置組態，不以 Release 推定 Debug。</param>
    /// <returns>各目標的 detached 節點。</returns>
    internal static IReadOnlyList<ArchitectureProject> ReadProjects(string root, IReadOnlyList<string> paths, bool verifyRestoredAssets = false, string configuration = "Release")
    {
        if (configuration is not "Debug" and not "Release")
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var snapshots = new Dictionary<string, ArchitectureSource>(StringComparer.OrdinalIgnoreCase);
        long snapshotBytes = 0;
        var projects = new List<ArchitectureProject>();
        foreach (var path in paths)
        {
            ValidateLocalPath(root, path);
            using var outer = Evaluate(root, path, null, configuration);
            var properties = outer.RootElement.GetProperty("Properties");
            var single = properties.GetProperty("TargetFramework").GetString();
            var multiple = properties.GetProperty("TargetFrameworks").GetString();
            var targets = (string.IsNullOrEmpty(multiple) ? single : multiple)?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            if (targets.Length == 0)
            {
                throw new InvalidDataException("Project has no evaluated target framework.");
            }

            foreach (var target in targets)
            {
                using var evaluated = Evaluate(root, path, target, configuration);
                var data = evaluated.RootElement;
                var items = data.GetProperty("Items");
                var references = items.GetProperty("ProjectReference").EnumerateArray()
                    .Select(item => Path.GetFileNameWithoutExtension(item.GetProperty("Identity").GetString())).ToArray();
                var packages = items.GetProperty("PackageReference").EnumerateArray()
                    .Select(item => item.GetProperty("Identity").GetString() ?? throw new InvalidDataException("Package identity is missing.")).ToArray();
                var test = string.Equals(data.GetProperty("Properties").GetProperty("IsTestProject").GetString(), "true", StringComparison.OrdinalIgnoreCase);
                long sourceBytes = 0;
                var sources = (test ? Array.Empty<JsonElement>() : items.GetProperty("Compile").EnumerateArray().ToArray())
                    .Select(item => item.GetProperty("FullPath").GetString() ?? item.GetProperty("Identity").GetString())
                    .Where(pathValue => !string.IsNullOrWhiteSpace(pathValue))
                    .Select(pathValue =>
                    {
                        var sourcePath = Path.GetFullPath(pathValue!);
                        ValidateLocalPath(root, sourcePath);
                        var relativePath = Path.GetRelativePath(root, sourcePath);
                        if (!snapshots.TryGetValue(sourcePath, out var source))
                        {
                            source = ReadSource(sourcePath, relativePath);
                            snapshotBytes += (long)source.Text.Length * sizeof(char);
                            if (snapshotBytes > MaximumSnapshotBytes)
                            {
                                throw new InvalidDataException("Architecture snapshot exceeded its total budget.");
                            }

                            snapshots.Add(sourcePath, source);
                        }
                        sourceBytes += (long)source.Text.Length * sizeof(char);
                        if (sourceBytes > MaximumSourceBytesPerTarget)
                        {
                            throw new InvalidDataException("Architecture source snapshot exceeded its target budget.");
                        }

                        return source;
                    })
                    .ToArray();
                if (verifyRestoredAssets && (target == "net462" || (test && target == "net48")))
                {
                    var assetsPath = data.GetProperty("Properties").GetProperty("ProjectAssetsFile").GetString();
                    if (string.IsNullOrEmpty(assetsPath)) throw new InvalidDataException("Restored assets path is missing.");
                    ValidateLocalPath(root, assetsPath);
                    using var stream = File.OpenRead(assetsPath);
                    using var assets = JsonDocument.Parse(stream);
                    var violations = ArchitectureRules.ValidateFrameworkAssets(assets.RootElement.GetProperty("targets").GetProperty(target));
                    if (violations.Count != 0) throw new InvalidDataException("Framework restore selected an incompatible product: " + string.Join(",", violations));
                }

                var assetsMetadata = verifyRestoredAssets && !test ? ReadAssetMetadata(root,
                    data.GetProperty("Properties").GetProperty("ProjectAssetsFile").GetString() ??
                    throw new InvalidDataException("Restored assets path is missing."), target) :
                    (Array.Empty<string>(), Array.Empty<string>());
                var sdkDirectory = data.GetProperty("Properties").GetProperty("MSBuildSDKsPath").GetString();
                var implicitFrameworkFile = string.IsNullOrEmpty(sdkDirectory) ? string.Empty :
                    Path.GetFullPath(Path.Combine(sdkDirectory, "Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.BeforeCommon.targets"));
                var assemblies = items.GetProperty("Reference").EnumerateArray()
                    .Where(item => !IsSdkImplicitReference(item, implicitFrameworkFile))
                    .Concat(items.GetProperty("FrameworkReference").EnumerateArray())
                    .Select(item => item.GetProperty("Identity").GetString()?.Split(',')[0] ??
                        throw new InvalidDataException("Assembly identity is missing.")).ToArray();
                var defines = data.GetProperty("Properties").GetProperty("DefineConstants").GetString() ?? string.Empty;
                projects.Add(new ArchitectureProject(Path.GetFileNameWithoutExtension(path), target, targets, references!, packages, test)
                {
                    Sources = sources,
                    RestoredDependencies = assetsMetadata.Item1,
                    ContractMetadataPaths = assetsMetadata.Item2,
                    AssemblyReferences = assemblies,
                    PreprocessorSymbols = defines.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    GlobalUsingDirectives = items.GetProperty("Using").EnumerateArray().Select(CreateGlobalUsing).ToArray(),
                    Configuration = configuration
                });
            }
        }

        return projects.AsReadOnly();
    }

    /// <summary>
    /// 讀取單一來源前先檢查檔案大小；檔案由目前方案宣告且不跨 reparse point，
    /// 讀取後立即脫離 stream，不把檔案 handle 或可變 reader 交給規則層。
    /// </summary>
    private static ArchitectureSource ReadSource(string sourcePath, string relativePath)
    {
        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = stream.Length;
        if (length > MaximumSourceFileBytes)
        {
            throw new InvalidDataException("Architecture source file exceeded its size budget.");
        }

        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        return new ArchitectureSource(relativePath.Replace('\\', '/'), new UTF8Encoding(false, true).GetString(bytes));
    }

    /// <summary>
    /// 僅辨識真正 SDK BeforeCommon 所宣告的隱含 Framework 可用組件；它不是產品顯式
    /// 相依或已輸出 AssemblyRef。專案自行設定 IsImplicitlyDefined 不能取得此例外。
    /// </summary>
    private static bool IsSdkImplicitReference(JsonElement item, string sdkDefinition) =>
        item.TryGetProperty("IsImplicitlyDefined", out var implicitFlag) &&
        string.Equals(implicitFlag.GetString(), "true", StringComparison.OrdinalIgnoreCase) &&
        item.TryGetProperty("DefiningProjectFullPath", out var origin) &&
        string.Equals(origin.GetString(), sdkDefinition, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 以 Roslyn 節點建立與 SDK Using 項目相同的 global using，保留 Alias／Static。
    /// 只在記憶體內組成語法，不手改 .g.cs、不執行 target；不合法項目由後續語法守門拒絕。
    /// </summary>
    private static string CreateGlobalUsing(JsonElement item)
    {
        var name = item.GetProperty("Identity").GetString() ??
            throw new InvalidDataException("Global using identity is missing.");
        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name))
            .WithGlobalKeyword(SyntaxFactory.Token(SyntaxKind.GlobalKeyword));
        if (item.TryGetProperty("Static", out var staticFlag) &&
            string.Equals(staticFlag.GetString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            directive = directive.WithStaticKeyword(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        }

        if (item.TryGetProperty("Alias", out var alias) && !string.IsNullOrEmpty(alias.GetString()))
        {
            directive = directive.WithAlias(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(alias.GetString()!)));
        }

        return directive.NormalizeWhitespace().ToFullString();
    }

    /// <summary>
    /// 由當前 Plugin 已還原資產取得正式 SDK／Workflow 契約，供離線合成來源測試使用。
    /// 不讀秘密、不載入產品、不連 NuGet；缺資產即明確失敗，不能改用同名假介面。
    /// </summary>
    internal static IReadOnlyList<string> ReadPluginContractMetadata()
    {
        var root = FindRoot();
        return ReadAssetMetadata(root, Path.Combine(root,
            "SpeechMessage.Dynamics.Plugins/obj/project.assets.json"), "net462").Item2;
    }

    /// <summary>
    /// 只讀已還原 NuGet 圖，保留傳遞相依名稱並解析官方 SDK 與 Workflow 契約檔。
    /// 所有候選路徑必須位於 NuGet 宣告的 package folder，不走 reparse point；JSON 上限三十二 MiB。
    /// 這是中繼資料讀取而非組件載入，不執行任何建置 target、還原或產品程式碼。
    /// </summary>
    private static (string[], string[]) ReadAssetMetadata(string root, string assetsPath, string target)
    {
        ValidateLocalPath(root, assetsPath);
        using var stream = File.OpenRead(assetsPath);
        if (stream.Length > 32L * 1024 * 1024)
        {
            throw new InvalidDataException("Architecture assets exceeded their budget.");
        }

        using var document = JsonDocument.Parse(stream);
        var data = document.RootElement;
        var dependencies = data.GetProperty("targets").GetProperty(target).EnumerateObject().ToArray();
        var names = dependencies.Select(item => item.Name.Split('/')[0]).ToArray();
        var paths = new List<string>();
        foreach (var library in data.GetProperty("libraries").EnumerateObject())
        {
            if (!library.Value.TryGetProperty("path", out var packagePath))
            {
                continue;
            }

            string[] files = library.Name.Split('/')[0] switch
            {
                "Microsoft.CrmSdk.CoreAssemblies" => new[] { "lib/net462/Microsoft.Xrm.Sdk.dll" },
                "Microsoft.NETFramework.ReferenceAssemblies.net462" => new[] { "build/.NETFramework/v4.6.2/System.Activities.dll" },
                _ => Array.Empty<string>()
            };
            foreach (var file in files)
            {
                var found = false;
                foreach (var folder in data.GetProperty("packageFolders").EnumerateObject())
                {
                    var candidate = Path.GetFullPath(Path.Combine(folder.Name, packagePath.GetString()!, file));
                    if (File.Exists(candidate))
                    {
                        ValidateLocalPath(Path.TrimEndingDirectorySeparator(folder.Name), candidate);
                        paths.Add(candidate);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new InvalidDataException("Restored architecture contract metadata is missing.");
                }
            }
        }

        return (names, paths.ToArray());
    }

    /// <summary>只向上尋找目前 checkout 的方案；找到即停，不以另一個工作樹補來源。</summary>
    internal static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Current solution was not found.");
    }

    /// <summary>拒絕跨工作樹及 reparse point，確保測試只檢視本次方案宣告的普通檔案。</summary>
    internal static void ValidateLocalPath(string root, string path)
    {
        // TrimEndingDirectorySeparator刻意保留磁碟根的分隔符；統一一次，避免W:\\誤拒絕W:\內檔案。
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Project path is outside the current worktree.");
        }

        for (FileSystemInfo? part = new FileInfo(path); part is not null && !string.Equals(part.FullName, root, StringComparison.OrdinalIgnoreCase); part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
        {
            if ((part.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Reparse point project paths are not allowed.");
            }
        }
    }

    /// <summary>
    /// 啟動不建置的 dotnet msbuild；並行 drain stdout／stderr 避免 pipe deadlock。失敗只回固定訊息，
    /// 不將工具輸出中的本機路徑或潛在設定展開到 assertion；逾時終止整個程序樹後等待退出再釋放。
    /// </summary>
    private static JsonDocument Evaluate(string root, string path, string? target, string configuration)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "msbuild", path, "-nologo", "-maxcpucount:1", "-nodeReuse:false", "-p:Configuration=" + configuration, "-getProperty:TargetFramework,TargetFrameworks,IsTestProject,ProjectAssetsFile,DefineConstants,MSBuildSDKsPath", "-getItem:ProjectReference,PackageReference,Reference,FrameworkReference,Compile,Using" })
        {
            start.ArgumentList.Add(argument);
        }

        if (target is not null)
        {
            start.ArgumentList.Add("-p:TargetFramework=" + target);
        }

        var result = BoundedProcessRunner.Run(start, TimeSpan.FromSeconds(30), 8_388_608);
        if (result.StandardOutputTruncated || result.StandardErrorTruncated)
        {
            throw new InvalidDataException("Read-only architecture evaluation exceeded its output limit.");
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidDataException("Read-only architecture evaluation failed.");
        }

        return JsonDocument.Parse(result.StandardOutput);
    }
}
