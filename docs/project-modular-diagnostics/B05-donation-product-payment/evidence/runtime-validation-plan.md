# B05 Runtime Validation Plan

Status: DEGRADED_REVIEW_PENDING
Nested agent count: 0

No runtime commands were executed. Product code, tests, restore/build/test outputs, lockfiles, caches, bin/obj, generated files, and package artifacts remained read-only.

## Planned Validations

- RV-SEC-001: duplicate callback, failed-after-success, success-after-failure, and unknown-order callback should not corrupt CRM state or duplicate notification.
- RV-SEC-002: malformed `/Payment/Return` and `/api/PaymentReturn/Return` should not emit raw stack trace/provider internals to broad logs or UI.
- RV-PERF-001: delayed/failing fake LINE workflow should quantify callback acknowledgement latency and prove target decoupling.
- RV-PERF-002: instrument CRM create/retrieve/update/assign count per checkout and callback.

## Commands Explicitly Not Run

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- package restore
- code generation
- formatting
- migrations
