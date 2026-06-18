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
