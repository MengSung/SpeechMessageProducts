/*
 * 檔案整體說明
 *
 * 目的與定位：啟動階段計時。用法：using (StartupProfiler.Phase("ConfigureServices")) { ... } 無共用可變狀態（每個 Phase 只持有自己的 
 * label+Stopwatch）。啟動時無任何使用者/請求，零 Session-Leakage 風險。
 *
 * 主要功能與流程：
 * 本檔宣告 StartupProfiler、NoOpTimer、PhaseTimer。
 * 主要操作入口包含 Phase、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
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
