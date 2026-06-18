#if DEBUG
using System.Diagnostics;
using System.Threading.Tasks;
using ChurchReport.Diagnostics.Profiling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ChurchReport.Middleware
{
    /// <summary>建立每請求 profiler 並於結束輸出 [Perf]（僅 Debug，且需 ProfilingSwitch 開啟）。</summary>
    public sealed class PerfProfilingMiddleware
    {
        private readonly RequestDelegate _next;
        public PerfProfilingMiddleware(RequestDelegate next) => _next = next;

        public async Task Invoke(HttpContext context)
        {
            if (!ProfilingSwitch.Enabled) { await _next(context); return; } // runtime 開關（預設關）

            var profiler = new RequestProfiler();
            context.Items[RequestProfiler.ItemsKey] = profiler;
            try
            {
                await _next(context);
            }
            finally
            {
                var totalMs = profiler.StopAndGetTotalMs();
                var path = GetRouteTemplate(context); // 樣板，不含實際 ID
                Debug.WriteLine(profiler.BuildSummaryLine(path, totalMs));
                foreach (var line in profiler.BuildEscalationLines(path))
                    Debug.WriteLine(line);
            }
        }

        // finally 在 _next 之後執行，此時路由已跑完，GetEndpoint() 可取得對應端點的路由樣板。
        private static string GetRouteTemplate(HttpContext ctx)
        {
            if (ctx.GetEndpoint() is RouteEndpoint re && !string.IsNullOrEmpty(re.RoutePattern?.RawText))
                return "/" + re.RoutePattern.RawText.TrimStart('/');
            return ctx.Request?.Path.Value ?? "?";
        }
    }
}
#endif
