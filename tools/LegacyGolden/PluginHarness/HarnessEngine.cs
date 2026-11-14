/*
檔案整體說明

目的與定位：組合受限 Roslyn compiler、逐情境 AppDomain runner 與輸出秘密掃描，形成一次完整的 PluginHarness 候選；本檔不負責檔案發布或外部服務。

主要功能與流程：RunSources 對合成來源執行相同安全編譯、拒絕未能證明的入口、使用單案例隔離域執行 Plugin 或 Activity，並在回傳前掃描完整輸出。任何編譯、執行或秘密驗證失敗皆回固定失敗結果且不包含部分案例。

使用邊界與維護：所有輸入、編譯位元組與結果都只在單次同步呼叫中存在；compiler 結果由 using Dispose，runner 擁有隔離域及 scratch 並在 finally 卸載／清除，沒有共享快取、Session、credential、timer 或外部連線。輸出只含實際假服務寫入、記錄傳輸、活動輸出與固定例外型別，不含原始碼與診斷本文。
*/
using System;
using System.IO;
using Newtonsoft.Json.Linq;
namespace LegacyGolden.PluginHarness;
/// <summary>將來源交給相同受限編譯與隔離執行管線，失敗時不回傳部分案例。</summary>
public static class GoldenHarness
{
    private static readonly string[] MissingEntryDiagnostic = { "EntryPointMissing" };
    private static readonly string[] RejectedOutputDiagnostic = { "OutputRejected" };

    /// <summary>編譯並實際執行合成或舊來源入口；輸出經秘密掃描後才回傳。</summary>
    public static JObject RunSources(string[] sources, string scenariosJson)
    {
        using var compilation = LegacyProjectCompiler.CompileSources(sources);
        if (compilation.Status != "Generated")
        {
            return Failure(compilation.Status, compilation.Diagnostics);
        }

        if (compilation.AssemblyBytes == null || compilation.EntryType == null)
        {
            return Failure("CompileFailed", MissingEntryDiagnostic);
        }

        var candidate = ScenarioRunner.Run(compilation.AssemblyBytes, compilation.EntryType, scenariosJson);
        if ((string?)candidate["status"] != "Generated")
        {
            return candidate;
        }

        try
        {
            OutputSecretScan.Validate(candidate);
            return candidate;
        }
        catch (InvalidDataException)
        {
            return Failure("CompileFailed", RejectedOutputDiagnostic);
        }
    }

    /// <summary>建立不帶部分案例、路徑或例外本文的固定失敗契約。</summary>
    private static JObject Failure(string status, System.Collections.Generic.IEnumerable<string> diagnostics) => new()
    {
        ["status"] = status,
        ["diagnostics"] = new JArray(diagnostics)
    };
}
