/*
檔案整體說明
目的與定位：保護 P5-02 PluginHarness 命令列的離線輸入與失敗不發布契約，補足直接呼叫編譯器及 runner 的測試。
主要功能與流程：由測試啟動正式工具子程序，傳入說明、未知／重複／缺漏參數、超限或損毀 UTF-8 情境，以及不存在的舊來源；核對固定結束碼、脫敏診斷、未建立輸出及既有完整輸出不變。另以外部合成專案證明成功路徑實際編譯、執行及發布欄位更新。
使用邊界與維護：只建立測試獨占的暫存目錄與合成資料，成功案例只提供合成 IPlugin，其餘使用不存在來源，不提供真實來源或網路端點。每次子程序有三十秒上限，測試 owner 負責等待輸出排空並 Dispose；逾時終止程序後再等待退出。finally 清除唯一目錄，避免並行案例共享 Console、預設 proxy、來源及黃金結果；此處不是 D365 或 LINE 實機驗收。
*/
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using LegacyGolden.PluginHarness;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Harness;

/// <summary>透過正式子程序隔離 Console 與 proxy，驗證 CLI 不會因無效輸入發布部分黃金。</summary>
public sealed class PluginHarnessCliTests
{
    /// <summary>單獨的說明旗標成功結束；不需來源，也不建立檔案或啟動舊程式。</summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Cli_Help_PrintsGenerateContract(string argument)
    {
        var result = Invoke(argument);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("generate --legacy-root", result.Output, StringComparison.Ordinal);
        Assert.Contains("--module", result.Output, StringComparison.Ordinal);
        Assert.Contains("--scenarios", result.Output, StringComparison.Ordinal);
        Assert.Contains("--out", result.Output, StringComparison.Ordinal);
    }

    /// <summary>未知、重複、缺值及混用 help 的參數一律拒絕，不能只取出已知選項後繼續執行。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("generate")]
    [InlineData("unknown")]
    [InlineData("--help generate")]
    [InlineData("generate --help")]
    [InlineData("generate --legacy-root root --module module --scenarios input --out output --unknown secret-marker")]
    [InlineData("generate --legacy-root root --module module --scenarios input --out output --module again")]
    [InlineData("generate --legacy-root root --module module --scenarios input --out")]
    [InlineData("generate --legacy-root root --module module --scenarios --out output")]
    [InlineData("generate --legacy-root root --module module --scenarios input --out output extra")]
    [InlineData("generate --legacy-root root --module module --scenarios input --out output --OUT another")]
    [InlineData("generate --legacy-root root --module module --scenarios input --output output")]
    public void Cli_InvalidArguments_ReturnUsageWithoutEcho(string arguments)
    {
        var result = Invoke(arguments);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-marker", result.Output, StringComparison.Ordinal);
    }

