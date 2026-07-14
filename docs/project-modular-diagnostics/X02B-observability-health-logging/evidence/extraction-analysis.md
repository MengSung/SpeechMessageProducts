# X02B Extraction And Acceleration Analysis

## Responsibility Shape

X02B is cohesive around observability surfaces:

- Logger providers: `FileLoggerProvider`, `TraceLoggerProvider`
- Diagnostic HTTP surface: `DiagnosticsController`
- Session monitoring signal: `SessionMonitoringMiddleware`
- Health signal composition: `Startup.cs` health checks and `/health`

The implementation is concentrated in the ChurchReport host. That means X02B is not a standalone package yet; its extraction boundary is currently conceptual and route/provider based rather than project based.

## Ranked Extraction / Acceleration Issues

### E1 - Runtime Validation Pending: X02B lacks explicit operational contract tests

The module map requires independent validation of logger output, health/diagnostic response, hosted service start/stop, and sensitive data masking. Static review found owner files and wiring, but no targeted X02B tests were observed for `DiagnosticsController`, `SessionMonitoringMiddleware`, `TraceLoggerProvider`, `FileLoggerProvider`, or `/health` behavior.

Evidence:

- module map validation requirement: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:537-538`
- owner files: `SpeechMessageProducts.ChurchReport/Logging/**`, `Controllers/DiagnosticsController.cs`, `Middleware/SessionMonitoringMiddleware.cs`
- health and middleware wiring: `SpeechMessageProducts.ChurchReport/Startup.cs:356-370`, `777`, `852`

Recommended acceleration: create future component tests around logger output redaction, health response accuracy, DEBUG diagnostics availability, Release diagnostics absence, and session monitoring behavior. Do not implement in this diagnostic pass.

### E2 - Runtime Validation Pending: diagnostics includes X02C-shaped performance endpoint

`DiagnosticsController.GetPerformanceInfo()` exposes memory/thread/runtime/GC counters. The X02B scope excludes request profiling and business KPI logic, but operational health signals are in scope. This endpoint is acceptable if treated as coarse operational signal; it should be kept out of request profiling ownership and validated as diagnostic-only.

Evidence:

- performance endpoint: `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:195-230`
- X02B exclusion: request profiling is excluded by module map row X02B.

Recommended acceleration: document endpoint ownership as X02B diagnostic response only; move any request timing/profiler concerns to X02C.

### E3 - No Action Required: legacy Trace internals can remain dependency context

`TraceLoggerProvider` bridges `ILogger` to `System.Diagnostics.Trace`, but this diagnostic did not target `Trace/**` legacy projects. The provider is X02B-owned because it is a logger provider in the active ChurchReport host; legacy Trace projects remain X02Q unless separately proven canonical.

Recommended acceleration: keep extraction notes at the provider boundary; avoid optimizing `Trace/**` from X02B.

## Suggested Future Extraction Boundary

If this area is extracted later, prefer a small host-facing contract:

- `IOperationalDiagnosticsEndpointPolicy` or equivalent policy for diagnostic response exposure.
- `ILogRedactionPolicy` for masking before any custom provider writes.
- Health check option binding owned by X04A, consumed by X02B health checks.
- Component tests in the ChurchReport test layer before moving files.
