using System;
using Microsoft.AspNetCore.Http;

namespace ChurchReport.Diagnostics.Profiling
{
    public static class PerfPhase
    {
        public static IDisposable Measure(HttpContext context, string name)
        {
#if DEBUG
            return RequestProfiler.MeasurePhase(context, name);
#else
            return NoopDisposable.Instance;
#endif
        }

        public static T Measure<T>(HttpContext context, string name, Func<T> action)
        {
            using (Measure(context, name))
            {
                return action();
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();

            private NoopDisposable()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
