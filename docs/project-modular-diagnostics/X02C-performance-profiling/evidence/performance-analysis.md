# X02C Performance Analysis

## Confirmed Performance Issues

No confirmed performance issue is raised in this diagnostic round.

## Rejected Candidate: PerformanceMonitor unbounded metric samples

Evidence checked:

- Startup.cs:284 registers IPerformanceMonitor as a DEBUG singleton.
- PerformanceMonitoringMiddleware.cs:61-62 records request duration and path-category metrics on successful non-static requests.
- PerformanceMonitoringMiddleware.cs:92 records failed request duration.
- PerformanceMonitor.cs:59 stores metrics in Dictionary<string, List<double>>.
- PerformanceMonitor.cs:83 appends a metric sample.
- PerformanceMonitor.cs:85-89 caps each metric list to the most recent 1000 records by removing the oldest sample.

Decision:

- Rejected as an unbounded-memory issue because a per-metric cap exists.
- A future optimization could consider replacing RemoveAt(0) with a ring buffer, but with a 1000-item cap this is not a confirmed diagnostic issue without runtime evidence.

## Other Performance Notes

- PerfProfilingMiddleware is gated by ProfilingSwitch.Enabled and skips static assets.
- RequestProfiler uses Stopwatch.GetTimestamp() for sub-millisecond CRM/phase accounting.
- parse-perf-log.ps1 reads the full log with Get-Content; acceptable as an offline diagnostic parser.
