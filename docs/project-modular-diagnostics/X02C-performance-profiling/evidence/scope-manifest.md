# X02C Scope Manifest

Leaf ID: X02C
Workspace: docs/project-modular-diagnostics/X02C-performance-profiling/
Mode: DIAGNOSIS_ONLY
Nested agent count: 0
Gate status: BLOCKED
Quarantine status: false
Map row: X02C Performance Profiling owns request/startup profiler, timing filter/middleware, threshold, perf parser/monitor; excludes cache correctness, logging provider, and business performance decisions; dependencies F02, F03A, X01.

## Primary Owner Files

- SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/RequestProfiler.cs
- SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/StartupProfiler.cs
- SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/ProfilingSwitch.cs
- SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/PerfThresholds.cs
- SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/PerfPhase.cs
- SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedOrganizationService.cs
- SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedToolUtilityProvider.cs
- SpeechMessageProducts.ChurchReport/Middleware/PerfProfilingMiddleware.cs
- SpeechMessageProducts.ChurchReport/Middleware/PerformanceMonitoringMiddleware.cs
- SpeechMessageProducts.ChurchReport/Filters/PerfTimingActionFilter.cs
- SpeechMessageProducts.ChurchReport/Services/Performance/PerformanceMonitor.cs
- SpeechMessageProducts.ChurchReport/Controllers/PerformanceController.cs
- SpeechMessageProducts.ChurchReport/Tools/parse-perf-log.ps1

## Dependencies Read Only

- F02 Dataverse connection foundation: CRM service interface/timing source context only.
- F03A CRM operations library: ToolUtility CRM calls as timing source only.
- X01 host composition: Startup registrations and middleware order only.
- X02B observability/session monitor: session statistics endpoint consumer context only.

## Explicit Exclusions

- Cache correctness and cache-specific monitor behavior.
- Logging provider internals.
- Business performance decisions or KPI thresholds.
- Product code optimization, package restore, build, test, code generation, formatting, migrations, bin/obj, caches, lockfiles, and ledger edits.
