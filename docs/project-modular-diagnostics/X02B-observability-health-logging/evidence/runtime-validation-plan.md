# X02B Runtime Validation Plan

No restore, build, test, package restore, code generation, formatting, migrations, or product writes are allowed during this diagnostic pass. This plan is for future execution after approval.

## Validation Goals

- Prove `/health` returns expected status and reflects intended memory threshold behavior.
- Prove `/diagnostics/*` routes are absent from Release builds and only available under DEBUG with authorization.
- Prove logger providers do not emit tokens, cookies, credentials, session IDs, or PII when registered.
- Prove session monitoring records expected DEBUG-only activity without affecting production request paths.

## Proposed Checks

1. Logger output redaction:
   - Register each active logger provider in a controlled test host.
   - Emit structured log state containing representative sensitive fields: token, cookie, password, session ID, user ID, account name, phone, and email.
   - Assert output either masks values or the provider is not active in production.

2. Health response:
   - Start the ChurchReport host with production-like configuration.
   - Request `GET /health`.
   - Assert the status code and body match the intended health contract.
   - Vary memory threshold configuration if the health check is expected to be config-driven.

3. Diagnostic response:
   - In DEBUG host, authenticate and request `/diagnostics`, `/diagnostics/session`, `/diagnostics/identity-audit`, and `/diagnostics/cache-headers`.
   - Assert route access requires authorization.
   - Assert returned fields are acceptable for DEBUG-only diagnostics or are masked.
   - In Release host, assert `/diagnostics/*` routes are unavailable.

4. Session monitoring:
   - In DEBUG host, send session-backed requests after `UseSession`.
   - Assert `ISessionMonitorService.RecordSessionActivity(...)` receives the session ID exactly once per eligible request.
   - In Release host, assert `SessionMonitoringMiddleware` is not wired.

## Runtime Preconditions

- Use isolated test secrets and synthetic identities only.
- Capture logs to a temp directory outside production paths.
- Do not run against production Dataverse, payment, or LINE integrations.
- Coordinate health threshold ownership with X04A if configuration binding is changed in a future implementation.

## Current Diagnostic Disposition

Runtime validation is required before X02B can be marked fully `APPROVED`; current static evidence supports `RUNTIME_VALIDATION_PENDING` unless CCG determines the pending items are sufficient for `NO_ACTION_REQUIRED` or `APPROVED_DEGRADED`.
