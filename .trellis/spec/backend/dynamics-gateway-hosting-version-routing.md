# Dynamics Gateway Hosting and CE 8.2/9.1 Routing Contract

## 0. Authoritative Microsoft NuGet worker direction

This section supersedes every older Web-API-first, optional-Web-API, or
universal-no-SDK transport statement in this file.

- The only supported transport kinds are `OfficialCrm82Worker` and
  `OfficialCrm91Worker`.
- Each transport is a separately version-pinned .NET Framework 4.8 process
  using Microsoft-published `Microsoft.CrmSdk.XrmTooling.CoreAssembly` /
  `CrmServiceClient` packages.
- Products, ProductClient, Abstractions, Gateway, Embedded, and ordinary tests
  must not reference or load Microsoft CRM SDK assemblies. Only the two explicit
  worker projects and worker-only tests may do so.
- Direct Web API is not a route, fallback, future adapter, readiness gate, or
  Phase 4 prerequisite. Existing WebApi code/scripts/tests are legacy
  replacement inputs pending removal.
- The D365APP01 CRMWeb/IFD HTTP 500, Deployment PowerShell channel, ASP.NET 1309
  events, IFD wizard, and direct Web API `WhoAmI` are not Gateway gates.
- Real-server validation means executing the actual website, Gateway, and
  selected official worker on the intended Windows host, then executing the
  approved Organization operation matrix against CE 8.2 or CE 9.1. That host
  may be a Visual Studio Local Gateway on the developer workstation; a separate
  Central/IIS deployment is not a prerequisite. Local hosting must still use
  the real pinned Worker and Organization Service rather than a fake transport.
- A failed worker request never changes transport, CE version, profile,
  organization, or credential. No automatic Web API or Data8 fallback exists.
- Worker IPC is bounded, length-prefixed, versioned, nonce-bound, and typed. It
  never carries CRM SDK types, arbitrary FetchXML, endpoint/connection strings,
  credentials, tokens, cookies, raw principals, browser sessions, LINE IDs, or
  `HttpContext`.
- Every worker process, `CrmServiceClient`, pipe, stream, timer, cancellation
  registration, request map, semaphore, queue entry, and background task has one
  bounded owner and a deterministic graceful-drain plus forced-termination
  cleanup path.
- Default safe concurrency is one active Organization operation per worker
  until exact package/target stress evidence proves a higher value safe.
  Throughput scales through a bounded worker pool under the shared organization
  admission budget.

## 1. Scope / Trigger

This contract applies whenever a SpeechMessage product:

- calls Dynamics 365 Customer Engagement through `SpeechMessage.Dynamics`;
- selects a shared Central Gateway or a product-local Gateway process;
- adds or changes a CE 8.2 or CE 9.1 organization profile;
- introduces or changes an official CE 8.2/9.1 worker, worker protocol,
  `CrmServiceClient`, worker supervisor, or legacy-removal boundary;
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

Product business code depends on `IDynamicsOperationExecutor` or a typed product client such as `IPackage01FeeReadClient`. It must not depend on CRM SDK, WCF, SOAP, Organization Service URL construction, authentication tokens, worker protocol, or a transport-specific client.

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

The deployment-owned profile contract exposes exactly one supported transport kind:

```text
OfficialCrm82Worker
OfficialCrm91Worker
```

The transport kind is fixed for one immutable profile generation. `WebApi`,
`OfficialServiceClient`, `OfficialLegacyWorker`, and
`TemporaryData8LegacyWorker` are rejected legacy values. The transport cannot
switch per request or silently fall back after a failure.

## 3. Contracts

### Product boundary

- Products know `ExecutionMode`, `ProfileAlias`, Gateway endpoint, API prefix, and typed operation parameters only.
- Product JSON must not contain a CRM organization-service URL, raw CRM Web API URL, username, password, client secret, access token, refresh token, certificate private key, SDK DLL path, worker executable/package path, or transport kind.
- Gateway-owned success envelopes must not serialize CRM hostname, Organization Service endpoint, `/api/data/` base path, credential, token, package path, pipe name, nonce, process ID, or other internal routing/lifecycle metadata.
- Raw SDK/Organization Service response types and upstream absolute URLs are not product-safe. The worker must project them into bounded typed DTOs before serialization; no SDK object crosses IPC.
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
- It owns one bounded worker-process pool per immutable profile generation. `crm82` and `crm91` never share an executable, SDK assembly graph, mutable client, credential, WCF channel, static cache, or session.
- Multiple products may share the same Central Gateway profile runtime only after server-side authorization resolves them to the approved alias and workload policy.

### Local Gateway ownership

- Local Gateway is a separate Windows process started beside one product for Visual Studio development, integration testing, or an explicitly isolated deployment.
- The product still calls localhost through the Gateway HTTP contract; Local Gateway is not Embedded mode.
- Every Local Gateway process owns and deterministically drains, terminates, and disposes its worker-process/pipe/process-handle pool.
- Local Gateway uses the same operation registry, profile validation, adapter contracts, health semantics, and secret-reference rules as Central Gateway.
- A local JSON file cannot grant production access by itself. Production-capable Local Gateway profiles require an approved manifest or central registry binding.

### Organization-level capacity

- Central and Local physical pools are process-local and are never the same object.
- All Central, Local, blue/green, canary, and draining runtime hosts that reach the same physical Dynamics organization share one `OrganizationAdmissionKey` and aggregate concurrency budget.
- Version labels, aliases, environment labels, or process boundaries must not multiply the physical organization's capacity.
- Every admitted request holds a bounded lease/permit before outbound Dynamics traffic. Loss or expiry of the host lease stops new admission and forces bounded drain/cancellation.

### CE 9.1 profile

- The only transport is `OfficialCrm91Worker`, a separately deployed .NET
  Framework 4.8 process with an immutable Microsoft XRM tooling package lock.
- The current package baseline is
  `Microsoft.CrmSdk.XrmTooling.CoreAssembly` 9.1.1.65; any change requires a
  new package-lock/executable generation and complete verification.
- Data8 and direct Web API are not CE 9.1 routes or fallbacks.
- A CE 9.1 profile has its own version-specific operation registry, worker pool,
  credential reference, recycle policy, and real-server evidence.

### CE 8.2 profile

- The only new transport is `OfficialCrm82Worker`, a separately deployed .NET
  Framework 4.8 process using a Microsoft-published XRM tooling version proven
  against the actual CE 8.2 target.
- The CE 8.2 package lock is selected independently. A CE 9.1 package that
  merely restores or compiles is not accepted as CE 8.2 compatibility proof.
- Data8 may continue only behind the existing unmigrated legacy product path
  until the official worker passes its operation/lifecycle/rollback gates. It
  is not selectable by new Gateway routing.
- CE 8.2 and CE 9.1 workers remain independently version-pinned processes; this
  task does not plan consolidation.

### Temporary Data8 boundary

