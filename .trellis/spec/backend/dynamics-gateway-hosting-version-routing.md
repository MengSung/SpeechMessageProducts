# Dynamics Gateway Hosting and CE 8.2/9.1 Routing Contract

## 1. Scope / Trigger

This contract applies whenever a SpeechMessage product:

- calls Dynamics 365 Customer Engagement through `SpeechMessage.Dynamics`;
- selects a shared Central Gateway or a product-local Gateway process;
- adds or changes a CE 8.2 or CE 9.1 organization profile;
- introduces a Web API, `ServiceClient`, `CrmServiceClient`, or temporary Data8 transport;
- changes connection/runtime pooling, authentication, profile reload, worker lifecycle, or SDK-removal behavior.

The product-facing architecture has two execution modes only:

- `Gateway`: the product calls the versioned Gateway REST contract.
- `Embedded`: the connector runtime is hosted in the product process.

Central Gateway and Local Gateway are two deployment topologies of `Gateway` mode. They are not additional `DynamicsExecutionMode` enum values. Central versus Local is selected by the configured Gateway endpoint and deployment ownership:

- Central: `Gateway.Endpoint` resolves to the shared internal Gateway service.
- Local: `Gateway.Endpoint` resolves to the product's separately running localhost Gateway process.

`Embedded` remains deferred until the Local Gateway, CE 8.2, CE 9.1, isolation, and lifecycle gates pass. Existing Embedded code may remain, but it is not the current recommended production or development path.

## 2. Signatures

### Product abstraction

```csharp
public interface IDynamicsOperationExecutor
{
    Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default);
}
```

Product business code depends on `IDynamicsOperationExecutor` or a typed product client such as `IPackage01FeeReadClient`. It must not depend on CRM SDK, WCF, SOAP, Web API URL construction, authentication tokens, or a transport-specific client.

### Gateway HTTP API

```http
POST /v1/organizations/{alias}/operations/{capabilityOperationId}
Authorization: <authenticated product workload>
Content-Type: application/json
```

Operation endpoint 是封閉的 JSON-only 契約：只接受不分大小寫的 `application/json`，可省略參數，
或只宣告一個 `charset=utf-8`／`charset=UTF-8`。缺少 Content-Type、無法解析的 header、
`application/*+json`、未知或重複參數，以及非 UTF-8 charset 全部回傳 `415 Unsupported Media Type`。

```json
{
  "idempotencyKey": "optional-bounded-key",
  "parameters": {
    "approvedParameter": "value"
  }
}
```

The Gateway derives `WorkloadSubjectId` from the authenticated caller. A client-supplied identity, CRM endpoint, credential, authorization header, table name, OData string, or unrestricted FetchXML document is not accepted as routing authority.

### Product Gateway configuration

Central deployment:

```json
{
  "DynamicsAccess": {
    "ExecutionMode": "Gateway",
    "ProfileAlias": "crm82",
    "Gateway": {
      "Endpoint": "https://dynamics-gateway.internal/",
      "ApiPrefix": "/v1"
    }
  }
}
```

Local deployment:

```json
{
  "DynamicsAccess": {
    "ExecutionMode": "Gateway",
    "ProfileAlias": "crm91",
    "Gateway": {
      "Endpoint": "https://localhost:7244/",
      "ApiPrefix": "/v1"
    }
  }
}
```

`CentralGateway` and `LocalGateway` are architecture labels only. They are invalid values for the current `DynamicsExecutionMode` enum unless a separately reviewed contract change intentionally adds them.

### Profile transport contract

Before multi-transport routing is enabled, the deployment-owned profile contract must expose one explicit transport kind:

```text
WebApi
OfficialServiceClient
OfficialLegacyWorker
TemporaryData8LegacyWorker
```

The transport kind is fixed for one immutable profile generation. It cannot switch per user request or silently fall back after a failure.

## 3. Contracts

### Product boundary

- Products know `ExecutionMode`, `ProfileAlias`, Gateway endpoint, API prefix, and typed operation parameters only.
- Product JSON must not contain a CRM organization-service URL, raw CRM Web API URL, username, password, client secret, access token, refresh token, certificate private key, SDK DLL path, or transport kind.
- Gateway-owned success envelopes must not serialize `ApprovedWebApiRoot`, CRM hostname, `/api/data/` base path, credential, token, or other internal routing metadata. The validated root remains owned by the profile runtime and is used only for outbound URI allowlisting and server-side paging validation.
- Raw upstream OData annotations that can contain absolute CRM URLs, including `@odata.context` and `@odata.nextLink`, are not automatically product-safe. Before a production operation can return them, the connector must project them into a typed product contract or consume a validated nextLink server-side without exposing the absolute URL.
- The same ProductClient and REST contract are used for Central and Local Gateway deployments.
- Changing between Central and Local requires configuration replacement plus restart/replace-and-drain. It is not a request-time switch.

### Gateway request Content-Type boundary

- Authentication 與 server-owned principal→workload→alias→operation authorization 必須先完成；未授權 caller 固定取得 401/403，不得利用 Content-Type 探測 body contract。
- 已授權要求必須在任何 request-body I/O、`ArrayPool<byte>.Rent`、JSON parser 或 executor request 建立之前驗證 Content-Type。
- 唯一核准的媒體型別是 `application/json`；比較不分大小寫。可省略參數，或只宣告一個值為 UTF-8 的 `charset`。
- `application/*+json` 不在目前契約內。未來若有 vendor media type 需求，必須以明確 allowlist、契約測試與版本化 API 變更另行審查，不得因為 suffix 是 `+json` 就自動接受。
- 415 response 只包含固定 status，不得回顯 caller-controlled Content-Type、body、principal、credential、token 或 session。
- Header validator 必須是無 I/O、無共享 mutable state 的 bounded 操作；不得建立 stream、buffer、timer、subscription、cache、cancellation registration 或 background work。

### Central Gateway ownership

- Central Gateway is the production default for five to ten products.
- It owns deployment profiles, secret references, authorization policy, operation registry, audit, telemetry, retries, health, readiness, and profile runtime generations.
- It owns one bounded runtime pool per immutable profile generation. `crm82` and `crm91` never share a mutable client, credential, token cache, WCF channel, or session.
- Multiple products may share the same Central Gateway profile runtime only after server-side authorization resolves them to the approved alias and workload policy.

### Local Gateway ownership

- Local Gateway is a separate Windows process started beside one product for Visual Studio development, integration testing, or an explicitly isolated deployment.
- The product still calls localhost through the Gateway HTTP contract; Local Gateway is not Embedded mode.
- Every Local Gateway process owns and deterministically disposes its physical handler/client/worker-proxy pool.
- Local Gateway uses the same operation registry, profile validation, adapter contracts, health semantics, and secret-reference rules as Central Gateway.
- A local JSON file cannot grant production access by itself. Production-capable Local Gateway profiles require an approved manifest or central registry binding.

### Organization-level capacity

- Central and Local physical pools are process-local and are never the same object.
- All Central, Local, blue/green, canary, and draining runtime hosts that reach the same physical Dynamics organization share one `OrganizationAdmissionKey` and aggregate concurrency budget.
- Version labels, aliases, environment labels, or process boundaries must not multiply the physical organization's capacity.
- Every admitted request holds a bounded lease/permit before outbound Dynamics traffic. Loss or expiry of the host lease stops new admission and forces bounded drain/cancellation.

### CE 9.1 profile

- The preferred transport is direct Microsoft Dynamics Web API v9.1 through the approved `HttpClient` runtime.
- Microsoft's official `ServiceClient` may be used when the real target's supported OAuth/authentication path is proven.
- Data8 is not a required CE 9.1 dependency.
- A CE 9.1 profile has its own version-specific capability registry and real-server evidence.

### CE 8.2 profile

- CE 8.2 does not inherently require the checked-in Data8 project.
- The current CE 8.2 IFD environment temporarily depends on the working WS-Trust/SOAP path because the Web API OAuth path is not yet proven.
- Preferred replacement A is direct Web API v8.2 after ADFS OAuth client registration, redirect URI, token acquisition/renewal, and required-operation compatibility pass.
- Preferred replacement B is a separately deployed .NET Framework 4.8 worker using Microsoft's official `Microsoft.CrmSdk.XrmTooling.CoreAssembly` / `CrmServiceClient`.
- CE 8.2 and CE 9.1 SDK workers are independently version-pinned processes until real-server testing proves that consolidation is safe.

### Temporary Data8 boundary

- `PowerPlatform.Dataverse.Client` in this repository is the third-party Data8 WS-Trust client, not Microsoft-owned source.
- It is temporary compatibility code only.
- The current `OnPremiseClient` implements `IOrganizationService` but not `IDisposable`; the existing `CrmConnectionPool` disposal cast therefore does not prove that its underlying WCF channels/factories are closed.
- The Data8 client must not become the permanent Central or Local Gateway in-process pool implementation.
- Before Gateway migration uses it under load, Data8 must be placed behind a bounded, recyclable Legacy Worker process or receive an independently verified deterministic WCF close/abort ownership fix.
- Worker termination is a fallback cleanup boundary, not a substitute for bounded request lifetime, health checks, process recycling policy, handle/socket baseline tests, and graceful shutdown.

