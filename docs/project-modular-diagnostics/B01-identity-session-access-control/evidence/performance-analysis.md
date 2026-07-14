# B01 Performance Analysis

Mode: DIAGNOSIS_ONLY

## Confirmed Performance Finding

### B01-PERF-001 Line-binding controller hides synchronous CRM operations behind Task-returning helpers

Evidence:

- `AuthenticationController.LineBinding.cs:66` declares `ProcessLineBinding` as an async MVC action.
- `AuthenticationController.LineBinding.cs:81`, `174`, `246`, `290`, `330`, and `373` await or call CRM work through helper methods during one binding flow.
- `AuthenticationController.LineBinding.cs:402-407` implements `ExecuteCrmAsync<T>` by executing `operation()` synchronously and then returning `Task.FromResult`.
- `AuthenticationController.LineBinding.cs:417-423` does the same for void operations and returns `Task.CompletedTask`.
- `SessionValidationMiddleware.cs:247` also blocks on `CommitAsync().GetAwaiter().GetResult()` in a separate B01 mismatch path.

Performance impact:

- CRM I/O remains on the request thread while the code shape implies asynchronous suspension.
- Slow CRM calls or concurrent LINE binding bursts can occupy ASP.NET Core request threads.
- The helper has no cancellation token, timeout contract, retry/backoff policy, or call-count instrumentation.
- The issue is the hidden synchronous boundary, not the absence of `Task.Run`; adding `Task.Run` would hide the dependency cost and add thread-pool churn.

Recommended optimization seam:

- Extract a line-binding application service with a measured CRM gateway dependency.
- Make the execution model explicit: either true async CRM operations through F03A, or synchronous operations with timeouts and request-level instrumentation.
- Do not offload synchronous CRM calls with `Task.Run` as a blanket fix.
- Return a binding result DTO, then map it to MVC JSON in the controller.

## Rejected Performance Candidates

- `IdentityAuditMiddleware` static dictionary leak: rejected. It is registered only under `#if DEBUG` in `Startup.cs:867-872`, and `IdentityAuditCleanupService.cs:93-120` runs cleanup against the dictionary.
- `SessionValidationMiddleware.ClearSessionAndRedirectToLogin` sync wait as a standalone issue: folded into B01-PERF-001 as supporting evidence because it shows the same async/sync cleanup need but occurs only in the mismatch path.

## Performance Validation Plan

- Before optimization, collect request duration and CRM call count for successful and duplicate LINE binding flows.
- Validate the extracted service with fake CRM gateways for existing-contact, duplicate-name, create-contact, placeholder cleanup, CRM fault, and timeout cases.
- If true async CRM operations are introduced, verify request cancellation propagates to the gateway.
