# B05 Runtime Validation Plan

Status: DRAFT
Nested agent count: 0

No runtime validation was executed in this diagnostic pass. Product code, tests, generated output, package restore, build, and test artifacts are read-only for this subagent.

## Validation Items

### B05-SEC-001 Callback diagnostic leakage

- Reproduce: submit malformed Sinopac return parameters to `/Payment/Return` or `/api/PaymentReturn/Return` in a controlled non-production environment.
- Observe: application log sinks, console output, trace output, and donor-facing payment result.
- Expected safe result: no stack trace or raw provider exception in broad console/trace output; donor-facing page shows generic support message only.
- Data to collect: correlation id, masked provider refs, log category, sink visibility.

### B05-PERF-001 Sync-over-async notification path

- Reproduce: invoke MyPay callback with a fake LINE workflow that delays or rejects sends.
- Observe: callback acknowledgement latency and thread usage.
- Expected safe result: provider acknowledgement is not blocked by LINE delivery latency beyond the intended policy.
- Data to collect: p50/p95 callback duration, number of blocked request threads, notification retry behavior.

### B05-PERF-003 Detached notification continuation

- Reproduce: force LINE send to exceed the 500 ms display timeout in manual dedication and ATM flows.
- Observe: background continuation completion/failure, shutdown behavior, and audit visibility.
- Expected safe result: delayed delivery has durable observability and does not depend on request lifetime.
- Data to collect: delivery success/failure event, retry key, user-visible status, server log correlation.

## Commands Not Run

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- package restore
- code generation
- formatting
- migrations
# B05 Runtime Validation Plan

Status: DEGRADED_REVIEW_PENDING
Nested agent count: 0

No runtime commands were executed in this diagnostic pass. Product code, tests, generated output, restore/build/test artifacts, lockfiles, caches, and package artifacts remained read-only.

## Validation Plan

### RV-SEC-001 Duplicate callback and state pollution

- Issue link: B05-SEC-003.
- Setup: use a fake provider callback result for the same `ProductOrderId` and fee entity.
- Steps:
  - Send a successful callback twice.
  - Send failed after success.
  - Send success after failed.
  - Send callback with missing/unknown order id.
- Expected:
  - Paid state is monotonic or explicitly audited.
  - Duplicate callback does not duplicate notification or corrupt paid amount/date.
  - Unknown order id is acknowledged according to provider contract without CRM mutation.
- Evidence to collect: CRM field before/after, notification count, acknowledgement body/status, structured log event.

### RV-SEC-002 Callback diagnostic leakage

- Issue link: B05-SEC-001.
- Setup: controlled non-production host with malformed Sinopac return request.
- Steps:
  - Hit `/Payment/Return` and `/api/PaymentReturn/Return` with malformed provider fields.
  - Inspect console, trace, structured logs, and donor-facing result.
- Expected:
  - No raw stack trace, inner exception, provider payload, or token appears in broad logs or UI.
  - Correlation id and masked provider references are enough for support triage.

### RV-PERF-001 Callback acknowledgement latency under LINE delay

- Issue link: B05-PERF-001.
- Setup: fake `ILineNotificationWorkflow` that delays and then succeeds/fails.
- Steps:
  - Invoke `MyPayNotify` callback through controller/integration harness.
  - Compare callback acknowledgement latency with LINE delay values of 0 ms, 500 ms, 2 seconds, and failure.
- Expected:
  - Target design should decouple provider acknowledgement from LINE transport latency.
  - Current design is expected to show synchronous blocking; measure before changing.

### RV-PERF-002 CRM call count and checkout latency

- Issue link: B05-PERF-004.
- Setup: fake or instrumented ToolUtility/CRM client.
- Steps:
  - Execute donation checkout and callback update flows.
  - Count create/retrieve/update/assign calls.
- Expected:
  - Baseline quantifies whether batching or field-shape reduction is worth doing.

## Commands Explicitly Not Run

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- package restore
- code generation
- formatting
- migrations
