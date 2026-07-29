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
      "Endpoint": "https://localhost:7443/",
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
- The same ProductClient and REST contract are used for Central and Local Gateway deployments.
- Changing between Central and Local requires configuration replacement plus restart/replace-and-drain. It is not a request-time switch.

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

- Every newly added production or test C# type must contain detailed Traditional Chinese XML documentation that explains its responsibility, trust boundary, ownership model, lifecycle, concurrency behavior, failure behavior, and the reason the type exists.
- Every newly added public or internal method that performs routing, admission, authentication, generation replacement, cancellation, draining, disposal, worker control, or resource ownership must contain detailed Traditional Chinese XML documentation. Non-obvious branches and ordering constraints require nearby Traditional Chinese implementation comments that explain why the order is safety-critical.
- Comments must explain design intent and invariants rather than merely translate the syntax. In particular, they must identify the unique owner and deterministic cleanup path for clients, handlers, streams, timers, cancellation registrations, semaphores, background tasks, admission permits, runtime leases, and worker processes.
- Newly added or modified source, configuration, test, script, and documentation files are stored as UTF-8. The repository `.editorconfig` is authoritative and currently requires UTF-8 without BOM plus CRLF for these file types.
- A missing or superficial comment on a new lifecycle/concurrency/security boundary, invalid UTF-8, or mixed encoding is a verification failure and blocks review completion.

## 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| `ExecutionMode` is `CentralGateway` or `LocalGateway` with the current enum | Startup validation fails. Use `Gateway` and select the deployment by endpoint. |
| `ExecutionMode=Gateway` without `ProfileAlias` or absolute HTTPS `Gateway.Endpoint` | Startup fails closed. No outbound CRM traffic. |
| Product JSON contains CRM credentials, token, raw CRM URL, or SDK path | Configuration is rejected and secret scanning fails the build/release gate. |
| Product requests an unknown or unauthorized alias/operation | Reject before profile resolution or outbound Dynamics traffic. |
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

### Base

- Only Central Gateway is deployed in production. Local Gateway is used by a developer with non-production secret references. Embedded remains compiled but unused.
- CE 8.2 continues through the current SOAP route while the ADFS OAuth proof is incomplete. This is an explicit temporary state with an owner and removal gate.

### Bad

- Product A directly references Data8 while Product B directly references `ServiceClient`, each with its own connection string and retry/pool implementation.
- A request selects `crm82`, fails Web API authentication, and silently retries through Data8. This changes transport and security semantics inside one request.
- One singleton pool contains clients for CE 8.2 and CE 9.1 or for multiple credentials/organizations.
- Each Local Gateway assumes its local maximum is independent and collectively overloads the same Dynamics organization.
- A Local Gateway reads production credentials directly from product-owned JSON.

## 6. Tests Required

### Contract and configuration

- Assert Central endpoint and localhost endpoint produce identical ProductClient request payloads and result parsing.
- Assert only `Gateway` and `Embedded` are accepted `DynamicsExecutionMode` values.
- Assert `Gateway` requires a non-empty `ProfileAlias`, absolute HTTPS endpoint, and bounded API prefix.
- Assert product configuration rejects secrets, raw CRM URLs, authorization headers, and transport selection.
- Assert unknown/unauthorized aliases and operation IDs fail before outbound transport invocation.

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

- Enumerate every newly added C# type and every new routing/admission/authentication/lifecycle method; assert each has substantive Traditional Chinese XML documentation and that critical ordering branches have explanatory Traditional Chinese comments.
- Decode every added or modified source/config/test/script/document file with a strict UTF-8 decoder; fail on invalid byte sequences.
- Verify `.editorconfig` still applies `charset = utf-8` to the changed file types and run `git diff --check` to reject whitespace/line-ending damage.

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
    "Endpoint": "https://localhost:7443/",
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

## Design Decisions

### Central Gateway is the production default

Central Gateway centralizes secrets, authorization, operation governance, audit, observability, profile lifecycle, and reusable outbound runtimes for the multi-product estate. This avoids duplicating high-risk integration state across five to ten products.

### Local Gateway replaces Embedded as the immediate developer path

Local Gateway gives Visual Studio a separately observable console/process while preserving the same HTTP boundary as production. It avoids loading CRM transport dependencies into ChurchReport and keeps failures, SDK conflicts, and worker recycling outside the product process.

### Compatibility is provided at the Gateway contract, not by one universal SDK

CE 8.2 and CE 9.1 share the product-facing API and policy model. They do not have to share a transport implementation, SDK version, authentication flow, token/WCF state, or physical connection pool.

### Data8 is retained now and removable later

Deleting Data8 now would break `ToolUtility` and the known-working CE 8.2 WS-Trust path. It becomes removable only after every consumer moves behind Gateway and one proven CE 8.2 replacement satisfies real-server, lifecycle, isolation, and rollback gates.