### Embedded boundary

- Embedded remains a reserved execution mode and may retain its existing project and research artifacts.
- New product rollouts use Local Gateway for developer convenience instead of expanding Embedded.
- Embedded development resumes only after Central/Local contract equivalence, real CE 8.2/9.1 validation, aggregate admission, secret isolation, and lifecycle baselines pass.
- Removing Embedded is a separate reviewed decision; it is not implied by choosing Local Gateway first.

### Source documentation and text encoding

- Every newly added or substantively modified Production, Test, Tool, or Script type and file must contain complete, in-depth, maintainable Traditional Chinese documentation. C# uses XML documentation; PowerShell uses comment-based help plus nearby Traditional Chinese block comments where Windows PowerShell 5.1 parsing makes single-line comments unsafe.
- Every newly added or substantively modified public or internal method and lifecycle member that performs routing, admission, authentication, generation replacement, cancellation, timeout handling, rollback, draining, disposal, worker control, or resource ownership must contain detailed Traditional Chinese documentation. Non-obvious branches and ordering constraints require nearby Traditional Chinese implementation comments that explain why the order is safety-critical.
- Comments must explain responsibility, trust boundary, the unique owner, concurrency invariants, fail-closed behavior, cancellation and timeout propagation, rollback/drain/dispose/cleanup ordering, and performance/memory trade-offs rather than merely translate the syntax or rely only on `<inheritdoc />`.
- The documentation must identify the deterministic cleanup path for clients, handlers, streams, timers, cancellation registrations, semaphores, background tasks, admission permits, runtime leases, worker processes, and every other retained or disposable resource. Missing ownership or cleanup documentation is treated as a possible resource-leak defect, not as a cosmetic documentation issue.
- Newly added or modified source, configuration, test, script, SPEC, and documentation files are stored as UTF-8. The repository `.editorconfig` is authoritative and currently requires UTF-8 without BOM, CRLF-only line endings, and a final CRLF for these file types.
- A missing, superficial, or behavior-inconsistent Traditional Chinese comment on a lifecycle/concurrency/security boundary, invalid UTF-8, a BOM where forbidden, mixed line endings, or a missing final CRLF is a verification failure and blocks review completion.

## 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| `ExecutionMode` is `CentralGateway` or `LocalGateway` with the current enum | Startup validation fails. Use `Gateway` and select the deployment by endpoint. |
| `ExecutionMode=Gateway` without `ProfileAlias` or absolute HTTPS `Gateway.Endpoint` | Startup fails closed. No outbound CRM traffic. |
| Product JSON contains CRM credentials, token, raw CRM URL, or SDK path | Configuration is rejected and secret scanning fails the build/release gate. |
| Gateway-owned success payload includes `ApprovedWebApiRoot`, CRM hostname, or `/api/data/` base path | Review and release fail. Remove the internal routing metadata while preserving the runtime-owned URI allowlist. |
| Upstream OData payload contains an absolute `@odata.context` or `@odata.nextLink` intended for a product response | Project or consume it server-side through a typed contract; do not pass the absolute CRM URL through by default. |
| Product requests an unknown or unauthorized alias/operation | Reject before profile resolution or outbound Dynamics traffic. |
| Authenticated but unauthorized request uses an invalid Content-Type | Return 403 before media-type validation and before body read. |
| Authorized operation request omits Content-Type or uses a non-approved media type/parameter/charset | Return 415 before request-body I/O, pooled-buffer rent, JSON parsing, executor invocation, or outbound Dynamics traffic. |
| Authorized operation request uses case-insensitive `application/json` with no parameters or one UTF-8 charset | Continue to the existing bounded byte/JSON validation path. |
| CE 8.2 profile selects `WebApi` without successful ADFS OAuth and operation evidence | Profile remains NotReady. No silent Data8 fallback. |
| CE 9.1 profile selects `ServiceClient` without a supported target authentication proof | Profile remains NotReady. |
| Data8 is loaded as an unbounded long-lived Gateway pool client without deterministic disposal proof | Release blocker. Isolate it in a recyclable worker or fix lifecycle ownership first. |
| CE 8.2 and 9.1 SDK assemblies require conflicting versions in one worker | Keep separate version-pinned worker processes. Do not solve by unverified binding redirects. |
| Two aliases/environments resolve to the same physical organization with different admission keys | Startup fails closed until one shared organization capacity entry is configured. |
| Central or Local host loses its runtime-host lease | Stop admitting new work, become NotReady, and drain/cancel within the configured fence. |
| Profile endpoint/version/organization identity does not match expected evidence | Profile remains NotReady; never auto-upgrade or auto-switch versions. |
| Embedded is selected before its trust/admission/lifecycle gates are approved | Startup remains NotReady. |
| A new C# type or lifecycle/concurrency/security method lacks detailed Traditional Chinese documentation | Review fails; add the missing intent, ownership, failure, and cleanup explanation before merge. |
| A changed source/config/test/script/document file is not valid UTF-8 or violates repository encoding rules | Verification fails before build/release completion. |

## 5. Good / Base / Bad Cases

### Good

- Ten products use the same ProductClient. Production points to the Central Gateway endpoint; ChurchReport development points to localhost. Both send the same operation request and receive the same result contract.
- `crm82` uses a temporary isolated Data8 worker while `crm91` uses Web API v9.1. Their clients, credentials, token/WCF state, and pools are separate, while aggregate organization admission is enforced by physical organization identity.
- A future `crm82` profile generation changes from `TemporaryData8LegacyWorker` to `OfficialLegacyWorker` only after real-server validation. The old generation drains and is disposed before removal.
- An authorized caller sends `Content-Type: application/json; charset=UTF-8`; Gateway validates the header before renting a body buffer, then applies the configured byte/depth/member limits.

### Base

- Only Central Gateway is deployed in production. Local Gateway is used by a developer with non-production secret references. Embedded remains compiled but unused.
- CE 8.2 continues through the current SOAP route while the ADFS OAuth proof is incomplete. This is an explicit temporary state with an owner and removal gate.
- An unauthorized caller sends `Content-Type: text/plain`; Gateway returns 403 without reading the stream, so media-type behavior does not become an authorization oracle.

### Bad

- Product A directly references Data8 while Product B directly references `ServiceClient`, each with its own connection string and retry/pool implementation.
- A request selects `crm82`, fails Web API authentication, and silently retries through Data8. This changes transport and security semantics inside one request.
- One singleton pool contains clients for CE 8.2 and CE 9.1 or for multiple credentials/organizations.
- Each Local Gateway assumes its local maximum is independent and collectively overloads the same Dynamics organization.
- A Local Gateway reads production credentials directly from product-owned JSON.
- Gateway accepts `text/plain` or arbitrary `application/*+json` merely because the body happens to parse as JSON, or reads the body before deciding to return 415.

## 6. Tests Required

### Contract and configuration

- Assert Central endpoint and localhost endpoint produce identical ProductClient request payloads and result parsing.
- Assert only `Gateway` and `Embedded` are accepted `DynamicsExecutionMode` values.
- Assert `Gateway` requires a non-empty `ProfileAlias`, absolute HTTPS endpoint, and bounded API prefix.
- Assert product configuration rejects secrets, raw CRM URLs, authorization headers, and transport selection.
- Assert a successful operation envelope preserves only the approved product fields and does not add `ApprovedWebApiRoot`, CRM hostname, or `/api/data/` routing metadata.
- Assert absolute OData context/nextLink annotations are either removed/projected from product payloads or validated and consumed only by the server-side paging implementation.
- Assert unknown/unauthorized aliases and operation IDs fail before outbound transport invocation.
- Assert missing Content-Type, `text/plain`, `application/*+json`, unknown/repeated parameters, and non-UTF-8 charset return 415 with zero body reads and zero executor calls.
- Assert unauthorized/unmapped caller with an invalid Content-Type still returns 403 with zero body reads, proving authorization precedes media-type validation.
- Assert `application/json` comparison is case-insensitive and accepts either no parameter or exactly one UTF-8 charset parameter.
- Assert 415 paths do not rent or return pooled body buffers because ownership never begins, and do not dispose the ASP.NET Core-owned request stream.
- A `WebApplicationFactory` Kestrel boundary fixture configured through `WithWebHostBuilder` must place `http://127.0.0.1:0` on that same `IWebHostBuilder` through `WebHostDefaults.ServerUrlsKey`, then call parameterless `UseKestrel()`. In .NET 10 minimal-host tests, `UseKestrel(0)` on the returned derived factory can leave the original factory's `CreateHost` delegate without the port value and silently bind the default `localhost:5000`. Assert the observed listener is not 5000 and run the fixture once while a test-owned listener reserves 5000.

### Isolation and capacity

