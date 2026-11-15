/*
檔案整體說明

目的與定位：為 P5-02 舊 Plugin／Workflow 的 Trace、Debug 與 EventLog 呼叫提供離線診斷替身；
只記錄到 ExecutionCapture.Current 所擁有的 scratch，不接觸系統事件記錄、原始路徑或全域 listener。
主要功能與流程：語意改寫器將指定 Trace／Debug 方法導至同名靜態方法，將 EventLog 型別與建構
改為本類的巢狀 EventLog，靜態 WriteEntry 則導至本類。所有文字經同一追加入口寫入固定邏輯檔名；
ScratchFiles 雜湊映射路徑並以有界 stream 限制 UTF-8 檔案總長一 MiB，超量明確失敗，不截斷冒充成功。
使用邊界與維護：內容可能包含舊程式診斷資料，只保留至 runner 結束案例、卸載 AppDomain 並刪除
scratch，絕不加入 Calls、黃金投影、console 或通知。每次同步追加即關閉 writer／stream；capture
仍依既有規則登錄並於 Dispose 反向清理，因此追加次數也受 capture 的 stream 登錄容量限制。
以 capture 本身作鎖，避免同案例同檔並寫；無跨案例字串快取、背景工作、訂閱或長駐資源。Flush／
Close 只驗證 owner，因每次追加已關閉檔案；Assert 失敗只記錄，不顯示視窗或終止程序。沒有 owner、
格式錯誤、容量與檔案錯誤直接傳出，不寫原機器診斷。此替身僅支援明列 API，不模擬完整 Trace API。
維護驗證：需由整合測試驗證語意改寫、容量拒絕、案例隔離、失敗斷言及 finally 清理；外部舊方案
唯讀盤點找到 Debug.WriteLine 與 listener 設定，未找到 EventLog.WriteEntry，後者是有界相容入口。
*/
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace LegacyGolden.PluginHarness;

/// <summary>
/// 舊診斷呼叫的同步 scratch 入口；不註冊系統 listener，也不將診斷加入黃金資料。
/// 改寫器必須依真實框架 symbol 白名單替換，未知成員仍拒絕，不能開放整個 Diagnostics namespace。
/// </summary>
public static class ScratchDiagnostics
{
    /// <summary>僅作 ScratchFiles 雜湊輸入的固定名稱，不採用舊程式提供的來源或實體路徑。</summary>
    private const string FileName = "legacy-diagnostics.log";

    /// <summary>與 scratch stream 一致的一 MiB 界線；文字及格式參數在建立額外字串前先檢查。</summary>
    private const int Capacity = 1024 * 1024;

    /// <summary>取得目前 owner 後鎖住同案例追加；每次關閉檔案，錯誤不回顯內容也不落回系統診斷。</summary>
    private static void Append(string? message, bool newline)
    {
        Validate(message);
        var owner = ExecutionCapture.Current;
        lock (owner)
        {
            ScratchFiles.AppendAllText(FileName, (message ?? string.Empty) + (newline ? "\r\n" : string.Empty));
        }
    }

    /// <summary>拒絕過長輸入，避免 category／格式組合前先建立明顯超量的副本；實際 UTF-8 bytes 由 stream 再檢查。</summary>
    private static void Validate(string? value)
    {
        if (value != null && value.Length > Capacity)
            throw new InvalidDataException("Scratch diagnostic capacity exceeded.");
    }

    /// <summary>單次物件轉文字，不保留物件參照；自訂 ToString 的失敗依原呼叫傳出，內容仍受容量限制。</summary>
    private static string? Text(object? value)
    {
        var text = value?.ToString();
        Validate(text);
        return text;
    }

    /// <summary>保留目前文化的框架格式化語意；先限制格式與參數數量，格式化緩衝另設容量上限；不保存參數或將錯誤寫到外部。</summary>
    private static string Format(string format, object?[]? args)
    {
        Validate(format);
        if (args == null || args.Length == 0) return format;
        if (args.Length > 256) throw new InvalidDataException("Scratch diagnostic argument capacity exceeded.");
        foreach (var arg in args)
            if (arg is string value) Validate(value);
        // 使用有容量上限的緩衝，避免巨幅 alignment 在寫檔檢查之前建立大型填充字串。
        // 自訂物件的 ToString／IFormattable 仍屬可信舊來源的同步程式，不能藉本替身宣稱可執行敵意來源。
        var buffer = new StringBuilder(16, Capacity);
        try
        {
            buffer.AppendFormat(CultureInfo.CurrentCulture, format, args);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("Scratch diagnostic capacity exceeded.");
        }
        var text = buffer.ToString();
        Validate(text);
        return text;
    }

    /// <summary>在區域字串內組合分類與本文；不將分類當檔名，兩段各自通過容量檢查。</summary>
    private static string Categorize(string? message, string? category)
    {
        Validate(message);
        Validate(category);
        return category == null ? message ?? string.Empty : category + ": " + message;
    }

