# Full Code Quality Audit And Fix Design

Date: 2026-07-10
Branch: `1.0.0.0.Initialization.Worktree`
Status: design approved in principle, awaiting implementation plan approval

## 1. Goal

Perform one comprehensive repair effort across the current solution to find and fix defects that can cause session leakage, cross-user data exposure, memory leaks, slow requests, thread-pool starvation, socket exhaustion, unbounded cache growth, and related reliability problems.

The user wants a single final delivery, not many user-facing mini projects. Internally, the work will still be layered so each class of defect can be verified before the final integrated handoff.

## 2. Current Evidence

The repository is a multi-project .NET solution with ChurchReport, LINE messaging, rich menu workflows, payment workflows, ToolUtility, Dataverse client code, Trace, and test projects.

Existing local evidence includes:

- `SpeechMessageProducts.ChurchReport/Startup.cs` configures session, cookies, auth, no-cache headers, session validation, memory cache, hosted services, CRM pooling, and many service lifetimes.
- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs` stores many per-session objects in `IMemoryCache` and has explicit comments about prior session bleeding fixes.
- `SpeechMessageProducts.ChurchReport/SessionAttribute.cs` contains stateful session-checking filter code that can become unsafe if reused across requests.
- Several production paths still use sync-over-async patterns such as `.GetAwaiter().GetResult()`.
- Several LINE-related call sites still construct clients directly, including compatibility constructors that allocate `HttpClient` internally.
- Existing ChurchReport memory optimization and performance optimization documentation is useful context, but current code is authoritative.
- CCG analysis ran through the project self-healing entrypoint. Claude completed; Gemini was blocked by provider quota or billing, so the result is degraded single-model fallback rather than full dual-model analysis.

## 3. One-Shot Delivery Shape

The final delivery should include all completed fixes, tests, static scans, and notes in one handoff. The implementation should not stop after the first narrow fix unless a hard blocker prevents meaningful progress.

Internally, the repair effort is split into workstreams:

1. Resource lifetime and HTTP client ownership.
2. Session identity, filter state, and cross-user isolation.
3. Per-session memory cache growth and disposable object eviction.
4. Sync-over-async and slow request paths.
5. API/controller authorization and object access boundaries.
6. CRM/Dataverse connection and query performance hot spots.
7. Encoding, logging, diagnostics, and test coverage needed to prove the fixes.

## 4. Architecture

The preferred architecture is to make resource ownership explicit and move reusable infrastructure into dependency injection.

LINE API access should flow through DI-created clients and workflows. Application code should not casually call compatibility constructors that allocate `HttpClient` internally. When direct construction remains for tests or compatibility, the owner must be clear and disposal must be deterministic.

Session-bound data should have stable isolation keys, bounded lifetimes, and deterministic cleanup for cached `IDisposable` values. Shared cache entries must be limited to truly global metadata and must not contain user-specific or session-specific data.

Request-path code should avoid blocking on asynchronous operations. Where a synchronous public API must remain for compatibility, it should be treated as a wrapper over an async implementation only when the caller is outside ASP.NET request execution, or it should be replaced at request call sites.

Authorization and object-level access checks should be close to the controller/API boundary. Any endpoint returning contact, group, payment, image, or personal data must prove that the current authenticated identity is allowed to see that object.

## 5. Workstreams

### 5.1 Resource Lifetime And HTTP Clients

Audit all production `new HttpClient`, `new LineMessagingClient`, `new RestClient`, and LINE utility construction. Replace request-path direct construction with injected clients or factories where local patterns already exist.

Expected fixes include:

- Use `IHttpClientFactory` or DI-registered LINE abstractions in ChurchReport call sites.
- Ensure utility classes that own disposable clients implement and are used through `IDisposable` correctly.
- Keep tests free to construct in-memory `HttpClient` instances around fake handlers.
- Preserve backward-compatible constructors only when needed, but mark request-path code away from them.

### 5.2 Session Identity And Cross-User Isolation

Audit session, cookie, auth, cache key, filter, and middleware behavior in ChurchReport.

Expected fixes include:

- Remove or neutralize stateful filter fields that can retain a prior request's session id.
- Verify session validation middleware does not block request threads unnecessarily.
- Confirm session cookie and auth cookie names remain distinct.
- Confirm no server-side cached object keeps an outdated `HttpContext`, `ISession`, user id, password, or LINE id across users.
- Add or update tests around session fallback and authorization behavior where feasible.

### 5.3 Memory Cache Boundaries

Audit `IMemoryCache` usage for per-session values, shared metadata, size limits, expiration, and eviction callbacks.

Expected fixes include:

- Ensure per-session cache entries have explicit absolute or sliding expiration.
- Ensure disposable cached values are disposed on eviction.
- Replace repeated double-read cache patterns with a consistent helper or `GetOrCreate` equivalent when safe.
- Avoid setting a global `SizeLimit` unless every cache entry using the shared `IMemoryCache` can supply a meaningful size.
- Separate shared metadata cache keys from session/user-specific cache keys.

### 5.4 Slow Paths And Sync-Over-Async

Audit `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, `Task.Run`, `Thread.Sleep`, and synchronous wrappers around network or session I/O.

Expected fixes include:

