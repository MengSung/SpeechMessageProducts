// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Diagnostics/Profiling/StartupProfiler.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 StartupProfiler 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class StartupProfiler、class NoOpTimer、class PhaseTimer
// 主要成員：Phase、Dispose
// 引用命名空間：System、System.Diagnostics
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
using System;
using System.Diagnostics;

namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// 啟動階段計時。用法：using (StartupProfiler.Phase("ConfigureServices")) { ... }
    /// 無共用可變狀態（每個 Phase 只持有自己的 label+Stopwatch）。啟動時無任何使用者/請求，零 Session-Leakage 風險。
    /// </summary>
    public static class StartupProfiler
    {
        public static IDisposable Phase(string name) =>
            ProfilingSwitch.Enabled ? new PhaseTimer(name) : NoOpTimer.Instance;

        private sealed class NoOpTimer : IDisposable
        {
            public static readonly NoOpTimer Instance = new NoOpTimer();
            private NoOpTimer() { }
            public void Dispose() { }
        }

        private sealed class PhaseTimer : IDisposable
        {
            private readonly string _name;
            private readonly Stopwatch _sw;
            public PhaseTimer(string name) { _name = name; _sw = Stopwatch.StartNew(); }
            public void Dispose()
            {
                _sw.Stop();
                Debug.WriteLine($"[Perf-Startup] phase={_name} ms={_sw.ElapsedMilliseconds}");
            }
        }
    }
}
#endif
