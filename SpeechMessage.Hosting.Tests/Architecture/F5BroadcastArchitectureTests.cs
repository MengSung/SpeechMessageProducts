/*
檔案整體說明
目的與定位：F5 唯一出口的離線來源及編譯器禁止契約，與既有架構圖測試位於同一測試專案。
主要功能與流程：掃描工作樹所有 C# 原文，只有根 SDK 與 bin/obj 可排除；合成目錄驗證相似
名稱與巢狀 SDK 不能躲過。另核對四個具體方法 doc-ID，保護介面與實作兩組多載。
真分析器驗證只在記憶體編譯合成的四個故意禁止呼叫，對照空清單確認 RS0030 確由禁令產生；
不輸出或執行合成 DLL。每案 AssemblyMetadata 與 JSON reader 都決定性釋放，參考名稱常數
只含無身分的不可變字串，不共用編譯、分析器實例或候選訊息。
使用邊界與維護：不編譯或執行掃描到的程式，不讀秘密或連網；逐檔限 16MiB、拒絕 reparse，
只保留至多 100 筆相對路徑。暫存目錄由案例獨佔並 finally 刪除，不能跨案例分享狀態。
掃描器的辨識字串採兩段常數組合，避免原文自指；這不是產品呼叫的反射或 dynamic 後門。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>來源掃描不依賴分析器 opt-in，測試專案也必須遵守相同禁止政策。</summary>
public sealed class F5BroadcastArchitectureTests
{
    /// <summary>合成編譯所需的固定 runtime 參考名；不保存 AssemblyMetadata 或其他案例資源。</summary>
    private static readonly ImmutableArray<string> RuntimeReferenceNames =
        ImmutableArray.Create("System.Runtime", "System.Collections", "System.Threading.Tasks", "netstandard");

    /// <summary>只有 SDK 本體可保留相容 API，其他來源包含測試都不得呼叫。</summary>
    [Fact]
    public void Source_NoBroadcastCallsOutsideSdk() =>
        Assert.Empty(Scan(ArchitectureProjectReader.FindRoot()));

    /// <summary>doc-ID 必須精確包含介面與具體型別兩個多載；不能以寬鬆字串取代。</summary>
    [Fact]
    public void BannedSymbols_BothOverloadsAndBothTypes_AreExplicit()
    {
        var lines = File.ReadAllLines(Path.Combine(ArchitectureProjectReader.FindRoot(), "BannedSymbols.txt"));
        foreach (var type in new[] { "ILineMessagingClient", "LineMessagingClient" })
        foreach (var parameters in new[]
        {
            "System.Collections.Generic.IList{Line.Messaging.ISendMessage}",
            "System.Collections.Generic.IList{Line.Messaging.ISendMessage},System.String"
        })
        {
            var id = "M:Line.Messaging." + type + ".Broadcast" + "MessageAsync(" + parameters + ")";
            Assert.Single(lines.Where(line => line.StartsWith(id + ";", StringComparison.Ordinal)
                && line.Contains("禁止 broadcast：同一 Channel 有 20 間教會", StringComparison.Ordinal)));
        }
    }

    /// <summary>
    /// 真正載入已還原的 BannedApi 分析器，合成四個禁止呼叫必須各產生 RS0030 Error；
    /// 空禁止清單對照必須無 RS0030。只編譯語法樹，不產生 DLL、不執行禁止方法。
    /// PE metadata 由案例擁有，分析完成後逐一 Dispose，不留下檔案 handle。
    /// </summary>
    /// <returns>正反對照分析都完成且 PE owner 清理後結束；建置或分析器故障不能當成成功禁用。</returns>
    [Fact]
    public async System.Threading.Tasks.Task BannedSymbols_RealAnalyzer_RejectsFourBoundOverloads()
    {
        var root = ArchitectureProjectReader.FindRoot();
        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "SpeechMessage.Hosting.Tests", "obj", "project.assets.json")));
        var library = assets.RootElement.GetProperty("libraries").EnumerateObject().Single(item =>
            item.Name.StartsWith("Microsoft.CodeAnalysis.BannedApiAnalyzers/", StringComparison.OrdinalIgnoreCase));
        var relative = library.Value.GetProperty("path").GetString()!;
        var analyzerPath = assets.RootElement.GetProperty("packageFolders").EnumerateObject()
            .Select(folder => Path.Combine(folder.Name, relative, "analyzers", "dotnet", "cs", "Microsoft.CodeAnalysis.CSharp.BannedApiAnalyzers.dll"))
            .Single(File.Exists);
        // 只載入真正保護 C# 禁令的公開 analyzer；整份 GetTypes 也會載入不使用的 IDE code-fix，
        // 無端要求 Workspaces／MEF，反而在分析開始前失敗。不得捕捉載入錯誤或略過 RS0030。
        var analyzerType = Assembly.LoadFrom(analyzerPath).GetType(
            "Microsoft.CodeAnalysis.CSharp.BannedApiAnalyzers.CSharpSymbolIsBannedAnalyzer", throwOnError: true)!;
        var analyzer = Assert.IsAssignableFrom<DiagnosticAnalyzer>(Activator.CreateInstance(analyzerType));
        Assert.Contains(analyzer.SupportedDiagnostics, diagnostic => diagnostic.Id == "RS0030");
        var analyzers = ImmutableArray.Create(analyzer);
        var method = "Broadcast" + "MessageAsync";
        var source = "using System.Collections.Generic; using System.Threading.Tasks; using Line.Messaging;"
            + " public static class IntentionalNegativeProbe { public static Task Check("
            + "ILineMessagingClient a, LineMessagingClient b, IList<ISendMessage> m) => Task.WhenAll("
            + "a." + method + "(m), a." + method + "(m, \"retry\"), b." + method + "(m), b." + method + "(m, \"retry\")); }";
        var owners = new List<AssemblyMetadata>();
        try
        {
            var paths = new[] { typeof(object).Assembly.Location, typeof(Line.Messaging.LineMessagingClient).Assembly.Location }
                .Concat(RuntimeReferenceNames
                    .Select(name => Assembly.Load(name).Location)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths) owners.Add(AssemblyMetadata.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("F5IntentionalNegativeProbe",
                new[] { CSharpSyntaxTree.ParseText(source) }, owners.Select(owner => owner.GetReference()),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    specificDiagnosticOptions: ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("RS0030", ReportDiagnostic.Error)));
            Assert.DoesNotContain(compilation.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
            var banned = File.ReadAllText(Path.Combine(root, "BannedSymbols.txt"));
            var rejected = await compilation.WithAnalyzers(analyzers,
                new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(new SymbolsText(banned)))).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(4, rejected.Count(item => item.Id == "RS0030" && item.Severity == DiagnosticSeverity.Error));
            Assert.DoesNotContain(rejected, item => item.Id == "AD0001");
            var control = await compilation.WithAnalyzers(analyzers,
                new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(new SymbolsText(string.Empty)))).GetAnalyzerDiagnosticsAsync();
            Assert.DoesNotContain(control, item => item.Id is "RS0030" or "AD0001");
        }
        finally { foreach (var owner in owners) owner.Dispose(); }
    }

    /// <summary>合成相似與巢狀路徑不得排除；SDK 根、bin、obj 精確段才可排除。</summary>
    [Fact]
    public void Source_Exclusions_AreExactPathSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-f5-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var call = "x.Broadcast" + "MessageAsync();";
            foreach (var path in new[] { "Line.Messaging/a.cs", "a/bin/a.cs", "a/obj/a.cs",
                "Line.Messaging.Tests/a.cs", "nested/Line.Messaging/a.cs", "binary/a.cs" })
            {
                var full = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, call, new UTF8Encoding(false));
            }
            var failures = Scan(root);
            Assert.Equal(3, failures.Count);
            Assert.Contains(failures, path => path.StartsWith("Line.Messaging.Tests/", StringComparison.Ordinal));
            Assert.Contains(failures, path => path.StartsWith("nested/", StringComparison.Ordinal));
            Assert.Contains(failures, path => path.StartsWith("binary/", StringComparison.Ordinal));
        }
        finally
        {
            // root 由本案例建立且是系統暫存下的完整隨機目錄，不從掃描結果計算刪除目標。
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>缺目錄或沒有任何納管 C# 檔不能假稱來源清白。</summary>
    [Fact]
    public void Source_MissingOrEmptyRoot_FailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-f5-empty-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() => Scan(root));
        Directory.CreateDirectory(root);
        try { Assert.Throws<InvalidDataException>(() => Scan(root)); }
        finally { Directory.Delete(root); }
    }

    /// <summary>
    /// 逐目錄讀取並在遞迴前裁決精確排除；不跟隨 link，不讓巨大來源或大量命中無界留存。
    /// 嚴格 UTF-8 解碼失敗直接傳出，不能以 replacement 隱藏禁止呼叫。
    /// </summary>
    private static List<string> Scan(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Source scan root is missing.");
        var pending = new Stack<string>();
        pending.Push(root);
        var failures = new List<string>();
        var count = 0;
        var needle = "Broadcast" + "MessageAsync(";
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Source scan cannot follow links.");
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(directory);
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                    || current == root && name.Equals("Line.Messaging", StringComparison.Ordinal)) continue;
                pending.Push(directory);
            }
            foreach (var file in Directory.EnumerateFiles(current, "*.cs"))
            {
                if (++count > 20000 || new FileInfo(file).Length > 16 * 1024 * 1024
                    || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Source scan exceeded its bounded input contract.");
                if (!File.ReadAllText(file, new UTF8Encoding(false, true)).Contains(needle, StringComparison.Ordinal)) continue;
                failures.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                if (failures.Count >= 100) throw new InvalidDataException("Too many prohibited source calls.");
            }
        }
        if (count == 0) throw new InvalidDataException("Source scan found no source files.");
        return failures;
    }

    /// <summary>純記憶體禁止清單輸入，不產生檔案；分析器完成後即可回收來源文字。</summary>
    private sealed class SymbolsText(string content) : AdditionalText
    {
        /// <summary>分析器要求的正式檔名，不是可由 request 選擇的磁碟路徑。</summary>
        public override string Path => "BannedSymbols.txt";
        /// <summary>回傳單案例不可變 SourceText，沒有跨執行共用內容。</summary>
        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => SourceText.From(content, Encoding.UTF8);
    }
}