- Assert `crm82` and `crm91` create different immutable runtime-generation keys and cannot share client/token/WCF state.
- Assert aliases that resolve to the same physical organization share one aggregate admission budget.
- Run concurrent Central plus Local host tests and assert total in-flight Dynamics work never exceeds `AggregateMaxInFlight`.
- After replace-and-drain, assert retired handlers, timers, registrations, queues, worker proxies, and strong runtime references return to baseline.

### CE 8.2 real-server gates

- `WhoAmI` or equivalent identity probe.
- Representative CRUD, Query/FetchXML, paging, and every approved action/function/organization request.
- ADFS authorization, token renewal/restart, or official Legacy Worker WS-Trust reconnect.
- Fault injection for ADFS/CRM timeout, worker crash, malformed response, 429/503, Gateway restart, and profile reload.
- Long-running worker/socket/handle soak proving a stable post-warm-up baseline.

### CE 9.1 real-server gates

- Identity probe, representative operations, paging, actions/functions, batch where used, token renewal, restart, and profile reload.
- Verify direct Web API and any selected official `ServiceClient` authentication mode against the actual target.

### Data8 removal gates

- No project or package reference to the checked-in `PowerPlatform.Dataverse.Client` project.
- No source construction of `OnPremiseClient`.
- No solution entry or reachable WCF/WS-Trust dependency retained solely for Data8.
- All CE 8.2 and 9.1 real-server, isolation, lifecycle, rollback, and operation-coverage gates pass through replacement adapters.

### Documentation and encoding gates

- Enumerate every newly added or substantively modified Production/Test/Tool/Script file, type, method, and lifecycle member. Assert that C# uses substantive Traditional Chinese XML documentation, PowerShell uses comment-based help, and critical ordering branches contain explanatory Traditional Chinese comments covering ownership and failure consequences.
- Decode every added or modified source/config/test/script/SPEC/document file with a strict UTF-8 decoder; fail on invalid byte sequences, UTF-8 BOM, bare LF, bare CR, a missing final CRLF, or Unicode replacement characters.
- Verify `.editorconfig` still applies `charset = utf-8` and CRLF to the changed file types, run the changed-program Traditional Chinese comment audit, and run `git diff --check` to reject whitespace or line-ending damage.

## 7. Wrong vs Correct

### Wrong: invent deployment-specific execution modes

```json
{
  "ExecutionMode": "LocalGateway",
  "ProfileAlias": "crm91"
}
```

This contradicts the current `DynamicsExecutionMode` contract and duplicates deployment topology in the product API.

### Correct: keep one Gateway contract and change the endpoint

```json
{
  "ExecutionMode": "Gateway",
  "ProfileAlias": "crm91",
  "Gateway": {
    "Endpoint": "https://localhost:7244/",
    "ApiPrefix": "/v1"
  }
}
```

The same product build can point to Central or Local Gateway without changing business code or CRM transport semantics.

### Wrong: pool every CRM client behind one singleton

```csharp
static readonly List<IOrganizationService> SharedConnections = new();
```

This can mix organizations, versions, identities, SDK binaries, WCF channels, and lifecycle ownership.

### Correct: isolate profile generations and share only capacity authority

```text
Central crm82 runtime pool  ----\
Local A crm82 runtime pool  -----+--> one OrganizationAdmissionKey / aggregate budget
Central crm91 runtime pool  ----/     (only when they resolve to the same physical organization)
```

Physical clients remain process/profile-generation owned. Only the bounded organization admission authority is shared.

### Wrong: keep Data8 as the permanent .NET 10 pool foundation

```text
Product -> Data8 OnPremiseClient singleton -> CE 8.2 and CE 9.1
```

### Correct: use a temporary isolated legacy boundary with explicit exit gates

```text
Product -> Gateway contract -> crm82 adapter -> recyclable Data8 Legacy Worker -> CE 8.2
                                      later -> official worker or proven Web API
```

The Data8 dependency is retained only while the current CE 8.2 IFD replacement is not yet proven.

### Wrong: comment only what the syntax already says

```csharp
// 釋放資源
await runtime.DisposeAsync();
```

This does not explain the owner, required ordering, or failure consequence.

### Correct: document the lifecycle invariant in Traditional Chinese

```csharp
// 必須先等待目前 Generation 的執行租約歸零，才能回收 Handler 與 Token Provider；
// 若提早 Dispose，仍在執行的要求可能使用已釋放的 Socket、Token 或 CancellationTokenSource。
await runtime.DrainAndDisposeAsync(cancellationToken);
```

The comment records the safety contract that future maintainers must preserve, and the containing method/type also carries complete Traditional Chinese XML documentation.

### Wrong: parse JSON regardless of the declared media type

```csharp
var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
```

This accepts caller-controlled non-JSON media types, begins stream/buffer ownership before the HTTP contract is validated, and can expose different body-parser behavior before the intended boundary.

### Correct: authorize first, then validate Content-Type before body I/O

```csharp
var authorization = operationAuthorizer.Authorize(httpContext.User, alias, operationId);
if (!authorization.Succeeded)
{
    return Results.Forbid();
}

var bodyRead = await bodyReader.ReadAsync(httpContext.Request, cancellationToken);
if (bodyRead.Status == GatewayOperationRequestBodyReadStatus.UnsupportedMediaType)
{
    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
}
```

The reader performs the strict JSON-only header check before `Request.Body.ReadAsync` or `ArrayPool<byte>.Rent`; therefore unauthorized callers still receive 403, while authorized invalid media types fail with 415 without acquiring body-lifecycle resources.

## Scenario: Multi-Profile Runtime Admission, Publication, and Rollback

### 1. Scope / Trigger

This scenario applies whenever Local or Central Gateway initializes multiple profile aliases, replaces an immutable profile generation, admits queued work, or rolls back partially acquired runtime/admission resources.

### 2. Signatures

```csharp
Task InitializeAsync(CancellationToken cancellationToken = default);

Task ReplaceAsync(
    DynamicsProfileDefinition definition,
    CancellationToken cancellationToken = default);

Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
    DispatchEnvelope envelope,
    CancellationToken cancellationToken);
```

The acquired execution boundary is one `IProfileExecutionLease` that owns both the selected runtime execution lease and the organization admission permit.

### 3. Contracts

- Alias resolution occurs before secret, factory, token, admission, or transport I/O. Unknown or unavailable aliases fail closed.
- Queue wait may retain only the bounded dispatch envelope, the entry-resolved admission manager, and its immutable plan. It must not retain an active runtime, client, handler, token provider, credential, user/session state, or generation reference.
- After admission succeeds, the manager resolves the current active runtime and verifies the same admission-manager identity, canonical organization key, and configuration digest before acquiring a runtime execution lease.
- A runtime is publishable and Gateway may report it Ready only after `EnsureHostSlotAsync` completes successfully for that runtime's canonical organization plan.
- Initialization publishes the catalog only after every candidate runtime is validated. Failure disposes all candidates, clears any partially published slot, marks the manager NotReady, resets the initialization task, and permits a later retry.
- A replacement publishes the new runtime and calls `BeginDrain` on the old runtime atomically under the catalog lock. A third active/draining generation is rejected before factory allocation.
- `ReplacementInProgress` is the single asynchronous lifecycle owner for one alias. A later `ReplaceAsync` may adopt a Draining runtime left by a completed/failed prior owner, but it must finish or retry that exact runtime's cleanup before incrementing the generation or invoking the factory.
- Draining-reference cleanup is determined by the runtime's terminal state and exact object identity, not by whether `DrainAndDisposeAsync` returned successfully. If the runtime reached `Disposed`, clear the exact slot reference even when cleanup reports an error, and propagate that error. If it remains `Draining`, retain the reference for a later replacement or manager shutdown; never orphan it.
- Replacement drain waits use the caller plus manager-shutdown linked cancellation token. Shutdown ends the current replacement owner promptly, then the manager's final dispose owner takes over every still-owned Active/Draining runtime without disposing resources that still have execution leases.
- Rollback follows reverse ownership order. Every acquired resource is attempted even if an earlier cleanup fails. The original operation failure remains the first reported cause; cleanup failures are aggregated rather than replacing it.
- Combined execution-lease disposal releases runtime execution ownership before returning organization capacity. Both releases are attempted and observed.
- Initialization must publish its task ownership before synchronously completing factories or test doubles can enter failure/reset logic. An implementation may use an explicit initial asynchronous boundary or another proven task-publication mechanism; it must have a regression test for retry after synchronous failure.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Unknown alias | Return sanitized NotReady before admission/factory/token/transport work. |
| Admission succeeds but active runtime cannot be acquired | Dispose the permit before returning sanitized NotReady. |
| Runtime acquisition throws after creating a lease | Attempt runtime-lease disposal, then permit disposal; aggregate cleanup failures with the original acquisition failure. |
| Initial profile N fails after earlier candidates were created | Dispose every candidate, clear partial catalog state, reset initialization ownership, remain NotReady, and allow retry. |
| Candidate disposal also throws | Preserve the initialization failure and every cleanup failure; state rollback still completes. |
| Host slot cannot be acquired | Candidate is never published and Gateway remains NotReady. |
| Replacement already in progress or one generation is draining | Reject before creating another client/token/handler graph. |
| A concurrent replacement owner is active | Reject before factory allocation. Do not allow two replacement owners to drain or publish the same alias concurrently. |
| A prior replacement owner exited while its old runtime remains `Draining` | The next replacement first retries that exact drain. Generation allocation and factory creation remain blocked until the slot no longer owns it. |
| Drain cleanup throws after the runtime reached `Disposed` | Remove only the exact disposed `slot.Draining` reference, propagate the cleanup error, retain the new Active runtime, and allow a later replacement. |
| Drain wait is cancelled or times out while the runtime remains `Draining` | Preserve the slot reference and report the failure. A later replacement or manager shutdown must be able to retry cleanup before any third runtime graph is allocated. |
| Manager shutdown begins during a published replacement drain | Linked cancellation ends the replacement lifecycle owner; final manager disposal retains ownership and completes cleanup after active leases are released. |
| Queue wait overlaps generation replacement | Resolve and use the new compatible active generation after dequeue; never keep the old runtime alive through the queue. |

