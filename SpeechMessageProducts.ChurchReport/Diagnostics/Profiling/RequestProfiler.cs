/*
 * 檔案整體說明
 *
 * 目的與定位：單一請求計時容器。只放 HttpContext.Items，請求結束即回收。 只記數字 + entity 邏輯名 + 操作名。CRM 時間以 ticks 累計避免 &lt;1ms 捨入低報。
 *
 * 主要功能與流程：
 * 本檔宣告 RequestProfiler、PhaseScope、NoopDisposable。
 * - Trace.log 的 [Perf] crm.ms 外層耗時。 此值由 TimedOrganizationService 在 DI 接縫包住整個 GatewayOrganizationService 呼叫而得，所以除
 * CRM 操作本身外，也包含租約取得、 Gateway 代理與首次建立池內連線等成本。它必須與同一 request 的 CRM 次數一起解讀， 不能假定等同 JSONL 的內層時間。 JSONL
 * request.end.crmMs 則由 CrmOperationTrace 僅累加 CRM 操作本身。 兩者的差值正是 Gateway／連線層成本；這是刻意保留的兩個量測邊界，而非重複計數或漏計。
 * - 控制器 action 耗時扣除 CrmMs 後的剩餘時間。 由於 CrmMs 已包含 Gateway 與連線建立成本， gap 表示的只是該外層 CRM 路徑以外的 action 時間；它不能與 JSONL 的內層
 * crmMs 直接相減或互相比對。
 * 主要操作入口包含 SetActionElapsed、MeasurePhase、RecordPhase、RecordCrmCall、StopAndGetTotalMs、BuildSummaryLine、
 * BuildPhaseLines、BuildEscalationLines；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/PerfPhase.cs、
 * SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedOrganizationService.cs、
 * SpeechMessageProducts.ChurchReport/Filters/PerfTimingActionFilter.cs、
 * SpeechMessageProducts.ChurchReport/Middleware/PerfProfilingMiddleware.cs 追查目前工作樹中的合作／使用位置。
 */
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

        /// <summary>
        /// <c>Trace.log</c> 的 <c>[Perf] crm.ms</c> 外層耗時。
        /// <para>
        /// 此值由 <see cref="TimedOrganizationService"/> 在 DI 接縫包住整個
        /// <c>GatewayOrganizationService</c> 呼叫而得，所以除 CRM 操作本身外，也包含租約取得、
        /// Gateway 代理與首次建立池內連線等成本。它必須與同一 request 的 CRM 次數一起解讀，
        /// 不能假定等同 JSONL 的內層時間。
        /// </para>
        /// <para>
        /// JSONL <c>request.end.crmMs</c> 則由 <c>CrmOperationTrace</c> 僅累加 CRM 操作本身。
        /// 兩者的差值正是 Gateway／連線層成本；這是刻意保留的兩個量測邊界，而非重複計數或漏計。
        /// </para>
        /// </summary>
        private long CrmMs => (long)(_crmTicks * TicksToMs);

        /// <summary>
        /// 控制器 action 耗時扣除 <see cref="CrmMs"/> 後的剩餘時間。
        /// 由於 <see cref="CrmMs"/> 已包含 Gateway 與連線建立成本，<c>gap</c> 表示的只是該外層
        /// CRM 路徑以外的 action 時間；它不能與 JSONL 的內層 <c>crmMs</c> 直接相減或互相比對。
        /// </summary>
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