- `PowerPlatform.Dataverse.Client` in this repository is the third-party Data8 WS-Trust client, not Microsoft-owned source.
- It is temporary compatibility code only.
- The current `OnPremiseClient` implements `IOrganizationService` but not `IDisposable`; the existing `CrmConnectionPool` disposal cast therefore does not prove that its underlying WCF channels/factories are closed.
- The Data8 client must not become the permanent Central or Local Gateway in-process pool implementation.
- New Gateway migration must not add Data8. Existing legacy use cases stay
  outside the new route until the matching official worker replaces them.
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
| Gateway-owned success payload includes CRM hostname, Organization Service endpoint, `/api/data/` base path, package path, pipe/nonce/process metadata, credential, or token | Review and release fail. Return only the bounded product DTO. |
| Worker response contains an SDK type, raw Entity/Organization response, or absolute CRM URL | Reject the frame, terminate/quarantine the worker generation, and return a sanitized protocol failure. |
| Product requests an unknown or unauthorized alias/operation | Reject before profile resolution or outbound Dynamics traffic. |
| Authenticated but unauthorized request uses an invalid Content-Type | Return 403 before media-type validation and before body read. |
| Authorized operation request omits Content-Type or uses a non-approved media type/parameter/charset | Return 415 before request-body I/O, pooled-buffer rent, JSON parsing, executor invocation, or outbound Dynamics traffic. |
| Authorized operation request uses case-insensitive `application/json` with no parameters or one UTF-8 charset | Continue to the existing bounded byte/JSON validation path. |
| Any profile selects `WebApi`, `OfficialServiceClient`, `OfficialLegacyWorker`, or `TemporaryData8LegacyWorker` | Startup fails closed; no worker or Dynamics traffic starts. |
| CE 8.2 or CE 9.1 profile uses the wrong worker/package-lock kind | Profile remains NotReady and the process is not published. |
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
- `crm82` uses `OfficialCrm82Worker` while `crm91` uses
  `OfficialCrm91Worker`. Their executables, pinned SDK graphs, clients,
  credentials, WCF/static state, pipes, and pools are separate, while aggregate
  organization admission is enforced by physical organization identity.
- A worker package/profile generation changes only through validate, publish,
  drain, dispose/terminate, and remove. The old generation never receives new
  work after publication of the replacement.
- An authorized caller sends `Content-Type: application/json; charset=UTF-8`; Gateway validates the header before renting a body buffer, then applies the configured byte/depth/member limits.

### Base

- Only Central Gateway is deployed in production. Local Gateway is used by a developer with non-production secret references. Embedded remains compiled but unused.
- Existing unmigrated CE 8.2 product traffic may continue through its named
  legacy implementation while the official CE 8.2 worker is built. New Gateway
  routing cannot select that legacy path.
- An unauthorized caller sends `Content-Type: text/plain`; Gateway returns 403 without reading the stream, so media-type behavior does not become an authorization oracle.

### Bad

- Product A directly references Data8 while Product B directly references `ServiceClient`, each with its own connection string and retry/pool implementation.
- A request selects one official worker, fails, and silently retries through
  Web API, Data8, another worker version, or another profile.
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
- Assert a successful operation envelope preserves only approved product fields
  and does not add CRM hostname, Organization Service endpoint, SDK type data,
  package path, pipe/nonce/process metadata, credential, token, or `/api/data/`
  routing metadata.
- Assert worker results project SDK responses into bounded DTOs and never
  serialize raw `Entity`, `OrganizationResponse`, or absolute CRM URLs.
- Assert unknown/unauthorized aliases and operation IDs fail before outbound transport invocation.
- Assert missing Content-Type, `text/plain`, `application/*+json`, unknown/repeated parameters, and non-UTF-8 charset return 415 with zero body reads and zero executor calls.
- Assert unauthorized/unmapped caller with an invalid Content-Type still returns 403 with zero body reads, proving authorization precedes media-type validation.
- Assert `application/json` comparison is case-insensitive and accepts either no parameter or exactly one UTF-8 charset parameter.
- Assert 415 paths do not rent or return pooled body buffers because ownership never begins, and do not dispose the ASP.NET Core-owned request stream.
- A `WebApplicationFactory` Kestrel boundary fixture configured through `WithWebHostBuilder` must place `http://127.0.0.1:0` on that same `IWebHostBuilder` through `WebHostDefaults.ServerUrlsKey`, then call parameterless `UseKestrel()`. In .NET 10 minimal-host tests, `UseKestrel(0)` on the returned derived factory can leave the original factory's `CreateHost` delegate without the port value and silently bind the default `localhost:5000`. Assert the observed listener is not 5000 and run the fixture once while a test-owned listener reserves 5000.

### Isolation and capacity

- Assert `crm82` and `crm91` create different executable/package/runtime
  generation keys and cannot share assembly, client, credential, WCF/static,
  pipe, process, or result state.
- Assert aliases that resolve to the same physical organization share one aggregate admission budget.
- Run concurrent Central plus Local host tests and assert total in-flight Dynamics work never exceeds `AggregateMaxInFlight`.
- After replace-and-drain, assert retired processes, official clients, pipes,
  streams, timers, registrations, queues, request maps, worker proxies, process
  handles, and strong runtime references return to baseline.

### CE 8.2 real-server gates

- Official-worker `WhoAmI` or equivalent identity operation through
  website -> Gateway -> worker.
- Representative CRUD, Query/FetchXML, paging, and every approved action/function/organization request.
- Official-client authentication, cold start, reconnect, worker recycle, and
  Gateway restart.
- Fault injection for CRM timeout, worker crash/hang, malformed IPC response,
  pipe break, Gateway restart, profile reload, and forced termination.
- Long-running worker/process/pipe/thread/handle/private-bytes soak proving a
  stable post-warm-up and post-recycle baseline.

### CE 9.1 real-server gates

- Official-worker identity operation, representative operations, paging,
  requests/actions, batch where used, reconnect, recycle, restart, and profile
  reload.
- Verify only the pinned `OfficialCrm91Worker` authentication and operation
  matrix against the actual target.

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

### Correct: replace the legacy route with version-specific official workers

```text
Product -> Gateway contract -> OfficialCrm82Worker -> CE 8.2
                            -> OfficialCrm91Worker -> CE 9.1
```

Data8 remains only in explicitly unmigrated legacy product code until the CE 8.2
official worker passes its gates; it is never a new Gateway transport.

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

## Scenario: Adjacent official-worker deployment overlay

### 1. Scope / Trigger

This scenario applies whenever the pinned CE 8.2/9.1 workers are published,
deployment-owned worker profiles are materialized, or the Gateway starts with
`dynamics-official-workers.gateway.json` beside its executable. The overlay is
the environment-specific artifact-identity bridge between the reviewed worker
publish manifest and the otherwise checked-in Gateway profile/runtime limits.

### 2. Signatures

```powershell
.\docs\scripts\Publish-DynamicsOfficialWorkers.ps1 `
  -RepositoryPath '<worktree-root>' `
  -OutputRoot '<final-worker-root>' `
  -Json

.\docs\scripts\New-DynamicsOfficialWorkerDeployment.ps1 `
  -ManifestPath '<final-worker-root>\official-worker-manifest.json' `
  -ProfileInputPath '<approved-deployment-profile-input.json>' `
  -OutputDirectory '<clean-gateway-host-directory>' `
  -Json
```

```text
<clean-gateway-host-directory>/
  SpeechMessage.Dynamics.Gateway.exe (or owning Gateway host executable)
  dynamics-official-workers.gateway.json

Program startup order:
  WebApplication.CreateBuilder(args)
  -> TryAddAdjacentOverlay(builder.Configuration, AppContext.BaseDirectory)
  -> LoadDynamicsProfileDefinitions(builder.Configuration, builder.Environment)
