# Cross-User Isolation and Sustainable Performance Contract

## 1. Scope / Trigger

This is a repository-wide, permanent contract. It applies to every existing
and future product line, whether it is hosted as an ASP.NET Core application,
an Embedded client, a Dedicated Gateway, a Central Gateway, a worker, a
background service, a browser client, a CLI/script, a test host, or an
integration component.

Apply this contract whenever code reads, writes, stores, transports, derives,
caches, pools, logs, queues, retries, renders, or disposes data that can be
connected to an authenticated user, workload, tenant, product, Dynamics
profile, organization, credential, session, or request.

**Absolute rule:** subject A must never see, receive, reuse, mutate, infer from
an error, or retain any data or mutable execution state belonging to subject B.
Cross-user, cross-tenant, cross-profile, and cross-product leakage is a
release blocker. Isolation takes priority over throughput, but implementations
must retain high safe sustained performance instead of substituting an
unbounded or per-request-expensive design for correct isolation.

## 2. Signatures

Every data path has a server-validated logical isolation boundary:

```text
IsolationBoundary = (
  AuthenticatedSubjectId or server-derived WorkloadSubjectId,
  TenantBoundary when the product has tenants,
  ProductBoundary,
  AuthorizationScope,
  ProfileAlias when Dynamics is used,
  GenerationId when a connector runtime is used)
```

The exact type may differ by product, but all values used to authorize,
partition, cache, or select a runtime must be server-derived and immutable for
the request. They are not accepted from a browser, IPC frame, JSON body,
background queue payload, or caller-controlled configuration as routing
authority.

Existing Dynamics execution remains behind the bounded lease contract:

```csharp
Task<OperationExecutionResult> ExecuteAsync(
    OperationExecutionRequest request,
    CancellationToken cancellationToken = default);

Task<OperationExecutionResult> ExecuteAsync(
    ConnectorOperation operation,
    CancellationToken cancellationToken = default);
```

The first signature is the product/Gateway boundary; the second is reachable
only through an acquired `IConnectorLease`. Neither signature permits a caller
to choose a credential, endpoint, connector kind, organization, or another
subject's authorization scope.

## 3. Contracts

1. Authentication and authorization are performed before any cache lookup,
   profile resolution, connector allocation, outbound call, response mapping,
   or background-work enqueue. Missing, ambiguous, expired, or mismatched
   isolation context fails closed.
2. A request may use only its own immutable isolation context. Do not keep
   `HttpContext`, `ClaimsPrincipal`, session objects, user DTOs, tenant values,
   authorization decisions, ORM entities, CRM entities, tokens, cookies, or
   mutable response objects in static fields, singletons, shared collections,
   closures that outlive the request, timers, subscriptions, or background
   queues.
3. A shared cache may store only data explicitly declared safe for every
   authorized caller, such as immutable metadata. User rows, search results,
   authorization decisions, member details, request DTOs, and error payloads
   must be request-local or use a cache partitioned by the full validated
   `IsolationBoundary`, with a bounded size, expiry, invalidation path, and
   deterministic eviction.
4. A reusable connection/client may be returned only to its source pool and
   only when it contains no user/request state. Dynamics clients are isolated
   by `(ProfileAlias, GenerationId)` and never cross aliases, generations,
   organizations, connector kinds, credentials, or profile state. A fault,
   timeout, cancellation, drain, or transport uncertainty makes the client
   ineligible for reuse and requires deterministic disposal.
5. A queue, retry record, event, diagnostic record, or background-work item
   contains only the minimum immutable, allowlisted fields needed to continue
   work. Its owner, maximum lifetime, cancellation, delete/acknowledge path,
   and error redaction must be explicit. It must never inherit a live request
   session or principal.
6. Logs, telemetry, exceptions, JSON responses, IPC, and UI state must not
   echo another subject's data, session, principal, token, credential, cache
   key, endpoint, or raw upstream response. Diagnostics use bounded categories
   and correlations that cannot be used to retrieve another subject's data.
7. Resource ownership is singular and explicit. Every lease, permit, client,
   WCF channel/factory, stream, buffer, timer, cancellation registration,
   process, pipe, queue entry, subscription, temporary file/directory, and
   background task has a bounded lifetime and deterministic `finally`/dispose/
   drain/termination path. Cleanup failure is a release-blocking failure, not
   a successful request with a warning.
8. Performance work may use bounded pooling, batching, projection, pagination,
   exact-key retrieval, asynchronous I/O, and back-pressure only after proving
   that these mechanisms preserve the full isolation boundary. Small fixed
   costs for validation, partition checks, cleanup, and fault eviction are
   required. Global mutable caches, global locks around remote I/O, unbounded
   scans, unbounded queues, and a fresh expensive runtime for every normal
   request are forbidden shortcuts.

