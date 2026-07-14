# X05Q Runtime Validation Plan

Module: X05Q
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

No runtime validation was executed in this diagnostic because the allowed write scope excludes product code, tests, generated output, `bin`, `obj`, cache, and lockfiles.

## Validation Targets

### X05Q-SEC-001

- Scenario: authenticated web login, LINE login, expired session, session/auth claim mismatch, and cache miss.
- Instrumentation: log-only counters in a future approved branch for session adapter decisions, ListManager rebuild count, and mismatch outcomes.
- Expected proof: exactly one validation decision per request, no session key rewrite unless auth ticket and expected account mode match.
- Keep/delete rule: keep if current code can rewrite or rehydrate identity state through more than one path; delete only if a source recheck proves all paths are already behind one validated adapter.

### X05Q-SEC-002

- Scenario: crawl all `/Home/*` route templates and compare against a route owner manifest.
- Instrumentation: route table dump, method/anti-forgery/auth metadata, downstream controller owner, parameter list.
- Expected proof: every compatibility route has one owner, one allowed method set, and declared auth/session preconditions.
- Keep/delete rule: keep until the manifest exists and no route manually service-locates controller dependencies.

### X05Q-PERF-001

- Scenario: load list manager pages with warm/cold cache and session mismatch inputs.
- Metrics: cache hit/miss ratio, CRM calls, ListManager setup count, request wall time, allocation snapshot.
- Expected proof: adapter batching reduces setup count to at most one per request.

### X05Q-PERF-002

- Scenario: download list hierarchy, member identity, weekly report, and present-record flows for small and large lists.
- Metrics: CRM query count, selected column count, total entities materialized, nested loop iteration count, elapsed time.
- Expected proof: batch query facade reduces query count and avoids repeated full materialization.

### X05Q-PERF-003

- Scenario: concurrent weekly report uploads for same and different list/week keys.
- Metrics: lock wait time, CRM write count, immediate reload query count, p95 latency.
- Expected proof: keyed concurrency preserves same-key consistency while allowing unrelated uploads to proceed.

## Allowed Future Validation Commands

Only after explicit optimization authorization:

- Route metadata inspection that does not restore packages.
- Unit tests against newly extracted pure adapters.
- Targeted integration tests with a known CRM test fixture and isolated output paths.

## Disallowed In This Diagnostic

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- package restore
- code generation
- migration
- commands that write `bin/**`, `obj/**`, caches, lockfiles, or test output.

## Bounded Validation Outcome - 2026-07-13

| ID | Required measurement | Current blocker |
|---|---|---|
| X05Q-PERF-001 | warm/cold cache hit ratio, CRM calls, setup count, wall time, allocations | no log-only counters |
| X05Q-PERF-002 | query count, selected columns, materialized entities, loop iterations, elapsed time | concrete CRM/static factory dependencies |
| X05Q-PERF-003 | same/different-key concurrency, lock wait, write/reload count, p95 | no isolated CRM tenant or safe synthetic cleanup fixture |

All three remain
`RUNTIME_VALIDATION_PENDING_BLOCKED_BY_INSTRUMENTATION_OR_ISOLATED_FIXTURE`.
Security scenarios are future acceptance checks for retained `KEEP` findings,
not new runtime-pending reviewer verdicts.