```

### 3. Contracts

- Publish the CE 8.2 and CE 9.1 workers into their final versioned deployment
  locations before generating the overlay. The overlay contains absolute worker
  executable paths, so moving a worker after generation invalidates the
  deployment contract.
- The manifest produced by that exact publication is the only executable-hash
  authority. Re-publishing may produce a different executable hash, so it must
  produce a new manifest and repeat independent artifact-to-manifest comparison;
  an earlier report's hash must never be copied into a later overlay.
- `OutputDirectory` is the clean Gateway publish/executable directory for the
  selected Local or Central host generation. The
  generator writes the overlay there and writes each `worker-profile.xml`
  beside its already-published worker executable.
- Use a clean/versioned host directory. The generator refuses to overwrite an
  existing overlay or `worker-profile.xml`; operators must not delete or merge
  files in place to bypass this fail-closed generation boundary.
- Never generate an environment-specific overlay inside
  `SpeechMessage.Dynamics.Gateway` source. The Web SDK includes JSON content in
  build/publish output, and a source-tree or test-bin overlay can silently alter
  another environment or `WebApplicationFactory` run.
- Gateway looks only for the exact file adjacent to `AppContext.BaseDirectory`.
  Absence is allowed and leaves the checked-in configuration unchanged; a
  present invalid file fails startup before profile materialization or worker
  creation.
- The overlay is added exactly once after all standard `CreateBuilder`
  configuration sources and before `LoadDynamicsProfileDefinitions`. Its
  allowlisted artifact/profile identity fields therefore override checked-in
  placeholders, while base runtime/admission/security settings remain owned by
  normal Gateway configuration.
- Loading is startup-only. The helper reads at most 256 KiB with JSON depth at
  most eight, clears the byte buffer in `finally`, and creates no file provider,
  `FileSystemWatcher`, reload-on-change owner, timer, or background task. A file
  change takes effect only after a controlled Gateway restart/replace-and-drain.
- A private fixed-snapshot source transfers its sole bounded dictionary to one
  provider with `Interlocked.Exchange`; the retained source no longer owns the
  original enumerable or a duplicate dictionary. `ConfigurationManager`/Host
  owns that provider and its one bounded change-token registration until Host
  disposal. No static mutable deployment snapshot or cross-Host state exists.
- The Phase 4C compatibility harness validates the supplied Gateway base URI as
  an absolute HTTPS URI with no user-info, query or fragment in both
  `ValidateOnly` and live modes. `ValidateOnly` must reject a target that could
  never be executed safely even though it creates no network resource; this
  prevents sanitized deployment evidence from approving a split validation/live
  trust boundary. The validated URI is process-local, never logged or cached,
  and is cleared after the one bounded run.
- The overlay accepts only `DynamicsProfiles:Profiles` artifact/profile identity
  fields. It rejects unknown or secret-shaped fields, case-colliding aliases,
  duplicate properties, unsupported worker kinds, relative or wrong executable
  paths, zero/non-hex hashes, and any expected organization GUID whose 16 bytes
  are all identical.
- Deployment generation requires authoritative profile identity and
  authentication inputs. Do not invent CE 8.2/9.1 Organization GUIDs,
  credential references, authentication modes, organization names, or home
  realms merely to create an overlay. Until those values are approved, keep
  `Package01FeeReadsEnabled=false` and leave Phase 4C open.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Overlay is absent beside the running Gateway | Continue with base configuration and add no provider. Placeholder profile values may still make that environment NotReady. |
| Overlay is present but malformed, oversized, too deep, duplicate-bearing, or contains an unknown/secret field | Throw the fixed sanitized invalid-overlay exception before mutating configuration or starting a worker. |
| Worker path is relative, names the wrong worker executable, or no longer matches the final location | Reject the overlay; do not search another directory or fall back to a checked-in path. |
| Executable hash is zero, malformed, or does not match the published manifest/artifact | Generation or startup remains fail closed; do not rewrite the hash from the live file implicitly. |
| Expected Organization GUID is empty, all-FF, or any other all-identical-byte value | Reject it as a placeholder before profile publication. |
| Overlay or worker profile already exists at generation targets | Refuse overwrite and require a clean/versioned deployment directory. |
| Overlay file changes after Gateway startup | Running configuration remains unchanged; perform a controlled restart to load a newly reviewed snapshot. |
| Two Gateway configuration instances load different overlays | Each instance owns a distinct provider/dictionary; mutation or disposal of one cannot affect the other. |
| Authoritative CE profile identity/authentication values are unavailable | Publish/hash verification may proceed, but do not generate a deployment overlay or claim Phase 4C. |

### 5. Good / Base / Bad Cases

- Good: workers are published to final immutable paths, manifest hashes are
  independently verified, the generator writes profiles beside those workers
  and the overlay beside the selected Gateway executable, then one restart loads
  one fixed snapshot.
- Base: no overlay exists in a developer/test output directory. Gateway uses its
  ordinary configuration and no watcher/provider is added by the helper.
- Bad: generate the production overlay under the Gateway source tree, move the
  workers afterward, or copy the overlay into a test output directory.
- Bad: use placeholder Organization GUIDs or guessed credential/authentication
  fields to make deployment generation pass.

### 6. Tests Required

- Assert the checked-in hash/GUID placeholders are overridden only by one
  `FixedSnapshotConfigurationProvider`, and deleting the source file after load
  does not change the running snapshot.
- Assert missing overlay adds no configuration source/provider.
- Assert secret/unknown fields, case-colliding aliases, duplicate properties,
  relative paths, zero hashes, and repeated-byte GUID placeholders fail before
  configuration mutation.
- Load two independent `ConfigurationManager` instances, mutate one, and prove
  provider identity and values do not cross instances. Dispose both managers
  deterministically.
- Source-scan `Program.cs` and assert exactly one overlay call occurs before
  profile materialization.
- Run deployment-generator tests proving manifest/package/artifact inventory,
  authentication union, duplicate JSON, secret-field, existing-output, and
  placeholder validation remain fail closed.
- Run the changed-file strict UTF-8/no-BOM/CRLF/final-CRLF gate and
  `git diff --check`.

### 7. Wrong vs Correct

#### Wrong

```powershell
# Source-tree output can be published accidentally, and these are guessed
# deployment identities rather than approved CE evidence.
.\docs\scripts\New-DynamicsOfficialWorkerDeployment.ps1 `
  -ManifestPath .\artifacts\dynamics-workers\official-worker-manifest.json `
  -ProfileInputPath .\guessed-profile.json `
  -OutputDirectory .\SpeechMessage.Dynamics.Gateway
```

#### Correct

```powershell
# Workers already occupy their final paths. The profile input is separately
# approved, and the clean output is the selected Local or Central Gateway host directory.
.\docs\scripts\New-DynamicsOfficialWorkerDeployment.ps1 `
  -ManifestPath '<final-worker-root>\official-worker-manifest.json' `
  -ProfileInputPath '<approved-profile-input.json>' `
  -OutputDirectory '<clean-gateway-host-directory>'
```

The correct path preserves adjacency, precedence, immutable startup semantics,
single ownership, deterministic Host cleanup, and the Phase 4C evidence gate.

## Scenario: Official NuGet worker protocol and lifecycle

### 1. Scope / Trigger

This scenario applies whenever Gateway starts, dispatches to, drains, recycles,
terminates, or replaces `OfficialCrm82Worker` or `OfficialCrm91Worker`.

### 2. Signatures

```text
WorkerRequestV1 =
  ProtocolVersion
  ProcessNonce
  RequestId
  ProfileGenerationId
  OperationDefinitionRevision
  CapabilityOperationId
  DeadlineUtcTicks
  BoundedTypedParameters

WorkerResponseV1 =
  ProtocolVersion
  ProcessNonce
  RequestId
  SanitizedOutcome
  BoundedTypedResultOrError
