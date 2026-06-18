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
        public static IDisposable Phase(string name) => new PhaseTimer(name);

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
