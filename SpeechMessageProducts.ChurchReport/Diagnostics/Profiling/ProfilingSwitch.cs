/*
 * 檔案整體說明
 *
 * 目的與定位：剖析總開關。僅 Debug 編譯存在；再加 runtime 旗標（預設關）雙重保險， 即使正式機誤以 Debug 部署，未開統一 DiagnosticsTrace 設定就不剖析； Release 編譯則由
 * Program 建立 disabled options，部署設定無法繞過。
 *
 * 主要功能與流程：
 * 本檔宣告 ProfilingSwitch。
 * - 取得或設定目前程序是否允許建立 request/CRM 效能剖析事件。Startup 只會從已通過 Debug 編譯防線的 DiagnosticTraceOptions.Enabled 指派此值；volatile
 * 確保各 request 執行緒立即看見程序級開關，但此欄位不得承載任何 request、使用者或租戶狀態。
 * 資料／選項欄位包含 ProfilingSwitch、Enabled，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/PerfPhase.cs、
 * SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/RequestProfiler.cs、
 * SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/StartupProfiler.cs、
 * SpeechMessageProducts.ChurchReport/Filters/PerfTimingActionFilter.cs、
 * SpeechMessageProducts.ChurchReport/Middleware/PerfProfilingMiddleware.cs 追查目前工作樹中的合作／使用位置。
 */
#if DEBUG
namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// 剖析總開關。僅 Debug 編譯存在；再加 runtime 旗標（預設關）雙重保險，
    /// 即使正式機誤以 Debug 部署，未開統一 DiagnosticsTrace 設定就不剖析；
    /// Release 編譯則由 Program 建立 disabled options，部署設定無法繞過。
    /// </summary>
    public static class ProfilingSwitch
    {
        /// <summary>
        /// 取得或設定目前程序是否允許建立 request/CRM 效能剖析事件。Startup 只會從已通過
        /// Debug 編譯防線的 <c>DiagnosticTraceOptions.Enabled</c> 指派此值；volatile 確保各 request
        /// 執行緒立即看見程序級開關，但此欄位不得承載任何 request、使用者或租戶狀態。
        /// </summary>
        public static volatile bool Enabled = false;
    }
}
#endif