```

```csharp
public interface IDynamicsWorkerSupervisor : IAsyncDisposable
{
    Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken);

    Task DrainAsync(CancellationToken cancellationToken);
}
```

### 3. Contracts

- `SpeechMessage.Dynamics.WorkerProtocol` targets `netstandard2.0`, references no
  Microsoft CRM SDK package, and exposes no SDK type.
- `SpeechMessage.Dynamics.Crm82Worker` and
  `SpeechMessage.Dynamics.Crm91Worker` target .NET Framework 4.8, reference only
  their own immutable Microsoft package lock, and never reference each other.
- The supervisor validates executable hash, package-lock ID, worker kind,
  protocol version, profile generation, one-time nonce, bounded READY deadline,
  and pipe ACL before publishing a worker.
- Process arguments contain only the random local pipe name, one-time nonce,
  protocol version, worker kind, package-lock ID, and profile-generation ID.
  Environment inheritance is allowlisted/minimized. Neither channel contains an
  endpoint, connection string, username, password, token, certificate, cookie,
  or caller identity.
- Endpoint and credential references are resolved inside the worker from an
  approved local secret provider. Raw resolved values stay in worker memory and
  are never serialized or logged.
- Frames are length-prefixed and reject zero/oversized length, incomplete read,
  trailing bytes, unsupported version, wrong nonce, duplicate request ID,
  expired deadline, unknown operation, excessive JSON depth/member/string/array
  size, or result-size overflow.
- Operation translation from bounded parameters to `Entity`,
  `QueryExpression`, server-owned FetchXML, or `OrganizationRequest` occurs only
  inside the worker. A generic Execute command, arbitrary FetchXML, dynamic
  entity/table/action name, CRM URL, or SDK-serialized payload is forbidden.
- One worker admits one active operation by default. A higher value requires an
  exact package/target concurrency and soak record. Worker-count scaling remains
  bounded by the existing organization admission plan.
- Each worker owns exactly one `CrmServiceClient` generation and disposes it once
  during graceful drain. The supervisor owns every process, pipe, stream,
  request map, cancellation source/registration, timer, semaphore, health loop,
  and process handle.
- Drain closes new admission, waits only until the finite deadline, cancels
  owned work, disposes the client/IPC, and exits. If the worker is hung or does
  not acknowledge drain, the supervisor terminates it after the grace deadline,
  waits for exit, disposes the process handle, and removes every retained
  request/generation reference.
- Process ownership is released only after the OS explicitly confirms exit and
  both redirected stdout/stderr reader tasks reach a terminal state. A worker
  parent may exit while a descendant still inherits an output handle; the
  supervisor must retain the `Process` and incomplete reader task references,
  return the fixed sanitized cleanup failure, and permit a later `DisposeAsync`
  retry. It must never exchange a reader/process field to `null` before the
  corresponding completion/disposal is confirmed.
- Concurrent `DisposeAsync` callers share exactly one cleanup-attempt task. A
  failed or timed-out attempt is not cached permanently: after it has stopped
  touching resources, the attempt task is cleared while all incomplete resource
  owners remain. The operation semaphore is disposed only after cleanup is
  complete and every caller that entered `ExecuteAsync`, including gate waiters,
  has left; calls arriving after admission closes fail before touching the
  disposed semaphore.
- An exception from `Process.HasExited`, `Kill`, or `WaitForExitAsync` is an
  unknown lifecycle result, not proof of process exit. Unknown results keep the
  process owner and fail cleanup closed.
- Worker age, completed-operation count, private bytes/working set, health
  failure, protocol violation, repeated timeout, and profile/package replacement
  are bounded recycle triggers.
- A worker crash, hang, malformed response, uncertain post-dispatch write, or
  timeout returns a sanitized typed failure. It never replays an uncertain write
  or falls back to Web API, Data8, another worker kind, version, profile, or
  credential.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Profile selects anything except `OfficialCrm82Worker` or `OfficialCrm91Worker` | Reject configuration before process start. |
| Worker executable/package lock/hash/CE kind does not match the profile | Do not publish the worker; drain/terminate and dispose all startup resources. |
| READY frame has wrong nonce/version/generation or arrives after deadline | Reject, terminate, and record only a sanitized startup category. |
| Request frame is malformed, oversized, expired, duplicate, or unknown | Reject before SDK invocation; keep memory and request-map growth bounded. |
| Secret-like value appears in args/env/log/IPC/error | Security gate fails; no release or live test proceeds. |
| Worker response contains an SDK type/raw CRM URL or exceeds bounds | Reject the frame and recycle/quarantine that worker generation. |
| Worker exceeds age/count/memory/health threshold | Stop new assignment and replace-and-drain it without exceeding aggregate capacity. |
| Graceful drain exceeds deadline | Force terminate, wait for exit, dispose handles/IPC/registrations, and prove counters return to zero. |
| Worker parent exits but a descendant still owns stdout/stderr handles | The first cleanup attempt returns `The official Dynamics worker cleanup did not complete.`, keeps the process and incomplete reader owners visible in the lifecycle snapshot, and allows a later retry after the handles close. |
| Process kill, exit wait, or state query cannot confirm exit | Treat cleanup as incomplete, retain the `Process` reference, keep readiness false, and retry only through the next serialized cleanup owner. |
| Official operation fails | Return its sanitized failure; do not change transport/profile/version/credential. |

### 5. Good / Base / Bad Cases

- Good: two bounded CE 8.2 workers and two CE 9.1 workers run concurrently;
  their SDK assemblies, clients, credentials, pipes, results, and static/WCF
  state remain isolated while the shared organization budget is respected.
- Good: a worker reaches its operation-count limit, stops new assignment,
  drains the in-flight request, disposes `CrmServiceClient`, exits, and is
  replaced without a capacity spike or retained process/pipe handle.
- Base: an SDK call hangs past the grace deadline; Gateway returns a typed
  timeout, force-terminates only that worker, and restores the bounded pool.
- Bad: put a password/connection string in `ProcessStartInfo.Arguments` or an
  environment variable, serialize an `Entity` over the pipe, or keep a static
  `CrmServiceClient` shared across worker generations.
- Bad: catch a CE 9.1 worker error and retry through Web API or Data8.

### 6. Tests Required

- Project/package scan proving the SDK allowlist contains only the two workers
  and worker-only tests.
- Protocol frame tests for size, partial read, depth/member/string/array limits,
  version, nonce, duplicate ID, deadline, trailing data, and result bounds.
- Secret scans over process-start projections, environment projections, logs,
  exceptions, protocol captures, crash evidence, and snapshots.
- Repeated start/READY/request/cancel/timeout/crash/drain/kill/recycle loops with
  counters for processes, pipes, streams, timers, registrations, request-map
  entries, semaphores, leases, permits, and strong generation references.
- Start a worker that launches a short-lived descendant inheriting stdout/stderr,
  then let the worker parent drain and exit. Assert concurrent first
  `DisposeAsync` callers share one task and receive the fixed cleanup failure,
  `OwnedProcessCount=1`, `OwnedBackgroundTaskCount>0`, and readiness false;
  after the descendant closes the handles, assert a second dispose succeeds and
  every ownership counter is zero.
- Simultaneous CE 8.2/9.1 workers proving no assembly/client/credential/result or
  mutable state crosses versions/profiles.
- Soak tests proving managed heap, private bytes, working set, handles, threads,
  process count, pipe count, and queues return to a declared post-warm-up
  baseline after recycle/drain.
- Deployed website -> Gateway -> official worker -> target CE tests for identity,
  representative reads/paging/query/metadata/actions, controlled test-owned
  writes, reconnect, recycle, and no-leak baselines.

### 7. Wrong vs Correct

#### Wrong

```text
Gateway (.NET 10) -> loads CE 8.2 and CE 9.1 CRM SDK DLLs in one process
failed official worker -> direct Web API fallback
```

#### Correct

```text
Gateway (.NET 10)
  -> bounded nonce-bound IPC -> Crm82Worker (net48, pinned Microsoft NuGet)
  -> bounded nonce-bound IPC -> Crm91Worker (net48, pinned Microsoft NuGet)