### 5. Good / Base / Bad Cases

- Good: a queued `crm91` request waits only on the shared admission manager, `crm91` is replaced, and the request acquires generation 2 after the permit becomes available.
- Base: one initialization factory fails, all earlier candidate runtimes dispose successfully, the original error is preserved, and a later operator retry initializes a new generation.
- Good failure handling: runtime-lease cleanup throws, permit cleanup still executes, active execution count and active permits both return to zero, and the caller receives an aggregate containing both causes.
- Good recovery: generation 1 reaches `Disposed` but reports a cleanup error after generation 2 is published; the caller observes the error, the catalog retains only generation 2, and a later replacement can create generation 3.
- Base recovery: generation 1 remains `Draining` after caller cancellation; a later replacement waits for generation 1 to finish, does not call the factory while it is pending, then creates generation 3 and drains generation 2.
- Bad: queueing captures `slot.Active` or `IDynamicsWebApiClient`; the old generation cannot drain until queued work dispatches or times out.
- Bad: a catch block awaits runtime-lease disposal and exits on that exception before returning the admission permit.
- Bad: candidate cleanup throws before `_ready` and initialization-task ownership are reset, permanently pinning a failed initialization task.
- Bad: a catch/finally block always clears `slot.Draining`; caller cancellation can orphan a still-live handler/token graph. The inverse is also bad: clearing only after a successful await permanently retains a runtime that reached `Disposed` but reported cleanup failure.

### 6. Tests Required

- Assert unknown aliases do not increase factory, admission, token, or transport invocation counts.
- Assert every initial profile completes host-slot acquisition before manager readiness becomes true.
- Hold a queue permit, replace the runtime, release the blocker, and assert the queued request executes only on the new generation.
- Inject runtime acquisition failure after lease creation plus runtime-lease disposal failure; assert the original error and cleanup error are both reported, runtime active count is zero, and admission active permits are zero.
- Inject a later-profile factory failure plus an earlier-candidate disposal failure; assert both errors are reported, the snapshot is NotReady and empty, and a second initialization succeeds with new generations.
- Inject a drain cleanup failure after the old runtime reaches `Disposed`; assert the error is reported, the disposed generation disappears from the manager snapshot, and a later replacement creates the next generation successfully.
- Cancel a published replacement while the old runtime still has an execution lease; assert the old runtime remains `Draining`, a later replacement does not increase factory creation count while waiting, and factory allocation occurs only after the lease is released and cleanup completes.
- Repeat the cancelled-drain retry through the production `DynamicsProfileRuntimeFactory` and `DynamicsProfileRuntime`, not only a fake. The test must prove a faulted/cancelled cached drain task is cleared while state remains `Draining`, so the manager's next retry creates a new drain attempt and eventually releases transport, token-provider, and admission-registration ownership.
- Begin manager shutdown while a published replacement is waiting on an old execution lease; assert the replacement observes shutdown cancellation promptly, manager disposal remains pending until the lease is released, and every runtime disposes exactly once.
- Repeatedly replace and dispose under load; assert at most active plus one draining runtime exists and retired runtime/handler/token/registration weak references return to baseline.

### 7. Wrong vs Correct

#### Wrong

```csharp
var runtime = slot.Active;
var permit = await admission.AcquireAsync(envelope, cancellationToken);
// The queued state machine now strongly retains the old runtime.
```

```csharp
catch
{
    await runtimeLease.DisposeAsync();
    await permit.DisposeAsync(); // skipped if the first cleanup throws
    throw;
}
```

#### Correct

```csharp
var permit = await admission.AcquireAsync(envelope, cancellationToken);
// Resolve current active runtime only after queue admission succeeds.
```

```csharp
catch (Exception originalFailure)
{
    var failures = new List<Exception> { originalFailure };
    await CaptureCleanupFailureAsync(runtimeLease, DisposeRuntimeLeaseAsync, failures);
    await CaptureCleanupFailureAsync(permit, DisposePermitAsync, failures);
    ThrowOriginalOrAggregate(failures);
}
```

```csharp
try
{
    await runtime.DrainAndDisposeAsync(linkedCancellationToken);
}
finally
{
    // Only a terminal runtime may leave the catalog. Preserve unfinished draining ownership.
    if (runtime.State == DynamicsProfileRuntimeState.Disposed)
    {
        ClearExactDrainingReference(runtime);
    }
}
```

## Scenario: ChurchReport Session-Owned Donation Resource Lifecycle

### 1. Scope / Trigger

This scenario applies when ChurchReport creates or reuses `DonationPaymentManager` from ASP.NET Session state, when logout/re-login resets identity, when `IMemoryCache` evicts a generation, or when the ChurchReport host stops. It also applies when Local Gateway preflight is enabled from the ChurchReport primary DI container.

### 2. Signatures

```csharp
SessionScopedResourceLease<TResource> AcquireForSessionRequest(
    HttpContext httpContext,
    ISession session,
    Func<TResource> factory,
    TimeSpan absoluteExpiration,
    TimeSpan slidingExpiration);

bool DrainSessionResourceScope(ISession session);

int ActiveEntryCount { get; }
int OutstandingLeaseCount { get; }
int CleanupFailureCount { get; }
```

ChurchReport obtains `DonationPaymentManager` only through `InMemoryDataContextSmallGroup.DonationPaymentManager`. The context must call `AcquireForSessionRequest`; it must not separately read the scope and later call `AcquireForRequest`.

### 3. Contracts

- The Session stores one random 256-bit Base64Url scope. The scope is not a Session ID, user ID, LINE ID, account, token, credential, endpoint, or pool key.
- Scope creation, scope lookup, generation publication, and request-lease publication use the same bounded stripe as `DrainSessionResourceScope`. Once logout/re-login acquires that stripe, an earlier request cannot later publish a generation under the retired scope.
- A cache entry owns visibility; a request lease owns in-flight use. Eviction or identity reset removes visibility first. The last lease return becomes the unique cleanup owner.
- Cache invalidation detected before the framework eviction callback completes must remove the stale slot and restart acquisition on a newly registered slot. Publishing on a removed slot is forbidden.
- A no-slot drain is a linearized no-op. It must not call `IMemoryCache.Remove`, because a later generation may already have been published after the no-slot observation.
- `DonationPaymentManager` disposes only its self-created LINE client and semaphore. Factory/DI-owned CRM utilities and workflows remain owned by their original containers.
- If resource cleanup throws, the entry remains strongly owned in `CleanupFailed`, `ActiveEntryCount` does not decrease, and `CleanupFailureCount` increases. A later serialized host `Dispose` may retry that exact entry. Active reaches zero only after cleanup succeeds.
- ChurchReport legacy controllers may construct `InMemoryDataContextSmallGroup` manually. Therefore the approved lease-return contract is response `OnCompleted` plus `RegisterForDispose`, both targeting one idempotent lease. The context itself is not the authoritative lease owner and must not release a request-shared lease early.
- `DynamicsGatewayPreflightHostedService` executes bounded `runtime.health.whoami` only when `DynamicsAccess:Package01FeeReadsEnabled=true` and mode is `Gateway`. Disabled and Embedded paths are strict no-ops. The process host is a primary-DI singleton and owns the only ProductClient provider/HTTP generation.
- Other legacy `InMemoryDataContextSmallGroup` Session cache properties currently hold data managers that do not implement `IDisposable`; several managers reference the same process-wide `ToolUtilityFactory` singleton. Their eviction callbacks must not dispose `subValue` or the shared `ToolUtilityClass`, because that would allow one Session eviction to invalidate CRM state used by other Sessions.
- The process-wide legacy `ToolUtilityFactory` currently has only an internal test reset and no proven Production host-shutdown owner. This is a pre-existing Phase 6 lifecycle/removal blocker: either retire the singleton behind Gateway or add one host-owned deterministic cleanup path before final release. Session cache modernization must not pretend to solve that process owner by disposing shared dependencies from an eviction callback.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Scope is not 43-character Base64Url | Fail before factory, Session clear, cache publication, or logging identity data. |
| Logout sees no slot while a later request publishes | Logout no-op does not remove the later cache generation. Session-bound acquisition prevents earlier requests from publishing after identity reset linearizes. |
| Cache value is gone but callback is delayed | Old entry enters Draining; acquisition restarts from `_slots.GetOrAdd`; no orphan generation is published. |
| Host stop begins after factory returns but before cache publication | Reject publication, place the created resource into the normal cleanup state machine, and retain failed cleanup for retry. |
| Resource `Dispose` fails | Propagate or trace the failure according to caller context, keep Active nonzero, retain the exact entry, and retry only through a serialized cleanup owner. |
| Main-DI coordinator is missing | Fail closed before constructing Donation manager/LINE/CRM resources. No static or `ConditionalWeakTable` fallback. |
| Gateway feature flag is false | Do not bind/resolve executor, create provider/HTTP resources, or send preflight traffic. |
| Gateway WhoAmI fails or times out | Block host readiness with a sanitized exception; do not fall back to Embedded, Central Gateway, Data8, or another profile. |
| A legacy Session cache entry referencing shared `ToolUtilityClass` is evicted | Drop only the Session-owned wrapper/data reference. Do not dispose the process-wide singleton from the callback. |
| Host stops while the legacy `ToolUtilityFactory` singleton is initialized | Final Phase 6 readiness must prove one process owner deterministically disposes or removes that shared CRM/file/trace graph; finalizer/process exit alone is insufficient. |

