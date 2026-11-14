/*
檔案整體說明

目的與定位：限制並讀取工作樹外 Dynamics365Line 第一層舊專案的 C# 來源，為後續安全掃描與記憶體內 Roslyn 編譯提供一次性輸入。

主要功能與流程：驗證根目錄名稱及工作樹邊界，僅列舉第一層含 csproj 的專案；遞迴來源時跳過 bin、obj、packages、backup 與 reparse point，套用檔案數、單檔及專案總量上限。每個檔案以 BOM 或嚴格 UTF-8／CP950 解碼，並以原始位元組雜湊供可重現摘要使用。讀取失敗只成為該專案的固定 CompileFailed 診斷，不把路徑或例外本文寫入候選。

使用邊界與維護：reader 不複製來源、不修改來源、不載入組件、不建立網路資源。所有文字只存於本次同步呼叫的 operation-local 物件，最大生命週期是分類完成；reparse point 一律拒絕下降，避免跨越來源所有權。任何根目錄邊界、大小上限或專案清單違反都 fail closed 且不交付候選。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LegacyGolden.PluginHarness;

/// <summary>一次讀取的原始 C# 檔案；只保留編譯與掃描必要內容，不對外序列化。</summary>
internal sealed class SourceFile
{
    /// <summary>初始化相對檔名、文字、位元組數與摘要。</summary>
    internal SourceFile(string relativePath, string text, int byteCount, string sha256)
    {
        RelativePath = relativePath;
        Text = text;
        ByteCount = byteCount;
        Sha256 = sha256;
    }

    /// <summary>相對於舊專案目錄的排序路徑。</summary>
    internal string RelativePath { get; }

    /// <summary>嚴格解碼後的 C# 文字，只在此次分類期間存活。</summary>
    internal string Text { get; }

    /// <summary>原始位元組數。</summary>
    internal int ByteCount { get; }

    /// <summary>原始位元組 SHA-256。</summary>
    internal string Sha256 { get; }
}

/// <summary>一個第一層舊專案的 bounded 讀取結果。</summary>
internal sealed class ProjectInput
{
    /// <summary>建立專案輸入；讀取問題只保留固定代碼，不保存例外本文。</summary>
    internal ProjectInput(string name, IEnumerable<SourceFile> sources, IEnumerable<string> readIssues)
    {
        Name = name;
        Sources = sources.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToList();
        ReadIssues = readIssues.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToList();
    }

    /// <summary>專案檔所在資料夾名稱。</summary>
    internal string Name { get; }

    /// <summary>排序後的安全來源快照。</summary>
    internal IReadOnlyList<SourceFile> Sources { get; }

    /// <summary>固定讀取錯誤分類。</summary>
    internal IReadOnlyList<string> ReadIssues { get; }

    /// <summary>來源總位元組數。</summary>
    internal long ByteCount { get { return Sources.Sum(item => (long)item.ByteCount); } }

    /// <summary>以來源路徑及原始雜湊產生穩定專案摘要。</summary>
    internal string SourceSha256
    {
        get
        {
            using (var sha = SHA256.Create())
            {
                var payload = string.Join("\n", Sources.Select(item => item.RelativePath + "\0" + item.Sha256));
                return BoundedSourceReader.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
        }
    }
}

/// <summary>有界且不具副作用的舊來源 reader。</summary>
internal static class BoundedSourceReader
{
    private const int MaxProjects = 256;
    private const int MaxFilesPerProject = 512;
    private const int MaxFileBytes = 1024 * 1024;
    private const long MaxProjectBytes = 16L * 1024 * 1024;
    private static readonly string[] ForbiddenDirectoryNames = { ".worktrees", "backup", "backups", "bin", "obj", "packages" };

    /// <summary>讀取根目錄第一層專案；根目錄失敗會中止整次操作，單專案失敗則保留固定 CompileFailed 結果。</summary>
    internal static IReadOnlyList<ProjectInput> ReadProjects(string sourceRoot)
    {
        var root = ValidateRoot(sourceRoot);
        var projectDirectories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !IsForbiddenDirectory(path) && !IsReparsePoint(path))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (projectDirectories.Count > MaxProjects)
        {
            throw new InvalidDataException("Source project count exceeds the bounded limit.");
        }

        var projects = new List<ProjectInput>();
        foreach (var projectDirectory in projectDirectories)
        {
            if (!Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly).Any())
            {
                continue;
            }

            projects.Add(ReadProject(projectDirectory));
        }

        if (projects.Count == 0)
        {
            throw new InvalidDataException("No first-level legacy projects were found.");
        }

        return projects;
    }

    /// <summary>驗證來源必須是工作樹外、名稱固定的 Dynamics365Line 目錄。</summary>
    internal static string ValidateRoot(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new ArgumentException("Source root is required.", nameof(sourceRoot));
        }

        var fullRoot = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(fullRoot) || !string.Equals(Path.GetFileName(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "Dynamics365Line", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Source root must be an existing Dynamics365Line directory.");
        }

        return fullRoot;
    }

    /// <summary>讀取單一專案；所有逐檔失敗只產生固定代碼，不將檔名或本文外洩。</summary>
    private static ProjectInput ReadProject(string projectDirectory)
    {
        var name = Path.GetFileName(projectDirectory);
        var paths = new List<string>();
        CollectSourcePaths(projectDirectory, paths);
        var sources = new List<SourceFile>();
        var issues = new List<string>();
        long bytes = 0;
        if (paths.Count > MaxFilesPerProject)
        {
            issues.Add("SourceFileCountExceeded");
            return new ProjectInput(name, sources, issues);
        }

        foreach (var path in paths.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var raw = File.ReadAllBytes(path);
                if (raw.Length > MaxFileBytes || raw.Length == 0)
                {
                    issues.Add("SourceFileSizeInvalid");
                    continue;
                }

                bytes += raw.Length;
                if (bytes > MaxProjectBytes)
                {
                    issues.Add("ProjectSourceSizeExceeded");
                    break;
                }

                var text = DecodeStrict(raw);
                if (text.IndexOf('\ufffd') >= 0)
                {
                    issues.Add("ReplacementCharacterDetected");
                    continue;
                }

                sources.Add(new SourceFile(RelativePath(projectDirectory, path), text, raw.Length, Hash(raw)));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is DecoderFallbackException || exception is NotSupportedException)
            {
                issues.Add("SourceReadFailed");
            }
        }

        if (sources.Count == 0)
        {
            issues.Add("NoReadableCSharpSource");
        }

        return new ProjectInput(name, sources, issues);
    }

    /// <summary>只下降一般目錄；junction、symlink 或禁止目錄不跨越。</summary>
    private static void CollectSourcePaths(string current, List<string> paths)
    {
        foreach (var file in Directory.EnumerateFiles(current, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (!IsReparsePoint(file))
            {
                paths.Add(file);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsForbiddenDirectory(directory) || IsReparsePoint(directory))
            {
                continue;
            }

            CollectSourcePaths(directory, paths);
            if (paths.Count > MaxFilesPerProject)
            {
                return;
            }
        }
    }

    /// <summary>以 BOM 或嚴格 UTF-8／CP950 讀取；不使用 replacement fallback 掩蓋損毀。</summary>
    private static string DecodeStrict(byte[] raw)
    {
        if (raw.Length >= 3 && raw[0] == 0xef && raw[1] == 0xbb && raw[2] == 0xbf)
        {
            return new UTF8Encoding(false, true).GetString(raw, 3, raw.Length - 3);
        }

        if (raw.Length >= 2 && raw[0] == 0xff && raw[1] == 0xfe)
        {
            return new UnicodeEncoding(false, true, true).GetString(raw, 2, raw.Length - 2);
        }

        if (raw.Length >= 2 && raw[0] == 0xfe && raw[1] == 0xff)
        {
            return new UnicodeEncoding(true, true, true).GetString(raw, 2, raw.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(raw);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(raw);
        }
    }

    /// <summary>取得穩定相對路徑而不輸出主機絕對路徑。</summary>
    private static string RelativePath(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/')
            : Path.GetFileName(path);
    }

    /// <summary>判斷路徑是否含被禁止的資料夾名稱。</summary>
    private static bool IsForbiddenDirectory(string path)
    {
        var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return ForbiddenDirectoryNames.Any(name => leaf.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>檢查 reparse point，避免透過 junction 或 symlink 越過來源所有權。</summary>
    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>以跨平台分隔符切分路徑，供邊界檢查使用。</summary>
    private static string[] PathSegments(string path)
    {
        return path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>以固定小寫十六進位格式計算雜湊。</summary>
    private static string Hash(byte[] raw)
    {
        using (var sha = SHA256.Create())
        {
            return Hex(sha.ComputeHash(raw));
        }
    }

    /// <summary>以 net48 可用的固定小寫十六進位格式表示雜湊。</summary>
    internal static string Hex(byte[] bytes)
    {
        const string alphabet = "0123456789abcdef";
        var chars = new char[bytes.Length * 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            chars[index * 2] = alphabet[bytes[index] >> 4];
            chars[index * 2 + 1] = alphabet[bytes[index] & 0x0f];
        }

        return new string(chars);
    }
}
