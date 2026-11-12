/*
 * 檔案整體說明
 *
 * 目的與定位：保護主方案受版控 C# 與 config 原始碼不含舊永豐金鑰識別字或固定十六進位金鑰材料。
 * 掃描透過 git 列舉目前工作樹的已追蹤檔案，只讀取 .cs 與 .config，並排除建置輸出、
 * 舊方案目錄及本測試檔；appsettings JSON 不在範圍內，避免觸碰使用者明確保留的既有設定。
 *
 * 主要情境：四種禁用樣式各自掃描每個候選檔，命中時只回報相對路徑與樣式名稱，
 * 不輸出檔案內容。測試不連外、不讀 User Secrets 或環境變數，也不保存命中的原文。
 *
 * 維護與生命週期：git 行程由單次測試同步啟動、讀取輸出並 Dispose；候選清單與檔案文字
 * 僅在測試呼叫內存活。新增產品程式檔後先納入 Git index 再執行本案例，以確保它也在掃描範圍。
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    public void Source_NoHardcodedKeys()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = GetTrackedSourceFiles(root);
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

    /// <summary>以 Git NUL 分隔輸出列舉已追蹤路徑，避免遞迴進入忽略的舊方案或巢狀 worktree。</summary>
    private static string[] GetTrackedSourceFiles(string repositoryRoot)
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
        process.StartInfo.ArgumentList.Add("--cached");
        process.StartInfo.ArgumentList.Add("-z");
        if (!process.Start())
        {
            throw new InvalidOperationException("Git source enumeration did not start.");
        }

        var output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Git source enumeration failed.");
        }

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