    /// <summary>同步追加文字到目前案例；null 視為空字串，無 owner 或超量即失敗。</summary>
    public static void Write(string? message) => Append(message, false);

    /// <summary>只在呼叫期間將物件轉文字，追加後不保存原物件或跨案例狀態。</summary>
    public static void Write(object? value) => Append(Text(value), false);

    /// <summary>分類只作本文前綴，不可改變固定 scratch 檔名或原系統事件來源。</summary>
    public static void Write(string? message, string? category) => Append(Categorize(message, category), false);

    /// <summary>將物件轉文字並附分類，整筆同步寫入同案例有界檔案。</summary>
    public static void Write(object? value, string? category) => Append(Categorize(Text(value), category), false);

    /// <summary>同步追加文字到目前案例；null 視為空字串，無 owner 或超量即失敗。</summary>
    public static void WriteLine(string? message) => Append(message, true);

    /// <summary>只在呼叫期間將物件轉文字，追加後不保存原物件或跨案例狀態。</summary>
    public static void WriteLine(object? value) => Append(Text(value), true);

    /// <summary>分類只作本文前綴，不可改變固定 scratch 檔名或原系統事件來源。</summary>
    public static void WriteLine(string? message, string? category) => Append(Categorize(message, category), true);

    /// <summary>將物件轉文字並附分類，整筆同步寫入同案例有界檔案。</summary>
    public static void WriteLine(object? value, string? category) => Append(Categorize(Text(value), category), true);

    /// <summary>提供 Debug.WriteLine 的格式多載；字串分類多載仍遵循框架的多載解析規則。</summary>
    public static void WriteLine(string format, params object?[]? args) => Append(Format(format, args), true);

    /// <summary>以固定嚴重性前綴記錄單筆文字，不觸發通知或原系統 listener。</summary>
    public static void TraceInformation(string? message) => Append(Categorize(message, "Information"), true);

    /// <summary>格式化後附固定嚴重性；參數不跨呼叫保留，錯誤直接傳出。</summary>
    public static void TraceInformation(string format, params object?[]? args) => TraceInformation(Format(format, args));

    /// <summary>以固定嚴重性前綴記錄單筆文字，不觸發通知或原系統 listener。</summary>
    public static void TraceWarning(string? message) => Append(Categorize(message, "Warning"), true);

    /// <summary>格式化後附固定嚴重性；參數不跨呼叫保留，錯誤直接傳出。</summary>
    public static void TraceWarning(string format, params object?[]? args) => TraceWarning(Format(format, args));

    /// <summary>以固定嚴重性前綴記錄單筆文字，不觸發通知或原系統 listener。</summary>
    public static void TraceError(string? message) => Append(Categorize(message, "Error"), true);

    /// <summary>格式化後附固定嚴重性；參數不跨呼叫保留，錯誤直接傳出。</summary>
    public static void TraceError(string format, params object?[]? args) => TraceError(Format(format, args));

    /// <summary>所有寫入已同步關閉；僅驗證目前案例 owner 存在，不操作原系統 listener。</summary>
    public static void Flush() => _ = ExecutionCapture.Current;

    /// <summary>不關閉共用或系統 listener；替身沒有長駐 handle，仍要求目前案例 owner。</summary>
    public static void Close() => _ = ExecutionCapture.Current;

    /// <summary>條件為真不產生紀錄；失敗記錄固定訊息，不顯示互動視窗或終止程序。</summary>
    public static void Assert(bool condition)
    {
        if (!condition) Fail("Assertion failed.");
    }

    /// <summary>只在斷言失敗時記錄本文，成功不讀寫 scratch；不將資料加入黃金。</summary>
    public static void Assert(bool condition, string? message)
    {
        if (!condition) Fail(message);
    }

    /// <summary>失敗才組合本文與詳細說明，維持單筆追加與有界容量。</summary>
    public static void Assert(bool condition, string? message, string? detailMessage)
    {
        if (!condition) Fail(message, detailMessage);
    }

    /// <summary>Debug 的詳細格式多載只在失敗時格式化；格式錯誤明確傳出，不被診斷吞掉。</summary>
    public static void Assert(bool condition, string? message, string detailMessageFormat, params object?[]? args)
    {
        if (!condition) Fail(message, Format(detailMessageFormat, args));
    }

    /// <summary>失敗只記錄單筆診斷，不呼叫框架 Assert／Fail，也不改變程序生命週期。</summary>
    public static void Fail(string? message) => Append(Categorize(message, "Fail"), true);

    /// <summary>本文及詳細說明組合後交由相同有界入口，不保留物件或來源狀態。</summary>
    public static void Fail(string? message, string? detailMessage) => Fail(Categorize(detailMessage, message));

