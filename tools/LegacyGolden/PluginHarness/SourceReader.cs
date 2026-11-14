/*
檔案整體說明

目的與定位：提供 PluginHarness 舊專案編譯器所需的唯讀來源閉包；來源來自工作樹外 Dynamics365Line 第一層 csproj，不以遞迴目錄掃描猜測編譯輸入。

主要功能與流程：解析禁止 DTD 的專案 XML，依明列 Compile Include 與 ProjectReference 讀取來源。排除 Properties、bin、obj、packages、backup 及 AssemblyInfo，拒絕未支援的條件／glob、越界路徑與 reparse point。來源上限為 512 檔、單檔 1 MiB、總量 16 MiB、64 個相依專案；嚴格解碼並以原始位元組雜湊建立摘要，來源中的既有常值不修改。

使用邊界與維護：不執行 MSBuild Import／Target，不寫回、複製或載入舊程式，不保存跨請求快取。所有文字只由單次 ProjectInput 擁有至編譯結束；讀取失敗回固定代碼，不含例外本文或來源秘密。檔案及 XML reader 由 using 決定性關閉；任何閉包失敗不得交付可執行結果。維護後以閉包、編碼與編譯測試核對本契約。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LegacyGolden.PluginHarness;

/// <summary>單次編譯借用的來源快照；文字不進診斷或輸出，原始雜湊用來辨識相同輸入。</summary>
internal sealed class SourceFile
{
    /// <summary>接收已經驗證的相對路徑及文字，不再讀取磁碟或修改常值。</summary>
    internal SourceFile(string relativePath, string text, int byteCount, string sha256)
    {
        RelativePath = relativePath;
        Text = text;
        ByteCount = byteCount;
        Sha256 = sha256;
    }

    /// <summary>相對舊方案的穩定來源路徑；診斷只取最後檔名，不揭露主機位置。</summary>
    internal string RelativePath { get; }

    /// <summary>嚴格解碼的完整來源，生命週期以此次編譯為限，不能寫入報告。</summary>
    internal string Text { get; }

    /// <summary>原始位元組數，納入整個來源閉包容量限制。</summary>
    internal int ByteCount { get; }

    /// <summary>原始位元組的雜湊，不以轉碼後文字替代來源證據。</summary>
    internal string Sha256 { get; }
}

/// <summary>單一入口專案與相依來源的區域候選；任何讀取錯誤都使編譯失敗。</summary>
internal sealed class ProjectInput
{
    /// <summary>凍結來源與錯誤排序，讓各專案閉包互不共享可變清單。</summary>
    internal ProjectInput(string name, IEnumerable<SourceFile> sources, IEnumerable<string> readIssues, string? legacyRoot = null)
    {
        Name = name;
        Sources = sources.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray();
        ReadIssues = readIssues.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        LegacyRoot = legacyRoot;
    }

    /// <summary>入口所在第一層資料夾名，供選擇屬於該專案的 Plugin／Activity 使用。</summary>
    internal string Name { get; }

    /// <summary>本次已驗證的舊方案根目錄，只供受限參考組件解析，不對外序列化。</summary>
    internal string? LegacyRoot { get; }

    /// <summary>完整相依閉包的一次性來源，沒有來源時不可宣稱編譯成功。</summary>
    internal IReadOnlyList<SourceFile> Sources { get; }

    /// <summary>固定錯誤代碼；不保存來源本文、完整路徑或例外訊息。</summary>
    internal IReadOnlyList<string> ReadIssues { get; }

    /// <summary>整個閉包的原始位元組數，以 long 避免容量加總溢位。</summary>
    internal long ByteCount => Sources.Sum(item => (long)item.ByteCount);

    /// <summary>依路徑及原始內容雜湊計算穩定摘要；不保存計算器或跨專案狀態。</summary>
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

/// <summary>有容量與路徑限制的專案 XML reader；不具執行 MSBuild 的能力。</summary>
internal static class BoundedSourceReader
{
    private const int MaxProjects = 256;
    private const int MaxFiles = 512;
    private const int MaxFileBytes = 1024 * 1024;
    private const long MaxTotalBytes = 16L * 1024 * 1024;
    private static readonly string[] ForbiddenNames = { ".worktrees", "backup", "backups", "bin", "obj", "packages", "properties" };
    private static readonly char[] InvalidProjectCharacters = { '/', '\\', ':' };
    private static readonly char[] UnsupportedIncludeCharacters = { '*', '?', '$', ';', '%' };

    /// <summary>列舉第一層入口，各入口分別建立來源閉包，任何入口失敗仍保留分類候選。</summary>
    internal static IReadOnlyList<ProjectInput> ReadProjects(string sourceRoot)
    {
        var root = ValidateRoot(sourceRoot);
        var directories = Directory.EnumerateDirectories(root).Where(path => !IsForbiddenName(path) && !IsReparsePoint(path))
            .OrderBy(path => path, StringComparer.Ordinal).Take(MaxProjects + 1).ToArray();
        if (directories.Length > MaxProjects)
        {
            throw new InvalidDataException("SourceProjectCountExceeded");
        }

        var projects = directories.Where(path => Directory.EnumerateFiles(path, "*.csproj").Any())
            .Select(path => ReadProject(root, Path.GetFileName(path))).ToArray();
        if (projects.Length == 0)
        {
            throw new InvalidDataException("NoLegacyProjects");
        }

        return projects;
    }

    /// <summary>驗證固定來源根目錄在目前工作樹外；不把呼叫端字串當成越界讀取授權。</summary>
    internal static string ValidateRoot(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new ArgumentException("SourceRootRequired", nameof(sourceRoot));
        }

        var root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workspace = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root) || !Path.GetFileName(root).Equals("Dynamics365Line", StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(workspace, StringComparison.OrdinalIgnoreCase) || IsReparsePoint(root))
        {
            throw new InvalidDataException("SourceRootBoundaryInvalid");
        }

        return root;
    }

    /// <summary>依入口資料夾名解析唯一 csproj；讀取與格式錯誤只轉成固定分類，不洩漏原文。</summary>
    internal static ProjectInput ReadProject(string sourceRoot, string projectName)
    {
        var root = ValidateRoot(sourceRoot);
        var sources = new List<SourceFile>();
        var issues = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(projectName) || projectName.IndexOfAny(InvalidProjectCharacters) >= 0 || projectName == "." || projectName == "..")
            {
                throw new InvalidDataException("ProjectNameInvalid");
            }

            var directory = CheckedPath(root, root, projectName);
            var projects = Directory.EnumerateFiles(directory, "*.csproj").Take(2).ToArray();
            if (projects.Length != 1)
            {
                throw new InvalidDataException("ProjectFileAmbiguous");
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Collect(root, projects[0], visited, paths);
            long total = 0;
            foreach (var path in paths.OrderBy(item => item, StringComparer.Ordinal))
            {
                var size = new FileInfo(path).Length;
                if (size > MaxFileBytes || total + size > MaxTotalBytes)
                {
                    throw new InvalidDataException("SourceCapacityExceeded");
                }

                var raw = File.ReadAllBytes(path);
                total += raw.Length;
                if (raw.Length > MaxFileBytes || total > MaxTotalBytes)
                {
                    throw new InvalidDataException("SourceCapacityExceeded");
                }

                using (var sha = SHA256.Create())
                {
                    sources.Add(new SourceFile(path.Substring(root.Length + 1).Replace('\\', '/'), DecodeStrict(raw), raw.Length, Hex(sha.ComputeHash(raw))));
                }
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is DecoderFallbackException
            || exception is EncoderFallbackException || exception is NotSupportedException || exception is ArgumentException || exception is XmlException)
        {
            issues.Add("SourceClosureReadFailed");
        }

        if (sources.Count == 0)
        {
            issues.Add("NoReadableCSharpSource");
        }

        return new ProjectInput(projectName, sources, issues, root);
    }

    /// <summary>只走明列來源與專案參考；不執行匯入或建置事件，不把磁碟副本納入閉包。</summary>
    private static void Collect(string root, string projectFile, HashSet<string> visited, HashSet<string> paths)
    {
        projectFile = CheckedPath(root, root, projectFile);
        if (!visited.Add(projectFile))
        {
            return;
        }

        if (visited.Count > 64 || new FileInfo(projectFile).Length > MaxFileBytes)
        {
            throw new InvalidDataException("ProjectClosureCapacityExceeded");
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxFileBytes };
        XDocument document;
        using (var reader = XmlReader.Create(projectFile, settings))
        {
            document = XDocument.Load(reader);
        }

        var directory = Path.GetDirectoryName(projectFile)!;
        foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "Compile" || item.Name.LocalName == "ProjectReference"))
        {
            var include = (string?)element.Attribute("Include");
            if (include == null || include.Length == 0 || include.IndexOfAny(UnsupportedIncludeCharacters) >= 0
                || element.AncestorsAndSelf().Any(item => item.Attribute("Condition") != null))
            {
                throw new InvalidDataException("UnsupportedProjectInclude");
            }

            if (element.Name.LocalName == "Compile" && (include.Split('\\', '/').Any(segment => segment.Equals("Properties", StringComparison.OrdinalIgnoreCase))
                || Path.GetFileName(include).Equals("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var path = CheckedPath(root, directory, include);
            if (element.Name.LocalName == "ProjectReference")
            {
                if (!path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("ProjectReferenceInvalid");
                }

                Collect(root, path, visited, paths);
            }
            else if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
                if (paths.Count > MaxFiles)
                {
                    throw new InvalidDataException("SourceFileCountExceeded");
                }
            }
            else
            {
                throw new InvalidDataException("SourceExtensionInvalid");
            }
        }
    }

    /// <summary>正規化後逐層檢查來源所有權；禁止 junction、任意 root 外來源或禁止目錄。</summary>
    private static string CheckedPath(string root, string directory, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(directory, relative));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("SourcePathOutsideRoot");
        }

        var cursor = root;
        foreach (var segment in full.Substring(prefix.Length).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            cursor = Path.Combine(cursor, segment);
            if (IsForbiddenName(cursor) || IsReparsePoint(cursor))
            {
                throw new InvalidDataException("SourcePathForbidden");
            }
        }

        return full;
    }

    /// <summary>依 BOM 判斷 UTF 編碼；無 BOM 先用嚴格 UTF-8，CP950 後備必須原位元組往返。</summary>
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
            var encoding = Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var text = encoding.GetString(raw);
            if (!encoding.GetBytes(text).SequenceEqual(raw))
            {
                throw new InvalidDataException("SourceEncodingNotRoundTrip");
            }

            return text;
        }
    }

    /// <summary>禁止目錄以完整名稱判斷，不將一般名稱相似的正式專案誤當副本。</summary>
    private static bool IsForbiddenName(string path) => ForbiddenNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>檢查真實檔案屬性，避免唯讀操作跨越符號連結與 junction。</summary>
    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>雜湊固定使用小寫十六進位，不依賴目前文化或跨呼叫狀態。</summary>
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