```

```csharp
// Await first; clear only the exact owner whose completion was confirmed.
var reader = Volatile.Read(ref _stdoutDiscardTask);
if (await AwaitReaderCompletionAsync(reader, drainTimeout).ConfigureAwait(false))
{
    Interlocked.CompareExchange(ref _stdoutDiscardTask, null, reader);
}

// A faulted cleanup attempt is retryable while incomplete owners remain.
catch
{
    lock (_disposeSync)
    {
        _disposeTask = null;
    }

    throw;
}
```

The process boundary isolates SDK versions and provides a deterministic final
cleanup boundary without changing the product contract or organization budget.

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

This scenario applies when Visual Studio starts `SpeechMessage.Dynamics.Gateway`
or ChurchReport under the `Development` environment, or when a compiled Host DLL
is executed directly for local verification. It defines the fail-closed Local
  Gateway configuration, durable single-machine control-plane ownership,
  product-to-Gateway boundary, and two independent runtime checks: Gateway health
  and policy verification, plus feature-disabled ChurchReport browser verification.
  It also defines the permitted Phase 4C Local Gateway lane: with an approved
  non-production or explicitly approved CE profile, Visual Studio may run the
  exact pinned Worker against the real Organization Service. It does not enable
  Phase 5 consumer traffic or permit invented profile/credential data.

### 2. Signatures

```text
SpeechMessage.Dynamics.Gateway/appsettings.Development.json
  ConnectionStrings:DynamicsControlPlane
  DynamicsProfiles:Profiles:*:WorkerKind
  DynamicsProfiles:Profiles:*:WorkerExecutablePath
  DynamicsProfiles:Profiles:*:WorkerExecutableSha256
  DynamicsProfiles:Profiles:*:PackageLockId
  DynamicsProfiles:Profiles:*:OrganizationBaseUri
  DynamicsGateway:ActiveWorkloadBindingSet = Local
  DynamicsGateway:WorkloadBindingSets:Local[*]

SpeechMessage.Dynamics.Gateway/appsettings.json
  DynamicsGateway:ActiveWorkloadBindingSet = Central
  DynamicsGateway:WorkloadBindingSets:Central[*]

