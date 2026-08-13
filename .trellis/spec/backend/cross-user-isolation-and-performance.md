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

## 8. Server-Authorized Browser Locator to Immutable Audit DTO Scenario

### 1. Scope / Trigger

Apply this scenario when an existing browser endpoint carries a target GUID but
must migrate from a session-owned CRM/legacy form path to a typed read response.
The target ID is a locator, never an authenticated subject, authorization scope,
profile, endpoint, credential, connector, or organization selector. This
scenario is mandatory for a Controller -> session manager -> service ->
ProductClient -> JSON path, such as ChurchReport's Package01 fee-audit read.

### 2. Signatures

```csharp
bool CanAccessFeeAudit(Entity? serverLoginContact);

Task<DonationFeeAuditReadResult> RetrieveFeeAuditByContactAsync(
    Guid contactId,
    CancellationToken cancellationToken = default);

public sealed class DonationFeeAuditReadResult
{
    public IReadOnlyList<DonationFeeAuditRow> Fees { get; }
    public int TotalAmount { get; }
}
```

`serverLoginContact` originates only from the server-resolved request/session
snapshot. `contactId` is parsed only after authorization and is sent only to a
deployment-owned typed operation. The result contains only immutable, allowlisted
view values and no CRM `Entity`, form model, profile, credential, token, cache
entry, cancellation state, or raw upstream exception.

### Server-loaded collection snapshot locator variant

