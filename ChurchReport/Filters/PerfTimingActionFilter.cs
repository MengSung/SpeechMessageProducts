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
