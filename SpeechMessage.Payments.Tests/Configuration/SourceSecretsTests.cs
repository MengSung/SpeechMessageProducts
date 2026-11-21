/*
 * 檔案整體說明
 *
 * 目的與定位：保護主方案受版控 C# 與 config 原始碼不含舊永豐金鑰識別字或固定十六進位金鑰材料。
 * 掃描透過 git 列舉目前工作樹的已追蹤檔案，只讀取存在的 .cs 與 .config；缺檔只有在 Git 明確
 * 列為工作樹刪除時才略過，其他 index／工作樹不一致仍會失敗。並排除建置輸出、
 * 舊方案目錄及本測試檔；appsettings JSON 不在範圍內，避免觸碰使用者明確保留的既有設定。
 *
 * 主要情境：四種禁用樣式各自掃描每個候選檔，命中時只回報相對路徑與樣式名稱，
 * 不輸出檔案內容。測試不連外、不讀 User Secrets 或環境變數，也不保存命中的原文。
 *
 * 維護與生命週期：兩個 git 列舉由單次測試依序啟動，30 秒內完成或終止整個程序樹；
 * 標準輸出與錯誤管線會同時排空，程序最後由測試 Dispose。候選清單與檔案文字僅在本次呼叫內存活。
 * 新增產品程式檔後先納入 Git index 再執行本案例，以確保它也在掃描範圍。
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SpeechMessage.Payments.Tests.Configuration;

/// <summary>只掃描指定版本控制原始碼並以不回顯本文的方式檢查舊金鑰標記。</summary>
public sealed class SourceSecretsTests
{
    /// <summary>
    /// 逐一掃描受版控的 .cs 與 .config，排除產物、舊方案及本檔；
    /// 任何禁用樣式只能產生安全的路徑／樣式摘要，不能輸出命中本文。
    /// </summary>
    [Fact]
    public async Task Source_NoHardcodedKeys()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = await GetTrackedSourceFilesAsync(root);
        var deletedFiles = await GetDeletedTrackedSourceFilesAsync(root);
        var patterns = new (string Name, Regex Matcher)[]
        {
            ("legacy-config-marker", new Regex("XKeyConfig", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
            ("legacy-id-marker", new Regex("_XKeyID", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
            ("legacy-hash-marker", new Regex("HASH_CODE", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
            ("hex-key-material", new Regex("[0-9A-Fa-f]{16}(?:,[0-9A-Fa-f]{16}){3}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        };
        var encoding = new UTF8Encoding(false, true);
        var hits = new List<string>();

        foreach (var relativePath in sourceFiles)
        {
            var normalizedPath = relativePath.Replace('\\', '/');
            if (normalizedPath == "SpeechMessage.Payments.Tests/Configuration/SourceSecretsTests.cs"
                || normalizedPath.Split('/').Any(segment =>
                    segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                    || segment == ".其他解決方案"))
            {
                continue;
            }

            var extension = Path.GetExtension(normalizedPath);
            if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".config", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, normalizedPath));
            if (!File.Exists(fullPath))
            {
                Assert.Contains(normalizedPath, deletedFiles);
                continue;
            }

            var content = File.ReadAllText(fullPath, encoding);
            foreach (var (name, matcher) in patterns)
            {
                if (matcher.IsMatch(content))
                {
                    hits.Add($"{normalizedPath} [{name}]");
                }
            }
        }

        Assert.Empty(hits);
    }

    /// <summary>從測試輸出目錄向上定位目前工作樹，禁止回退至父工作樹或其他方案副本。</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    /// <summary>以 Git NUL 分隔輸出列舉已追蹤路徑，不遞迴進入忽略的舊方案或巢狀 worktree。</summary>
    private static async Task<string[]> GetTrackedSourceFilesAsync(string repositoryRoot)
    {
        var output = await RunGitLsFilesAsync(repositoryRoot, "--cached");
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>取得 Git 明確辨識的刪除路徑；Git 失敗時中止測試，不把缺檔當成安全略過。</summary>
    private static async Task<HashSet<string>> GetDeletedTrackedSourceFilesAsync(string repositoryRoot)
    {
        var output = await RunGitLsFilesAsync(repositoryRoot, "--deleted");
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// 有界執行 Git 路徑列舉，並同時排空兩個輸出管線以避免管線回壓造成死結。
    /// 逾時時終止整個程序樹、等待程序結束並排空輸出後才釋放程序資源。
    /// </summary>
    private static async Task<string> RunGitLsFilesAsync(string repositoryRoot, string option)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true),
                StandardErrorEncoding = new UTF8Encoding(false, true)
            }
        };
        process.StartInfo.ArgumentList.Add("ls-files");
        process.StartInfo.ArgumentList.Add(option);
        process.StartInfo.ArgumentList.Add("-z");
        if (!process.Start())
        {
            throw new InvalidOperationException("Git source enumeration did not start.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);
            throw new TimeoutException("Git source enumeration exceeded its 30-second limit.");
        }

        var output = await outputTask;
        _ = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Git source enumeration failed.");
        }

        return output;
    }
}
