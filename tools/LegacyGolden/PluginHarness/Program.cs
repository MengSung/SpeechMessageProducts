/*
檔案整體說明
目的與定位：提供 P5-02 PluginHarness 的正式 generate 命令入口，將操作者指定的可信舊專案及合成情境交給離線編譯、隔離執行與安全發布管線。
主要功能與流程：程序啟動先設定失效 loopback 預設 proxy，再嚴格解析四個唯一必要選項；以一 MiB 位元組上限完整讀取嚴格 UTF-8 情境，呼叫 LegacyProjectCompiler.Compile。僅 Generated 且具有組件與入口型別時呼叫 ScenarioRunner.Run，完整成功候選才交給 GoldenWriter 掃描及原子發布。
使用邊界與維護：來源安全與傳輸攔截由 compiler 負責，runner 擁有每案例 AppDomain 及 scratch 的逾時、卸載與清理。此處以 using 擁有情境檔案及 CompilationResult，先等待 runner 完成卸載，再清零編譯位元組；不快取來源、情境、credential 或結果。proxy 是本工具程序全生命週期的額外防線，不能代替語意攔截。所有失敗只輸出固定代碼，不顯示輸入路徑、診斷本文或例外內容；驗證使用離線子程序測試與位元組檢查。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace LegacyGolden.PluginHarness;

/// <summary>只擁有 CLI 輸入邊界及單次編譯結果，不承擔舊入口執行或黃金序列化。</summary>
internal static class Program
{
    private const int MaxScenarioBytes = 1024 * 1024;

    /// <summary>
    /// 在任何來源處理前設定本機失效 proxy；參數、編譯或執行失敗均不呼叫發布入口。
    /// 舊入口同步執行完畢且 runner 卸載執行域後，才釋放編譯結果與其中組件位元組。
    /// </summary>
    /// <param name="args">generate 與 legacy-root、module、scenarios、out 四組區分大小寫選項，或單獨 help。</param>
    /// <returns>零表示說明或完整發布成功；二表示參數錯誤；一表示輸入、編譯、執行或發布失敗。</returns>
    private static int Main(string[] args)
    {
        try
        {
            WebRequest.DefaultWebProxy = new WebProxy("http://127.0.0.1:9", false);
            if (args != null && args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
            {
                PrintUsage();
                return 0;
            }

            var options = ParseOptions(args);
            if (options == null)
            {
                PrintUsage();
                return 2;
            }

            string scenarios;
            try
            {
                scenarios = ReadScenarios(options["--scenarios"]);
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException || exception is NotSupportedException || exception is System.Security.SecurityException)
            {
                return Failure("ScenarioInputInvalid");
            }

            CompilationResult compilation;
            try
            {
                compilation = LegacyProjectCompiler.Compile(options["--legacy-root"], options["--module"]);
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException
                || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                return Failure("CompileFailed");
            }

            using (compilation)
            {
                if (!string.Equals(compilation.Status, "Generated", StringComparison.Ordinal))
                {
                    return Failure(string.Equals(compilation.Status, "BlockedByNetworkApi", StringComparison.Ordinal)
                        ? "BlockedByNetworkApi" : "CompileFailed");
                }

                var bytes = compilation.AssemblyBytes;
                var entryType = compilation.EntryType;
                if (bytes == null || bytes.Length == 0 || string.IsNullOrWhiteSpace(entryType))
                {
                    return Failure("CompiledEntryMissing");
                }

                var candidate = ScenarioRunner.Run(bytes, entryType!, scenarios);
                if (!string.Equals((string?)candidate["status"], "Generated", StringComparison.Ordinal))
                {
                    return Failure("ScenarioExecutionFailed");
                }

                GoldenWriter.Write(options["--out"], candidate);
                return 0;
            }
        }
        catch (Exception)
        {
            // 最外層包含 writer 拒絕與基礎設施失敗；不能把含路徑或來源常值的例外本文回顯。
            return Failure("OperationFailed");
        }
    }

    /// <summary>只接受 generate 加四組必要選項；未知、重複、空值、旗標當值與額外位置參數在任何讀取前拒絕。</summary>
    private static Dictionary<string, string>? ParseOptions(string[]? args)
    {
        if (args == null || args.Length != 9 || args[0] != "generate")
        {
            return null;
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            var name = args[index];
            var value = args[index + 1];
            if ((name != "--legacy-root" && name != "--module" && name != "--scenarios" && name != "--out")
                || options.ContainsKey(name) || string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal))
            {
                return null;
            }

            options.Add(name, value);
        }

        return options;
    }

    /// <summary>
    /// 從單一唯讀 handle 讀到 EOF，最多保留一 MiB 加一位元組以辨識超限，避免 FileInfo 檢查後檔案成長繞過上限。
    /// 嚴格解碼完整 UTF-8，允許 UTF-8 BOM 但不自動切換 UTF-16；讀取及解碼失敗不得交給 compiler。
    /// </summary>
    private static string ReadScenarios(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > MaxScenarioBytes)
            {
                throw new InvalidDataException("ScenarioCapacityExceeded");
            }

            var buffer = new byte[MaxScenarioBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = stream.Read(buffer, length, buffer.Length - length);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length == 0 || length > MaxScenarioBytes)
            {
                throw new InvalidDataException("ScenarioCapacityExceeded");
            }

            var offset = length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(buffer, offset, length - offset);
        }
    }

    /// <summary>僅輸出呼叫端傳入的固定安全代碼；禁止傳入任何操作者值、編譯診斷或例外訊息。</summary>
    private static int Failure(string code)
    {
        Console.Error.WriteLine("PluginHarness: " + code);
        return 1;
    }

    /// <summary>輸出固定契約，不回顯路徑、module 或不合法參數內容。</summary>
    private static void PrintUsage() => Console.Error.WriteLine(
        "Usage: PluginHarness generate --legacy-root <Dynamics365Line> --module <project> --scenarios <scenarios.json> --out <legacy.json> | --help | -h");
}
