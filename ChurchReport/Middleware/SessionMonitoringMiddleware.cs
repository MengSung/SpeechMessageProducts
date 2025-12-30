#if DEBUG
using System.Threading.Tasks;
using ChurchReport.Services.Monitoring;
using Microsoft.AspNetCore.Http;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// Session 監控中介軟體
    /// ? Phase 8: 自動追蹤每個請求的 Session 活動
    /// ?? 僅在 DEBUG 編譯模式下啟用
    /// </summary>
    public class SessionMonitoringMiddleware
    {
        private readonly RequestDelegate _next;

        public SessionMonitoringMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ISessionMonitorService sessionMonitor)
        {
            // 確保 Session 已初始化
            if (context.Session != null && context.Session.IsAvailable)
            {
                // 記錄 Session 活動
                sessionMonitor.RecordSessionActivity(context.Session.Id);
            }

            await _next(context);
        }
    }
}
#endif