    /// <summary>不存在來源只回固定編譯失敗碼；先後驗證不建立目標與不覆蓋既有目標，輸出不洩漏路徑。</summary>
    [Fact]
    public void Cli_MissingSource_NeverPublishesGolden()
    {
        WithDirectory(directory =>
        {
            var scenarios = Path.Combine(directory, "scenarios.json");
            File.WriteAllText(scenarios, "{\"cases\":[]}", new UTF8Encoding(false));
            var output = Path.Combine(directory, "golden.json");
            var arguments = GenerateArguments(directory, scenarios, output);
            var first = Invoke(arguments);
            Assert.Equal(1, first.ExitCode);
            Assert.Contains("CompileFailed", first.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, first.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
            File.WriteAllText(output, "previous-complete-snapshot");
            var second = Invoke(arguments);
            Assert.Equal(1, second.ExitCode);
            Assert.Equal("previous-complete-snapshot", File.ReadAllText(output));
            Assert.DoesNotContain(directory, second.Output, StringComparison.Ordinal);
        });
    }

    /// <summary>超限檔與非法 UTF-8 必須在編譯前拒絕；不得截斷一 MiB 或使用替代字元繼續處理。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cli_InvalidScenarioBytes_NeverPublishesGolden(bool oversized)
    {
        WithDirectory(directory =>
        {
            var scenarios = Path.Combine(directory, "scenarios.json");
            File.WriteAllBytes(scenarios, oversized ? new byte[1024 * 1024 + 1] : new byte[] { 0xC3, 0x28 });
            var output = Path.Combine(directory, "golden.json");
            var result = Invoke(GenerateArguments(directory, scenarios, output));
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("ScenarioInputInvalid", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, result.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        });
    }

    /// <summary>UTF-16 BOM 不能被 StreamReader 自動偵測當成 UTF-8 接受，否則違反輸入格式契約。</summary>
    [Fact]
    public void Cli_Utf16Scenario_IsRejectedBeforeCompilation()
    {
        WithDirectory(directory =>
        {
            var scenarios = Path.Combine(directory, "scenarios.json");
            File.WriteAllText(scenarios, "{\"cases\":[]}", Encoding.Unicode);
            var output = Path.Combine(directory, "golden.json");
            var result = Invoke(GenerateArguments(directory, scenarios, output));
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("ScenarioInputInvalid", result.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        });
    }

    /// <summary>完整一 MiB 與 UTF-8 BOM 都可讀取；只有最後位元組損毀時必須在來源編譯前拒絕。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Cli_ReadsCompleteScenarioWithinByteLimit(bool bom, bool invalidTail)
    {
        WithDirectory(directory =>
        {
            var scenarios = Path.Combine(directory, "scenarios.json");
            var bytes = new byte[1024 * 1024];
            for (var index = 0; index < bytes.Length; index++) { bytes[index] = 0x20; }
            if (bom) { bytes[0] = 0xEF; bytes[1] = 0xBB; bytes[2] = 0xBF; }
            if (invalidTail) { bytes[bytes.Length - 1] = 0xC3; }
            File.WriteAllBytes(scenarios, bytes);
            var output = Path.Combine(directory, "golden.json");
            var result = Invoke(GenerateArguments(directory, scenarios, output));
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(invalidTail ? "ScenarioInputInvalid" : "CompileFailed", result.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        });
    }

    /// <summary>在獨占外部合成專案中編譯真 IPlugin，斷言 CLI 確實交給 runner 記錄更新後才發布。</summary>
    [Fact]
    public void Cli_Generate_CompilesRunsAndPublishesActualWrites()
    {
        WithDirectory(directory =>
        {
            var root = Path.Combine(directory, "Dynamics365Line");
            var project = Path.Combine(root, "Sample");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "Sample.csproj"),
                "<Project><ItemGroup><Compile Include=\"Sample.cs\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(project, "Sample.cs"), """
                using System; using Microsoft.Xrm.Sdk;
                public sealed class Sample : IPlugin { public void Execute(IServiceProvider p) {
                var c=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext));
                var f=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory));
                var s=f.CreateOrganizationService(c.UserId); var e=(Entity)c.InputParameters["Target"];
                s.Update(new Entity(e.LogicalName,e.Id){["name"]="cli-observed"}); } }
                """, new UTF8Encoding(false));
            var scenarios = Path.Combine(directory, "scenarios.json");
            File.WriteAllText(scenarios, """
                {"cases":[{"name":"normal","seed":[{"alias":"contact-1","entity":"contact","fields":{}}],
                "trigger":{"type":"Sample","message":"Update","entity":"contact","target":"contact-1","fields":{},"depth":1}}]}
                """, new UTF8Encoding(false));
            var output = Path.Combine(directory, "golden.json");
            var arguments = "generate --out \"" + output + "\" --scenarios \"" + scenarios
                + "\" --module Sample --legacy-root \"" + root + "\"";
            var result = Invoke(arguments);
            Assert.True(result.ExitCode == 0, result.Output);
            var candidate = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(output));
            Assert.Equal("Generated", (string?)candidate["status"]);
            Assert.Equal("cli-observed", (string?)candidate["cases"]![0]!["writes"]![0]!["fields"]!["name"]);
        });
    }

    /// <summary>建立包含不存在來源的正式參數；路徑雙引號保留空白，不把測試名稱當成正式來源。</summary>
    private static string GenerateArguments(string directory, string scenarios, string output) =>
        "generate --legacy-root \"" + Path.Combine(directory, "missing-source-private-marker")
        + "\" --module PresentRecordLinePlugIn --scenarios \"" + scenarios + "\" --out \"" + output + "\"";

    /// <summary>測試 owner 只刪除自行建立的唯一暫存目錄，子程序已退出才清理，沒有跨案例可變資料。</summary>
    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "smv-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>啟動實際 exe 並同時排空 stdout／stderr；逾時終止且確認退出，避免等待輸出造成死鎖或殘留程序。</summary>
    private static (int ExitCode, string Output) Invoke(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(new Uri(typeof(LegacyProjectCompiler).Assembly.CodeBase).LocalPath, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill();
            Assert.True(process.WaitForExit(5000), "CLI process did not exit after termination.");
            output.GetAwaiter().GetResult();
            error.GetAwaiter().GetResult();
            Assert.Fail("CLI process exceeded the offline test timeout.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
}
