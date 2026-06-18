#if DEBUG
namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>效能剖析門檻（僅 Debug）。集中管理，方便調整。</summary>
    internal static class PerfThresholds
    {
        /// <summary>同一請求 CRM 呼叫次數超過此值 → 視為疑似 N+1，加印明細。</summary>
        public const int NPlusOneCrmCount = 10;

        /// <summary>單次 CRM 呼叫超過此毫秒 → 視為慢呼叫，加印明細。</summary>
        public const long SlowSingleCallMs = 100;

        /// <summary>action − 已計時 CRM 超過此毫秒 → 視為「未歸因時間」過大（盲區/CPU），加印 [Perf-Gap]。</summary>
        public const long GapMs = 150;

        /// <summary>named phase 超過此毫秒才輸出，避免 log 被微小階段淹沒。</summary>
        public const long PhaseMs = 100;
    }
}
#endif
