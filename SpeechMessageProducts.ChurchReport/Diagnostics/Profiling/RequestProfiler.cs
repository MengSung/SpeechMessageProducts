// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Diagnostics/Profiling/RequestProfiler.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 RequestProfiler 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class RequestProfiler、class PhaseScope、class NoopDisposable
// 主要成員：SetActionElapsed、MeasurePhase、RecordPhase、RecordCrmCall、StopAndGetTotalMs、BuildSummaryLine、BuildPhaseLines、BuildEscalationLines、Dispose
// 引用命名空間：System.Collections.Generic、System.Diagnostics、System.Text、System、Microsoft.AspNetCore.Http
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System;
using Microsoft.AspNetCore.Http;

namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// 單一請求計時容器。只放 HttpContext.Items，請求結束即回收。
    /// 只記數字 + entity 邏輯名 + 操作名。CRM 時間以 ticks 累計避免 &lt;1ms 捨入低報。
    /// </summary>
    public sealed class RequestProfiler
    {
        public const string ItemsKey = "__PerfProfiler";
        public const string RouteTemplateItemsKey = "__PerfRouteTemplate";
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly object _lock = new object();
        private long _actionMs;
        private int _crmCount;
        private long _crmTicks;                       // 累計 ticks（高精度）
        private string _slowestEntity;
        private string _slowestOp;
        private long _slowestMs;
        private readonly Dictionary<string, (int count, long ms)> _crmByOp =
            new Dictionary<string, (int, long)>();
        private readonly List<(string entity, string op, long ms)> _slowCalls =
            new List<(string, string, long)>();
        private readonly List<(string name, long ms)> _phases =
            new List<(string, long)>();
        private readonly Dictionary<string, (int count, long ms)> _phaseByName =
            new Dictionary<string, (int, long)>();

        public void SetActionElapsed(long ms) => _actionMs = ms;

        public static IDisposable MeasurePhase(HttpContext context, string name)
        {
            if (!ProfilingSwitch.Enabled
                || context?.Items == null
                || !context.Items.TryGetValue(ItemsKey, out var profiler)
                || profiler is not RequestProfiler requestProfiler)
            {
                return NoopDisposable.Instance;
            }

            return new PhaseScope(requestProfiler, name);
        }

        public void RecordPhase(string name, long elapsedTicks)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "?";
            }

            var ms = (long)(elapsedTicks * TicksToMs);
            lock (_lock)
            {
                _phaseByName[name] = _phaseByName.TryGetValue(name, out var current)
                    ? (current.count + 1, current.ms + ms)
                    : (1, ms);

                if (ms >= PerfThresholds.PhaseMs)
                {
                    _phases.Add((name, ms));
                }
            }
        }

        /// <param name="elapsedTicks">由裝飾器以 Stopwatch.GetTimestamp() 差值傳入。</param>
        public void RecordCrmCall(string entity, string op, long elapsedTicks)
        {
            entity ??= "?";
            op ??= "?";
            long ms = (long)(elapsedTicks * TicksToMs);
            lock (_lock)
            {
                _crmCount++;
                _crmTicks += elapsedTicks;
                if (ms > _slowestMs) { _slowestMs = ms; _slowestEntity = entity; _slowestOp = op; }
                var key = entity + "." + op;
                _crmByOp[key] = _crmByOp.TryGetValue(key, out var cur)
                    ? (cur.count + 1, cur.ms + ms) : (1, ms);
                if (ms > PerfThresholds.SlowSingleCallMs) _slowCalls.Add((entity, op, ms));
            }
        }

        public long StopAndGetTotalMs() { _total.Stop(); return _total.ElapsedMilliseconds; }

        private long CrmMs => (long)(_crmTicks * TicksToMs);
        private long Gap => (_actionMs - CrmMs) > 0 ? (_actionMs - CrmMs) : 0;

        public string BuildSummaryLine(string path, long totalMs)
        {
            var slowest = _slowestMs > 0 ? $" slowest={_slowestEntity}.{_slowestOp}:{_slowestMs}ms" : "";
            return $"[Perf] path={path} total={totalMs}ms action={_actionMs}ms "
                 + $"crm{{n={_crmCount},ms={CrmMs}}} gap={Gap}ms{slowest}";
        }

        public IEnumerable<string> BuildPhaseLines(string path)
        {
            var lines = new List<string>();
            lock (_lock)
            {
                foreach (var phase in _phaseByName)
                {
                    if (phase.Value.count <= 1 || phase.Value.ms < PerfThresholds.PhaseMs)
                    {
                        continue;
                    }

                    var avg = phase.Value.ms / phase.Value.count;
                    lines.Add($"[Perf-Phase] path={path} phase={phase.Key} count={phase.Value.count} ms={phase.Value.ms} avg={avg}");
                }

                foreach (var phase in _phases)
                {
                    lines.Add($"[Perf-Phase] path={path} phase={phase.name} ms={phase.ms}");
                }
            }

            return lines;
        }

        public IEnumerable<string> BuildEscalationLines(string path)
        {
            var lines = new List<string>();
            if (_crmCount > PerfThresholds.NPlusOneCrmCount)
            {
                var sb = new StringBuilder();
                lock (_lock)
                    foreach (var kv in _crmByOp)
                        sb.Append($"{kv.Key} ×{kv.Value.count} (Σ{kv.Value.ms}ms), ");
                lines.Add($"[Perf-N+1] path={path} crm.n={_crmCount} 詳列: "
                        + sb.ToString().TrimEnd(',', ' '));
            }
            lock (_lock)
                foreach (var c in _slowCalls)
                    lines.Add($"[Perf-Slow] path={path} {c.entity}.{c.op} {c.ms}ms");
            if (Gap > PerfThresholds.GapMs)
                lines.Add($"[Perf-Gap] path={path} action={_actionMs}ms crm.ms={CrmMs} gap={Gap}ms "
                        + "(未歸因:可能 gateway 代理路徑或非 CRM 運算)");
            return lines;
        }

        private sealed class PhaseScope : IDisposable
        {
            private readonly RequestProfiler _profiler;
            private readonly string _name;
            private readonly long _startTimestamp;
            private bool _disposed;

            public PhaseScope(RequestProfiler profiler, string name)
            {
                _profiler = profiler;
                _name = name;
                _startTimestamp = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _profiler.RecordPhase(_name, Stopwatch.GetTimestamp() - _startTimestamp);
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();

            private NoopDisposable()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
#endif
