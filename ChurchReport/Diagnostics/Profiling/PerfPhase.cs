using System;
using Microsoft.AspNetCore.Http;

namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// Call-site wrapper for controller phase timing.
    /// Release: Measure returns default(PerfScope) (readonly struct, empty Dispose elided by the JIT),
    ///          and there is NO Func overload, so call sites allocate no closure -> truly zero overhead.
    /// Debug  : delegates to RequestProfiler.MeasurePhase (still gated by ProfilingSwitch).
    /// Usage is always a using block: using (PerfPhase.Measure(HttpContext, "Name")) { ... }
    /// </summary>
    public static class PerfPhase
    {
        public static PerfScope Measure(HttpContext context, string name)
        {
#if DEBUG
            return new PerfScope(RequestProfiler.MeasurePhase(context, name));
#else
            return default;
#endif
        }
    }

    /// <summary>Phase-timing scope. Release: empty-shell struct (zero allocation, Dispose elided).</summary>
    public readonly struct PerfScope : IDisposable
    {
#if DEBUG
        private readonly IDisposable _inner;
        internal PerfScope(IDisposable inner) { _inner = inner; }
        public void Dispose() => _inner?.Dispose();
#else
        public void Dispose() { }
#endif
    }
}
