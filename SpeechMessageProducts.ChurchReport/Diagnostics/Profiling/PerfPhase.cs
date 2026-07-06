// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Diagnostics/Profiling/PerfPhase.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 PerfPhase 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class PerfPhase、struct PerfScope
// 主要成員：Measure、Dispose
// 引用命名空間：System、Microsoft.AspNetCore.Http
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.AspNetCore.Http;

namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// Call-site wrapper for controller phase timing.
    /// Release: Measure returns default(PerfScope) (readonly struct, empty Dispose elided by the JIT),
    ///          and there is NO Func overload, so call sites allocate no closure -> truly zero overhead.
    /// Debug  : delegates to RequestProfiler.MeasurePhase (still gated by ProfilingSwitch).
    /// Usage is always a using block: using (PerfPhase.Measure(HttpContext, "Name")) { ... }
    /// </summary>
    public static class PerfPhase
    {
        public static PerfScope Measure(HttpContext context, string name)
        {
#if DEBUG
            return new PerfScope(RequestProfiler.MeasurePhase(context, name));
#else
            return default;
#endif
        }
    }

    /// <summary>Phase-timing scope. Release: empty-shell struct (zero allocation, Dispose elided).</summary>
    public readonly struct PerfScope : IDisposable
    {
#if DEBUG
        private readonly IDisposable _inner;
        internal PerfScope(IDisposable inner) { _inner = inner; }
        public void Dispose() => _inner?.Dispose();
#else
        public void Dispose() { }
#endif
    }
}
