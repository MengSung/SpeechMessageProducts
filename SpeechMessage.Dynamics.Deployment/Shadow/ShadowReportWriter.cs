/*
 * 檔案整體說明
 * 目的與定位：Deployment 影子執行完成後的最小安全 Markdown 報告發布邊界，
 * 接收核定模組、基準及 runner 回傳的三方比較，不讀情境本文、SDK 或秘密。
 * 主要功能與流程：先驗證固定白名單、一至六筆唯一情境序號與完整比較，再建立
 * operation-local 摘要。使用借用 TimeProvider 的 UTC 日期命名，CreateNew 獨占
 * 建立檔案，以 UTF-8 無 BOM、CRLF 寫入並 flush 到磁碟，關閉後才回傳路徑與結果。
 * 使用邊界與維護：outputRoot 是呼叫端核定的報告目錄，檔名只含固定模組及日期；
 * 每次呼叫唯一擁有新建 stream，失敗先 Dispose 再刪除自己建立的部分檔，既有檔
 * 絕不覆寫或刪除。若清理也失敗，聚合保留原始及清理例外，由命令共用診斷處理。
 * 不快取結果、不保留連線與跨使用者狀態；有界摘要只存活於同步呼叫，報告保留期
 * 由呼叫端管理。比較位置可能含欄位或身分，故一律只輸出數量；殘留核對依 runner
 * 成功返回契約，本檔不能獨立證明實機殘留為零或環境驗收已通過。
 * 新輪必須另通過完整意圖比較，不以最終值相同忽略額外欄位或中間寫入。
 * legacy switch 專用入口明列僅舊輪範圍，只驗舊輪欄位；不得帶可比的新輪證據或
 * 將欄位相符宣稱完整影子／推播驗收。兩種範圍共用相同有界檔案 owner。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>
/// 已完成 flush 與關閉的報告結果；Path 是呼叫端報告目錄內的絕對檔案路徑，
/// Passed 只表示核定基準所要求的比較可比且零差異，不代表本型別執行實機驗證。
/// </summary>
/// <param name="Path">成功發布且不覆寫的 Markdown 路徑。</param>
/// <param name="Passed">所有要求的比較與舊輪執行狀態皆符合核定基準。</param>
internal sealed record ShadowReportResult(string Path, bool Passed);

/// <summary>無共享可變狀態的同步報告 writer；每次呼叫只持有自己的檔案與摘要。</summary>
internal static class ShadowReportWriter
{
    /// <summary>
    /// 將 runner 完整成功返回的結果寫入 outputRoot 下的 UTC 日期-module.md。
    /// new／reading／legacy-golden 要求未執行舊輪、舊側不可比較且新側零差異；
    /// legacy-live／legacy-live-only 要求真跑舊輪及三側零差異。deferred-to-P7 不可通過。
    /// 基準的離線來源由 caller 核定，本 writer 不獨立驗證黃金檔的產生來源。
    /// 不滿足比較仍保存失敗報告，輸入契約錯誤則在建立目錄前拒絕。
    /// 同日同模組已存在時拋出 I/O 例外，不刪除其他呼叫建立的報告。
    /// </summary>
    /// <param name="module">與 ShadowScenarioFiles 一致的十個固定小寫模組代號。</param>
    /// <param name="basis">與情境 loader 一致且由呼叫端核定的基準。</param>
    /// <param name="results">runner 完成還原、停用及殘留核對後返回的完整結果。</param>
    /// <param name="outputRoot">呼叫端核定的報告目錄；不在此判定組織或使用者授權。</param>
    /// <param name="timeProvider">借用時鐘，僅取一次 UTC 日期，不建立 timer 或持有時鐘。</param>
    /// <returns>僅在寫入、flush 及關閉全部成功後返回報告路徑與比較成功狀態。</returns>
    public static ShadowReportResult Write(string module, string basis,
        IReadOnlyList<ShadowCaseResult> results, string outputRoot, TimeProvider timeProvider) =>
        WriteCore(module, basis, results, outputRoot, timeProvider, false);

    /// <summary>
    /// 只發布 legacy switch 受控第一輪的欄位比較，明標 scope，不冒充新輪或推播內容驗證。
    /// 只接受真實舊輪基準，所有新輪比較都必須不可比且沒有差異，以防混用證據。
    /// </summary>
    internal static ShadowReportResult WriteLegacyRound(string module, string basis,
        IReadOnlyList<ShadowCaseResult> results, string outputRoot, TimeProvider timeProvider) =>
        WriteCore(module, basis, results, outputRoot, timeProvider, true);

    /// <summary>在完整輸入驗證後才取得新檔 owner；只把核定範圍的結果格式化為安全數量。</summary>
    private static ShadowReportResult WriteCore(string module, string basis,
        IReadOnlyList<ShadowCaseResult> results, string outputRoot, TimeProvider timeProvider, bool legacyOnly)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        if (module is not ("diagnostics" or "localization" or "weeklyreports" or "attendance" or
            "lessons" or "appointments" or "qrcodes" or "care" or "fees" or "messaging") ||
            basis is not ("legacy-live" or "legacy-golden" or "legacy-live-only" or "reading" or "deferred-to-P7" or "new") ||
            results.Count is < 1 or > 6 || (legacyOnly && basis is not ("legacy-live" or "legacy-live-only")))
            throw Invalid();

        var text = new StringBuilder("# Shadow report\r\n\r\n");
        text.Append("module=").Append(module).Append("; basis=").Append(basis).Append("\r\n");
        if (legacyOnly)
            text.Append("scope=legacy-round-only\r\n本報告只驗舊輪欄位；未執行新輪，不代表完整影子比較或 LINE 推播內容／送達通過。\r\n");
        text.Append("殘留核對與停用／還原由 runner 成功返回契約證明；本 writer 不執行遠端查詢，離線報告不代表實機驗收。\r\n");
        if (basis is "new" or "reading" or "legacy-golden")
            text.Append("本基準未執行實機舊輪；兩輪互比及舊輪對期望均不可比較。\r\n");
        var indices = new HashSet<int>();
        var passed = true;
        foreach (var item in results)
        {
            if (item is null || item.Index is < 1 or > 6 || !indices.Add(item.Index)) throw Invalid();
            text.Append("\r\nCase=").Append(item.Index.ToString(CultureInfo.InvariantCulture))
                .Append("; basis=").Append(basis).Append("; LegacyExecuted=").Append(Boolean(item.LegacyExecuted)).Append("\r\n");
            var between = AppendComparison(text, "BetweenRounds", item.BetweenRounds);
            var legacy = AppendComparison(text, "LegacyToExpected", item.LegacyToExpected);
            var shadow = AppendComparison(text, "ShadowToExpected", item.ShadowToExpected);
            var intents = AppendComparison(text, "ShadowIntentsToExpected", item.ShadowIntentsToExpected);
            if (legacyOnly)
            {
                passed &= item.LegacyExecuted && legacy && Unavailable(item.BetweenRounds) &&
                    Unavailable(item.ShadowToExpected) && Unavailable(item.ShadowIntentsToExpected);
                continue;
            }
            passed &= intents;
            passed &= basis switch
            {
                "new" or "reading" or "legacy-golden" => !item.LegacyExecuted &&
                    !item.BetweenRounds.Comparable && item.BetweenRounds.Differences.Count == 0 &&
                    !item.LegacyToExpected.Comparable && item.LegacyToExpected.Differences.Count == 0 && shadow,
                "legacy-live" or "legacy-live-only" => item.LegacyExecuted && between && legacy && shadow,
                _ => false
            };
        }
        text.Append("\r\nPassed=").Append(Boolean(passed)).Append("\r\n");
        var bytes = new UTF8Encoding(false, true).GetBytes(text.ToString());
        var date = timeProvider.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var root = Path.GetFullPath(outputRoot);
        var path = Path.Combine(root, date + "-" + module + ".md");
        Directory.CreateDirectory(root);

        // CreateNew 必須在清理 try 外：建立失敗的呼叫從未取得檔案所有權，不能刪檔。
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (stream)
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            return new ShadowReportResult(path, passed);
        }
        catch (Exception failure)
        {
            try { File.Delete(path); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Shadow report write and cleanup failed.", failure, cleanupFailure);
            }
            throw;
        }
    }

    /// <summary>
    /// 只讀取 Comparable 與差異數量，不存取可能含原始 ID、欄位路徑或秘密的差異內容。
    /// 缺少比較或差異集合視為不完整輸入，不能當成零差異。
    /// </summary>
    private static bool AppendComparison(StringBuilder text, string name, ShadowComparison comparison)
    {
        if (comparison is null || comparison.Differences is null) throw Invalid();
        var count = comparison.Differences.Count;
        if (count < 0) throw Invalid();
        text.Append(name).Append(": Comparable=").Append(Boolean(comparison.Comparable))
            .Append("; Differences=").Append(count.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        return comparison.Comparable && count == 0;
    }

    /// <summary>未執行的比較必須明確不可比且沒有差異，不能帶入另一範圍的證據。</summary>
    private static bool Unavailable(ShadowComparison comparison) =>
        !comparison.Comparable && comparison.Differences.Count == 0;

    /// <summary>固定 ASCII 布林字串，不依賴當前文化或輸入本文。</summary>
    private static string Boolean(bool value) => value ? "true" : "false";

    /// <summary>固定安全錯誤訊息，不把模組、基準或比較本文帶入例外內容。</summary>
    private static InvalidDataException Invalid() => new("Shadow report safety precondition failed.");
}
