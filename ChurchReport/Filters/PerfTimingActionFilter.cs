// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Filters/PerfTimingActionFilter.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 PerfTimingActionFilter 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class PerfTimingActionFilter
// 主要成員：OnActionExecuting、OnActionExecuted、BuildRouteTemplate
// 引用命名空間：System、System.Collections.Generic、System.Diagnostics、ChurchReport.Diagnostics.Profiling、Microsoft.AspNetCore.Mvc.Filters
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using ChurchReport.Diagnostics.Profiling;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChurchReport.Filters
{
    /// <summary>量每支 controller action 執行時間，寫入當前請求 profiler（僅 Debug）。</summary>
    public sealed class PerfTimingActionFilter : IActionFilter
    {
        private const string Key = "__PerfActionStopwatch";

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!ProfilingSwitch.Enabled)
            {
                return;
            }

            context.HttpContext.Items[RequestProfiler.RouteTemplateItemsKey] = BuildRouteTemplate(context);
            context.HttpContext.Items[Key] = Stopwatch.StartNew();
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (!ProfilingSwitch.Enabled)
            {
                return;
            }

            if (context.HttpContext.Items.TryGetValue(Key, out var o) && o is Stopwatch sw)
            {
                sw.Stop();
                if (context.HttpContext.Items.TryGetValue(RequestProfiler.ItemsKey, out var p)
                    && p is RequestProfiler profiler)
                {
                    profiler.SetActionElapsed(sw.ElapsedMilliseconds);
                }
            }
        }

        private static string BuildRouteTemplate(ActionExecutingContext context)
        {
            var attributeTemplate = context.ActionDescriptor.AttributeRouteInfo?.Template;
            if (!string.IsNullOrWhiteSpace(attributeTemplate))
            {
                return "/" + attributeTemplate.TrimStart('/');
            }

            var segments = new List<string>();
            if (context.RouteData.Values.TryGetValue("controller", out var controller) && controller != null)
            {
                segments.Add(controller.ToString());
            }

            if (context.RouteData.Values.TryGetValue("action", out var action) && action != null)
            {
                segments.Add(action.ToString());
            }

            foreach (var routeValue in context.RouteData.Values)
            {
                if (routeValue.Value == null
                    || string.Equals(routeValue.Key, "controller", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(routeValue.Key, "action", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                segments.Add("{" + routeValue.Key + "}");
            }

            return segments.Count > 0
                ? "/" + string.Join("/", segments)
                : context.HttpContext.Request?.Path.Value ?? "?";
        }
    }
}
#endif
