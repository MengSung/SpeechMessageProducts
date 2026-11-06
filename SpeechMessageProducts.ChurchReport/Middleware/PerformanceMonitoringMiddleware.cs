/*
 * 檔案整體說明
 *
 * 目的與定位：效能監控中介軟體 用於追蹤每個請求的效能指標 ?? 僅在 DEBUG 編譯模式下啟用（Release 版本不會包含此程式碼）
 *
 * 主要功能與流程：
 * 本檔宣告 PerformanceMonitoringMiddleware。
 * 主要操作入口包含 InvokeAsync、GetPathCategory。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
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
