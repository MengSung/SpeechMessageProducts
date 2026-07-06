// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Middleware/PerformanceMonitoringMiddleware.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 PerformanceMonitoringMiddleware 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class PerformanceMonitoringMiddleware
// 主要成員：InvokeAsync、GetPathCategory
// 引用命名空間：System.Diagnostics、System.Threading.Tasks、Microsoft.AspNetCore.Http、Microsoft.Extensions.Logging
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// 效能監控中介軟體
    /// 用於追蹤每個請求的效能指標
    /// ?? 僅在 DEBUG 編譯模式下啟用（Release 版本不會包含此程式碼）
    /// </summary>
    public class PerformanceMonitoringMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PerformanceMonitoringMiddleware> _logger;
        private readonly Services.Performance.IPerformanceMonitor _performanceMonitor;

        public PerformanceMonitoringMiddleware(
            RequestDelegate next,
            ILogger<PerformanceMonitoringMiddleware> logger,
            Services.Performance.IPerformanceMonitor performanceMonitor)
        {
            _next = next;
            _logger = logger;
            _performanceMonitor = performanceMonitor;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "/";
            if (StaticRequestPathHelper.IsStaticAssetPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context);

                stopwatch.Stop();

                // 記錄請求時間
                _performanceMonitor.RecordMetric("RequestDuration", stopwatch.ElapsedMilliseconds);
                _performanceMonitor.RecordMetric($"Request_{GetPathCategory(path)}", stopwatch.ElapsedMilliseconds);

                // 記錄成功請求
                if (_performanceMonitor is Services.Performance.PerformanceMonitor monitor)
                {
                    monitor.IncrementRequests();
                }

                // 記錄慢請求
                if (stopwatch.ElapsedMilliseconds > 1000)
                {
                    _logger.LogWarning(
                        "[慢請求] {Method} {Path} - {Duration}ms - Status: {Status}",
                        context.Request.Method,
                        path,
                        stopwatch.ElapsedMilliseconds,
                        context.Response.StatusCode);
                }
            }
            catch
            {
                stopwatch.Stop();

                // 記錄失敗請求
                if (_performanceMonitor is Services.Performance.PerformanceMonitor monitor)
                {
                    monitor.IncrementRequests();
                    monitor.IncrementFailedRequests();
                }

                _performanceMonitor.RecordMetric("FailedRequestDuration", stopwatch.ElapsedMilliseconds);

                throw;
            }
        }

        /// <summary>
        /// 將路徑分類，避免產生太多指標
        /// </summary>
        private static string GetPathCategory(string path)
        {
            if (path.StartsWith("/SmallGroup")) return "SmallGroup";
            if (path.StartsWith("/api")) return "API";
            if (path.StartsWith("/Authentication")) return "Auth";
            if (path.StartsWith("/Dedication")) return "Dedication";
            if (path.StartsWith("/Personal")) return "Personal";
            if (path.Contains(".")) return "Static";
            return "Other";
        }
    }
}
#endif