### 5. Good / Base / Bad Cases

- Good: a request holds a Donation lease, logout removes the opaque scope and drains visibility, the request completes, and only the final lease return disposes the manager.
- Good: cleanup fails once, Active remains one, and a later host drain retries the same resource successfully before Active becomes zero.
- Base: a legacy controller manually creates a context; response completion returns the request lease even though scoped DI disposal never runs.
- Bad: read the scope, release the identity-reset lock, then publish the resource. Logout can complete in the gap and the old scope can reappear.
- Bad: decrement Active in a `finally` even when `resource.Dispose()` throws. This produces a false clean baseline and loses deterministic ownership.
- Bad: create a fallback coordinator outside the primary DI container. Host shutdown cannot prove that its cache entries and callbacks were drained.
- Bad: change every legacy eviction callback to `(subValue as IDisposable)?.Dispose()`. Some cached managers reference the same process-wide ToolUtility singleton, so one Session could break every other Session.
- Base debt: legacy cache `Get`-then-`Set` may duplicate short-lived wrapper/data creation under concurrency, but the entries are bounded by Session-key expiry and do not create a distinct ToolUtility connection graph. Migrate them only with explicit ownership tests.

### 6. Tests Required

- Race a no-slot drain with a later cache publication and assert factory count stays one.
- Remove cache visibility while delaying eviction callbacks; assert the next two acquisitions share generation two and no generation three is created.
- Hold the Session-bound factory, start identity reset, and assert reset cannot complete until generation/lease publication finishes.
- Inject cleanup failure on final lease return; assert Active remains one, failure count increments, and later host drain retries the same resource.
- Stop the host after factory creation but before publication; assert failed pre-publication cleanup remains owned and retryable.
- Execute the real `Logout` action and real re-login initialization method; assert both call drain before `Session.Clear`, preserve in-flight leases, and return to baseline.
- Run the full ChurchReport tests, Dynamics non-live tests, Release solution build, scoped format verification, UTF-8/no-BOM/CRLF/final-CRLF gate, `git diff --check`, and added-line sensitive-data scan.
- Before migrating another legacy Session cache property, assert whether its value owns disposable resources or only references a process/DI-owned dependency. Cover concurrent first access, eviction, logout, and host stop without cross-Session use-after-dispose.
- Before Phase 6 completion, add a Production host-shutdown assertion proving the legacy ToolUtility singleton is deterministically disposed exactly once, or prove the singleton and its direct product references have been removed.

### 7. Wrong vs Correct

#### Wrong

```csharp
var scope = coordinator.GetOrCreateResourceScopeId(session);
// Logout can clear the Session here.
return coordinator.AcquireForRequest(httpContext, scope, factory, absolute, sliding);
```

```csharp
try
{
    resource.Dispose();
}
finally
{
    Interlocked.Decrement(ref _activeEntryCount);
}
```

#### Correct

```csharp
return coordinator.AcquireForSessionRequest(
    httpContext,
    session,
    factory,
    absolute,
    sliding);
```

```csharp
try
{
    resource.Dispose();
    MarkDisposedAndDecrementActive();
}
catch
{
    RetainFailedCleanupOwner();
    throw;
}
```

```csharp
// Legacy Session cache eviction may release only Session-owned wrapper/data state.
// It must not Dispose ToolUtilityFactory.GetInstance(), which is shared across Sessions.
RemoveSessionOwnedReferenceOnly();

// The separate process/host owner must retire or deterministically dispose the shared legacy graph.
await processLifetimeOwner.DisposeSharedLegacyRuntimeAsync().ConfigureAwait(false);
```

## Scenario: Local Gateway Development Configuration And Safe Runtime Verification

### 1. Scope / Trigger

This scenario applies when Visual Studio starts `SpeechMessage.Dynamics.Gateway` and ChurchReport under the `Development` environment, when a compiled Host DLL is executed directly for local verification, or when WinRM readiness/administration is attempted against the DC or Dynamics application VM. It defines the fail-closed Local Gateway configuration, durable single-machine control-plane ownership, product-to-Gateway boundary, browser smoke evidence, safe remote-administration gate, and the exact limit of what that evidence proves. It does not authorize real CE traffic or Phase 5 consumer migration.

### 2. Signatures

```text
SpeechMessage.Dynamics.Gateway/appsettings.Development.json
  ConnectionStrings:DynamicsControlPlane
  DynamicsGateway:Profiles[*]:ApprovedWebApiRoot
  DynamicsGateway:ActiveWorkloadBindingSet = Local
  DynamicsGateway:WorkloadBindingSets:Local[*]

SpeechMessage.Dynamics.Gateway/appsettings.json
  DynamicsGateway:ActiveWorkloadBindingSet = Central
  DynamicsGateway:WorkloadBindingSets:Central[*]

SpeechMessageProducts.ChurchReport/appsettings.Development.json
  DynamicsAccess:ExecutionMode
  DynamicsAccess:ProfileAlias
  DynamicsAccess:CeVersion
  DynamicsAccess:Gateway:Endpoint
  DynamicsAccess:Gateway:ApiPrefix
  DynamicsAccess:Package01FeeReadsEnabled

GET /health
GET /ready
POST /v1/{profileAlias}/operations/{operationId}

# Direct DLL verification runs from the owning project content root.
cd SpeechMessage.Dynamics.Gateway
dotnet .\bin\Debug\net10.0\SpeechMessage.Dynamics.Gateway.dll --urls https://localhost:7244

cd ..\SpeechMessageProducts.ChurchReport
dotnet .\bin\Debug\net10.0\SpeechMessageProducts.ChurchReport.dll --urls http://localhost:5080

# Remote administration uses DNS plus an already approved Negotiate identity.
New-PSSession -ComputerName <approved-dns-name> -Authentication Negotiate
```

### 3. Contracts