- Convert ASP.NET request-path blocking code to async all the way where possible.
- Keep compatibility sync wrappers only when they are not used from request execution.
- Add focused tests for changed async behavior and error mapping.
- Avoid wrapping synchronous CRM SDK calls in `Task.Run` unless a measured benefit justifies it.

### 5.5 API And Object Access Boundaries

Audit controller and API endpoints that return or mutate user-related data.

Expected fixes include:

- Verify authenticated identity and object-level authorization before returning contact images, personal data, payment data, group data, or administrative lookup data.
- Keep anonymous or public endpoints explicitly documented and constrained.
- Add regression tests for endpoints that had weak or ambiguous access checks.

### 5.6 CRM And Query Performance

Audit CRM/Dataverse connection pooling, repeated queries, unbounded result sets, and cache invalidation.

Expected fixes include:

- Verify connection pool acquire/release behavior and disposal.
- Avoid expensive validation calls in hot paths unless cached, throttled, or required for correctness.
- Add paging or bounds where queries can grow without limit.
- Use existing query/cache services before adding new abstractions.

### 5.7 Diagnostics And Encoding

Audit diagnostics that affect performance, security, or maintainability.

Expected fixes include:

- Keep sensitive values out of production logs.
- Keep debug-only verbose tracing out of release runtime cost.
- Preserve UTF-8 readable source and docs for touched files.
- Avoid bulk re-encoding unrelated files unless encoding is directly blocking the repair.

## 6. Data Flow

The intended safe request flow is:

1. Request enters middleware.
2. Session and auth cookies are validated without sharing request state through static or singleton mutable fields.
3. Controller/API resolves scoped services.
4. User identity is read from server-issued auth/session state, not from client-controllable form fields alone.
5. Object-level access is checked before data access or response generation.
6. External LINE/CRM/payment calls use DI-owned clients or explicitly owned disposable resources.
7. Per-session objects are cached only with bounded expiration and deterministic eviction cleanup.
8. Response headers prevent user-specific content from being reused across users.

## 7. Error Handling

Fixes must preserve user-visible workflows while making failure modes explicit.

- Provider/network timeouts should map to existing provider timeout or unavailable result types.
- Session validation failure should clear session safely and redirect or reject consistently.
- Cache eviction cleanup should never throw into the request path.
- Authorization failure should return the project's existing redirect, forbidden, or not-found behavior depending on the endpoint pattern.
- Unexpected exceptions should be logged without leaking secrets or personal data.

## 8. Testing And Verification

Completion requires evidence, not intent.

Required static verification:

- `dotnet build SpeechMessageProducts.sln`
- Relevant `dotnet test` projects for touched areas.
- `rg` scans for risky construction and blocking patterns.
- `rg` scans for direct session or identity usage in endpoints touched by the repair.
- `git diff` review to confirm changes remain within the repair scope.

Required behavioral verification where feasible:

- Unit tests for ownership/disposal changes.
- Unit tests for session filter or middleware changes.
- Unit tests for async error mapping changes.
- Authorization regression tests for endpoints whose access checks are changed.
- Cache eviction tests for disposable values if cache cleanup behavior is changed.

Runtime profiling is required before claiming runtime-only hypotheses are fully fixed:

- Memory growth needs app execution or load simulation plus `dotnet-counters`, `dotnet-gcdump`, or equivalent evidence.
- Socket exhaustion needs repeated LINE/HTTP path exercise plus handler/socket counter evidence.
- CRM query performance needs representative CRM/Dataverse behavior or a test double that captures query shape and call count.

If a runtime environment is not available, the final report must distinguish static fixes completed from runtime claims not yet proven.

## 9. External Review

Because this is an L+ task and changes are expected to exceed 30 lines, CCG external review is required before final completion.

The project rule requires using `docs/scripts/Start-CcgDualModelRun.ps1`, not direct Gemini or Claude calls.

Gemini is currently quota or billing blocked. If that persists, review can continue only as the approved degraded fallback when Claude completes with usable output. The final report must state that degraded status and must not claim full dual-model review.

## 10. Scope Guardrails

In scope:

- Production C# code involved in resource ownership, session isolation, cache lifetime, sync-over-async, endpoint authorization, CRM/query performance, diagnostics, and related tests.
- Existing docs and CCG task artifacts needed to record the repair.

Out of scope unless directly required by a verified defect:

- Visual redesign.
- Feature behavior changes unrelated to reliability, security, or performance.
- Git history rewrite or secret rotation. If secrets are found, record the risk and remove active checked-in secrets from current code where safe, but history purge and credential rotation require owner action.
- Broad framework/library upgrades beyond what is necessary to build and fix the defects.
- Rewriting large modules purely for style.

## 11. Completion Criteria

The effort is complete only when:

- High-confidence static defects found in the scoped categories are fixed or explicitly documented as false positives with evidence.
- Build and relevant tests pass, or failures are documented as pre-existing/unrelated with evidence.
- Risky pattern scans show no remaining unreviewed production matches in the scoped categories.
- CCG review has run through the project runner, or provider quota fallback is recorded accurately.
- The final report separates proven fixes from runtime hypotheses that still require a deployed or load-test environment.

## 12. Implementation Plan Handoff

After this design is approved, the next artifact should be an implementation plan that lists concrete files, tests, validation commands, and rollback points. Implementation should then proceed in internal workstreams but be delivered as one comprehensive repair set.