SpeechMessageProducts.ChurchReport/appsettings.Development.json
  DynamicsAccess:ExecutionMode
  DynamicsAccess:ProfileAlias
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
```

### 3. Contracts

- Development Gateway durable coordination uses the explicitly provisioned same-Windows-user LocalDB instance and a dedicated `SpeechMessageDynamicsControlPlane` database. The connection uses integrated authentication, bounded pool size, and bounded connect timeout. Gateway startup validates the schema; it does not connect to Dynamics native SQL, auto-create the database, or fall back to in-memory coordination.
- The checked-in Development CRM target remains deliberately non-routable. A permitted operation against it must fail in a controlled, sanitized way without falling back to Central Gateway, Embedded, Data8, another alias, or a production endpoint.
- A real CE Phase 4C run replaces that non-routable target only through one
  approved Local Gateway overlay/profile generation. Its paths need to remain
  stable for that generation, but they do not need to be a final Central/IIS
  deployment directory. The Local Gateway verifies the current manifest hash,
  package lock, Worker kind, organization identity, and secret reference before
  the Worker starts; the overlay and every Worker profile remain outside product
  JSON and are removed or retained only by their explicit local deployment owner.
- ChurchReport Development uses `ExecutionMode=Gateway`, `ProfileAlias=crm82`,
  HTTPS loopback, and API prefix `/v1`. The product does not select or duplicate
  the CE version; the deployment-owned Gateway profile selects the pinned worker.
  `Package01FeeReadsEnabled=false` remains the authoritative consumer-traffic
  gate.
- Feature-disabled ChurchReport startup must not create ProductClient, HTTP handler/pool, token cache, timer, or Dynamics preflight/operation traffic. Development configuration alignment alone does not enable Package 1.
- Local Gateway authentication uses server-established Windows Negotiate identity plus server-owned workload bindings. Client JSON and spoofable headers never select principal, workload, alias permission, or operation permission.
- A syntactically valid authenticated Windows SID is authoritative. When it is present, authorization performs only the SID lookup; an unmapped SID fails closed and must not fall back to a matching principal name. Exact principal-name fallback is allowed only when the authenticated principal has no usable SID at all. This prevents a newly created account with the same name but a different SID from inheriting the retired account's workload permissions.
- `DynamicsGateway:ActiveWorkloadBindingSet` is the deployment-owned selector and is mandatory. The authorizer enumerates direct children under `DynamicsGateway:WorkloadBindingSets`, resolves exactly one case-insensitive matching set, and materializes only that set. It must not concatenate the selector into a configuration path or enumerate all sets.
- Central, Local, and Testing binding sets may coexist in the merged configuration because they are separate named subtrees. `appsettings.Development.json` changes only the selector to `Local`; therefore .NET configuration's numeric-array and nested-leaf merge behavior cannot import a Central principal or Central operation into the Local frozen authorization snapshot.
- An empty, whitespace, wildcard, unknown, ambiguous, scalar-only, or childless active set is a startup failure before the listener, secret resolution, admission, executor, or outbound transport. There is no fallback to `Central`, the first set, the base provider, or the union of all sets.
- Runtime verification artifacts may record HTTP status categories, test counts, readiness state, JavaScript error count, and sanitized policy outcomes. They must not persist credentials, tokens, passwords, Session identifiers, client identifiers, callback values, private VM addresses, complete AD FS/CRM endpoints, or secret-reference values.
- Raw workload-binding arrays at one shared configuration path are forbidden. .NET configuration merges arrays and nested lists by numeric leaf key; changing index `1` to `0` can still retain base `CapabilityOperationIds:1..N`. Named sets plus one strict selector are the required replacement boundary.
- A compiled ASP.NET Core DLL resolves `appsettings.json`, `appsettings.{Environment}.json`, content files, and relative configuration from its content root. Local verification must set the process working directory to the owning project directory or pass an explicit reviewed content root. Running the Gateway DLL from the solution root can omit its profile configuration and produce a misleading fail-closed profile-URI startup exception; do not weaken validation or edit deployment JSON to compensate for the wrong content root.
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
| A Visual Studio Local Gateway Phase 4C profile is approved and its manifest/overlay/Worker chain is valid | Start only the selected pinned Worker and run the fixed approved operation matrix against that exact CE target. |
| A Visual Studio Local Gateway Phase 4C profile is absent, malformed, hash-drifted, unapproved, or owns unstable paths | Remain NotReady; do not substitute a fake Worker, Web API, Data8, Central profile, or guessed identity. |
| `Package01FeeReadsEnabled=false` | ChurchReport root may run, but no Package 1 Dynamics traffic or preflight resources are created. |
| `ActiveWorkloadBindingSet` is missing, blank, contains wildcard text, names no direct child set, names a scalar/empty set, or is otherwise ambiguous | Host startup fails closed; do not start the listener or fall back to another set. |
| A Central principal authenticates against a Development Host whose selector is `Local` | Return 403 `unmapped-principal`; do not resolve an alias, operation, secret, admission permit, executor request, or outbound connection. |
| A compiled Gateway/ChurchReport DLL is started from the solution root without an explicit content root | Startup may fail closed because project appsettings are not loaded. Restart from the owning project directory; do not relax profile validation or copy secrets/configuration into the solution root. |
| The browser rejects the local Gateway development certificate | Do not bypass the security interstitial or mutate trust. Preserve CLI HTTPS status evidence and require a trusted certificate for full browser proof. |

### 5. Good / Base / Bad Cases

- Good: Gateway `/health` and durable `/ready` return 200, anonymous `/v1` returns 401, the current Windows workload catalog is authorized, wrong alias and unauthorized operation return 403, and the sole allowed operation fails against the non-routable target with a sanitized controlled response.
- Good: with `Package01FeeReadsEnabled=false`, ChurchReport starts alone; its login
  page reaches `readyState=complete` with zero JavaScript errors, creates no
  `/v1` request, Gateway/Worker process, or listener on 7244/57244, and releases
  its own 5080 listener on shutdown.
- Good: Base and Development JSON both remain loaded, but `ActiveWorkloadBindingSet=Local` causes the authorizer to materialize only `WorkloadBindingSets:Local`; a Central principal and every Central-only data operation remain unavailable.
- Good: a principal presents SID-B and name X while only SID-A/name X was previously authorized; SID-B is unmapped, so the request receives 403 and cannot inherit the old workload by name.
- Base: Central, Local, and Testing sets coexist as deployment data, while exactly one selector is active for one Host generation. Changing the selector requires configuration replacement plus Host restart/replace-and-drain; it is never a request-time switch.
- Base: a legacy authenticated principal has no usable SID claim but has an exact configured principal name; name fallback remains available without wildcard, prefix, substring, or caller-header matching.
- Bad: replace the checked-in Development CRM target with a routable URL merely to make a smoke test green. Use one approved Local Gateway overlay instead, with the same immutable profile rules as a Central host.
- Bad: set `Package01FeeReadsEnabled=true` to force preflight evidence before real CE 8.2/9.1 and rollback gates exist.
- Bad: define Central and Local entries under one `WorkloadBindings` array and assume a later provider replaces the collection; numeric leaf merging can preserve both entire bindings and nested operation entries.
- Bad: a valid but unmapped SID is allowed to continue into principal-name lookup. Account-name reuse can then grant a different Windows security authority the old account's alias, operation, capacity, and audit identity.
- Good: direct DLL verification runs each host from its own project directory;
  Gateway and ChurchReport checks are separate, and cleanup stops only the
  listener owner whose command line matches the expected DLL.
- Base: the development Gateway certificate is accepted by CLI loopback verification only; the in-app browser validates ChurchReport and the authorization redirect while Gateway browser proof remains gated on certificate trust.
- Bad: run the Gateway DLL from the solution root, observe a missing-profile exception, and modify profile JSON or weaken fail-closed validation instead of correcting the content root.

### 6. Tests Required

- Configuration precedence tests assert the LocalDB instance, dedicated control-plane database, integrated authentication, bounded pool, bounded timeout, non-routable CRM target, deployment-owned worker version, ChurchReport Local Gateway alias/prefix, absence of a product-side CE version selector, and Package 1 false state.
- Load real base plus Development JSON, authenticate with the Central binding principal, and assert Local authorization returns `unmapped-principal` with zero executor/outbound work. This regression must fail against a shared `WorkloadBindings` array implementation.
- Authenticate with a syntactically valid but unmapped SID plus a principal name that otherwise matches an authorized binding. Assert 403, `unmapped-principal`, zero executor calls, and no materialized execution request. Separately assert a principal with no usable SID still succeeds through the exact principal-name compatibility binding.
- Assert a missing selector, leading/trailing whitespace, `*` and `?` wildcard text, an unknown name, a delimiter-bearing value such as `Local:0`, scalar-only, scalar-plus-children, and a true childless JSON set all fail Host startup. Assert exact set selection is case-insensitive. Testing factories must select an explicit nonempty `Testing` set rather than inheriting `Central`.
- Execute the opt-in live LocalDB durable coordinator contract against the explicitly provisioned database and assert lease/fencing behavior without auto-provisioning.
- Start the real Development Gateway and verify `/health`, `/ready`, 401 anonymous, authorized workload catalog, 403 wrong alias, 403 unauthorized operation, and controlled no-fallback connector failure.
- With an approved Local Gateway Phase 4C profile, start the Gateway from the
  Visual Studio/project-owned local host, then prove the website -> localhost
  Gateway -> pinned Worker -> CE identity/read/paging/recycle matrix. Assert
  that the worker/pipe/process/resource counters return to baseline after
  controlled shutdown. This is real CE compatibility evidence; it does not
  require a Central or IIS deployment.
- Start ChurchReport alone with `Package01FeeReadsEnabled=false`; use a browser to
  assert the login page completes with zero JavaScript errors, no `/v1` request
  or login POST occurs, no Gateway/Worker/TestHost process or 7244/57244 listener
  exists, then stop only the captured ChurchReport PID and assert port 5080 and
  all captured processes return to baseline.
- Run Dynamics tests, ChurchReport tests, Release solution build, changed-file format, strict UTF-8/no-BOM/CRLF/final-CRLF, `git diff --check`, and added-line sensitive-literal scans.
- Start each compiled host independently from its project content root. Assert
  the Gateway 200/200/401/200/403/403/controlled-400 matrix in the Gateway lane,
  and ChurchReport `readyState=complete`, zero JavaScript errors, zero Dynamics
  traffic/processes, and listener count zero after cleanup in the disabled lane.
- Add a negative runtime check that starts the Gateway DLL from the wrong content root and proves it fails closed without opening a listener; the correction is the process content root, not a configuration or validation change.

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

## Design Decisions

### Central Gateway is the production default

Central Gateway centralizes secrets, authorization, operation governance, audit, observability, profile lifecycle, and reusable outbound runtimes for the multi-product estate. This avoids duplicating high-risk integration state across five to ten products.

### Local Gateway replaces Embedded as the immediate developer path

Local Gateway gives Visual Studio a separately observable console/process while preserving the same HTTP boundary as production. It avoids loading CRM transport dependencies into ChurchReport and keeps failures, SDK conflicts, and worker recycling outside the product process.

### Compatibility is provided at the Gateway contract, not by one universal SDK

CE 8.2 and CE 9.1 share the product-facing API and policy model. They do not have to share a transport implementation, SDK version, authentication flow, token/WCF state, or physical connection pool.

### Data8 remains only in unmigrated legacy product paths

Deleting Data8 before its current consumers migrate would break legacy traffic.
It is not a Gateway transport. It becomes removable after those consumers move
to `OfficialCrm82Worker` and the worker satisfies real-server, lifecycle,
isolation, rollback, and no-leak gates.

## Retired artifact: direct-Web-API live-smoke harness

This section is retained only as historical evidence for code scheduled for
deletion. It is non-normative, must not be executed as part of Phase 4, and does
not define a supported route, readiness gate, fallback, or operator action. The
official worker compatibility harness replaces it.

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
- Before setting any `DYNAMICS_SMOKE_*` bridge variable, the harness snapshots
  the exact Process-scope value (including absence) of every bridge key. A
  `finally` block restores every key after `dotnet test` returns or throws, so
  an interactive shell cannot retain a prior root, CE version, profile alias,
  contact ID, authentication mode, or secret-reference *name* for the next
  invocation. The harness never reads or emits a referenced secret value.
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
| `dotnet test` returns nonzero or bridge setup throws after Process variables changed | Restore every original bridge value in `finally`; do not leave a root, profile, credential mode, contact ID, or secret-reference name in the operator shell. |
| CRM returns an application 500 | Record it as target-environment evidence; do not change DNS, WinRM, authentication mode, or profile route automatically. |

### 5. Good / Base / Bad Cases

- Good: an operator explicitly supplies the reviewed v9.1 root and receives the
  connector's actual `WhoAmI` result.
- Base: live mode is omitted; the script prints safe usage and exits without
  any network traffic.
- Base: a live invocation finishes or fails; the caller's Process environment
  is bit-for-bit equivalent for every `DYNAMICS_SMOKE_*` bridge key after the
  script's `finally` cleanup.
- Bad: a script defaults to a historical organization URL and reports a 401
  from an unauthenticated `HEAD` as an IWA/connector failure.
- Bad: a `SecretReference` branch hardcodes environment-variable names tied to
  one organization, making another approved profile silently use the wrong
  identity source.
- Bad: a harness writes bridge variables directly into an interactive
  PowerShell Process and returns on an error path without restoring them.

### 6. Tests Required

- Execute `docs/scripts/Invoke-DynamicsLiveSmoke.Tests.ps1` and assert dry-run
  guidance contains an explicit `-WebApiRoot` placeholder, no historical host,
  and no target-specific secret-reference default.
- Assert `-EnableLive` without `-WebApiRoot` exits nonzero before external
  activity.
- Assert the source snapshots all `DYNAMICS_SMOKE_*` bridge variables and uses
  `finally` with the Process environment API to restore every original value.
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

#### Correct: Process-scope bridge cleanup

```powershell
$original = [Environment]::GetEnvironmentVariable(
    'DYNAMICS_SMOKE_WEBAPI_ROOT',
    [EnvironmentVariableTarget]::Process)
