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

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 從實際方案逐 csproj、逐目標呼叫 MSBuild 的唯讀評估；避免自行猜測 Condition／Import 規則漏掉
/// Framework 專用參考。每個程序最長三十秒，輸入只來自受信任方案，程序、串流與 JSON 均有限持有。
/// </summary>
internal static class ArchitectureProjectReader
{
    private const long MaximumSourceFileBytes = 16L * 1024 * 1024;
    private const long MaximumSourceBytesPerTarget = 64L * 1024 * 1024;

    /// <summary>定位當前工作樹的方案並評估每目標；不走父工作樹、不讀 appsettings 或連線資訊。</summary>
    /// <returns>只含相依名稱與目標的 detached 評估圖。</returns>
    internal static IReadOnlyList<ArchitectureProject> ReadSolution()
    {
        var root = FindRoot();
        var solution = File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.sln"));
        var matches = Regex.Matches(solution, "(?m)^Project\\([^\\r\\n]+?= \"[^\"]+\", \"(?<path>[^\"]+\\.csproj)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var paths = matches.Select(match => Path.GetFullPath(Path.Combine(root, match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)))).ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidDataException("Solution contains no project entries.");
        }

        return ReadProjects(root, paths, verifyRestoredAssets: true);
    }

    /// <summary>評估呼叫者擁有的受信任 csproj 集合；同樣逐目標解析 Condition，不自行模擬 MSBuild。</summary>
    /// <param name="root">限制讀取範圍的絕對根目錄。</param><param name="paths">本次專案路徑。</param>
    /// <param name="verifyRestoredAssets">實際方案必須同時檢查 NuGet 已還原的 Framework 選擇；純 XML 範例不需要套件還原。</param>
    /// <returns>各目標的 detached 節點。</returns>
    internal static IReadOnlyList<ArchitectureProject> ReadProjects(string root, IReadOnlyList<string> paths, bool verifyRestoredAssets = false)
    {
        var projects = new List<ArchitectureProject>();
        foreach (var path in paths)
        {
            ValidateLocalPath(root, path);
            using var outer = Evaluate(root, path, null);
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
                using var evaluated = Evaluate(root, path, target);
                var data = evaluated.RootElement;
                var items = data.GetProperty("Items");
                var references = items.GetProperty("ProjectReference").EnumerateArray()
                    .Select(item => Path.GetFileNameWithoutExtension(item.GetProperty("Identity").GetString())).ToArray();
                var packages = items.GetProperty("PackageReference").EnumerateArray()
                    .Select(item => item.GetProperty("Identity").GetString() ?? throw new InvalidDataException("Package identity is missing.")).ToArray();
                long sourceBytes = 0;
                var sources = items.GetProperty("Compile").EnumerateArray()
                    .Select(item => item.GetProperty("FullPath").GetString() ?? item.GetProperty("Identity").GetString())
                    .Where(pathValue => !string.IsNullOrWhiteSpace(pathValue))
                    .Select(pathValue =>
                    {
                        var sourcePath = Path.GetFullPath(pathValue!);
                        ValidateLocalPath(root, sourcePath);
                        var relativePath = Path.GetRelativePath(root, sourcePath);
                        var source = ReadSource(sourcePath, relativePath);
                        sourceBytes += source.Length * sizeof(char);
                        if (sourceBytes > MaximumSourceBytesPerTarget)
                        {
                            throw new InvalidDataException("Architecture source snapshot exceeded its target budget.");
                        }

                        return source;
                    })
                    .ToArray();
                var test = string.Equals(data.GetProperty("Properties").GetProperty("IsTestProject").GetString(), "true", StringComparison.OrdinalIgnoreCase);
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

                projects.Add(new ArchitectureProject(Path.GetFileNameWithoutExtension(path), target, targets, references!, packages, test)
                {
                    Sources = sources
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
        var length = new FileInfo(sourcePath).Length;
        if (length > MaximumSourceFileBytes)
        {
            throw new InvalidDataException("Architecture source file exceeded its size budget.");
        }

        return new ArchitectureSource(relativePath, File.ReadAllText(sourcePath, new UTF8Encoding(false, true)));
    }

    /// <summary>只向上尋找目前 checkout 的方案；找到即停，不以另一個工作樹補來源。</summary>
    private static string FindRoot()
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
    private static void ValidateLocalPath(string root, string path)
    {
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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
    private static JsonDocument Evaluate(string root, string path, string? target)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "msbuild", path, "-nologo", "-maxcpucount:1", "-nodeReuse:false", "-p:Configuration=Release", "-getProperty:TargetFramework,TargetFrameworks,IsTestProject,ProjectAssetsFile", "-getItem:ProjectReference,PackageReference,Compile" })
        {
            start.ArgumentList.Add(argument);
        }

        if (target is not null)
        {
            start.ArgumentList.Add("-p:TargetFramework=" + target);
        }

        var result = BoundedProcessRunner.Run(start, TimeSpan.FromSeconds(30), 1_048_576);
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