    /// <summary>事件來源及附加中繼資料僅為呼叫相容參數，不註冊系統來源、不保存 rawData；只將文字寫入 scratch。</summary>
    public static void WriteEntry(string source, string message) => Append(Categorize(message, "EventLog"), true);

    /// <summary>事件來源及附加中繼資料僅為呼叫相容參數，不註冊系統來源、不保存 rawData；只將文字寫入 scratch。</summary>
    public static void WriteEntry(string source, string message, EventLogEntryType type) => Append(Categorize(message, "EventLog"), true);

    /// <summary>事件來源及附加中繼資料僅為呼叫相容參數，不註冊系統來源、不保存 rawData；只將文字寫入 scratch。</summary>
    public static void WriteEntry(string source, string message, EventLogEntryType type, int eventID) => Append(Categorize(message, "EventLog"), true);

    /// <summary>事件來源及附加中繼資料僅為呼叫相容參數，不註冊系統來源、不保存 rawData；只將文字寫入 scratch。</summary>
    public static void WriteEntry(string source, string message, EventLogEntryType type, int eventID, short category) => Append(Categorize(message, "EventLog"), true);

    /// <summary>事件來源及附加中繼資料僅為呼叫相容參數，不註冊系統來源、不保存 rawData；只將文字寫入 scratch。</summary>
    public static void WriteEntry(string source, string message, EventLogEntryType type, int eventID, short category, byte[]? rawData) => Append(Categorize(message, "EventLog"), true);

    /// <summary>
    /// EventLog 宣告與建構的最小替身；不繼承系統 EventLog，也不保存來源、機器、記錄名稱或 handle。
    /// 改寫器必須同時替換型別與建構，不能先建立真 EventLog 再只改寫 WriteEntry。
    /// </summary>
    public sealed class EventLog : IDisposable
    {
        /// <summary>接受舊建構參數但不查詢機器或保存其值；實例沒有需要釋放的資源。</summary>
        public EventLog() { }

        /// <summary>接受舊建構參數但不查詢機器或保存其值；實例沒有需要釋放的資源。</summary>
        public EventLog(string logName) { }

        /// <summary>接受舊建構參數但不查詢機器或保存其值；實例沒有需要釋放的資源。</summary>
        public EventLog(string logName, string machineName) { }

        /// <summary>接受舊建構參數但不查詢機器或保存其值；實例沒有需要釋放的資源。</summary>
        public EventLog(string logName, string machineName, string source) { }

        /// <summary>不建立系統事件記錄；委派靜態有界入口，附加資料不被保留。</summary>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "保留舊 EventLog 實例方法的改寫契約；替身刻意不保存機器或來源狀態。設為靜態會破壞既有呼叫語法。")]
        public void WriteEntry(string message) => ScratchDiagnostics.WriteEntry(string.Empty, message);

        /// <summary>不建立系統事件記錄；委派靜態有界入口，附加資料不被保留。</summary>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "保留舊 EventLog 實例方法的改寫契約；替身刻意不保存機器或來源狀態。設為靜態會破壞既有呼叫語法。")]
        public void WriteEntry(string message, EventLogEntryType type) => ScratchDiagnostics.WriteEntry(string.Empty, message, type);

        /// <summary>不建立系統事件記錄；委派靜態有界入口，附加資料不被保留。</summary>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "保留舊 EventLog 實例方法的改寫契約；替身刻意不保存機器或來源狀態。設為靜態會破壞既有呼叫語法。")]
        public void WriteEntry(string message, EventLogEntryType type, int eventID) => ScratchDiagnostics.WriteEntry(string.Empty, message, type, eventID);

        /// <summary>不建立系統事件記錄；委派靜態有界入口，附加資料不被保留。</summary>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "保留舊 EventLog 實例方法的改寫契約；替身刻意不保存機器或來源狀態。設為靜態會破壞既有呼叫語法。")]
        public void WriteEntry(string message, EventLogEntryType type, int eventID, short category) => ScratchDiagnostics.WriteEntry(string.Empty, message, type, eventID, category);

        /// <summary>不建立系統事件記錄；委派靜態有界入口，附加資料不被保留。</summary>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "保留舊 EventLog 實例方法的改寫契約；替身刻意不保存機器或來源狀態。設為靜態會破壞既有呼叫語法。")]
        public void WriteEntry(string message, EventLogEntryType type, int eventID, short category, byte[]? rawData) => ScratchDiagnostics.WriteEntry(string.Empty, message, type, eventID, category, rawData);

        /// <summary>本替身沒有長駐資源；Close 不影響目前 capture 或其他實例，後續寫入仍走同一入口。</summary>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "保留舊 EventLog 的實例關閉語法，替身沒有長駐資源。")]
        public void Close() { }

        /// <summary>與框架常用 using 相容；沒有訂閱、handle 或保留參數，清理不接觸其他案例。</summary>
        public void Dispose() { }
    }
}