try {
    $env:DYNAMICS_SMOKE_WEBAPI_ROOT = $WebApiRoot
    & dotnet test $project --nologo
}
finally {
    [Environment]::SetEnvironmentVariable(
        'DYNAMICS_SMOKE_WEBAPI_ROOT',
        $original,
        [EnvironmentVariableTarget]::Process)
}
```

The production harness applies this same snapshot/restore ownership rule to
every bridge key. The target, authentication mode, and resulting evidence
remain explicit and traceable, while a real CRM fault remains an external gate
rather than a reason to weaken the Gateway boundary or retain caller state.

## Retired artifact: Dynamics Deployment PowerShell Claims/IFD diagnostic

This section is retained only as historical evidence. It is non-normative and
must not be used as a Gateway prerequisite or Phase 4 action. Do not establish a
D365APP01 management channel, reopen the IFD wizard, repeat direct Web API
`WhoAmI`, or collect new ASP.NET 1309 evidence for the official-worker task.

### 1. Scope / Trigger

This scenario applies when a real CE 8.2/9.1 CRMWeb request reaches CRM but
fails before an authentication verdict, and an approved D365 application-server
operator must distinguish persisted Claims/IFD configuration from Gateway,
WinRM, DNS, SQL, IIS, or ADFS alternatives. It is a read-only diagnostic
boundary, not a Deployment Manager replacement or a configuration writer.

### 2. Signatures

```powershell
Get-DynamicsCrmWebIfdDiagnostics.ps1 `
  -WebApiRoot '<explicit HTTPS /api/data/v8.2/ or /api/data/v9.1/ root>' `
  [-ExpectedIfdExternalDomain '<bare expected external-domain hostname>'] `
  [-LookbackMinutes 1..1440] `
  [-MaxEvents 1..50] `
  [-ProbeWhoAmI]
```

The output contains one sanitized snapshot with `DeploymentShell`,
`DeploymentSettings`, bounded ASP.NET event evidence, IIS evidence, and an
opt-in `Probe`. The setting projection reads only `IfdSettings` and
`ClaimsSettings` through `Get-CrmSetting`.

### 3. Contracts

- The command accepts only a `Cmdlet` registered by the official
  `Microsoft.Crm.PowerShell` snap-in. A same-named function, alias, module, or
  another snap-in is not a trusted substitute.
- If the command is absent, the diagnostic may temporarily load exactly that
  registered snap-in only in Windows PowerShell Desktop 5.1. It records whether
  the activation was already loaded or temporarily loaded, and a `finally`
  block removes only an activation owned by the diagnostic.
- The projection reports property names and safe shape booleans only; it never
  returns raw domain, URI, credential, cookie, header, event-message, or DWS
  values. URI recognition uses an anchored name suffix so scalar fields such as
  a security-token lifetime cannot be misclassified because their names contain
  the letters `uri`.
- When `-ExpectedIfdExternalDomain` is supplied, `IfdSettings` additionally
  carries `ExternalDomainExpectation` with only `Present`,
  `ContainsWhitespace`, `ContainsScheme`, `Representation`, `NormalizedHostMatches`,
  `HasUnexpectedUriShape`, and `MatchesExpectedContract`. It never serializes
  the configured or persisted value. Microsoft documents the IFD External
  Domain input as a bare hostname. A scheme-bearing value is therefore a
  fail-closed `absolute-uri-requires-supported-review` result: its normalized
  host and safe URI shape are supporting evidence only, not an automatic match
  and not proof that the server is misconfigured. Non-HTTPS, non-default port,
  non-root path, user-info, query, fragment, or whitespace also fails closed.
- Default execution sends no CRM request. `-ProbeWhoAmI` creates one disposable,
  no-cookie, no-proxy, no-redirect `UseDefaultCredentials` request using the
  current approved host identity and returns only status-category evidence.
- The diagnostic does not create remote sessions, prompt for or persist a
  credential, change Deployment settings, or use SQL, Registry, IIS, DNS, ADFS,
  Basic, CredSSP, unencrypted WinRM, or `TrustedHosts` as a fallback.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| `Get-CrmSetting` resolves to an untrusted command source | Return `DeploymentShell.Activation=untrusted-command`; do not invoke it. |
| Desktop PowerShell 5.1 is unavailable | Return `desktop-powershell-required`; do not attempt a module or remote fallback. |
| The official snap-in is not registered | Return `not-registered`; preserve the Deployment-shell boundary. |
| A temporary activation succeeds | Read both setting shapes and remove that snap-in exactly once before returning. |
| A trusted `Get-CrmSetting` query fails | Report `deployment-setting-query-failed`; do not reinterpret it as an authorization to use another management surface. |
| A scalar field contains the text `uri` within its name | Exclude it unless the name has a URI/URL/endpoint/address suffix. |
| DWS returns `ExternalDomain` with a scheme instead of the documented bare hostname | Return `ContainsScheme=true` and `MatchesExpectedContract=false`; preserve normalized-host and shape evidence, then require one supported Deployment Manager/servicing review. Do not rewrite the setting solely from this shape result. |
| Expected External Domain has a wrong host or an unsafe URI shape | Return `MatchesExpectedContract=false`; permit only one official Deployment Manager review, never SQL/Registry/IIS/DNS/ADFS/remoting substitution. |
| `-ProbeWhoAmI` returns HTTP 500 | Preserve CRMWeb as an external failed gate; do not change routing, authentication mode, or another infrastructure layer automatically. |

### 5. Good / Base / Bad Cases

- Good: an already approved Deployment PowerShell console reads both setting
  shapes, compares target-specific expected values without printing them, and
  accepts only the documented bare-host External Domain form automatically.
- Base: a scheme-bearing External Domain result retains no raw value and blocks
  automatic green status pending a supported Deployment Manager/servicing
  review; it does not itself authorize a server mutation.
- Base: a generic administrative PowerShell has no snap-in; the result reports
  that boundary and allocates no remote connection, credential, or CRM request.
- Base: a snap-in is loaded by the diagnostic for one invocation, then removed
  before the function returns; an operator-owned snap-in stays loaded.
- Bad: use a raw same-named PowerShell function as `Get-CrmSetting`, print
  complete configuration objects, or treat a shape-only result as proof that
  every exact persisted value is correct.
- Bad: treat a scheme-bearing `IfdSettings.ExternalDomain` string as
  automatically equivalent to the Deployment Manager bare-host contract, or
  change the setting solely because a redacted shape check returned false.
