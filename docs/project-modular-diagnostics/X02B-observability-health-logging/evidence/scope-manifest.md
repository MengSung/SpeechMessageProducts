# X02B Scope Manifest

Workspace: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`

Mode: `DIAGNOSIS_ONLY`

Nested agent count: 0

## Module Map Anchor

X02B owns Observability, Health and Logging:

- Owner scope: logger provider, session monitoring, diagnostics endpoint, health/operational signal.
- Excluded scope: request profiling, business KPI logic, legacy Trace internals except as dependency/consumer context.
- Adjacent owners: X01 and X04A.
- Independent validation requirement: logger output, health/diagnostic response, hosted service start/stop, and sensitive data masking.

## Primary Owner Files

- `SpeechMessageProducts.ChurchReport/Logging/FileLoggerProvider.cs`
- `SpeechMessageProducts.ChurchReport/Logging/TraceLoggerProvider.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`
- `SpeechMessageProducts.ChurchReport/Middleware/SessionMonitoringMiddleware.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs` lines 356-370, 687-702, 777, and 852 as composition and endpoint wiring context.
- `SpeechMessageProducts.ChurchReport/appsettings.json` and `appsettings.Production.json` as logging, trace, profiling, and health configuration context.

## Dependencies Read As Context

- `ChurchReport.Middleware.IdentityAuditMiddleware.GetTrackingSnapshot()` and `CleanupOldTracking(...)` are consumed by `DiagnosticsController`.
- `ChurchReport.Services.Monitoring.ISessionMonitorService` is consumed by `SessionMonitoringMiddleware`.
- `System.Diagnostics.Trace` is the output dependency for `TraceLoggerProvider`.
- `Microsoft.Extensions.Diagnostics.HealthChecks` is used through `services.AddHealthChecks()` and `app.UseHealthChecks("/health")`.

## Consumers

- ASP.NET Core host composition in `SpeechMessageProducts.ChurchReport/Startup.cs`.
- Operational users of `/health` and DEBUG-only `/diagnostics/*` endpoints.
- Any future registration of `FileLoggerProvider` or `TraceLoggerProvider`.

## Scope Exclusions Applied

- Request profiling and performance timing middleware are treated as X02C, not X02B.
- Business KPI and payment debug logging are excluded except where they prove logging consumer behavior.
- `Trace/**` legacy projects are excluded except as historical context.

## Write Boundary

Allowed writes for this run:

- `docs/project-modular-diagnostics/X02B-observability-health-logging/**`
- `.ccg/dual-model-runs/x02b-*`

No product code, project files, configs, tests, generated files, `bin/obj`, caches, lockfiles, or ledger files were intentionally modified.
