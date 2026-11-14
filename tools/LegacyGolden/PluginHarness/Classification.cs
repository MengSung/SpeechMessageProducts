/*
檔案整體說明

目的與定位：負責將已 bounded 讀取的舊 C# 專案做離線安全分類與 Roslyn 編譯，不執行舊程式、不載入 DLL，也不嘗試建立外部 API 替身。

主要功能與流程：先掃描固定秘密樣式及無法安全攔截的連外 API；Network API 命中立即分類 BlockedByNetworkApi，秘密命中分類 CompileFailed，兩者都不會把匹配本文寫入輸出。無阻擋時以明確 framework metadata references 建立 deterministic CSharpCompilation，僅輸出診斷代碼與計數；編譯成功才分類 Generated。

使用邊界與維護：Roslyn compilation 是單次區域物件，沒有全域快取、請求狀態、網路連線、背景 task 或可重用的未確定傳輸。掃描採固定 Regex 逾時並只回傳固定代碼。不能證明安全的來源一律拒絕，安全性優先於編譯覆蓋率。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LegacyGolden.PluginHarness;

/// <summary>固定模式掃描器；只回傳分類代碼，不回傳匹配字串。</summary>
internal static class SafetyScanner
{
    private static readonly string[] NetworkPatterns =
    {
        @"(?i)\bHttpClient\b", @"(?i)\bWebClient\b", @"(?i)\bHttpWebRequest\b", @"(?i)\bWebRequest\b",
        @"(?i)\bOrganizationServiceProxy\b", @"(?i)\bIOrganizationService\b", @"(?i)\bServiceClient\b",
        @"(?i)\bRestClient\b", @"(?i)\bLineMessagingClient\b", @"(?i)\bNotifyBot\b", @"(?i)\bTcpClient\b",
        @"(?i)\bUdpClient\b", @"(?i)\bSocket\b", @"(?i)\bSmtpClient\b", @"(?i)\bWebSocket\b",
        @"(?i)\bDns\s*\.\s*", @"(?i)https?://"
    };

    private static readonly string[] SecretPatterns =
    {
        @"(?i)(?:channel\s*secret|access[\s_-]*token|client[\s_-]*secret|api[\s_-]*key|password)\s*[:=]\s*[""']?[A-Za-z0-9+/=_-]{8,}",
        @"(?<![A-Za-z0-9])[UCR][0-9a-fA-F]{32}(?![A-Za-z0-9])"
    };

    /// <summary>回傳是否含不可安全攔截的連外 API。</summary>
    internal static bool HasNetworkApi(IEnumerable<SourceFile> sources)
    {
        return ContainsAny(sources, NetworkPatterns);
    }

    /// <summary>回傳是否含疑似秘密或固定平台識別樣式。</summary>
    internal static bool HasSecretPattern(IEnumerable<SourceFile> sources)
    {
        return ContainsAny(sources, SecretPatterns);
    }

    /// <summary>驗證候選 JSON 不含秘密樣式或 replacement character。</summary>
    internal static void EnsureSafeOutput(string json)
    {
        if (json.IndexOf('\ufffd') >= 0 || RegexMatches(json, SecretPatterns))
        {
            throw new InvalidDataException("Candidate output failed the secret-pattern scan.");
        }
    }

    /// <summary>固定 pattern 清單逐檔掃描，Regex 逾時時 fail closed。</summary>
    private static bool ContainsAny(IEnumerable<SourceFile> sources, IEnumerable<string> patterns)
    {
        foreach (var source in sources)
        {
            if (RegexMatches(source.Text, patterns))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>執行有限時間的文化不變 Regex 判斷，不保存 Match 或來源片段。</summary>
    private static bool RegexMatches(string text, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            try
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                throw new InvalidDataException("Safety scan timed out.");
            }
        }

        return false;
    }
}

/// <summary>Roslyn 舊專案分類器；只編譯記憶體中的來源，不產生或執行 DLL。</summary>
internal static class LegacyClassifier
{
    /// <summary>依安全掃描、讀取狀態與 Roslyn 診斷產生單一脫敏結果。</summary>
    internal static ProjectResult Classify(ProjectInput input)
    {
        var diagnostics = new List<string>(input.ReadIssues);
        if (SafetyScanner.HasNetworkApi(input.Sources))
        {
            diagnostics.Add("NetworkApiRefused");
            return new ProjectResult(input.Name, ProjectClassification.BlockedByNetworkApi, input.Sources.Count, input.ByteCount, input.SourceSha256, diagnostics);
        }

        if (SafetyScanner.HasSecretPattern(input.Sources))
        {
            diagnostics.Add("SecretPatternDetected");
            return new ProjectResult(input.Name, ProjectClassification.CompileFailed, input.Sources.Count, input.ByteCount, input.SourceSha256, diagnostics);
        }

        if (input.ReadIssues.Count > 0 || input.Sources.Count == 0)
        {
            return new ProjectResult(input.Name, ProjectClassification.CompileFailed, input.Sources.Count, input.ByteCount, input.SourceSha256, diagnostics);
        }

        try
        {
            var trees = input.Sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), source.RelativePath)).ToArray();
            var compilation = CSharpCompilation.Create(
                assemblyName: "LegacyGolden_" + StableAssemblySuffix(input.Name),
                syntaxTrees: trees,
                references: FrameworkReferences(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, deterministic: true));
            var errors = compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).Take(16).ToList();
            if (errors.Count > 0)
            {
                diagnostics.Add("RoslynErrorCount:" + errors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (var error in errors.Select(item => item.Id).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
                {
                    diagnostics.Add("Roslyn:" + error);
                }

                return new ProjectResult(input.Name, ProjectClassification.CompileFailed, input.Sources.Count, input.ByteCount, input.SourceSha256, diagnostics);
            }

            return new ProjectResult(input.Name, ProjectClassification.Generated, input.Sources.Count, input.ByteCount, input.SourceSha256, diagnostics);
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is NotSupportedException || exception is ArgumentException)
        {
            diagnostics.Add("RoslynInfrastructureFailure");
            return new ProjectResult(input.Name, ProjectClassification.CompileFailed, input.Sources.Count, input.ByteCount, input.SourceSha256, diagnostics);
        }
    }

    /// <summary>取得目前 net48 執行環境可用的固定 framework references；缺少參考時編譯失敗而不猜測。</summary>
    private static IEnumerable<MetadataReference> FrameworkReferences()
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(typeof(object).Assembly.Location);
        Add(typeof(Uri).Assembly.Location);
        Add(typeof(Enumerable).Assembly.Location);
        Add(typeof(System.Data.DataSet).Assembly.Location);
        var frameworkDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var name in new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Data.dll", "System.Xml.dll", "Microsoft.CSharp.dll" })
        {
            Add(Path.Combine(frameworkDirectory, name));
        }

        foreach (var location in locations)
        {
            yield return MetadataReference.CreateFromFile(location);
        }

        void Add(string location)
        {
            if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            {
                locations.Add(location);
            }
        }
    }

    /// <summary>以名稱雜湊形成不含來源路徑的 deterministic assembly name。</summary>
    private static string StableAssemblySuffix(string name)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in name)
            {
                hash = hash * 31 + character;
            }

            return Math.Abs(hash).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