## 4. Validation & Error Matrix

| Condition | Required behavior |
|---|---|
| Missing or unvalidated subject, tenant, product, authorization scope, profile, or generation | Reject before cache, allocation, outbound I/O, or response creation; emit no prior-request data. |
| Caller supplies routing identity, tenant, profile, credential, endpoint, connector, or organization | Ignore it as authority and fail closed with a bounded error. |
| Cache entry is not proven to match the full validated isolation boundary | Treat as a cache miss, discard it if unsafe, recompute within the current request, and never emit it. |
| Lease/client is cancelled, timed out, faulted, draining, or has uncertain transport state | Do not return it to a pool; dispose it, release permits in `finally`, and surface sanitized failure. |
| Queue/retry/background work lacks a valid immutable isolation boundary or exceeds its lifetime | Do not execute it; cancel/delete/quarantine it through its owner without using another request context. |
| Cleanup, drain, response-buffer release, temporary-data cleanup, or resource-counter baseline check fails | Mark the operation/check `no-go`; continue best-effort ordered cleanup and do not report success. |
| Pool/queue reaches its safe bound | Apply bounded back-pressure or reject with a retryable bounded result; never borrow another profile's client/session or serialize all users behind an unbounded global lock. |

## 5. Good / Base / Bad Cases

- **Good:** A and B make interleaved requests through the same product. Each
  request resolves its server-derived authorization scope before reading data;
  both use request-local DTOs. A bounded, healthy Data8 client may be reused
  only within the same immutable Profile/Generation after its prior lease is
  fully released, and it carries no A/B request state.
- **Base:** A cache miss or pool saturation adds a small bounded wait and a
  recomputation/retryable response. The system remains responsive and returns
  no data until authorization and partitioning are proven.
- **Bad:** A singleton stores the last authenticated user, a controller puts a
  user-specific result under a generic cache key, a faulted client returns to
  the idle pool, or a background task captures `HttpContext`. Any of these is
  a release blocker even when an ordinary happy-path test passes.

## 6. Tests Required

1. Run concurrent/interleaved A/B isolation tests with distinguishable
   synthetic data. Assert that A's response, rendered/UI state, cache read,
   logs, telemetry, exceptions, and downstream request never contain B's
   marker, identifier, authorization decision, or response field; assert the
   symmetric case for B.
2. Run equivalent tests across different tenants, products, Dynamics profiles,
   and runtime generations when those boundaries exist. Include the case where
   two profiles reach one physical organization: they may share admission
   budget but never mutable client/session/profile state.
3. Inject cancellation, timeout-after-dispatch, disposal failure, queue delay,
   cache eviction, and profile replacement. Assert faulted resources are not
   reused, permits are released exactly once, user-specific state is cleared,
   and unsafe data is never emitted.
4. Run bounded repeated-request/lease/queue soak tests. After drain/disposal,
   client, permit, process, handle, temporary-data, timer, subscription, and
   retained-memory ownership counters must return to the declared baseline.
5. Measure the normal safe path against the established workload baseline.
   Reject an optimization that removes an isolation check; also reject a
   solution that creates a major sustained throughput/latency regression by
   serializing all users, scanning unbounded data, or reconstructing expensive
   infrastructure for each ordinary request.

## 7. Wrong vs Correct

### Wrong

```csharp
// Cross-user state and a generic cache key make the next request unsafe.
private static ClaimsPrincipal? LastPrincipal;

public Task<MemberDto> LoadAsync(string contactId)
{
    return _cache.GetOrCreateAsync(
        "member:" + contactId,
        _ => _memberStore.LoadAsync(LastPrincipal!, contactId));
}
```

This retains a mutable principal beyond its request and allows callers with a
different authorization scope to reuse a user-specific cached result.

### Correct

```csharp
// The server validates a request-local scope first. Only universally safe
// metadata may use a shared cache; user data remains request-local or is
// partitioned by the complete validated isolation boundary.
public async Task<MemberDto> LoadAsync(
    ValidatedRequestScope scope,
    Guid contactId,
    CancellationToken cancellationToken)
{
    await _authorizer.RequireVisibleContactAsync(scope, contactId, cancellationToken);
    return await _memberStore.LoadForScopeAsync(scope, contactId, cancellationToken);
}
```

`ValidatedRequestScope` is illustrative; each product may use its existing
typed context. The non-negotiable behavior is server validation before data
access, request-local mutable state, full-boundary cache partitioning where a
user-specific cache is genuinely required, and deterministic cleanup.
