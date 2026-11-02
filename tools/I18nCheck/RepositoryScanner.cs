using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace I18nCheck;

/// <summary>單檔計數中繼資料，不包含原始常值；相對路徑是唯一的檔案身分。</summary>
/// <param name="Path">相對 repo 路徑，固定使用斜線。</param>
/// <param name="Count">該檔案的 CJK 顯示片段數。</param>
internal sealed record ScanFile(string Path, int Count);

/// <summary>單一專案的完整候選結果；掃描成功後才發布，不把半成品交給序列化器。</summary>
/// <param name="Project">相對 csproj 路徑，作為跨掃描身分。</param>
/// <param name="Count">此專案所有檔案的總數。</param>
/// <param name="Files">單次命令建立且不向外部修改的唯讀檔案清單。</param>
internal sealed record ScanProject(string Project, int Count, IReadOnlyList<ScanFile> Files);

/// <summary>版本化的中文字串基準格式；只保存計數與相對路徑，不保存來源或機密。</summary>
/// <param name="Version">本版格式固定為一。</param>
/// <param name="Projects">各專案的完整獨立結果，發布前檢查身分唯一。</param>
internal sealed record ScanSnapshot(int Version, IReadOnlyList<ScanProject> Projects);

/// <summary>
/// 以最近方案及其明列專案為唯一來源邊界；不進入父 worktree、未列專案、生成輸出或重新解析點。
/// 每次依專案建立並釋放語法／語意候選，單專案原文總量上限六十四 MiB，不保留到下一命令。
/// </summary>
internal static class RepositoryScanner
{
    /// <summary>只辨識方案明列的 C# 專案，不把 solution folder 或任意目錄當成來源。</summary>
    private static readonly Regex ProjectPattern = new("^Project\\(\"[^\"]+\"\\)\\s*=\\s*\"[^\"]*\",\\s*\"(?<path>[^\"]+\\.csproj)\"", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>所有目錄邊界與前包一致；先剪枝才走訪，避免讀入被排除的內容。</summary>
    private static readonly HashSet<string> ExcludedDirectories = new(["bin", "obj", ".git", ".worktrees", ".其他解決方案", "docs", ".ccg", ".trellis", "node_modules", "artifacts", "TestResults"], StringComparer.OrdinalIgnoreCase);

    /// <summary>只向上定位最近方案標記；來源走訪之後固定在回傳的根之下。</summary>
    /// <param name="start">執行位置。</param>
    /// <returns>最近的 SpeechMessageProducts.sln 根；找不到直接失敗。</returns>
    internal static string FindRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new InvalidDataException("I18N301: SpeechMessageProducts.sln was not found.");
    }

    /// <summary>完整掃描後才回傳候選，單次結果不含任何語法樹、來源字串或開啟檔案。</summary>
    /// <param name="root">明確選定、不可退回父目錄的方案根。</param>
    /// <returns>固定排序、身分唯一的計數快照。</returns>
    internal static ScanSnapshot Scan(string root)
    {
        var projects = new List<ScanProject>();
        foreach (var project in ProjectPaths(root))
        {
            var files = Files(root, Path.GetDirectoryName(project)!).ToArray();
            if (files.Sum(path => new FileInfo(path).Length) > 64L * 1024 * 1024)
                throw new InvalidDataException("I18N302: project source size exceeds the bounded scan limit.");
            var trees = new List<SyntaxTree>();
            var counts = new List<ScanFile>();
            foreach (var path in files)
            {
                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    trees.Add(CSharpSyntaxTree.ParseText(LocalFiles.ReadSource(path), path: path));
                else if (path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
                    counts.Add(new ScanFile(Relative(root, path), SourceScanner.CountRazor(LocalFiles.ReadSource(path))));
            }
            var compilation = SourceScanner.CreateCompilation(trees);
            foreach (var tree in trees)
                counts.Add(new ScanFile(Relative(root, tree.FilePath), SourceScanner.CountCSharp(tree, compilation.GetSemanticModel(tree))));
            projects.Add(new ScanProject(Relative(root, project), counts.Sum(file => file.Count), counts.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray()));
        }
        return new ScanSnapshot(1, projects.OrderBy(project => project.Project, StringComparer.Ordinal).ToArray());
    }

    /// <summary>列出明確非測試專案，拒絕重複、外部路徑與空方案，避免假綠燈。</summary>
    /// <param name="root">本命令選定的根。</param>
    /// <returns>不含外部來源的固定專案清單。</returns>
    internal static IReadOnlyList<string> ProjectPaths(string root)
    {
        var solution = LocalFiles.ReadSource(LocalFiles.Resolve(root, "SpeechMessageProducts.sln"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projects = new List<string>();
        foreach (Match match in ProjectPattern.Matches(solution))
        {
            var path = LocalFiles.Resolve(root, match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar));
            if (Relative(root, path).Split('/').SkipLast(1).Any(ExcludedDirectories.Contains))
                throw new InvalidDataException("I18N307: solution project is inside an excluded directory.");
            if (!seen.Add(path)) throw new InvalidDataException("I18N303: duplicate solution project identity.");
            if (seen.Count > 256) throw new InvalidDataException("I18N304: solution project count exceeds the scan limit.");
            var document = LocalFiles.ReadXml(path);
            var test = Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || document.Descendants().Any(element => element.Name.LocalName == "IsTestProject" && element.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                || document.Descendants().Any(element => element.Name.LocalName == "PackageReference" && (string?)element.Attribute("Include") == "Microsoft.NET.Test.Sdk");
            if (!test) projects.Add(path);
        }
        if (projects.Count == 0) throw new InvalidDataException("I18N305: solution contains no non-test C# project.");
        return projects.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>在專案根內剪枝走訪，檔數及目錄數各限兩萬，不穿越重新解析點。</summary>
    /// <param name="root">方案邊界。</param>
    /// <param name="projectDirectory">已通過邊界檢查的專案目錄。</param>
    /// <returns>只有 C#、Razor、resx 的固定來源清單。</returns>
    internal static IEnumerable<string> Files(string root, string projectDirectory)
    {
        var directories = new Stack<string>();
        directories.Push(projectDirectory);
        var count = 0;
        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                if (++count > 20_000) throw new InvalidDataException("I18N306: project entry count exceeds the scan limit.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!ExcludedDirectories.Contains(Path.GetFileName(entry))) directories.Push(entry);
                    continue;
                }
                var extension = Path.GetExtension(entry);
                if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".resx", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || entry.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)) continue;
                yield return LocalFiles.Resolve(root, entry);
            }
        }
    }

    /// <summary>跨機器一致的相對路徑格式，不把機器名稱或絕對目錄寫入基準。</summary>
    /// <param name="root">方案根。</param>
    /// <param name="path">根內來源。</param>
    /// <returns>斜線分隔的穩定來源身分。</returns>
    internal static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