For a read-only page whose existing authorization source is a server-loaded collection (for example,
the current login's lesson list), the collection may authorize a browser GUID only when all of the
following are true before parsing the GUID:

```csharp
feeList.EnsureLoginScope(account, password); // scope-only; must not load CRM data
if (!FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(
        feeList.IsLessonListLoadedFor(account, password),
        feeList.LessonList,
        out var authorizedLessonIds))
{
    return FixedDenied();
}

if (!Guid.TryParse(browserLessonLocator, out var lessonId)
    || !FeeEditorLessonAccessResolver.IsAuthorizedTarget(authorizedLessonIds, lessonId))
{
    return FixedDenied();
}
```

The scope check may clear a mismatched legacy cache but must not invoke a loader, CRM lookup, or
outbound I/O. The server collection must already be loaded for the current login; null, malformed or
duplicate identifiers make the snapshot ambiguous and fail closed. The resolver copies unique IDs into
a fresh request-local allowlist and never accepts browser input as profile, owner, connector,
organization, or authorization authority.

### 3. Contracts

1. Rehydrate existing request context and evaluate the server login snapshot
   before parsing the browser locator, acquiring a session manager, target CRM
   lookup, cache access, or typed/legacy dispatch.
2. Missing/empty login ID, absent/non-string role, or insufficient role returns
   the same fixed, de-identified denial response. Do not use a syntactically
   valid target GUID to distinguish existence or obtain authority.
3. A disabled deployment gate may retain the existing compatibility route. The
   enabled branch must use only the typed operation: no target `Entity` read,
   DTO-to-`Entity` rehydration, request-time fallback, retry, or form-model
   mutation.
4. Map all typed rows and calculate totals in request-local variables first.
   Validate each value and the final `Int32` total before publishing a result.
   Overflow, cancellation and typed faults leave no partially published model
   or result.
5. An immutable row type alone is insufficient. The result must defensively
   copy rows and publish a read-only wrapper rather than expose a backing array;
   otherwise a caller can cast the array and replace a row between authorization
   and JSON serialization.
6. Cancellation propagates unchanged through all async layers. A controller's
   generic failure handler excludes `OperationCanceledException`; every acquired
   semaphore/lease releases exactly once in `finally`/`await using`.

### 4. Validation & Error Matrix

| Condition | Required behavior |
|---|---|
| No server login snapshot, empty ID, invalid role attribute, or insufficient role | Fixed de-identified denial before locator parse or I/O. |
| Malformed browser GUID after successful authorization | Same fixed denial; no target lookup or dispatch. |
| Gate disabled | Use documented legacy compatibility route only; do not silently construct a typed alternative. |
| Gate enabled but profile/client unavailable | Fail before outbound work; do not fallback to legacy. |
| Typed row/total outside `Int32` range or null row | Throw/fail closed before publishing any result. |
| Request cancellation or typed fault | Propagate cancellation/fault, release owner resources, do not emit raw detail or retry/fallback. |
| Caller casts published rows to an array or writable collection | Array cast is impossible; any collection mutation attempt throws and leaves the published rows unchanged. |
| Server-loaded authorization collection is absent, belongs to a previous login, contains null/invalid IDs, or duplicates an ID | Reject before browser GUID parsing, CRM loading, client composition, or typed dispatch. |

### 5. Good / Base / Bad Cases

- **Good:** An accounting-authorized request obtains a fresh typed result whose
  copied, read-only rows can be serialized but cannot be replaced. Interleaved
  A/B calls return distinct row collections and totals.
- **Base:** The gate remains false. The legacy route remains compatible, while
  the typed implementation is locally tested without being represented as live
  cutover or CE evidence.
- **Bad:** The controller parses the browser GUID before authorization, passes
  it to a role resolver, retrieves the target `Entity`, catches cancellation as
  a generic error, or exposes `DonationFeeAuditRow[]` directly as an
  `IReadOnlyList`. Each creates either an IDOR, lifecycle, or mutable-result
  leak risk and is release-blocking.
- **Bad:** An endpoint calls `SetupLessonList`, `EnsureLessonListLoaded`, or another legacy CRM loader
  merely because its request-time browser GUID needs authorization. That turns a locator into an
  authorization-triggered data load and can resurrect another login's collection state.

### 6. Tests Required

1. Resolver tests prove only a valid server login snapshot with the established
   role succeeds; null/empty/missing-role/non-authorized snapshots fail.
2. Source/controller contract tests assert authorization occurs before GUID
   parsing, manager access and dispatch; verify disabled/enabled branch shape,
   no target retrieve, no raw exception message, and cancellation exclusion.
3. Typed-client tests assert fixed profile/workload, null caller name, exact
   cancellation forwarding, no form-model input, per-row/total overflow
   rejection, and no fallback.
4. Interleaved A/B tests assert independently allocated lists/totals. A
   regression must prove published rows are not assignable to an array and that
   writable-collection replacement is rejected.
5. Run the owning focused suite, product suite, full solution Release tests,
   Release build, byte-level UTF-8-no-BOM/CRLF/final-CRLF scan, and
   `git diff --check` before classifying the local migration as checked.
6. When authorization uses an existing server-loaded collection, test current-login mismatch, unloaded,
   null/invalid/duplicate snapshot entries, target-not-in-snapshot, and interleaved A/B results. A
   source/controller contract test must prove the snapshot is validated before browser GUID parsing and
   that no legacy loader, CRM `RetrieveEntity`, `FeeList` data mutation, fallback, or retry enters the
   new route.

### 7. Wrong vs Correct

#### Wrong

```csharp
// The browser value enters authority before server authorization, and the
// backing array can later be downcast and mutated.
var target = Guid.Parse(Request.Query["id"]);
var result = new DonationFeeAuditReadResult(rows.ToArray(), total);
return Json(result);
```

#### Correct

```csharp
EnsureCorrectUserData();
if (!DonationFeeAuditAccessResolver.CanAccessFeeAudit(serverLoginContact))
{
    return FixedDenied();
}

if (!Guid.TryParse(browserId, out var target))
{
    return FixedDenied();
}

var result = await manager.RetrieveFeeAuditByContactAsync(target, requestAborted);
return Json(new { result.Fees, result.TotalAmount });
```

The manager/service owns request-local typed mapping; its result makes a
defensive copy and publishes a read-only wrapper. The flag remains a
deployment-owned rollback boundary and is not live-cutover evidence.

## 9. Disabled Package03 Contact-Image Read Boundary Scenario

### 1. Scope / Trigger

Apply this scenario when ChurchReport adds or changes the independent Package03 contact-image read endpoint. It applies to the disabled-by-default route, its deployment-owned feature gate, server authorization, typed ProductClient composition and response bytes. It does not replace the legacy `GetContactImage` route, authorize CE mutation, enable traffic, prove parity, remove ToolUtility or satisfy a P7.5/P8 gate.

### 2. Signatures

```csharp
[HttpGet]
[Route("/MemberInfo/Package03ContactImage")]
public async Task<IActionResult> Package03ContactImage(string contactId);

public static bool IsPackage03SpecialResourcesEnabled(IConfiguration configuration);

public static IPackage03SpecialResourceClient? TryCreatePackage03SpecialResourceClient(
    IConfiguration configuration,
    IPackage03SpecialResourceClient? injectedClient = null);

public Task<Package03ContactImageReadResult> RetrieveAsync(
    Guid contactId,
    CancellationToken cancellationToken = default);
```

The only deployment key is `DynamicsAccess:Package03SpecialResourcesEnabled`; checked-in base and development settings remain `false`. `DynamicsAccess:ProfileAlias` and the fixed workload `church-report-member-info-image-read` are server-owned scalars, never route, query, header, session or browser input.

### 3. Contracts

1. After safe retrieval of deployment `IConfiguration`, the gate is the first executable decision. When false, return a fixed 404 before session/user hydration, MemberInfo scope resolution, GUID parsing, target authorization, client/host construction, cache access or outbound I/O.
2. When true, execute `EnsureCorrectUserData()` and validate `GetAccess()` for the server-side MemberInfo scope before parsing the browser locator. Parse `contactId` only as a locator, then call `CanViewContact(Guid)` before typed dispatch. The locator never selects profile, workload, connector, endpoint, credential, owner or organization.
3. The enabled branch may create only the Package03 typed client from the deployment-owned process host and call `RetrieveContactImageAsync` with `HttpContext.RequestAborted`. It must not create a provider, handler, pool, credential graph or connector per request.
4. The service maps only `ContactImageMediaKind.Png` to `image/png` and `ContactImageMediaKind.Jpeg` to `image/jpeg`; it rejects empty bytes, unknown media kinds, blank profile and incomplete upstream results before publishing response bytes. Result construction and every bytes getter make a defensive copy.
5. The new action must not call `GetConnection`, use `IOrganizationService`, `Entity`, `IMemoryCache`, `ToolUtility`, redirect to LINE, call `GetDefaultImage`, use `GetContactImage`, retry or use a legacy fallback. Preserve the legacy route unchanged because its LINE redirect and gender-avatar behavior is outside the Package03 DTO contract.
6. `OperationCanceledException` propagates unchanged. A generic controller catch filters it out; non-cancellation typed faults return only fixed 404 and never echo an upstream exception, endpoint, credential, token, contact data or image bytes.
7. The service/result owns no stream, decoder, cache, client, lease, timer, subscription, static mutable state or background work. The existing profile/generation process host remains the single owner of reusable executor resources.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| `Package03SpecialResourcesEnabled` missing or false | Return 404 before session, locator parse, authorization, client/host creation or I/O. |
| Scope invalid, locator invalid or target not visible | Return the same 404 before typed dispatch; do not infer contact existence. |
| Gate true but profile is blank or typed client unavailable | Fail closed before outbound work; do not use legacy CRM or another profile. |
| Typed result null, image bytes empty or media kind unknown | Reject before response publication; no partial bytes, fallback or retry. |
| Request cancellation | Propagate cancellation unchanged; downstream owner releases its own uncertain transport/lease. |
| Non-cancellation typed fault | Return fixed 404 without raw exception detail or a second data path. |

### 5. Good / Base / Bad Cases

- **Good:** Two interleaved authorized requests receive separately allocated PNG/JPEG byte arrays after their own target authorization; neither response can mutate the other's bytes or profile context.
- **Base:** The gate is false. The route returns 404 and the legacy image route retains its existing behavior; this local implementation remains neither CE evidence nor a traffic cutover.
- **Bad:** Parse the GUID before server scope validation, allow a query profile, cache image bytes without the full isolation boundary, reuse legacy avatar/LINE fallback, catch cancellation as a normal 404, or construct a new product provider per request.

### 6. Tests Required

1. Source/controller contract tests assert gate ordering; scope before locator parse; target authorization before client creation; exact `RequestAborted` forwarding; no CRM SDK/cache/legacy/fallback/retry symbols; and unchanged cancellation filtering.
2. Service tests assert fixed profile/workload, exact cancellation-token forwarding, PNG/JPEG MIME mapping, empty/unknown failure before publish and defensive output copying.
3. An interleaved A/B fake-client test must use distinct markers and prove results, content types and arrays are independent.
4. Bootstrap tests assert false gate returns null before host resolution and true gate requires a non-empty deployment profile even when a test client is injected.
5. Run targeted tests, the full ChurchReport test project, full Release solution tests/build, UTF-8 no-BOM/CRLF/final-CRLF byte checks and `git diff --check`. Keep the gate false and do not run CE, traffic, P7.5 or P8 work as part of these local tests.

### 7. Wrong vs Correct

#### Wrong

```csharp
var contactId = Guid.Parse(Request.Query["contactId"]);
var profile = Request.Query["profile"];
return Redirect(GetContactImage(contactId.ToString()));
```

The caller controls routing, target parsing occurs before server authorization, and the typed boundary silently reuses legacy behavior.

#### Correct

```csharp
if (!DonationDynamicsAccessBootstrap.IsPackage03SpecialResourcesEnabled(configuration))
{
    return NotFound();
}

EnsureCorrectUserData();
if (GetAccess() is not (MemberInfoAccess.Church or MemberInfoAccess.ShepherdList) ||
    !Guid.TryParse(contactId, out var target) ||
    !CanViewContact(target))
{
    return NotFound();
}

var result = await service.RetrieveAsync(target, HttpContext.RequestAborted);
return File(result.GetImageBytes(), result.ContentType);
```

The gate, scope and target are all server validated before the fixed-profile typed dispatch; bytes are request-local copies and failure cannot select a legacy or alternate transport path.

## 10. Full Package03 Contact-Image Display And Derived Matrix Mapping Scenario

### 1. Scope / Trigger

Apply this scenario when a pre-existing Package03 image-bytes operation cannot express every visible branch of the
same legacy endpoint, such as a contact image, approved LINE profile redirect and gender-based default avatar.
The source `normalizedCallSites` inventory remains immutable: an extra typed response contract is represented only
as a separately named `derivedOperationMappings` entry that cites exactly one existing source call-site ID. This is
local-only contract work until its own CE, capacity, parity, rollout and rollback evidence exists.

### 2. Signatures

```text
CapabilityOperationId = "memberinfo.contact.retrieve.image.display"
ContactImageDisplayKind = Image | LineRedirect | DefaultAvatar

GET /MemberInfo/Package03FullContactImage?contactId=<GUID>&size=<int>&fit=<bool>
```

The connector owns one fixed `Retrieve(contact)` projection of `entityimage`, `new_line_picture_url` and
`gendercode`. The browser supplies only the contact locator and presentation scalars; it never selects the profile,
workload, CRM attribute, URL, host, connector, endpoint, owner or CE version.

### 3. Contracts

1. `normalizedCallSites` stays the unique 70-row source inventory. Every derived mapping has one existing source ID,
   a distinct capability ID, the same closed row schema and a unique response policy; it may not add an untracked CRM
   call or overwrite a source operation.
2. Connector identity must be exactly `contact` plus the requested `contactId` before reading any attribute. A null,
   wrong logical name or wrong ID fails closed before image, redirect or avatar mapping.
3. The output union has exactly one branch, selected in strict order: validated bounded PNG/JPEG copy, then approved
   LINE redirect, then optional pure `gendercode`. Entity, `OptionSetValue`, stream, raw URL, cache entry and raw
   upstream exception never leave the connector request scope.
4. A LINE redirect is absolute HTTPS with no user-info, fragment or non-default port and with exact host equality to
   `profile.line-scdn.net` or `obs.line-apps.com`. Connector and ChurchReport independently apply this same policy;
   a later layer rejects a malformed or policy-divergent union instead of redirecting it.
5. Base Package03 gate and full-display sub-gate are both false in checked-in configuration. The route order is gate →
   server scope → GUID locator parse → exact target authorization → fixed-profile typed client → dispatch. It has no
   ToolUtility/CRM SDK path, server image cache, retry or request-time legacy fallback.
6. Image arrays are copied at each boundary; the service, controller and result own no connector, stream, cache,
   timer, subscription, background task or cancellation registration. Cancellation leaves unchanged so the existing
   profile/generation lease owner can evict uncertain transport and release resources deterministically.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Derived mapping lacks one source row, repeats source capability, or duplicates a derived capability | Fail the matrix/registry agreement test before build or connector dispatch. |
| CRM retrieve identity is null, non-contact or mismatched | Fail closed; do not return a different contact's image, LINE URL or gender. |
| Image is absent/invalid and URL is absent, unapproved, non-HTTPS, has user-info/fragment or a non-default port | Publish only the default-avatar branch. |
| Full-display gate is false or scope/target validation fails | Fixed 404 before locator I/O/client construction or typed dispatch. |
| Product layer observes an invalid redirect union | Throw/fail closed before MVC redirect; no avatar or legacy fallback is substituted. |
| Cancellation, connector fault or incomplete branch | Propagate cancellation or return the existing fixed failure; do not retry, cache, partially publish or reuse transport. |

### 5. Good / Base / Bad Cases

- **Good:** A Data8 retrieve for A and B returns distinct contact entities. Each result is identity-checked, copied and
  mapped request-locally; the registered derived capability remains traceable to `ORG-CALL-00028` without changing
  the 70-row source inventory.
- **Base:** Both display gates are false. The new route is unreachable, the legacy route is untouched and the local
  contract is not CE/traffic/P7.5/P8 evidence.
- **Bad:** Replace the source image operation with the display operation, create a 71st source call-site, accept an
  allowlisted host on port 8443, select a host from a browser parameter, redirect after a typed failure, or cache a
  contact image under a shared key.

### 6. Tests Required

1. Matrix agreement tests prove the source row count remains 70; each derived mapping has one source ID and a
   distinct/unique capability ID; merged source-plus-derived policy rows exactly equal the compiled registry.
2. Data8 tests inject mismatched entity identity, image/LINE/avatar priority, unapproved host, default-port and
   non-default-port URLs, `OptionSetValue`/integer gender, cancellation-before-dispatch and service disposal.
3. Product/service tests prove exact profile/workload/token forwarding, defensive copies, A/B interleaving and
   product-layer rejection of an otherwise allowlisted host with a non-default port.
4. Controller source/contract tests prove gate/scope/locator/target/client order, false default settings and absence
   of ToolUtility, SDK entity, cache, retry and legacy fallback in the new action.
5. Run impacted suites, full Dynamics/ChurchReport tests, full solution Release tests/build, UTF-8-no-BOM/CRLF/final
   CRLF scan and `git diff --check`. Record any bounded external-review timeout as dual-model-not-completed.

### 7. Wrong vs Correct

#### Wrong

```text
normalizedCallSites += displayContract; // changes immutable source count
if (allowedHosts.Contains(uri.Host)) return Redirect(uri); // permits :8443
return legacy.GetContactImage(contactId); // typed fallback
```

#### Correct

```text
derivedOperationMappings += sourceId=ORG-CALL-00028 + distinct display capability
if (!uri.IsDefaultPort || !ExactLineHost(uri.Host)) fail closed
gate -> scope -> locator -> target authorization -> fixed typed display dispatch
```

This retains an auditable source inventory, maintains identical connector/product redirect policy and preserves the
request-local isolation/cleanup boundary without claiming live cutover.

## 11. Authentication Credential Verification Boundary Scenario

### 1. Scope / Trigger

Apply this scenario when migrating any account/password, PIN, secret, token, or other credential
verification path from legacy CRM or application code to a typed ProductClient/Gateway capability.
A contact/profile read is not a credential-verification capability. This scenario applies even when an
existing read DTO contains a contact identifier or display fields.

### 2. Signatures

The future verification capability must use a fixed, server-owned operation and return only a fixed
non-secret classification:

```text
CapabilityOperationId = "auth.contact.credential.verify"
CredentialVerificationOutcome =
  verified | invalid-credentials | ambiguous | profile-unavailable
```

The actual language signature may evolve in its owning task, but it must accept server-derived
`ProfileAlias` and workload/authorization scope. Browser account/password values are untrusted inputs;
they never choose an operation, credential, endpoint, connector, organization, owner or profile.

### 3. Contracts

1. The credential source, hash/upgrade policy and single secret owner are approved before implementation.
   A legacy plaintext comparison is not an acceptable typed migration source.
2. Secrets are compared only inside the controlled owner. Wire DTOs, ProductClient results, Session,
   logs, task artifacts, exceptions and browser responses contain no plaintext, hash, salt, token,
   cookie, raw CRM `Entity`, endpoint, credential or secret-presence detail.
3. Contact-read and credential-verification operations remain separate. A read DTO must not be extended
   with secret fields, rehydrated into a CRM `Entity`, or used to synthesize a successful login/session.
4. The false deployment gate ends before profile resolution, client/handler/pool creation or outbound I/O.
   Once a typed verification is dispatched, an ambiguous, failed, cancelled, timed-out or faulted result
   fails closed without a legacy fallback or retry of uncertain transport state.
5. A successful non-secret outcome does not itself authorize arbitrary contact retrieval. Any session
   handoff or later projection is a separately authorized, request-local flow that preserves the complete
   `IsolationBoundary` and has deterministic cleanup.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| No approved non-plaintext credential source/policy | Do not create or wire the verification capability. |
| Gate false | Zero typed I/O; preserve the documented compatibility path without constructing typed resources. |
| Secret, hash, token, CRM entity or raw upstream detail appears in result/log/artifact | Reject before publication; treat as a release-blocking secret/isolation defect. |
| Zero, duplicate or ambiguous account match | Return the fixed `ambiguous`/failure classification; never select a first match. |
| Cancellation, timeout, fault or cleanup uncertainty | Fail closed; evict uncertain transport resources, release ownership in `finally`, and do not retry/fallback. |
| Session handoff lacks validated subject/profile/generation scope | Reject before session mutation, cache access, client allocation or data projection. |

### 5. Good / Base / Bad Cases

- **Good:** A fixed, deployment-owned executor compares the secret internally and emits only
  `verified`; a separately authorized request-local handoff creates no shared CRM entity, DTO, credential
  or session cache.
- **Base:** The gate is false. The future typed verification path performs no I/O and is not represented
  as CE, traffic-cutover, ToolUtility-removal or P8 evidence.
- **Bad:** A contact read returns a password/hash, a controller reconstructs a CRM `Entity` from a read
  DTO, or a typed failure queries legacy CRM as a fallback. Each breaks the trust boundary and is a
  release blocker.

### 6. Tests Required

1. Contract tests prove every result/log/task artifact excludes secret material, raw CRM entities and
   upstream fault detail.
2. Tests prove the false gate performs no profile/client/handler/pool/CE work; enabled execution uses
   server-owned routing only and never selects a first duplicate match.
3. Interleaved A/B tests use distinct account/session/profile markers and prove no cross-response,
   cross-session, cross-cache or cross-log state.
4. Fault-injection tests cover invalid credentials, ambiguous match, cancellation, timeout-after-dispatch
   and cleanup failure. They prove no legacy fallback/retry and that uncertain resources are not reused.
5. Any session handoff test proves validation occurs before session mutation and that a login outcome
   cannot be converted into a caller-selected contact/entity projection.

### 7. Wrong vs Correct

#### Wrong

```csharp
var contact = await contactReadClient.RetrieveByAccountAsync(profile, subject, account, cancellationToken);
if (contact.Found && contact.PasswordHash == Hash(password))
{
    return CreateSession(new Entity("contact", contact.ContactId));
}
```

This copies or assumes secret material at a read boundary and synthesizes legacy entity/session state.

#### Correct

```text
Validate server-owned authentication scope
  -> fixed credential-verification operation owns secret comparison
  -> emit only fixed non-secret outcome
  -> independently authorize any request-local session handoff
```

The verification boundary is fail-closed, secret-free outside its owner and cannot silently select a
legacy path after typed dispatch.

## 12. QR／Browser Locator 的共享狀態拒絕情境

### 1. Scope / Trigger

當 browser POST、route、query string、QR code 或其他 caller-supplied locator 會進入 Dynamics read／write
流程時套用。本情境特別適用於既有 Controller 以 `InMemoryContext`、static manager、singleton 或共享
collection 保存 QR、LINE user、group、room、view type、contact、meeting 或 weekly-report context 的路徑。

### 2. Signatures

新的 typed path 必須先建立不可變的 server-validated request scope，再將 locator 當成待驗證的定位資訊：

```text
ValidatedRequestScope = server-authenticated subject + product + authorization scope + profile alias + generation
TryNormalizeLocator(ValidatedRequestScope scope, string browserOrRouteLocator)
    -> immutable, bounded command or fixed denial
```

locator 不得選擇 subject、owner、profile、credential、endpoint、organization、operation 或 mutable shared
context。實際 type 可因產品不同而異，但 scope 的來源與生命週期必須是 request-local。

### 3. Contracts

1. 在解析 locator、讀取 CRM、解析 profile、建立 client／lease、快取查詢或執行 I/O 前，完成 server
   authentication 與 authorization。
2. 不得把 `UserLineId`、`GroupId`、`RoomId`、`ViewType`、QR 值或從它們導出的 target 寫入
   `InMemoryContext`、static field、singleton、shared collection、跨 request closure 或 background work。
3. QR 或 browser locator 只可在已驗證 scope 中作為 locator；missing、ambiguous、malformed 或 target
   unauthorized 都在 I/O 前回傳固定去識別化拒絕。
4. 已存在的 local reducer／plan 只能表達 local decision，不能證明 server authorization、ledger、fixture
   ownership、CE dispatch、consumer cutover、traffic 或 rollback。把 reducer 直接接到 shared-context legacy
   path 是 forbidden read-new/write-legacy bridge。
5. 若一次 QR 操作混合 attendance create/update、relationship、weekly-report update、aggregate recomputation
   或 notification，先拆成固定 command capability。每個 mutation family 都須有自己的 idempotency、ledger、
   exact read-back、reconcile、rollback owner 與 deterministic cleanup；static `lock` 不可當成跨 host/process
   concurrency authority。

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Browser／route locator 在 authorization 前被寫入 shared state | Reject the migration design; do not create ProductClient/CE wiring. |
| Scope missing, expired, ambiguous or target is unauthorized | Fixed denial before locator parse, CRM read, client composition or any mutation. |
| Local reducer is the only available evidence | Keep `CeDispatchAllowed=false` and consumer disabled; do not infer live capability. |
| QR flow contains multiple mutation or notification side effects | Split into separate capability families and fail closed until each owns idempotency and cleanup. |
| Timeout, ambiguous transport, read-back mismatch or cleanup uncertainty | Stop that mutation family, evict uncertain resources and do not retry it. |

### 5. Good / Base / Bad Cases

- **Good:** A request-local, authenticated scope authorizes the scanner and target before the QR locator is parsed.
  The resulting immutable command uses a fixed operation and never writes browser data to shared state.
- **Base:** A legacy QR flow is preserved but its new typed gate remains false. The local plan is tested and no
  client, profile resolution, CE operation or product traffic is started.
- **Bad:** A controller stores a browser QR or LINE/group/room fields in `InMemoryContext`, then constructs a
  utility that reads the same global object and performs CRM work. Concurrent users can overwrite the authority
  boundary; the migration is a release blocker even if a single-user test passes.

### 6. Tests Required

1. Source/contract test proves authorization is evaluated before locator parsing, `InMemoryContext` access, client
   composition, profile resolution and I/O.
2. Interleaved A/B request tests with distinct QR/subject/profile markers prove neither command, response, ledger,
   cache, log nor resource owner crosses the boundary.
3. Tests cover missing/malformed/ambiguous/unauthorized locator, cancellation, timeout-after-dispatch, read-back
   mismatch and cleanup failure; all fail closed and never retry/fallback.
4. For multi-effect QR flows, a test proves each fixed command rejects undeclared relationship, weekly-report,
   notification or owner mutation.

### 7. Wrong vs Correct

#### Wrong

```csharp
// Browser input becomes process-wide authority before authorization.
InMemoryContext.UserLineId = Request.Form["userLineId"];
InMemoryContext.ListManager.QrCodeId = RouteData.Values["qr"]?.ToString();
return legacyQrUtility.Sign();
```

#### Correct

```text
Validate request-local server scope
  -> normalize browser QR only as a locator
  -> authorize target within the validated scope
  -> construct one immutable fixed command
  -> dispatch only through the command's owned idempotency/cleanup boundary
```

The correct path has no browser-derived shared mutable authority. If the required command boundary is not yet
implemented, it returns a fixed no-go rather than borrowing legacy global state.
