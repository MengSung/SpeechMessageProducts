#if DEBUG
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
            context.HttpContext.Items[Key] = Stopwatch.StartNew();
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
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
    }
}
#endif
