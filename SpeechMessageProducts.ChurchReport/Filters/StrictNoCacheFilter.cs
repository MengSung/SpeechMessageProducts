// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Filters/StrictNoCacheFilter.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 StrictNoCacheFilter 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class StrictNoCacheFilter
// 主要成員：OnActionExecuting、OnActionExecuted
// 引用命名空間：Microsoft.AspNetCore.Mvc.Filters、System、System.Diagnostics
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Diagnostics;

namespace ChurchReport.Filters
{
    /// <summary>
    /// 全域無快取過濾器 (Session Bleeding 防護 - 第三層)
    ///
    /// 設計原則:
    /// - Single Responsibility Principle (SRP): 專注於設定 HTTP 快取標頭
    /// - Open/Closed Principle: 透過 IActionFilter 介面擴展，不需修改現有代碼
    /// - Dependency Inversion Principle: 依賴抽象 IActionFilter，不依賴具體實現
    ///
    /// 作用:
    /// 在每個 Action 執行完成後，強制設定最嚴格的 HTTP 快取標頭，
    /// 確保回應不會被任何中間層代理伺服器或瀏覽器快取。
    ///
    /// 這是防止 Session Bleeding（會話串連）問題的第三層防護。
    ///
    /// 防護架構:
    /// 第一層: 全站無快取中介軟體 (Middleware)
    /// 第二層: ResponseCacheAttribute (MVC Framework)
    /// 第三層: StrictNoCacheFilter (Action Filter) ← 此檔案
    ///
    /// 使用方式:
    /// 在 Startup.cs 的 ConfigureServices 中全域註冊:
    /// <code>
    /// services.AddMvc(options =>
    /// {
    ///     options.Filters.Add&lt;StrictNoCacheFilter&gt;();
    /// });
    /// </code>
    ///
    /// 或在特定 Controller/Action 上使用:
    /// <code>
    /// [ServiceFilter(typeof(StrictNoCacheFilter))]
    /// public class MyController : Controller { }
    /// </code>
    /// </summary>
    public class StrictNoCacheFilter : IActionFilter
    {
        /// <summary>
        /// Action 執行前的處理（此過濾器不需要在執行前處理）
        /// </summary>
        /// <param name="context">Action 執行前的上下文</param>
        public void OnActionExecuting(ActionExecutingContext context)
        {
            // 不需要在 Action 執行前做任何處理
            // 保持此方法為空，符合介面要求
        }

        /// <summary>
        /// Action 執行後的處理：設定最嚴格的快取控制標頭
        ///
        /// 設定的 HTTP Headers:
        /// 1. Cache-Control: no-store, no-cache, must-revalidate, max-age=0
        ///    - no-store: 完全禁止快取（最嚴格）
        ///    - no-cache: 每次都要重新驗證
        ///    - must-revalidate: 過期後必須重新驗證
        ///    - max-age=0: 立即過期
        ///
        /// 2. Pragma: no-cache
        ///    - HTTP/1.0 的向後相容標頭
        ///
        /// 3. Expires: -1
        ///    - 表示內容已過期（HTTP/1.0 相容）
        ///
        /// 為什麼需要三個標頭?
        /// - Cache-Control: 現代瀏覽器和代理伺服器使用
        /// - Pragma: 舊版 HTTP/1.0 客戶端使用
        /// - Expires: 向後相容，確保所有客戶端都理解
        /// </summary>
        /// <param name="context">Action 執行後的上下文</param>
        public void OnActionExecuted(ActionExecutedContext context)
        {
            var response = context.HttpContext.Response;

            // 設定最嚴格的快取策略：禁止瀏覽器與任何中間代理伺服器存儲內容
            response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Expires"] = "-1";

#if DEBUG
            // DEBUG 模式下記錄詳細資訊，便於除錯
            var path = context.HttpContext.Request.Path;
            var method = context.HttpContext.Request.Method;
            var traceId = context.HttpContext.TraceIdentifier;

            Debug.WriteLine($"[StrictNoCacheFilter] Action 完成");
            Debug.WriteLine($"  - Path: {path}");
            Debug.WriteLine($"  - Method: {method}");
            Debug.WriteLine($"  - TraceId: {traceId}");
            Debug.WriteLine($"  - Headers 已設定:");
            Debug.WriteLine($"    * Cache-Control: {response.Headers["Cache-Control"]}");
            Debug.WriteLine($"    * Pragma: {response.Headers["Pragma"]}");
            Debug.WriteLine($"    * Expires: {response.Headers["Expires"]}");
#endif
        }
    }
}