- Development Gateway durable coordination uses the explicitly provisioned same-Windows-user LocalDB instance and a dedicated `SpeechMessageDynamicsControlPlane` database. The connection uses integrated authentication, bounded pool size, and bounded connect timeout. Gateway startup validates the schema; it does not connect to Dynamics native SQL, auto-create the database, or fall back to in-memory coordination.
- The checked-in Development CRM target remains deliberately non-routable. A permitted operation against it must fail in a controlled, sanitized way without falling back to Central Gateway, Embedded, Data8, another alias, or a production endpoint.
- ChurchReport Development uses `ExecutionMode=Gateway`, `ProfileAlias=crm82`, `CeVersion=8.2`, HTTPS loopback, and API prefix `/v1`. `Package01FeeReadsEnabled=false` remains the authoritative consumer-traffic gate.
- Feature-disabled ChurchReport startup must not create ProductClient, HTTP handler/pool, token cache, timer, or Dynamics preflight/operation traffic. Development configuration alignment alone does not enable Package 1.
- Local Gateway authentication uses server-established Windows Negotiate identity plus server-owned workload bindings. Client JSON and spoofable headers never select principal, workload, alias permission, or operation permission.
- A syntactically valid authenticated Windows SID is authoritative. When it is present, authorization performs only the SID lookup; an unmapped SID fails closed and must not fall back to a matching principal name. Exact principal-name fallback is allowed only when the authenticated principal has no usable SID at all. This prevents a newly created account with the same name but a different SID from inheriting the retired account's workload permissions.
- `DynamicsGateway:ActiveWorkloadBindingSet` is the deployment-owned selector and is mandatory. The authorizer enumerates direct children under `DynamicsGateway:WorkloadBindingSets`, resolves exactly one case-insensitive matching set, and materializes only that set. It must not concatenate the selector into a configuration path or enumerate all sets.
- Central, Local, and Testing binding sets may coexist in the merged configuration because they are separate named subtrees. `appsettings.Development.json` changes only the selector to `Local`; therefore .NET configuration's numeric-array and nested-leaf merge behavior cannot import a Central principal or Central operation into the Local frozen authorization snapshot.
- An empty, whitespace, wildcard, unknown, ambiguous, scalar-only, or childless active set is a startup failure before the listener, secret resolution, admission, executor, or outbound transport. There is no fallback to `Central`, the first set, the base provider, or the union of all sets.
- The retired `Invoke-AdfsTokenProbe.ps1` is a fixed fail-closed compatibility entrypoint. It accepts no credential/token/result parameters, reads no appsettings, performs no network or file output, and directs operators to the existing Public Client authorization-code diagnostic flow.
- Runtime verification artifacts may record HTTP status categories, test counts, readiness state, JavaScript error count, and sanitized policy outcomes. They must not persist credentials, tokens, passwords, Session identifiers, client identifiers, callback values, private VM addresses, complete AD FS/CRM endpoints, or secret-reference values.
- Raw workload-binding arrays at one shared configuration path are forbidden. .NET configuration merges arrays and nested lists by numeric leaf key; changing index `1` to `0` can still retain base `CapabilityOperationIds:1..N`. Named sets plus one strict selector are the required replacement boundary.
- A compiled ASP.NET Core DLL resolves `appsettings.json`, `appsettings.{Environment}.json`, content files, and relative configuration from its content root. Local verification must set the process working directory to the owning project directory or pass an explicit reviewed content root. Running the Gateway DLL from the solution root can omit its profile configuration and produce a misleading fail-closed profile-URI startup exception; do not weaken validation or edit deployment JSON to compensate for the wrong content root.
- WinRM mutation requires an authenticated administrative owner obtained from an already approved Kerberos/Negotiate session or credential store. Every `PSSession` is removed in `finally`, credential/session variables are cleared, and pre-state plus exact rollback are captured before mutation. Basic authentication, `AllowUnencrypted=true`, broad `TrustedHosts`, repeated password attempts, and persisted `PSCredential` or remote object graphs are forbidden.
- If the caller is not domain joined, is not elevated, has no approved credential/session, or cannot authenticate to the target, verification stops at DNS/TCP/WSMan identify probes. An existing insecure local WinRM client pre-state may be reported as a blocker, but it must not be used as an authorization path or silently changed without the required administrative owner.
- The in-app browser must not bypass a self-signed HTTPS warning or install/trust a development certificate merely to make a smoke test green. A local CLI probe may explicitly ignore the development certificate only for bounded loopback status verification; browser evidence remains limited to pages reachable through the browser's normal trust policy. Production Gateway evidence requires a deployment-trusted certificate.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| LocalDB schema/database is absent or inaccessible | `/ready` remains unavailable; no in-memory or Dynamics-SQL fallback. |
| Anonymous request reaches `/v1` | Return 401 before body parsing, CRM work, token acquisition, or queue allocation. |
| Authenticated principal has no workload binding | Return 403 without alias/operation execution. |
| Authenticated principal supplies a valid SID that has no binding, while its principal name matches a configured binding | Return 403 `unmapped-principal`; do not fall back to the name and do not create executor, admission, secret, token, or outbound transport work. |
| Authenticated principal has no usable SID and its exact principal name has a binding | Permit the existing name-compatibility path, subject to the same alias and operation allowlists. |
| Workload requests an unbound alias or unauthorized operation | Return 403 before connector/token/transport work. |
| Authorized operation reaches the non-routable Development CRM target | Return controlled sanitized 4xx; do not fall back to any other transport or endpoint. |
| `Package01FeeReadsEnabled=false` | ChurchReport root may run, but no Package 1 Dynamics traffic or preflight resources are created. |
| Retired AD FS probe is invoked | Fail immediately with fixed guidance; allocate no network, file, timer, background, credential, or token resource. |
| `ActiveWorkloadBindingSet` is missing, blank, contains wildcard text, names no direct child set, names a scalar/empty set, or is otherwise ambiguous | Host startup fails closed; do not start the listener or fall back to another set. |
| A Central principal authenticates against a Development Host whose selector is `Local` | Return 403 `unmapped-principal`; do not resolve an alias, operation, secret, admission permit, executor request, or outbound connection. |
| A compiled Gateway/ChurchReport DLL is started from the solution root without an explicit content root | Startup may fail closed because project appsettings are not loaded. Restart from the owning project directory; do not relax profile validation or copy secrets/configuration into the solution root. |
| WinRM listener responds but no approved authenticated administrative identity is available | Perform only DNS/TCP/WSMan identify probes, remove any temporary session in `finally`, and report the remote mutation gate as blocked. Do not attempt passwords, Basic, unencrypted transport, or `TrustedHosts=*`. |
| Local WinRM client already has Basic or unencrypted transport enabled | Treat it as pre-existing insecure state. Do not use it for the VM operation; hardening requires an elevated, separately owned change with rollback. |
| The browser rejects the local Gateway development certificate | Do not bypass the security interstitial or mutate trust. Preserve CLI HTTPS status evidence and require a trusted certificate for full browser proof. |

### 5. Good / Base / Bad Cases

- Good: Gateway `/health` and durable `/ready` return 200, anonymous `/v1` returns 401, the current Windows workload catalog is authorized, wrong alias and unauthorized operation return 403, and the sole allowed operation fails against the non-routable target with a sanitized controlled response.
- Good: ChurchReport and Local Gateway run concurrently; the ChurchReport login page reaches `readyState=complete`, JavaScript error count is zero, and both processes stop with their listeners released.
- Good: Base and Development JSON both remain loaded, but `ActiveWorkloadBindingSet=Local` causes the authorizer to materialize only `WorkloadBindingSets:Local`; a Central principal and every Central-only data operation remain unavailable.
- Good: a principal presents SID-B and name X while only SID-A/name X was previously authorized; SID-B is unmapped, so the request receives 403 and cannot inherit the old workload by name.
- Base: Central, Local, and Testing sets coexist as deployment data, while exactly one selector is active for one Host generation. Changing the selector requires configuration replacement plus Host restart/replace-and-drain; it is never a request-time switch.
- Base: a legacy authenticated principal has no usable SID claim but has an exact configured principal name; name fallback remains available without wildcard, prefix, substring, or caller-header matching.
- Base: read-only AD FS administration proves exactly one Public Client and callback plus approved description markers without printing their actual values.
- Bad: replace the Development CRM target with a routable production URL merely to make a smoke test green.
- Bad: set `Package01FeeReadsEnabled=true` to force preflight evidence before real CE 8.2/9.1 and rollback gates exist.
- Bad: define Central and Local entries under one `WorkloadBindings` array and assume a later provider replaces the collection; numeric leaf merging can preserve both entire bindings and nested operation entries.
- Bad: a valid but unmapped SID is allowed to continue into principal-name lookup. Account-name reuse can then grant a different Windows security authority the old account's alias, operation, capacity, and audit identity.
- Good: direct DLL verification runs from each project's directory, the Gateway and ChurchReport both reach health, and cleanup stops only listener owners whose command lines match the expected DLLs.
- Good: WinRM readiness proves DNS, TCP 5985, and WSMan identify without printing target addresses; when authentication is unavailable, no remote mutation or password attempt occurs and the final `PSSession` count is zero.
- Base: the development Gateway certificate is accepted by CLI loopback verification only; the in-app browser validates ChurchReport and the authorization redirect while Gateway browser proof remains gated on certificate trust.
- Bad: run the Gateway DLL from the solution root, observe a missing-profile exception, and modify profile JSON or weaken fail-closed validation instead of correcting the content root.
- Bad: use a pre-existing Basic/unencrypted WinRM client or broaden TrustedHosts to work around missing administrative authentication.

### 6. Tests Required

