# X01 Security Analysis

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Findings

### S1 - Confirm middleware/auth/session order with runtime route smoke

Severity: Medium
Status: hypothesis, runtime validation required

Evidence:

- `Startup.cs` registers `UseSession` before `UseAuthentication`.
- Session validation and session monitoring middleware run after `UseSession`.
- `MiniAppDetectionMiddleware` and `IdentityAuditMiddleware` run after authentication.
- Routes are registered through a large legacy `UseMvc` table.

Risk:

- Host-level authentication/session behavior depends on middleware order and route compatibility.
- X01 owns the ordering contract even though B01 owns authentication/session business behavior.
- Without a host smoke or route snapshot, a future route or middleware change can silently bypass the expected order.

Recommended diagnostic action:

- Add a host smoke that starts the app with test configuration and verifies representative unauthenticated/authenticated paths traverse expected middleware.
- Add a route snapshot or endpoint inventory for the legacy `UseMvc` table before route extraction or optimization.

### S2 - Debug-only trace listener and GC monitor require shutdown/disposal validation

Severity: Low
Status: hypothesis, runtime validation required

Evidence:

- `Program.cs` initializes debug trace listener before service registration and again after `builder.Build`.
- `InitializeTraceListener` guards duplicate listener registration with a static lock and existing listener check.
- `ApplicationStopping` removes, flushes, and disposes the listener.
- Debug GC monitoring starts an unbounded `Task.Run` loop.

Risk:

- Release builds are guarded by `#if DEBUG`, reducing production exposure.
- Debug host runs may retain background work until process exit. That is acceptable for diagnostics only if it does not block shutdown or keep file handles open.

Recommended diagnostic action:

- Runtime validation should confirm debug startup/shutdown closes `Logs/Trace.log` cleanly.
- If future work touches this area, prefer an `IHostedService` with cancellation over an untracked background loop.

### S3 - Web cache deception guard has a scoped test candidate

Severity: Info
Status: supported by existing test file

Evidence:

- `WebCacheDeceptionMiddleware` is registered before `UseStaticFiles`.
- `StaticRequestPathHelperTests` covers legitimate static assets and dynamic routes that mimic static extensions.

Risk:

- This is a positive security-control finding. The test is X01-owned and should be part of the eventual host/security baseline.

Recommended diagnostic action:

- Include `StaticRequestPathHelperTests` in the eventual X01 validation command once the test gate is defined.
