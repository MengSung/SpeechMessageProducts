// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Middleware/PerfProfilingMiddleware.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 PerfProfilingMiddleware 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class PerfProfilingMiddleware
// 主要成員：Invoke、GetRouteTemplate、SanitizePath
// 引用命名空間：System.Diagnostics、System.Linq、System.Threading.Tasks、ChurchReport.Diagnostics.Profiling、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Routing
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
using System.Diagnostics;
using System.Linq;
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
            if (!ProfilingSwitch.Enabled || StaticRequestPathHelper.IsStaticAssetPath(context.Request.Path))
            {
                await _next(context);
                return;
            } // runtime 開關（預設關）

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
                foreach (var line in profiler.BuildPhaseLines(path))
                    Debug.WriteLine(line);
                foreach (var line in profiler.BuildEscalationLines(path))
                    Debug.WriteLine(line);
            }
        }

        // finally 在 _next 之後執行，此時路由已跑完，GetEndpoint() 可取得對應端點的路由樣板。
        private static string GetRouteTemplate(HttpContext ctx)
        {
            if (ctx.Items.TryGetValue(RequestProfiler.RouteTemplateItemsKey, out var routeTemplate)
                && routeTemplate is string route
                && !string.IsNullOrWhiteSpace(route))
            {
                return route;
            }

            if (ctx.GetEndpoint() is RouteEndpoint re && !string.IsNullOrEmpty(re.RoutePattern?.RawText))
                return "/" + re.RoutePattern.RawText.TrimStart('/');

            return SanitizePath(ctx.Request?.Path.Value ?? "?");
        }

        private static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "?")
            {
                return "?";
            }

            var segments = path.Split('/')
                .Select(segment =>
                {
                    if (System.Guid.TryParse(segment, out _)
                        || (segment.Length > 0 && segment.All(char.IsDigit)))
                    {
                        return "{id}";
                    }

                    return segment;
                });

            return string.Join("/", segments);
        }
    }
}
#endif
