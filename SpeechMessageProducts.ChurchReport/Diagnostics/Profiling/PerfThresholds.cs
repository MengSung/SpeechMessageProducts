/*
 * 檔案整體說明
 *
 * 目的與定位：效能剖析門檻（僅 Debug）。集中管理，方便調整。
 *
 * 主要功能與流程：
 * 本檔宣告 PerfThresholds。
 * - 同一請求 CRM 呼叫次數超過此值 → 視為疑似 N+1，加印明細。
 * - 單次 CRM 呼叫超過此毫秒 → 視為慢呼叫，加印明細。
 * - action − 已計時 CRM 超過此毫秒 → 視為「未歸因時間」過大（盲區/CPU），加印 [Perf-Gap]。
 * - named phase 超過此毫秒才輸出，避免 log 被微小階段淹沒。
 * 資料／選項欄位包含 PerfThresholds、NPlusOneCrmCount、SlowSingleCallMs、GapMs、PhaseMs，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/RequestProfiler.cs 追查目前工作樹中的合作／使用位置。
 */
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