- Bad: repeat the same `WhoAmI` probe without a changed persisted setting or
  replace the diagnostic with SQL, Registry, IIS, DNS, ADFS, password, Basic,
  CredSSP, or `TrustedHosts` workarounds.

### 6. Tests Required

- Parse the script under Windows PowerShell 5.1 and run the no-probe path with
  an explicit non-routable example root; assert exactly one structured snapshot.
- Assert source-level rejection of credential, remote-session, configuration
  write, secret, cookie, proxy, and raw diagnostic-text paths.
- Mock a registered official snap-in; assert that both settings are projected,
  temporary activation is reported, and exactly one owned snap-in removal occurs.
- Mock a same-named non-cmdlet; assert `untrusted-command` and no invocation.
- Include a scalar `SessionSecurityTokenLifetimeInHours` property and assert it
  is not projected as URI/domain evidence.
- Mock `ExternalDomain` as an HTTPS root URI and assert
  `ExternalDomainExpectation.ContainsScheme=true` plus
  `MatchesExpectedContract=false` without the raw setting value appearing in
  serialized evidence; assert a bare hostname remains the only automatic
  match and a non-root-path URI fails closed.

### 7. Wrong vs Correct

#### Wrong

```powershell
# A same-named local function can shadow the official Deployment cmdlet.
Get-CrmSetting -SettingType IfdSettings | Format-List *

# A missing cmdlet is not permission to inspect CRM native SQL or change IIS.

# A scheme-bearing value is not an automatic green match for the documented
# External Domain hostname contract.
([string]$ifd.ExternalDomain).Trim() -eq 'expected.example.invalid'
```

#### Correct

```powershell
$evidence = .\Get-DynamicsCrmWebIfdDiagnostics.ps1 `
    -WebApiRoot 'https://example.invalid/api/data/v9.1/' `
    -ExpectedIfdExternalDomain 'expected.example.invalid'

$evidence.DeploymentShell
$evidence.DeploymentSettings |
    Select-Object SettingType, Status, Enabled, FailureCategory
$evidence.DeploymentSettings |
    Where-Object SettingType -eq 'IfdSettings' |
    Select-Object -ExpandProperty ExternalDomainExpectation
```

The correct path preserves command provenance, suppresses raw setting values,
reports a scheme-bearing External Domain as a supported-review boundary rather
than a proven root cause, and keeps shell discovery, DWS query access, and
CRMWeb live behavior as distinct evidence boundaries.

## Scenario: LocalDB legacy epoch 的顯式 drained recovery

### 1. Scope / Trigger

當早期 SqlRuntimeHostSlotCoordinator schema 已留下沒有
RuntimeHostOrganizationBinding 的 LocalDB epoch/slot row，且 operator 能以 row 的年齡、
lease、quarantine 與 binding 缺失共同證明它已完成 drain 時，才適用這個 recovery。
它處理的是單機 Development control-plane 的 legacy state；不是 CRM 資料修復、身份驗證
修復、真機 CE smoke、或任何 production database/remote administration 工具。

### 2. Signatures

    .\docs\scripts\Provision-DynamicsControlPlaneLocalDb.ps1 -RemoveDrainedUnboundEpochs

輸出只可加入下列有界、非機密欄位：

    DrainedUnboundRecoveryRequested : bool
    RemovedDrainedUnboundSlotRows   : int
    RemovedDrainedUnboundEpochRows  : int

schema migration 中，dynamic constraint drop 必須先把完整 command 組入
nvarchar(max) variable，再使用 EXEC(@variable)；不得寫成
EXEC(N'...' + QUOTENAME(...))，因 SQL Server 2025 LocalDB 不能解析後者。

### 3. Contracts

- -RemoveDrainedUnboundEpochs 未出現時，provisioning 不得刪除任何 durable epoch、
  slot 或 canonical binding row。
- script 只接受 MSSQLLocalDB、(localdb)\MSSQLLocalDB、
  SpeechMessageDynamicsControlPlane 與 checked-in schema 的 exact path；所有 sqlcmd
  invocation 均使用 -E integrated authentication。它不得接受 CRM SQL/remote SQL
  target、-U/-P SQL credential、credential object、token、session 或 WinRM input。
- opt-in SQL 必須以一個 SERIALIZABLE transaction 建立 connection-owned
  #UnboundEpoch 集合。它只考慮沒有 canonical binding 的 epoch，且在刪除前拒絕最近
  一小時更新的 epoch、最近 touched/未到期/quarantine 的 slot，以及有 host owner 但無
  expiry 的 slot。
- 成功路徑必須先刪 slot、再刪 epoch，不能刪 canonical binding。temporary table、
  transaction、sqlcmd process、mutex 與 bounded row-count output 的唯一 owner 是該
  provisioning invocation；不得保存 connection、credential、token、session、namespace
  list 或 background work。
- native exit code、target precondition 或 structured row-count output 任一失敗，script
  必須 fail closed，且不得以 CRM SQL、in-memory coordinator、DNS/IIS/ADFS、Basic、
  CredSSP、TrustedHosts 或未加密 remoting 作為 fallback。

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Recovery switch is absent | Do not issue recovery SQL; report both removed-row counts as 0. |
| Instance, database, or schema path differs from the fixed LocalDB contract | Throw before a native process, SQL connection, or DDL/recovery operation is started. |
| Epoch was updated in the previous hour | SQL throws 51005; the transaction rolls back and no candidate row is removed. |
| Slot is recently touched, still leased, quarantined, or owner-assigned without expiry | SQL throws 51006; the transaction rolls back and no candidate row is removed. |
| sqlcmd returns no exact slotCount|epochCount line | Throw and do not continue to schema apply. |
| A future migration uses EXEC(N'...' + QUOTENAME(...)) | Static schema regression fails before a LocalDB run; assign the complete SQL to nvarchar(max) and call EXEC(@variable). |

### 5. Good / Base / Bad Cases

- Good: an operator explicitly opts in once after evidence proves an old,
  unbound LocalDB host has drained; the invocation removes only its stale slot
  then epoch and returns bounded counts.
- Base: the script runs without the switch and validates/applies the checked-in
  schema without deleting any durable row.
- Bad: a helper infers that every unbound epoch is stale, uses an arbitrary
  server/database name, or uses SQL/remote credentials to make recovery easier.

### 6. Tests Required

- Localdb_provisioning_script_is_explicit_idempotent_and_least_privilege
  statically asserts the switch is explicit; fixed LocalDB/integrated-auth
  targets, serializable transaction, drain predicates, slot-before-epoch order,
  structured output checks, and the absence of CRM/credential/remoting fallback
  remain present.
- Durable_schema_requires_canonical_organization_binding_and_ordinal_string_semantics
  asserts both runtime and checked-in schema use variable-backed EXEC for
  dynamic constraint names and remain aligned on the relevant canonical-binding
  contract.
- An opt-in live LocalDB contract may prove real deletion only against an
  explicitly provisioned Development database with test-owned stale rows; it
  must then prove counts and cleanup without retaining connections or rows.
- All test classes that use the fixed Development LocalDB belong to one named
  xUnit collection with collection-level parallelization disabled. Random
  namespaces isolate durable rows, but they do not isolate the single LocalDB
  process or short lease/fencing deadlines; cross-class parallel execution can
  create test-harness contention and false expiry failures. Do not disable
  parallelization for the entire test assembly.

### 7. Wrong vs Correct

#### Wrong

    # Default provisioning silently decides old state is safe to remove.
    DELETE dbo.RuntimeHostAdmissionEpoch;

#### Correct

    if ($RemoveDrainedUnboundEpochs) {
        # SERIALIZABLE evidence gate; delete only proven-drained, unbound rows.
        DELETE slotRow;
        DELETE epochRow;
    }

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
