#if DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

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

        public void SetActionElapsed(long ms) => _actionMs = ms;

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
                        + "(未歸因:可能 m_OrganizationService proxy 路徑或非 CRM 運算)");
            return lines;
        }
    }
}
#endif
