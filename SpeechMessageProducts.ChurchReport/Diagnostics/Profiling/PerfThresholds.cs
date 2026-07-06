// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Diagnostics/Profiling/PerfThresholds.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 PerfThresholds 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class PerfThresholds
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