- Configuration precedence tests assert the LocalDB instance, dedicated control-plane database, integrated authentication, bounded pool, bounded timeout, non-routable CRM target, ChurchReport Local Gateway alias/version/prefix, and Package 1 false state.
- Load real base plus Development JSON, authenticate with the Central binding principal, and assert Local authorization returns `unmapped-principal` with zero executor/outbound work. This regression must fail against a shared `WorkloadBindings` array implementation.
- Authenticate with a syntactically valid but unmapped SID plus a principal name that otherwise matches an authorized binding. Assert 403, `unmapped-principal`, zero executor calls, and no materialized execution request. Separately assert a principal with no usable SID still succeeds through the exact principal-name compatibility binding.
- Assert a missing selector, leading/trailing whitespace, `*` and `?` wildcard text, an unknown name, a delimiter-bearing value such as `Local:0`, scalar-only, scalar-plus-children, and a true childless JSON set all fail Host startup. Assert exact set selection is case-insensitive. Testing factories must select an explicit nonempty `Testing` set rather than inheriting `Central`.
- Execute the opt-in live LocalDB durable coordinator contract against the explicitly provisioned database and assert lease/fencing behavior without auto-provisioning.
- Start the real Development Gateway and verify `/health`, `/ready`, 401 anonymous, authorized workload catalog, 403 wrong alias, 403 unauthorized operation, and controlled no-fallback connector failure.
- Start ChurchReport and Gateway together, use a browser to assert the login page completes with zero JavaScript errors, then stop both hosts and assert both listeners are released.
- Verify the AD FS Public Client/callback/description markers read-only without writing or printing sensitive values.
- Parse the retired PowerShell entrypoint, assert it has no secret/result parameters or network/file code path, and verify it fails closed.
- Run Dynamics tests, ChurchReport tests, Release solution build, changed-file format, strict UTF-8/no-BOM/CRLF/final-CRLF, `git diff --check`, and added-line sensitive-literal scans.
- Start each compiled host from its project content root and assert the 200/200/401/200/403/403/controlled-400 matrix, ChurchReport `readyState=complete`, zero JavaScript errors, and listener count zero after cleanup.
- Add a negative runtime check that starts the Gateway DLL from the wrong content root and proves it fails closed without opening a listener; the correction is the process content root, not a configuration or validation change.
- For WinRM work, assert DNS/TCP/WSMan identify results are sanitized, authenticated mutation is skipped when no approved admin identity exists, no password retry occurs, and the final owned `PSSession` count is zero.

### 7. Wrong vs Correct

#### Wrong

```json
{
  "DynamicsAccess": {
    "Package01FeeReadsEnabled": true
  }
}
```

This couples deployment readiness to consumer migration and can move multiple ChurchReport read paths before real CE, parity, rollback, and soak evidence exists.

#### Correct

```json
{
  "DynamicsAccess": {
    "ExecutionMode": "Gateway",
    "ProfileAlias": "crm82",
    "CeVersion": "8.2",
    "Gateway": {
      "Endpoint": "https://localhost:7244",
      "ApiPrefix": "/v1"
    },
    "Package01FeeReadsEnabled": false
  }
}
```

This configures the Local Gateway boundary for development while keeping consumer traffic fail closed until Phase 4 and Phase 5 evidence explicitly unlock it.

#### Wrong: array overlay for authorization

```json
{
  "DynamicsGateway": {
    "WorkloadBindings": {
      "1": {
        "PrincipalName": "LOCAL-PRINCIPAL",
        "CapabilityOperationIds": ["runtime.health.whoami"]
      }
    }
  }
}
```

This appends or partially overwrites numeric leaf keys. It does not prove that base index `0` or nested operation indices were removed.

#### Correct: named sets with one strict selector

```json
{
  "DynamicsGateway": {
    "ActiveWorkloadBindingSet": "Local",
    "WorkloadBindingSets": {
      "Local": [
        {
          "PrincipalName": "LOCAL-PRINCIPAL",
          "WorkloadSubjectId": "local-workload",
          "ProfileAliases": ["crm82"],
          "CapabilityOperationIds": ["runtime.health.whoami"]
        }
      ]
    }
  }
}
```

The Host validates the selector once, materializes only the Local subtree into frozen dictionaries, and fails startup if the selected set is invalid or empty.

#### Wrong: treat an unmapped valid SID as permission to try the account name

```csharp
if (!_bindingsByWindowsSid.TryGetValue(windowsSid, out var binding))
{
    _bindingsByPrincipalName.TryGetValue(principal.Identity.Name, out binding);
}
```

This changes identity authority after a SID lookup failure. A replacement account can reuse the same name while having a different SID and incorrectly inherit the previous workload binding.

#### Correct: a present valid SID is the only lookup authority

```csharp
var windowsSid = TryGetAuthenticatedWindowsSid(principal);
if (windowsSid is not null)
{
    _bindingsByWindowsSid.TryGetValue(windowsSid, out var sidBinding);
    return sidBinding;
}

// Exact name compatibility is available only when the principal has no usable SID.
```

An unmapped SID returns `null`, so authorization fails before alias/operation execution. The name path remains available only for authenticated environments that genuinely provide no usable SID.

#### Wrong: direct DLL execution from the solution root

```powershell
dotnet .\SpeechMessage.Dynamics.Gateway\bin\Debug\net10.0\SpeechMessage.Dynamics.Gateway.dll
```

The process content root is the solution directory, so project appsettings may be absent even though the DLL path is correct.

#### Correct: bind the process to the owning project content root

```powershell
Push-Location .\SpeechMessage.Dynamics.Gateway
try {
    dotnet .\bin\Debug\net10.0\SpeechMessage.Dynamics.Gateway.dll --urls https://localhost:7244
}
finally {
    Pop-Location
}
```

The Host loads the reviewed project configuration and keeps the same fail-closed validation. The runtime verifier records the listener owner and stops only that exact DLL owner during cleanup.

#### Wrong: compensate for missing WinRM authentication with an unsafe client path

```powershell
Set-Item WSMan:\localhost\Client\TrustedHosts -Value '*'
Set-Item WSMan:\localhost\Client\AllowUnencrypted -Value $true
```

#### Correct: require approved Negotiate authentication and deterministic cleanup

```powershell
$session = $null
try {
    $session = New-PSSession -ComputerName $approvedDnsName -Authentication Negotiate -ErrorAction Stop
    Invoke-Command -Session $session -ScriptBlock { Get-Service WinRM }
}
finally {
    if ($null -ne $session) {
        Remove-PSSession -Session $session
    }
    $session = $null
}
```

If session creation fails, stop at read-only WSMan readiness evidence. Do not prompt repeatedly, persist a credential, enable Basic, allow unencrypted transport, or broaden TrustedHosts.

## Design Decisions

### Central Gateway is the production default

Central Gateway centralizes secrets, authorization, operation governance, audit, observability, profile lifecycle, and reusable outbound runtimes for the multi-product estate. This avoids duplicating high-risk integration state across five to ten products.

### Local Gateway replaces Embedded as the immediate developer path

Local Gateway gives Visual Studio a separately observable console/process while preserving the same HTTP boundary as production. It avoids loading CRM transport dependencies into ChurchReport and keeps failures, SDK conflicts, and worker recycling outside the product process.

### Compatibility is provided at the Gateway contract, not by one universal SDK

CE 8.2 and CE 9.1 share the product-facing API and policy model. They do not have to share a transport implementation, SDK version, authentication flow, token/WCF state, or physical connection pool.

### Data8 is retained now and removable later

Deleting Data8 now would break `ToolUtility` and the known-working CE 8.2 WS-Trust path. It becomes removable only after every consumer moves behind Gateway and one proven CE 8.2 replacement satisfies real-server, lifecycle, isolation, and rollback gates.

## Scenario: Explicit live-smoke target and credential-reference boundary

### 1. Scope / Trigger

This scenario applies whenever `docs/scripts/Invoke-DynamicsLiveSmoke.ps1` or an
equivalent operator harness enables a real CE 8.2/9.1 request. A stale default
host or an authentication-different preflight can produce a convincing but false
failure before the connector itself executes. The harness is an operator tool,
not a profile store, secret store, endpoint registry, or alternative transport.

### 2. Signatures

```powershell
.\docs\scripts\Invoke-DynamicsLiveSmoke.ps1 `
  -EnableLive `
  -WebApiRoot '<explicit HTTPS /api/data/v8.2/ or /api/data/v9.1/ root>' `
  [-CeVersion '8.2'|'9.1'] `
  [-CredentialSource HostIdentity|SecretReference] `
  [-ProfileAlias '<explicit alias for fee smoke only>'] `
  [-UserNameSecretName '<environment-variable name>'] `
  [-PasswordSecretName '<environment-variable name>'] `
  [-DomainSecretName '<optional environment-variable name>']
```

`-WebApiRoot` is optional only when live mode is disabled. `-ProfileAlias` is
required only when a `-ContactId` enables the fee operation. `UserName`,
`Password`, and `Domain` values are never command parameters.

### 3. Contracts

- `-EnableLive` without `-WebApiRoot` fails before DNS, HTTPS, CRM, ADFS, or
  connector activity. The script contains no target-specific CRM hostname or
  profile-alias default.
- The harness invokes the connector-owned `WhoAmI` smoke as the authentication
  authority. It does not issue an anonymous or credential-different HTTP `HEAD`
  request and reinterpret its 401/302 result as connector reachability.
- `HostIdentity` clears all `DYNAMICS_SMOKE_*_SECRET` bridge variables before
  starting `dotnet test`, so an earlier interactive `SecretReference` run cannot
  affect the current identity mode.
- `SecretReference` requires explicit, conventional environment-variable names
  for username and password, verifies only their presence, and passes their
  names through `DYNAMICS_SMOKE_USERNAME_SECRET`,
  `DYNAMICS_SMOKE_PASSWORD_SECRET`, and optional
  `DYNAMICS_SMOKE_DOMAIN_SECRET`. It never reads or prints secret values.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Live mode lacks `-WebApiRoot` | Fail closed before any external request. |
| Fee smoke has `-ContactId` but lacks `-ProfileAlias` | Fail closed before `dotnet test` or CRM traffic. |
| `SecretReference` lacks username/password reference names | Fail closed before connector creation or CRM traffic. |
| A supplied reference name is not an environment-variable identifier, or is absent | Fail closed without printing the name's value. |
| `HostIdentity` follows a prior `SecretReference` invocation in one interactive shell | Remove secret bridge variables before test process creation. |
| CRM returns an application 500 | Record it as target-environment evidence; do not change DNS, WinRM, authentication mode, or profile route automatically. |

### 5. Good / Base / Bad Cases

- Good: an operator explicitly supplies the reviewed v9.1 root and receives the
  connector's actual `WhoAmI` result.
- Base: live mode is omitted; the script prints safe usage and exits without
  any network traffic.
- Bad: a script defaults to a historical organization URL and reports a 401
  from an unauthenticated `HEAD` as an IWA/connector failure.
- Bad: a `SecretReference` branch hardcodes environment-variable names tied to
  one organization, making another approved profile silently use the wrong
  identity source.

### 6. Tests Required

- Execute `docs/scripts/Invoke-DynamicsLiveSmoke.Tests.ps1` and assert dry-run
  guidance contains an explicit `-WebApiRoot` placeholder, no historical host,
  and no target-specific secret-reference default.
- Assert `-EnableLive` without `-WebApiRoot` exits nonzero before external
  activity.
- Parse the script under Windows PowerShell 5.1 and run a no-live explicit-root
  dry run; then run the opt-in .NET smoke project with live mode disabled.

### 7. Wrong vs Correct

#### Wrong

```powershell
[string]$WebApiRoot = 'https://historical.example/api/data/v9.1/'
Invoke-WebRequest -Uri $WebApiRoot -Method Head
```

This selects a deployment the operator did not approve and uses a request whose
authentication semantics can differ from the connector.

#### Correct

```powershell
if ($EnableLive -and [string]::IsNullOrWhiteSpace($WebApiRoot)) {
    throw 'Live mode requires an explicit -WebApiRoot. No CRM request was made.'
}

# The connector-owned WhoAmI test is the single authentication verdict.
& dotnet test $project --nologo
```

The target, authentication mode, and resulting evidence remain explicit and
traceable, while a real CRM fault remains an external gate rather than a reason
to weaken the Gateway boundary.

## Scenario: 耐久 SQL 控制平面的整合驗證與帳密拒絕邊界

### 1. Scope / Trigger

當 Gateway、Local Gateway 或測試建立 `SqlRuntimeHostSlotCoordinator` 時，
`ConnectionStrings:DynamicsControlPlane` 是跨 host 容量、fencing token、
AdmissionEpoch 與 quarantine 的控制平面連線，不是 CRM 資料或憑證存放區。
此規則用來阻止 SQL 帳密因設定漂移被保留在 coordinator 的長生命週期狀態中。

### 2. Signatures

```csharp
public sealed class SqlRuntimeHostSlotCoordinatorOptions
{
    public const string RequiredDatabaseName = "SpeechMessageDynamicsControlPlane";

    public string ConnectionString { get; set; }

    public void Validate();
}
```

### 3. Contracts

- `Validate()` 在任何 `SqlConnection`、connection pool、transaction 或背景 SQL
  作業建立以前執行。
- `Initial Catalog` 必須精確為 `SpeechMessageDynamicsControlPlane`；不得連接 CRM
  資料庫。
- 連線字串必須使用 `Integrated Security=true` 的 Windows host identity。
- `User ID` 與 `Password` 欄位一律禁止，即使 SqlClient 在整合驗證時可能忽略它們；
  否則字串仍可能被 runtime、例外或診斷路徑長期保留。
- `SqlRuntimeHostSlotCoordinator` 建構後必須只保留已驗證的 immutable scalar snapshot
  （connection string、command timeout、quarantine）；不得持有可被 DI 或其他元件
  後續修改的 options 物件。組態變更必須建立並驗證新的 coordinator/runtime generation，
  不能原地改寫既有 lease owner。
- Development 的實際 instance 是已佈建的使用者 LocalDB；Central/production 的
  durable backend 若不同，仍必須遵守同一個整合驗證與「無 SQL 帳密」合約。
- 每次 durable host-slot acquire 都必須攜帶已驗證的
  `CanonicalOrganizationCapacityKey`；`LeaseNamespaceId` 只是 deployment 的名稱，
  絕不可由它、主機名、環境名、Session、Token 或 credential 猜測實體 Dynamics
  Organization。
- `RuntimeHostOrganizationBinding` 必須以 binary ordinal (`Latin1_General_100_BIN2`)
  儲存並一對一繫結 `LeaseNamespaceId`、expected Organization GUID 與 normalized
  HTTPS base URI。binding 在 slot release 後仍保留；否則另一個 process 可以改用
  不同 namespace 重新建立同一個 Organization 的完整容量預算。
- admission epoch 必須以外鍵指向 canonical binding。schema migration 若發現舊
  epoch 沒有可信 binding，必須以 `51006` fail-closed，先由 operator drain 並受控
  backfill；不得從 configuration digest 或其他不可逆摘要猜測 Organization 對應。
- Gateway readiness 的 `VerifySchemaAsync` 必須驗證 binding table、兩個 canonical
  unique constraint、epoch foreign key 與所有 string identity column 的 binary
  ordinal collation。runtime release 沒有刪除或重綁既有 binding 的權限。

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Connection string is absent or targets another database | Throw before opening a SQL connection. |
| `Integrated Security` is false | Throw `Windows integrated authentication` validation failure before any pool allocation. |
| Integrated security is true but `User ID` or `Password` is present | Throw `must not contain SQL credential fields` before any connection, log, retry, or background owner can retain it. |
| A caller mutates the original options object after coordinator construction | Existing coordinator ignores the mutation and uses only its validated immutable snapshot. |
| Command timeout or quarantine is outside the bounded range | Throw before coordinator construction succeeds. |
| Dedicated schema is missing | `VerifySchemaAsync` fails closed; Gateway must not auto-provision or fall back to CRM SQL/in-memory coordination. |
| A legacy epoch row has no canonical organization binding during schema migration | Throw SQL `51006`; drain and explicitly migrate the legacy control-plane rows before deployment. |
| A second namespace attempts to bind the same Organization GUID or normalized base URI | Throw SQL `51005`; never split one physical Organization into two host-slot budgets. |

### 5. Good / Base / Bad Cases

- Good: a Development process uses the dedicated LocalDB database with integrated
  authentication and validates the schema before readiness.
- Base: a production durable store uses an approved Windows service/gMSA identity
  and the same no-SQL-credential validation.
- Good: a DI options singleton is accidentally changed after coordinator creation;
  the existing lease owner still uses its original validated scalar snapshot.
- Bad: an otherwise integrated connection string also contains `User ID`; the
  coordinator accepts it because the client library happens to ignore the field.
- Bad: a coordinator stores the mutable options reference and an after-start
  configuration mutation redirects an existing runtime to another database.

### 6. Tests Required

- `Options_reject_sql_authentication_connection_strings` proves non-integrated
  SQL authentication is rejected without opening a connection.
- `Options_reject_sql_user_fields_when_integrated_security_is_enabled` proves a
  stray SQL user field cannot survive merely because integrated security is true.
- `Coordinator_snapshots_validated_options_before_any_connection_attempt` proves
  that a post-construction options mutation cannot alter the active coordinator's
  connection path or leave an active database-operation counter retained.
- Development configuration tests assert the dedicated database, LocalDB target,
  integrated authentication, bounded pool, and bounded connect timeout.
- The opt-in live SQL contract test continues to prove that an approved,
  provisioned LocalDB schema performs lease fencing, quarantine, namespace
  isolation, and deterministic cleanup.
- Contract tests prove that a durable request carries the canonical key, a legacy
  namespace-only SQL acquire fails before opening a connection, and test cleanup
  deletes slot rows, epoch rows, then its uniquely owned canonical binding rows.
- A two-coordinator live contract must prove that, after coordinator A releases
  its slot, coordinator B still cannot bind the same physical Organization to a
  different namespace. This is a durable-store assertion, not a shared-static
  or Session-based test.

### 7. Wrong vs Correct

#### Wrong

```csharp
var options = new SqlRuntimeHostSlotCoordinatorOptions
{
    ConnectionString =
        "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SpeechMessageDynamicsControlPlane;" +
        "Integrated Security=True;User ID=unexpected"
};
options.Validate(); // A stray SQL identity remains in long-lived configuration.
```

#### Correct

```csharp
var options = new SqlRuntimeHostSlotCoordinatorOptions
{
    ConnectionString =
        "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SpeechMessageDynamicsControlPlane;" +
        "Integrated Security=True"
};
options.Validate(); // Validation rejects every SQL credential field before connection ownership begins.
```
