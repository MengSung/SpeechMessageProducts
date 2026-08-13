# Dynamics Gateway Hosting and CE 8.2/9.1 Routing Contract

## 0. 2026-08-07 connection-management supersession

The user-approved contract in `docs/dynamics-connection-management-spec.md`,
as rebaselined by
`.trellis/tasks/08-05-official-worker-router-ce-integration/scope-rebaseline-2026-08-07.md`,
supersedes any conflicting historic wording below, including statements that
Data8 is temporary-only, that the only modes are `Gateway`/`Embedded`, or that
Embedded must be deferred. Official Worker live evidence is conditional on the
selected profile and is not a prerequisite for a Data8-only ChurchReport route.

- `ConnectionMode` has exactly `Embedded`, `DedicatedGateway`, and
  `CentralGateway`. All three are permanent deployment choices. Dedicated and
  Central Gateway share the HTTPS product contract; their deployment location
  does not change product-facing behavior.
- `ConnectorKind` has exactly `Data8`, `OfficialCrm82Worker`, and
  `OfficialCrm91Worker`. Data8 is a permanent supported choice, not merely a
  fallback. Connector selection belongs to a deployment-owned Profile and
  cannot be selected or changed by a request.
- A Data8 `OnPremiseClient` is the unique owner of its Federated WCF Channel,
  ChannelFactory, or AD authentication client. It must Close each healthy WCF
  object, Abort it if Close fails, continue attempting all later cleanup, and
  surface aggregated cleanup failures to its pool owner. Construction rollback
  follows the same ownership rule. A legacy pool must nevertheless release its
  own capacity slot in `finally` even when downstream disposal fails; otherwise
  it leaks capacity and eventually rejects unrelated profiles as permanently
  full. Do not log raw cleanup exception details because WCF exception text may
  contain endpoint or authentication data.
- Profile isolation is `(ProfileAlias, GenerationId)`. Organization-level
  capacity is keyed by the confirmed `OrganizationId`, so aliases of the same
  physical Organization share a budget while never sharing mutable sessions,
  credentials, connections, workers, or profile state.
- Products expose only `ConnectionMode`, `ProfileAlias`, and optional Gateway
  endpoint settings. They never contain Organization ID, connector kind, CRM
  endpoint, credential reference, token, or pool configuration.
- Every mode executes the same RequestGuard before Profile resolution,
  admission, Connector allocation, or outbound work. `organizationId`,
  `connectorKind`, `credential`, `endpoint`, and `fetchXml` are reserved input
  names and must fail closed.

The older official-worker material remains applicable when the selected
connector is `OfficialCrm82Worker` or `OfficialCrm91Worker`, especially for
net48 process isolation and deterministic worker cleanup. It is not an
exclusive transport mandate.

## 0. Official Worker direction when that ConnectorKind is selected

This section governs only a deployment profile whose selected `ConnectorKind`
is `OfficialCrm82Worker` or `OfficialCrm91Worker`. It supersedes older
Web-API-first or optional-Web-API transport statements, but does not make an
Official Worker the exclusive transport mandate.

- `OfficialCrm82Worker` and `OfficialCrm91Worker` are separately supported
  version-pinned Worker ConnectorKinds. `Data8` remains the permanent .NET 10
  ConnectorKind and is not a fallback.
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
- Real-server validation for an Official Worker means executing the actual
  website, hosting mode, and selected Worker on the intended Windows host, then
  executing the approved Organization operation matrix against CE 8.2 or CE
  9.1. It proves only that Worker/profile/version combination. It must not be
  used to block a Data8-only capability that does not select an Official Worker.
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
- selects a shared Central Gateway or a product-local Dedicated Gateway process;
- adds or changes a CE 8.2 or CE 9.1 organization profile;
- introduces or changes an official CE 8.2/9.1 worker, worker protocol,
  `CrmServiceClient`, worker supervisor, or legacy-removal boundary;
- changes connection/runtime pooling, authentication, profile reload, worker lifecycle, or SDK-removal behavior.

The product-facing architecture has exactly three `ConnectionMode` values:

- `Embedded`: the connector runtime is hosted in the product process.
- `DedicatedGateway`: the product calls a separately deployed HTTPS Gateway for
  the one product.
- `CentralGateway`: the product calls the shared HTTPS Gateway service.

`ConnectionMode` and `ConnectorKind` are independent deployment dimensions.
For ChurchReport on Lenovo, `Embedded + Data8` and `DedicatedGateway + Data8`
are both required, configurable routes. The first cloud ChurchReport route is
`CentralGateway + Data8`. Neither mode selection nor a connector failure may
change ConnectorKind, ProfileAlias, CE version, or endpoint at request time.

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
    "ConnectionMode": "CentralGateway",
    "ProfileAlias": "crm82",
    "Gateway": {
      "Endpoint": "https://dynamics-gateway.internal/",
      "ApiPrefix": "/v1"
    }
  }
}
```

Dedicated local deployment:

```json
{
  "DynamicsAccess": {
    "ConnectionMode": "DedicatedGateway",
    "ProfileAlias": "crm91",
    "Gateway": {
      "Endpoint": "https://localhost:7244/",
      "ApiPrefix": "/v1"
    }
  }
}
```

`ConnectionMode` is the product-facing hosting selector and accepts exactly
`Embedded`, `DedicatedGateway`, or `CentralGateway`. `LocalGateway` and the old
`Gateway`/`DynamicsExecutionMode` names are historical terminology, not valid
configuration values. The connector remains deployment-owned by the selected
profile and is not included in product JSON.

### Profile transport contract

The deployment-owned profile contract exposes exactly one selected transport
kind per immutable profile generation. The supported values are:

```text
Data8
OfficialCrm82Worker
OfficialCrm91Worker
```

The transport kind is fixed for one immutable profile generation. `WebApi`,
`OfficialServiceClient`, `OfficialLegacyWorker`, and
`TemporaryData8LegacyWorker` are rejected legacy values. The transport cannot
switch per request or silently fall back after a failure.

## 3. Contracts

### Product boundary

- Products know `ConnectionMode`, `ProfileAlias`, Gateway endpoint, API prefix,
  and typed operation parameters only.
- Product JSON must not contain a CRM organization-service URL, raw CRM Web API URL, username, password, client secret, access token, refresh token, certificate private key, SDK DLL path, worker executable/package path, or transport kind.
- Gateway-owned success envelopes must not serialize CRM hostname, Organization Service endpoint, `/api/data/` base path, credential, token, package path, pipe name, nonce, process ID, or other internal routing/lifecycle metadata.
- Raw SDK/Organization Service response types and upstream absolute URLs are not product-safe. The worker must project them into bounded typed DTOs before serialization; no SDK object crosses IPC.
- The same ProductClient and REST contract are used for Dedicated and Central
  Gateway deployments.
- Changing between Embedded, Dedicated, and Central requires configuration
  replacement plus restart/replace-and-drain. It is not a request-time switch.

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

### Dedicated Gateway ownership

- Dedicated Gateway is a separate Windows process started beside one product for Visual Studio development, integration testing, or an explicitly isolated deployment.
- The product calls the configured HTTPS endpoint (often localhost during Lenovo development); Dedicated Gateway is not Embedded mode.
- Every Dedicated Gateway process owns and deterministically drains, terminates, and disposes its worker-process/pipe/process-handle pool.
- Dedicated Gateway uses the same operation registry, profile validation, adapter contracts, health semantics, and secret-reference rules as Central Gateway.
- A local JSON file cannot grant production access by itself. Production-capable Dedicated Gateway profiles require an approved manifest or central registry binding.

### Organization-level capacity

- Central and Dedicated physical pools are process-local and are never the same object.
- All Central, Dedicated, blue/green, canary, and draining runtime hosts that reach the same physical Dynamics organization share one `OrganizationAdmissionKey` and aggregate concurrency budget.
- Version labels, aliases, environment labels, or process boundaries must not multiply the physical organization's capacity.
- Every admitted request holds a bounded lease/permit before outbound Dynamics traffic. Loss or expiry of the host lease stops new admission and forces bounded drain/cancellation.

### CE 9.1 profile

- A CE 9.1 profile may select `Data8` or `OfficialCrm91Worker`; the immutable
  deployment profile chooses one ConnectorKind and the request cannot override
  it. `OfficialCrm91Worker` is a separately deployed .NET Framework 4.8
  process with an immutable Microsoft XRM tooling package lock.
- The current package baseline is
  `Microsoft.CrmSdk.XrmTooling.CoreAssembly` 9.1.1.65; any change requires a
  new package-lock/executable generation and complete verification.
- Direct Web API is not a CE 9.1 route or fallback. Data8 and Official Worker
  are separate profile-selected paths; a failure of either never retries the
  other.
- A CE 9.1 profile has its own version-specific operation registry, pool,
  credential reference, recycle policy, and real-server evidence.

### CE 8.2 profile

- A CE 8.2 profile may select `Data8` or `OfficialCrm82Worker`; the immutable
  deployment profile chooses one ConnectorKind. `OfficialCrm82Worker` is a
  separately deployed .NET Framework 4.8 process using a Microsoft-published
  XRM tooling version proven against the actual CE 8.2 target.
- The CE 8.2 package lock is selected independently. A CE 9.1 package that
  merely restores or compiles is not accepted as CE 8.2 compatibility proof.
- Data8 is a supported CE 8.2 route when the selected profile declares it. An
  Official Worker that lacks real evidence remains `evidence-pending`; it does
  not make the selected Data8 profile unsupported.
- CE 8.2 and CE 9.1 workers remain independently version-pinned processes; this
  task does not plan consolidation.

## Scenario: Disabled authentication-contact typed read boundary

### 1. Scope / Trigger

Apply this scenario when a ChurchReport capability needs a typed, read-only
contact lookup as a prerequisite to a future authentication or LINE flow.  The
capability can be introduced as a disabled local boundary, but it is not an
authorization, password-validation, Session, claims, traffic-cutover, CE
evidence, P7.5-removal, or P8-deployment claim.

### 2. Signatures

```csharp
Task<AuthenticationContactReadResult> RetrieveByAccountAsync(
    string profileAlias,
    string workloadSubjectId,
    string accountLookupValue,
    CancellationToken cancellationToken = default);

Task<AuthenticationContactReadResult> RetrieveByLineIdAsync(
    string profileAlias,
    string workloadSubjectId,
    string lineIdLookupValue,
    CancellationToken cancellationToken = default);
```

The only permitted operation IDs are
`auth.contact.retrieve.by.account` and `auth.contact.retrieve.by.lineid`.
Their Data8 queries are fixed `contact` QueryExpressions, include
`statecode = 0`, use `TopCount = 2`, and project only contact ID, account
locator, display name, and active state.  A caller cannot supply a query,
entity, owner, connector, organization, endpoint, credential, or profile as
routing authority.

### 3. Contracts

- The wire record, ProductClient DTO, result, JSON, log, and error category
  must never include `new_app_pass`, any password/hash, token, cookie, raw CRM
  `Entity`, raw response, endpoint, credential, or raw exception.
- The response envelope itself must enforce the same maximum of two records as
  the query.  The envelope must reject a third record before it reaches a
  ProductClient or retained response object; do not rely on the current
  connector's `TopCount` as the only retention bound.
- The response operation ID must match the typed public method before zero,
  one, or duplicate cardinality is classified.  A response-kind or operation-ID
  mismatch is `ProfileUnavailable`, never `NotFound` or `Ambiguous`.
- A disabled gate returns before option binding, ProfileAlias validation, host,
  handler, client, pool, lease, or CE I/O.  Gate=false does not use a legacy
  fallback.  The deployment-owned gate remains false until an independent,
  approved rollout task proves authorization, parity, capacity, cleanup, and
  rollback requirements.
- Cancellation propagates unchanged.  Timeout, transport failure, malformed
  response, secret detection, or ambiguity never retries or changes profile,
  transport, or legacy path.

### 4. Validation & Error Matrix

| Condition | Required result |
| --- | --- |
| Blank, malformed, or oversized lookup | `InvalidInput` before executor dispatch. |
| Zero safe records | `NotFound` with no contact DTO. |
| Two safe records | `Ambiguous` with no contact DTO. |
| Third record at response-envelope boundary | Reject envelope immediately; do not retain it. |
| Secret classification, response-kind mismatch, operation-ID mismatch, fault, or missing data | `SecretPresent` or `ProfileUnavailable`, no DTO and no raw detail. |
| Gate is false | `null` composition result before profile/host/client/I/O; legacy consumer remains untouched. |

### 5. Good / Base / Bad Cases

- **Good:** Interleaved A/B account and LINE requests create fresh immutable
  DTOs, preserve their respective markers, and retain no contact/identity
  state after the request.
- **Base:** A disabled deployment returns no typed client and performs zero CE
  work; a future owner can remove the registration to roll back the local
  candidate.
- **Bad:** Returning a password in a DTO, choosing the first duplicate contact,
  classifying an operation mismatch as a normal miss, accepting more than two
  records because a current connector happens to limit its query, or wiring the
  read directly into a Session/login flow.

### 6. Tests Required

- Verify the fixed operation IDs, parameter names, active filter, `TopCount =
  2`, safe projection, and envelope's third-record rejection.
- Verify blank input avoids executor dispatch; zero/duplicate/secret/
  response-kind/operation-ID outcomes fail closed; cancellation is forwarded
  unchanged; no secret-named public property serializes.
- Run interleaved A/B request-local isolation tests and gate=false source/
  bootstrap tests proving no bind/profile/host/client/I/O or legacy fallback.
- Before committing, run focused Dynamics and ChurchReport tests, the complete
  Release solution tests/build, UTF-8-without-BOM/CRLF/final-CRLF byte checks,
  and `git diff --check`.

### 7. Wrong vs Correct

#### Wrong

```csharp
// Query TopCount happens to be two, but a faulty transport can still retain
// an arbitrarily large projected list in the cross-layer response envelope.
return new OperationResponseData(records);
```

#### Correct

```csharp
// The wire envelope rejects the third record, matching the fixed query budget.
if (records.Count > 2)
{
    throw new ArgumentException("Authentication contact response exceeded the fixed limit.");
}
```

The second boundary prevents a later connector or test double from silently
weakening the data-retention and duplicate-detection contract.

### Data8 boundary

- `PowerPlatform.Dataverse.Client` in this repository is the third-party Data8 WS-Trust client, not Microsoft-owned source.
- It is a permanent ConnectorKind owned by this repository. The project owns
  its compatibility, lifecycle, security and verification obligations.
- The current `OnPremiseClient` implements `IOrganizationService` but not `IDisposable`; the existing `CrmConnectionPool` disposal cast therefore does not prove that its underlying WCF channels/factories are closed.
- A Data8 client used by `Embedded`, `DedicatedGateway`, or `CentralGateway`
  must remain behind the same profile/admission/pool/lease contract; products
  never create it directly.
- Data8 lifecycle ownership is not weakened by selecting it as the route. Each
  client/channel/factory/AD auth resource has a bounded owner, deterministic
  close-or-abort cleanup and a release baseline.
- Worker termination remains a cleanup boundary when an Official Worker is
  selected; it is never a Data8 fallback or a substitute for bounded request
  lifetime, health checks, process recycling policy, handle/socket baseline
  tests, and graceful shutdown.

### Embedded boundary

- Embedded is a permanent `ConnectionMode`, not a bypass or deferred-only
  placeholder. It executes the same RequestGuard, immutable profile resolution,
  admission, Connector Router and pool/lease lifecycle as Gateway modes; it
  omits only the HTTP hop.
- ChurchReport local development must retain `Embedded + Data8` alongside
  `DedicatedGateway + Data8`. A capability cannot hard-code a mode or Connector
  in product business code.
- Removing Embedded is a separate reviewed decision; it is not implied by
  choosing Dedicated or Central Gateway first.

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
| `ConnectionMode` is not exactly `Embedded`, `DedicatedGateway`, or `CentralGateway` | Startup validation fails. No outbound CRM traffic. |
| `ConnectionMode=DedicatedGateway` or `CentralGateway` without `ProfileAlias` or absolute HTTPS `Gateway.Endpoint` | Startup fails closed. No outbound CRM traffic. |
| `ConnectionMode=Embedded` provides a Gateway endpoint | Embedded ignores or rejects the endpoint according to its product validator; it never silently creates a Gateway client. |
| Product JSON contains CRM credentials, token, raw CRM URL, or SDK path | Configuration is rejected and secret scanning fails the build/release gate. |
| Gateway-owned success payload includes CRM hostname, Organization Service endpoint, `/api/data/` base path, package path, pipe/nonce/process metadata, credential, or token | Review and release fail. Return only the bounded product DTO. |
| Worker response contains an SDK type, raw Entity/Organization response, or absolute CRM URL | Reject the frame, terminate/quarantine the worker generation, and return a sanitized protocol failure. |
| Product requests an unknown or unauthorized alias/operation | Reject before profile resolution or outbound Dynamics traffic. |
| Authenticated but unauthorized request uses an invalid Content-Type | Return 403 before media-type validation and before body read. |
| Authorized operation request omits Content-Type or uses a non-approved media type/parameter/charset | Return 415 before request-body I/O, pooled-buffer rent, JSON parsing, executor invocation, or outbound Dynamics traffic. |
| Authorized operation request uses case-insensitive `application/json` with no parameters or one UTF-8 charset | Continue to the existing bounded byte/JSON validation path. |
| Any profile selects `WebApi`, `OfficialServiceClient`, `OfficialLegacyWorker`, or `TemporaryData8LegacyWorker` | Startup fails closed; no worker or Dynamics traffic starts. `Data8`, `OfficialCrm82Worker`, and `OfficialCrm91Worker` remain the only valid ConnectorKind values. |
| CE 8.2 or CE 9.1 profile uses the wrong worker/package-lock kind | Profile remains NotReady and the process is not published. |
| Data8 is loaded as an unbounded long-lived client without deterministic disposal proof | Release blocker. Use the bounded Data8 pool/lease ownership contract and fix lifecycle ownership first. |
| CE 8.2 and 9.1 SDK assemblies require conflicting versions in one worker | Keep separate version-pinned worker processes. Do not solve by unverified binding redirects. |
| Two aliases/environments resolve to the same physical organization with different admission keys | Startup fails closed until one shared organization capacity entry is configured. |
| Central or Dedicated host loses its runtime-host lease | Stop admitting new work, become NotReady, and drain/cancel within the configured fence. |
| Profile endpoint/version/organization identity does not match expected evidence | Profile remains NotReady; never auto-upgrade or auto-switch versions. |
| Embedded bypasses RequestGuard, profile resolution, admission, Router, or pool/lease lifecycle | Release blocker; fail closed before outbound Dynamics traffic. |
| A new C# type or lifecycle/concurrency/security method lacks detailed Traditional Chinese documentation | Review fails; add the missing intent, ownership, failure, and cleanup explanation before merge. |
| A changed source/config/test/script/document file is not valid UTF-8 or violates repository encoding rules | Verification fails before build/release completion. |

## 5. Good / Base / Bad Cases

### Good

- Ten products use the same ProductClient. Production points to the Central Gateway endpoint; ChurchReport development can point to a Dedicated Gateway on localhost or use Embedded composition. Both send the same operation request and receive the same result contract.
- `crm82` uses `OfficialCrm82Worker` while `crm91` uses
  `OfficialCrm91Worker`. Their executables, pinned SDK graphs, clients,
  credentials, WCF/static state, pipes, and pools are separate, while aggregate
  organization admission is enforced by physical organization identity.
- A worker package/profile generation changes only through validate, publish,
  drain, dispose/terminate, and remove. The old generation never receives new
  work after publication of the replacement.
- An authorized caller sends `Content-Type: application/json; charset=UTF-8`; Gateway validates the header before renting a body buffer, then applies the configured byte/depth/member limits.

### Base

- ChurchReport development selects `Embedded + Data8` or `DedicatedGateway +
  Data8` by deployment configuration; product behavior and typed contracts are
  the same. A future cloud deployment selects `CentralGateway + Data8`.
- An Official Worker profile can remain `evidence-pending` while a selected
  Data8 profile continues through its own capability evidence gate. Neither
  route is a retry target for the other.
- An unauthorized caller sends `Content-Type: text/plain`; Gateway returns 403 without reading the stream, so media-type behavior does not become an authorization oracle.

### Bad

- Product A directly references Data8 while Product B directly references `ServiceClient`, each with its own connection string and retry/pool implementation.
- A request selects one official worker, fails, and silently retries through
  Web API, Data8, another worker version, or another profile.
- One singleton pool contains clients for CE 8.2 and CE 9.1 or for multiple credentials/organizations.
- Each Dedicated Gateway assumes its local maximum is independent and collectively overloads the same Dynamics organization.
- A Dedicated Gateway reads production credentials directly from product-owned JSON.
- Gateway accepts `text/plain` or arbitrary `application/*+json` merely because the body happens to parse as JSON, or reads the body before deciding to return 415.

## 6. Tests Required

### Contract and configuration

- Assert Central endpoint and localhost endpoint produce identical ProductClient request payloads and result parsing.
- Assert only `Embedded`, `DedicatedGateway`, and `CentralGateway` are accepted `ConnectionMode` values.
- Assert `DedicatedGateway` and `CentralGateway` require a non-empty `ProfileAlias`, absolute HTTPS endpoint, and bounded API prefix; Embedded does not create an HTTP client.
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
- A `WebApplicationFactory` Kestrel boundary fixture configured through `WithWebHostBuilder` must override the Kestrel endpoint on that same test-owned configuration layer, then call parameterless `UseKestrel()` on the returned factory. For HTTPS tests use `https://127.0.0.1:0`: Kestrel does not support `https://localhost:0`, and a configuration-defined `Kestrel:Endpoints:*:Url` takes precedence over `WebHostDefaults.ServerUrlsKey`. Read the actual endpoint from `IServerAddressesFeature`, assert HTTPS plus loopback plus a positive OS-assigned port, and use that URI for the client. Never alter the product's fixed development `https://localhost:7244` setting or remove a Windows excluded-port range to make a test pass; fixed-port availability is a separate deployment listener-preflight gate.

### Isolation and capacity

- Assert `crm82` and `crm91` create different executable/package/runtime
  generation keys and cannot share assembly, client, credential, WCF/static,
  pipe, process, or result state.
- Assert aliases that resolve to the same physical organization share one aggregate admission budget.
- Run concurrent Central plus Local host tests and assert total in-flight Dynamics work never exceeds `AggregateMaxInFlight`.
- After replace-and-drain, assert retired processes, official clients, pipes,
  streams, timers, registrations, queues, request maps, worker proxies, process
  handles, and strong runtime references return to baseline.

### Conditional Official Worker real-server gates

The following gates apply only when a deployment profile explicitly selects an
Official Worker. They are not a prerequisite for a Data8-only capability and
the current P6 record remains `evidence-pending` until a future, independently
authorized task completes them.

- Official-worker `WhoAmI` or equivalent identity operation through
  website -> Gateway -> worker.
- Representative CRUD, Query/FetchXML, paging, and every approved action/function/organization request.
- Official-client authentication, cold start, reconnect, worker recycle, and
  Gateway restart.
- Fault injection for CRM timeout, worker crash/hang, malformed IPC response,
  pipe break, Gateway restart, profile reload, and forced termination.
- Long-running worker/process/pipe/thread/handle/private-bytes soak proving a
  stable post-warm-up and post-recycle baseline. The test must execute the same
  bounded request through a distinct warm-up window and measured window (the
  current WorkerTestHost gate uses 64 plus 64 requests, a 128-operation recycle
  budget, and samples only the measured window). Keep the relative trend
  threshold unchanged; do not force GC, skip the assertion, or treat a short
  process's initial CLR segment allocation as retention without a measured-window
  reproduction.

### Conditional Official Worker CE 9.1 real-server gates

- Official-worker identity operation, representative operations, paging,
  requests/actions, batch where used, reconnect, recycle, restart, and profile
  reload.
- Verify only the pinned `OfficialCrm91Worker` authentication and operation
  matrix against the actual target.

### Legacy-removal gates (Data8 is retained)

- Data8 is not removed by this contract. A Data8 profile remains a supported
  route for CE 8.2 and CE 9.1 when selected by deployment configuration.
- ChurchReport may remove its direct ToolUtility/CRM SDK production dependency
  only after the P7 capability matrix, Data8 lifecycle evidence, and rollback
  gates pass; that removal does not delete the repository-owned Data8 connector.
- Any future Official Worker replacement or removal must be a separately scoped
  task with its own CE/version evidence and rollback boundary.

### Documentation and encoding gates

- Enumerate every newly added or substantively modified Production/Test/Tool/Script file, type, method, and lifecycle member. Assert that C# uses substantive Traditional Chinese XML documentation, PowerShell uses comment-based help, and critical ordering branches contain explanatory Traditional Chinese comments covering ownership and failure consequences.
- Decode every added or modified source/config/test/script/SPEC/document file with a strict UTF-8 decoder; fail on invalid byte sequences, UTF-8 BOM, bare LF, bare CR, a missing final CRLF, or Unicode replacement characters.
- Verify `.editorconfig` still applies `charset = utf-8` and CRLF to the changed file types, run the changed-program Traditional Chinese comment audit, and run `git diff --check` to reject whitespace or line-ending damage.

## 7. Wrong vs Correct

### Wrong: invent deployment-specific execution modes

```json
{
  "ConnectionMode": "LocalGateway",
  "ProfileAlias": "crm91"
}
```

This contradicts the current `ConnectionMode` contract. `LocalGateway` is not
an enum value; local one-product hosting is `DedicatedGateway`.

### Correct: select a hosting mode and keep the connector deployment-owned

```json
{
  "ConnectionMode": "DedicatedGateway",
  "ProfileAlias": "crm91",
  "Gateway": {
    "Endpoint": "https://localhost:7244/",
    "ApiPrefix": "/v1"
  }
}
```

The same product build can select `Embedded + Data8`, `DedicatedGateway +
Data8`, or (in a later cloud deployment) `CentralGateway + Data8` without
changing business code or introducing request-time connector fallback.

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

### Wrong: collapse every connector into one mutable singleton

```text
Product -> one mutable client/singleton -> CE 8.2 and CE 9.1
```

### Correct: select a connector per immutable deployment profile

```text
Product -> Gateway contract -> Data8                -> CE 8.2 or CE 9.1
                            -> OfficialCrm82Worker  -> CE 8.2 (when selected)
                            -> OfficialCrm91Worker  -> CE 9.1 (when selected)
```

Data8 is a permanent supported ConnectorKind and is the current ChurchReport
local/cloud baseline (`Embedded + Data8`, `DedicatedGateway + Data8`, and the
future `CentralGateway + Data8`). Official Workers remain separately pinned,
non-fallback alternatives whose live evidence is required only when a deployment
explicitly selects them. A failed connector never silently changes profile,
version, or transport.

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
  selected Dedicated or Central host generation. The
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
# approved, and the clean output is the selected Dedicated or Central Gateway host directory.
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

This scenario applies when ChurchReport creates or reuses `DonationPaymentManager` from ASP.NET Session state, when logout/re-login resets identity, when `IMemoryCache` evicts a generation, or when the ChurchReport host stops. It also applies when Dedicated Gateway preflight is enabled from the ChurchReport primary DI container.

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
- `DynamicsGatewayPreflightHostedService` executes bounded `runtime.health.whoami` only when `DynamicsAccess:Package01FeeReadsEnabled=true` and mode is `DedicatedGateway` or `CentralGateway`. Disabled and Embedded paths are strict no-ops. The process host is a primary-DI singleton and owns the only ProductClient provider/HTTP generation.
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

## Scenario: Dedicated Gateway Development Configuration And Safe Runtime Verification

### 1. Scope / Trigger

This scenario applies when Visual Studio starts `SpeechMessage.Dynamics.Gateway`
or ChurchReport under the `Development` environment, or when a compiled Host DLL
is executed directly for local verification. It defines the fail-closed Dedicated
Gateway configuration, durable single-machine control-plane ownership,
product-to-Gateway boundary, and two independent runtime checks: Gateway health
and policy verification, plus feature-disabled ChurchReport browser verification.
With an approved non-production or explicitly approved CE profile, Visual Studio
may run the selected Data8 profile through Dedicated Gateway. An Official Worker
live run is a separate, future task and is not implied by this local scenario.

### 2. Signatures

```text
SpeechMessage.Dynamics.Gateway/appsettings.Development.json
  ConnectionStrings:DynamicsControlPlane
  DynamicsProfiles:Profiles:*:WorkerKind
  DynamicsProfiles:Profiles:*:WorkerExecutablePath
  DynamicsProfiles:Profiles:*:WorkerExecutableSha256
  DynamicsProfiles:Profiles:*:PackageLockId
  DynamicsProfiles:Profiles:*:OrganizationBaseUri
  DynamicsGateway:ActiveWorkloadBindingSet = Dedicated
  DynamicsGateway:WorkloadBindingSets:Dedicated[*]

SpeechMessage.Dynamics.Gateway/appsettings.json
  DynamicsGateway:ActiveWorkloadBindingSet = Central
  DynamicsGateway:WorkloadBindingSets:Central[*]

SpeechMessageProducts.ChurchReport/appsettings.Development.json
  DynamicsAccess:ConnectionMode
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
- A real CE local run replaces that non-routable target only through one
  approved Dedicated Gateway overlay/profile generation. Its paths need to remain
  stable for that generation, but they do not need to be a final Central/IIS
  deployment directory. The Dedicated Gateway verifies the selected profile
  manifest, package lock, ConnectorKind, organization identity, and secret
  reference before its connector starts; the overlay and any Worker profile remain outside product
  JSON and are removed or retained only by their explicit local deployment owner.
- ChurchReport Development may use `ConnectionMode=DedicatedGateway`,
  `ProfileAlias=sunnyvalechback`, HTTPS loopback, and API prefix `/v1`, or it may
  use `ConnectionMode=Embedded` with the same Data8 profile. The product does not
  select or duplicate the CE version or ConnectorKind; the deployment-owned
  profile selects Data8 (or a separately authorized Official Worker).
  `Package01FeeReadsEnabled=false` remains the authoritative consumer-traffic
  gate.
- Feature-disabled ChurchReport startup must not create ProductClient, HTTP handler/pool, token cache, timer, or Dynamics preflight/operation traffic. Development configuration alignment alone does not enable Package 1.
- Dedicated Gateway authentication uses server-established Windows Negotiate identity plus server-owned workload bindings. Client JSON and spoofable headers never select principal, workload, alias permission, or operation permission.
- A syntactically valid authenticated Windows SID is authoritative. When it is present, authorization performs only the SID lookup; an unmapped SID fails closed and must not fall back to a matching principal name. Exact principal-name fallback is allowed only when the authenticated principal has no usable SID at all. This prevents a newly created account with the same name but a different SID from inheriting the retired account's workload permissions.
- `DynamicsGateway:ActiveWorkloadBindingSet` is the deployment-owned selector and is mandatory. The authorizer enumerates direct children under `DynamicsGateway:WorkloadBindingSets`, resolves exactly one case-insensitive matching set, and materializes only that set. It must not concatenate the selector into a configuration path or enumerate all sets.
- Central, Dedicated, and Testing binding sets may coexist in the merged configuration because they are separate named subtrees. `appsettings.Development.json` changes only the selector to `Dedicated`; therefore .NET configuration's numeric-array and nested-leaf merge behavior cannot import a Central principal or Central operation into the Dedicated frozen authorization snapshot.
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
| A Visual Studio Dedicated Gateway profile is approved and its manifest/overlay/connector chain is valid | Start only the selected deployment-owned ConnectorKind and run the fixed approved operation matrix against that exact CE target. |
| A Visual Studio Dedicated Gateway profile is absent, malformed, hash-drifted, unapproved, or owns unstable paths | Remain NotReady; do not substitute a fake Worker, Web API, another ConnectorKind, Central profile, or guessed identity. |
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
- Bad: replace the checked-in Development CRM target with a routable URL merely to make a smoke test green. Use one approved Dedicated Gateway overlay instead, with the same immutable profile rules as a Central host.
- Bad: set `Package01FeeReadsEnabled=true` to force preflight evidence before real CE 8.2/9.1 and rollback gates exist.
- Bad: define Central and Local entries under one `WorkloadBindings` array and assume a later provider replaces the collection; numeric leaf merging can preserve both entire bindings and nested operation entries.
- Bad: a valid but unmapped SID is allowed to continue into principal-name lookup. Account-name reuse can then grant a different Windows security authority the old account's alias, operation, capacity, and audit identity.
- Good: direct DLL verification runs each host from its own project directory;
  Gateway and ChurchReport checks are separate, and cleanup stops only the
  listener owner whose command line matches the expected DLL.
- Base: the development Gateway certificate is accepted by CLI loopback verification only; the in-app browser validates ChurchReport and the authorization redirect while Gateway browser proof remains gated on certificate trust.
- Bad: run the Gateway DLL from the solution root, observe a missing-profile exception, and modify profile JSON or weaken fail-closed validation instead of correcting the content root.

### 6. Tests Required

- Configuration precedence tests assert the LocalDB instance, dedicated control-plane database, integrated authentication, bounded pool, bounded timeout, non-routable CRM target, deployment-owned ConnectorKind/profile, ChurchReport Dedicated Gateway alias/prefix, absence of a product-side CE version selector, and Package 1 false state.
- Load real base plus Development JSON, authenticate with the Central binding principal, and assert Local authorization returns `unmapped-principal` with zero executor/outbound work. This regression must fail against a shared `WorkloadBindings` array implementation.
- Authenticate with a syntactically valid but unmapped SID plus a principal name that otherwise matches an authorized binding. Assert 403, `unmapped-principal`, zero executor calls, and no materialized execution request. Separately assert a principal with no usable SID still succeeds through the exact principal-name compatibility binding.
- Assert a missing selector, leading/trailing whitespace, `*` and `?` wildcard text, an unknown name, a delimiter-bearing value such as `Local:0`, scalar-only, scalar-plus-children, and a true childless JSON set all fail Host startup. Assert exact set selection is case-insensitive. Testing factories must select an explicit nonempty `Testing` set rather than inheriting `Central`.
- Execute the opt-in live LocalDB durable coordinator contract against the explicitly provisioned database and assert lease/fencing behavior without auto-provisioning.
- Start the real Development Gateway and verify `/health`, `/ready`, 401 anonymous, authorized workload catalog, 403 wrong alias, 403 unauthorized operation, and controlled no-fallback connector failure.
- With an approved Dedicated Gateway profile, start the Gateway from the
  Visual Studio/project-owned local host, then prove the website -> localhost
  Gateway -> selected Data8 profile -> CE identity/read/paging matrix. Assert
  that connector/process/resource counters return to baseline after controlled
  shutdown. If an Official Worker is selected, its READY/read-only evidence is
  governed by a separate future task; it does not become a Data8 prerequisite.
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
    "ConnectionMode": "DedicatedGateway",
    "ProfileAlias": "crm82",
    "Gateway": {
      "Endpoint": "https://localhost:7244",
      "ApiPrefix": "/v1"
    },
    "Package01FeeReadsEnabled": false
  }
}
```

This configures the Dedicated Gateway boundary for development while keeping
consumer traffic fail closed until the capability and P7 rollout evidence
explicitly unlock it.

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

### Embedded and Dedicated Gateway are parallel local paths

Embedded gives Visual Studio the lowest-latency same-process route while still
using Guard→Profile→Admission→Router→Pool. Dedicated Gateway gives the same
typed product contract a separately observable process boundary. ChurchReport
must keep both `Embedded + Data8` and `DedicatedGateway + Data8` selectable;
the choice belongs to deployment configuration, not product business code.

### Compatibility is provided at the Gateway contract, not by one universal SDK

CE 8.2 and CE 9.1 share the product-facing API and policy model. They do not have to share a transport implementation, SDK version, authentication flow, token/WCF state, or physical connection pool.

### Data8 is a permanent profile-selected ConnectorKind

Data8 executes directly under .NET 10 and is the ChurchReport P7 local route
and first P8 cloud route. It is not a request-time fallback from an Official
Worker, and an Official Worker is not a fallback from Data8. Any future change
to this decision requires an explicit spec/plan update and independent
capability evidence.

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

當 Gateway、Dedicated Gateway 或測試建立 `SqlRuntimeHostSlotCoordinator` 時，
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

## Scenario: Capability-scoped CE write evidence authorization

### 1. Scope / Trigger

This contract applies whenever a Gateway migration needs a live CE 8.2 or CE 9.1
write, Action, or Function operation for compatibility, parity, or rollout
evidence. It separates environment isolation from operation authorization and
prevents a safe development Organization from becoming an unbounded test target.

### 2. Signatures

```text
CapabilityWriteEvidencePlan = {
  operationId,
  ceVersion,
  profileAlias,
  fixtureOwner,
  allowedMutations,
  precondition,
  idempotencyPolicy,
  cleanupPlan,
  reconciliationPlan,
  evidenceProjection
}
```

The plan is produced from the P7.0 support matrix and is an activation input for
the P7.2 child. `fixtureOwner` is the sole owner of creation, verification,
cleanup, and reconciliation for that operation family.

### 3. Contracts

- An Organization being separate from production is an environment-level
  feasibility fact, not authorization for arbitrary writes.
- P6 remains read-only for business semantics. P6 proves ConnectorKind／CE
  version routing, Official Worker process/IPC, Router／Pool／Lease／admission,
  IFD identity, and deterministic cleanup. P7.2 proves product write semantics.
- A live write is allowed only when P7.0 marks that CE/version/capability
  combination `required` and the child task contains a bounded fixture owner,
  allowed mutation set, precondition, idempotency policy, cleanup, and
  reconciliation plan.
- CE 9.1 `sunnyvalechback` may be a test-owned fixture host after operator
  confirmation; a single test member cannot be reused as an implicit fixture
  for unrelated financial, appointment, permission, attachment, or destructive
  operation families.
- CE 8.2 write evidence is required only when the support matrix says
  `required`; `unsupported` combinations fail closed before dispatch and are
  recorded as matrix outcomes, not as failed live tests.
- An ambiguous timeout after dispatch is reconciled before any retry. Writes are
  never blindly replayed. Cleanup is bounded, repeatable, and owned by the same
  operation family; unresolved cleanup keeps that slice No-Go.
- Evidence contains only sanitized operation/category/status/resource-baseline
  fields. It must not contain SDK objects, raw Entity／OrganizationRequest,
  endpoint, OrganizationId, credential, token, cookie, or complete personal data.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Environment is isolated but no operation-specific plan exists | Reject before live write; retain the child in planning/no-go. |
| P7.0 marks the combination `unsupported` | Fail closed before dispatch; record the stable reason and do not seek another connector/version. |
| Fixture owner, allowed mutation, cleanup, or reconciliation is missing | Do not activate the slice; produce a scoped operator handoff. |
| Precondition does not identify a test-owned record | Reject before mutation; do not guess or reuse an opaque record. |
| Write times out after dispatch | Stop automatic retry; run the bounded reconciliation plan and preserve an ambiguous result if unresolved. |
| Cleanup fails or resource counters do not return to baseline | Keep the slice No-Go, retain the cleanup owner, and stop further writes in that family. |
| Evidence contains secret, SDK, endpoint, or full payload data | Reject the evidence artifact and remove the sensitive projection before any completion claim. |

### 5. Good / Base / Bad Cases

- Good: P7.0 marks a CE 9.1 member-create capability required, names one
  test-owned member fixture and owner, verifies the precondition, performs one
  idempotent operation, reconciles the result, cleans it up, and records a
  sanitized baseline.
- Base: a CE 8.2 capability is unsupported for the first ChurchReport product;
  the matrix records `unsupported`, the dispatcher refuses it, and CE 9.1
  evidence is not incorrectly copied across versions.
- Bad: infer that any financial or appointment write is safe because
  `sunnyvalechback` is separate, or reuse one test member across unrelated
  operation families without an owner and cleanup contract.

### 6. Tests Required

- Validator tests reject a write plan that lacks any required field and accept a
  complete plan with stable, deterministic output.
- Contract tests prove an `unsupported` CE/version combination performs zero
  connector, admission, and outbound invocations.
- Live-bridge tests (when explicitly authorized) assert exactly one
  test-owned fixture owner, bounded idempotency/reconciliation, cleanup after
  success and failure, and no secret or full-payload fields in evidence.
- Fault tests inject timeout-after-dispatch and cleanup failure; assertions prove
  no blind replay, the family remains No-Go, and resource/admission counters do
  not report a false clean baseline.
- Scope tests prove P6's allowlist remains read-only and that P7.2 is the only
  path allowed to run the capability-specific live write evidence.

### 7. Wrong vs Correct

#### Wrong

```text
sunnyvalechback is non-production, so run every ChurchReport write and delete
whatever was created if the test looks wrong.
```

#### Correct

```text
P7.0 matrix -> required CE/profile -> operation-specific fixture owner
  -> bounded mutation + idempotency -> reconcile -> cleanup -> sanitized evidence
```

The correct path preserves the distinction between connector readiness and
business semantics, prevents cross-family test data leakage, and keeps an
ambiguous or unclean write from becoming a false Green gate.

## Scenario: Operation registry and Phase 0 matrix synchronization

### 1. Scope / Trigger

This contract applies whenever a Dynamics capability is added or its typed
request, response discriminator, server-owned template, encoding context,
page/byte limit, or CE evidence changes. It keeps the compiled registry, the
machine-readable Phase 0 matrix, its JSON schema, and P7 fixture artifacts as
one cross-layer contract.

### 2. Signatures

The registry definition is the executable source for these fields:

```text
OperationDefinition(
  capabilityOperationId, operationKind, templateKind, templateId,
  responseKind, maximumPageCount, maximumPageBytes,
  maximumCumulativeResponseBytes, maximumResultItemCount,
  dataClassification, auditRequirement, idempotencyClass, parameters[])
```

The corresponding matrix row must contain the same fields under
`serverOwnedTemplate`, `typedParameters`, `encodingContexts`,
`versionEvidence`, `responseKind`, and the four maximum fields. The P7.2
fixture matrix records the exact byte-level SHA-256 of the source matrix in
`sourceMatrixSha256`.

### 3. Contracts

- Every current `Package01OperationRegistry` definition has exactly one
  `normalizedCallSites` row with the same operation ID; no registry operation
  may be silently absent from the matrix.
- `serverOwnedTemplate.templateHash` is generated from the compiled registry
  material; hand-copying an earlier hash is invalid.
- A row with a typed response must use a closed `responseKind` enum and include
  all four conservative page/byte limits. If a registry parameter uses an
  encoding context such as `server-enum`, the schema enum must declare it.
- After changing the source matrix, recompute `sourceMatrixSha256` over the
  exact UTF-8/no-BOM/CRLF bytes. A stale hash is a fixture-artifact failure,
  not a reason to bypass the registry agreement gate.
- CE-version evidence remains independent per row. `metadata-only` or
  `unsupported` evidence cannot be promoted to live `passed` by copying the
  other CE version.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Registry operation has zero or multiple matrix rows | Agreement test fails; dispatch remains fail-closed. |
| Template ID, hash, parameter list, encoding, response kind, or limits differ | Agreement test fails; do not update the consumer or enable rollout. |
| Matrix response discriminator is absent from the schema enum | Schema/agreement validation fails before live evidence. |
| Registry introduces an encoding context missing from the schema | Schema validation fails; add the closed enum value before proceeding. |
| P7 fixture artifact has a stale source-matrix SHA-256 | Fixture preflight is invalid and no CE operation starts. |

### 5. Good / Base / Bad Cases

- Good: add the registry definition, update one matrix row and schema enum,
  regenerate the template hash, refresh the fixture source hash, then run the
  agreement tests before any CE evidence.
- Base: a CE 8.2 row remains `unsupported` while the CE 9.1 row is
  `metadata-only`; both rows still match the registry contract.
- Bad: change only C# registry code and leave an older matrix hash or omit the
  new response discriminator because the focused connector tests pass.

### 6. Tests Required

- `OperationRegistryAgreementTests.Compiled_registry_exactly_matches_enforced_phase0_matrix_rows`
  asserts the complete field-for-field registry/matrix agreement.
- `OperationRegistryAgreementTests.Matrix_response_policy_is_present_for_exactly_current_registry_rows`
  asserts the closed response row set and all four limits.
- `OperationRegistryAgreementTests.Matrix_schema_declares_closed_response_policy_contract`
  asserts that every compiled response discriminator and encoding context is
  declared by the schema.
- Fixture preflight tests assert that `sourceMatrixSha256` equals the current
  source bytes and reject stale artifacts without connector or CE calls.

### 7. Wrong vs Correct

#### Wrong

```text
Add an operation to Package01OperationRegistry and rely on connector tests;
leave the Phase 0 row, schema enum, and P7 fixture hash unchanged.
```

#### Correct

```text
Registry -> matrix row -> schema enum/limits -> fixture source hash
         -> agreement tests -> only then live CE evidence
```

## Scenario: P6.2 local IFD Official Worker profile input

### 1. Scope / Trigger

This contract applies when a Lenovo or future deployment host must turn
operator-supplied, non-secret CE 8.2/9.1 IFD metadata into the single local
profile document consumed by both the Official Worker readiness probe and the
deployment-overlay generator. It prevents a schema mismatch from producing a
permanent false No-Go and prevents secrets or mutable endpoint choices from
crossing into source control, task evidence, Gateway APIs, or IPC payloads.

### 2. Signatures

```powershell
New-DynamicsOfficialWorkerProfileInput.ps1 `
  -ManifestPath <official-worker-manifest.json> `
  -Crm82OrganizationBaseUri <canonical-https-root-uri> `
  -Crm82OrganizationName <identifier> `
  -Crm82ExpectedOrganizationId <guid> `
  -Crm82HomeRealm <https-uri> `
  -Crm82CredentialTarget <credential-target-name> `
  -Crm82ProfileGenerationId <generation-id> `
  -Crm91OrganizationBaseUri <canonical-https-root-uri> `
  -Crm91OrganizationName <identifier> `
  -Crm91ExpectedOrganizationId <guid> `
  -Crm91HomeRealm <https-uri> `
  -Crm91CredentialTarget <credential-target-name> `
  -Crm91ProfileGenerationId <generation-id> `
  -Json

Test-DynamicsOfficialWorkerDeploymentReadiness.ps1 `
  -ManifestPath <official-worker-manifest.json> `
  -ProfileInputPath "$env:LOCALAPPDATA\SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json" `
  -ExpectedExecutionIdentity <windows-identity> `
  -Json
```

The generated top-level shape is exact and versioned:

```json
{
  "schemaVersion": 1,
  "profiles": [
    {
      "profileAlias": "crm82|crm91",
      "workerKind": "OfficialCrm82Worker|OfficialCrm91Worker",
      "packageLockId": "manifest-derived",
      "profileGenerationId": "operator-supplied-non-secret-id",
      "organizationBaseUri": "https://canonical-host/",
      "organizationName": "safe-identifier",
      "expectedOrganizationId": "non-placeholder-guid",
      "authentication": "Ifd",
      "identity": {
        "mode": "WindowsCredentialReference",
        "reference": "credential-target-name",
        "homeRealm": "https://..."
      }
    }
  ]
}
```

### 3. Contracts

- One local document has exactly `schemaVersion: 1` and exactly two profiles:
  `crm82` maps to `OfficialCrm82Worker`/CE 8.2 and `crm91` maps to
  `OfficialCrm91Worker`/CE 9.1. Readiness and deployment must both accept this
  same versioned shape; neither tool may maintain a different top-level schema.
- `workerKind` and `packageLockId` are derived from the immutable manifest, not
  typed by an operator. The manifest must declare exactly both approved Worker
  kinds, their matching CE version, safe package-lock identifiers, schema
  version 1, and a disabled feature gate.
- Authentication is case-sensitive `Ifd`; identity is always
  `WindowsCredentialReference` with an HTTPS home realm. `HostIdentity` and
  Active Directory are not fallback forms for an IFD target.
- `organizationBaseUri` is the canonical IFD HTTPS host root, exactly
  `https://canonical-host/`: it has a DNS host, lowercase IDN host spelling,
  an explicit final `/`, no organization path, no query/fragment/user info,
  and no explicit default port. `organizationName` carries the organization
  separately. `homeRealm` remains a safe full HTTPS URI because its AD FS path
  is part of its identity contract.
- The document is created only at
  `%LOCALAPPDATA%\SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json`
  using atomic create-new semantics. Existing output is never overwritten.
- The document may contain non-secret deployment metadata locally. Credential
  values remain solely in Credential Manager or another approved secret owner.
  The generator never accepts, reads, validates, serializes, or logs a
  password, token, cookie, connection string, private key, or credential blob.
- `-Json` output is a sanitized `{ schemaVersion, outcome, profileCount }` on
  success or `{ schemaVersion, outcome, reason }` on failure. It must not echo
  URI, Organization ID, home realm, credential-target name, local path, or raw
  exception text.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Profile document omits `schemaVersion`, adds an unexpected top-level field, or version is not integer `1` | Readiness returns No-Go with profile-input validation failure; no credential lookup or CE action starts. |
| Manifest lacks either approved Worker, has a wrong CE pairing, unsafe package-lock ID, unsupported schema, or enabled gate | Generator fails before creating a profile file. |
| `organizationBaseUri` is not the exact canonical HTTPS root, or another URI is not absolute HTTPS DNS / contains user info/query/fragment, organization name/generation/target name is unsafe, or GUID is malformed/placeholder | Generator fails before creating a profile file. |
| Credential target is absent for the same Windows identity | Readiness returns sanitized No-Go; it never reads a credential value. |
| Output file already exists or another process wins the create-new race | Generator refuses the write and leaves the existing bytes unchanged. |
| Unknown secret parameter is supplied | PowerShell parameter binding fails and no profile file is created. |
| Readiness outcome is not `go` | Do not publish overlay, start Worker, execute CE operations, enable a feature flag, or advance P7/P8. |

### 5. Good / Base / Bad Cases

- Good: an operator enters the two approved IFD metadata sets locally, the
  generator derives both locks from the manifest, emits no metadata to the
  console, and readiness returns `go` only after both same-user target names
  are resolvable.
- Good: a browser navigation address may contain an organization path, but the
  operator enters only its confirmed canonical IFD host root in
  `organizationBaseUri` and enters the organization name in its own field.
- Base: the CE 9.1 development target is prepared while CE 8.2 approval is
  still absent. The generator is not run with a guessed CE 8.2 target; P6 stays
  No-Go and the scoped operator handoff records the missing authority.
- Bad: store credential values in the profile JSON, choose ConnectorKind or CE
  version per request, place the organization path in `organizationBaseUri`,
  hand-edit a package-lock identifier, or overwrite an existing local profile
  file to force a Green probe.

### 6. Tests Required

- `Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1` must construct the
  versioned top-level document and prove unresolved targets report only
  sanitized No-Go reasons.
- `New-DynamicsOfficialWorkerProfileInput.Tests.ps1` must prove valid manifest
  derivation, exact versioned shape, strict UTF-8 without BOM/CRLF text,
  sanitized output, HTTP URI rejection, organization-path base-URI rejection,
  missing Worker rejection, unknown password parameter rejection, and
  byte-identical create-new refusal.
- Existing deployment, publish, and compatibility PowerShell harness tests must
  remain green, followed by the focused/full Dynamics tests, ChurchReport tests,
  and Release solution build.

### 7. Wrong vs Correct

#### Wrong

```text
The profile generator accepts https://crm.example.test/organization/ while the
deployment generator later requires https://crm.example.test/, leaving a local
profile that can never produce deployment material.
```

#### Correct

```text
Approved non-secret IFD metadata with canonical HTTPS roots + immutable manifest
  -> atomic local { schemaVersion: 1, profiles }
  -> same-user sanitized readiness probe
  -> go only before overlay/Worker/CE evidence is allowed
```

The correct path preserves deployment ownership, profile-generation isolation,
credential secrecy, deterministic file ownership, and the no-request-time-
fallback rule across Local and future Central Gateway hosts.

## 10. P7.1 Data8 read evidence handoff

### 1. Scope / Trigger

This contract applies to the Lenovo-only, operator-triggered P7.1 Package01
read evidence lane. It is intentionally narrower than P7.4 cutover: the
selected ChurchReport development profile is `sunnyvalechback`, CE 9.1,
`Embedded + Data8`; Dedicated Gateway live listener evidence remains a P7.4
responsibility. The handoff must prove the six typed read operations without
changing the product consumer flag or starting an Official Worker.

### 2. Signatures

The Windows PowerShell 5.1 entry point is:

```text
docs/scripts/Invoke-Package01Data8ReadEvidence.ps1
  -RepositoryPath <worktree-root>
  -ContactId <test-owned-guid>
  -DedicationBookingId <test-owned-guid>
  -DiscipleLessonId <test-owned-guid>
  -StartDate <ISO-8601-UTC>
  -EndDate <ISO-8601-UTC>
  -PaidPeriod <bounded-string>
  -Json
```

The child test is opt-in only when `SPEECHMESSAGE_P7_1_LIVE=1` and the same
process has `CRM_PASSWORD` plus all six `P7_1_*` fixture variables. The script
itself never accepts a password or credential-reference argument: after all
repository and fixture validation succeeds, it reads only its fixed local
Windows Generic Credential target, sets `CRM_PASSWORD` in its own short-lived
process environment for the spawned child, and restores the prior environment
in `finally`.

### 3. Contracts

Before repository or fixture validation can take an early-exit path, the script
snapshots every process environment variable it might later override. It sets
the fixture variables and ephemeral password only in its own short-lived
process environment, so the spawned child inherits them; `finally` restores
every prior value even when validation fails. The Generic Credential native
buffer has a single owner: `CredRead` is followed by `CredFree` in `finally`,
and any managed character buffer is cleared before the helper returns. Temporary
TRX deletion is best-effort and must not throw from `finally`, because it cannot
block restoration or secret clearing. Neither the target name nor the secret
may appear in stdout, stderr, TRX, task artifacts, or JSON. The fixed output
shape is:

```json
{
  "schemaVersion": 1,
  "outcome": "go|no-go|error",
  "reason": "fixed-sanitized-reason",
  "mode": "Embedded",
  "profileAlias": "sunnyvalechback",
  "ceVersion": "9.1",
  "operationExecuted": false,
  "featureFlagChanged": false,
  "operations": [
    { "operationId": "allowlisted-id", "status": "succeeded|failed", "rowCount": 0 }
  ]
}
```

The output must not contain a password, token, cookie, endpoint, Organization
GUID, account name, CRM payload, TRX path, or raw exception. The six operation
IDs are fixed by `Package01OperationRegistry`; no generic CRUD, arbitrary
FetchXML, endpoint, profile, CE version, or ConnectorKind is accepted.

Each CRM page independently consumes `OperationDefinition.MaximumPageBytes`.
The projected record is first counted against that page-local budget and then
against `MaximumCumulativeResponseBytes`; a page that fits the four-page total
but exceeds its own 64 KiB limit must fail closed before any partial DTO is
returned. Page counters are request-local and never cross a lease, profile, or
operation boundary.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Worktree or test project is missing | Emit `error/repository-invalid`; do not start `dotnet` or CE. |
| Any fixture GUID/date/period is missing, malformed, empty, or unbounded | Emit `error/fixture-input-invalid`; do not start `dotnet` or CE. |
| Fixed Generic Credential is unavailable, malformed, non-Generic, or unreadable | Emit `no-go/credential-unavailable`; do not prompt, log the native failure, start `dotnet`, or retry. |
| Child test exceeds 180 seconds | Kill the bounded child process and emit `no-go/test-timeout`; do not retry in the same handoff. |
| TRX has zero, multiple, malformed, or unexpected evidence markers | Emit `error/evidence-result-unavailable`; do not infer success from a zero exit code. |
| Any of six operations fails | Emit `no-go/one-or-more-operations-failed`; keep the consumer flag off and preserve the operation classifications. |
| All six operations succeed and cleanup completes | Emit `go` with exactly six sanitized operation entries. |

### 5. Good / Base / Bad Cases

- Good: an approved test-owned fixture is available and the fixed Generic
  Credential exists for the same Windows identity; the script supplies the
  secret only to its bounded child process, executes six read-only typed calls,
  clears/restores the environment, and emits one sanitized marker after runtime
  disposal.
- Base: fixtures are not yet available. The script remains runnable as a
  bounded handoff, but P7.1 evidence stays `evidence-pending`; no P6 profile is
  rebuilt and no broad retry is attempted.
- Bad: put a password or CRM URL on the command line, enable
  `Package01FeeReadsEnabled`, use a production member as a guessed fixture, or
  treat a skipped test / missing TRX marker as `go`.

### 6. Tests Required

- `Invoke-Package01Data8ReadEvidence.Tests.ps1` must prove strict UTF-8/CRLF,
  invalid repository fail-closed behavior, fixed JSON output, no feature-flag
  mutation, fixed-target `CredRead`/`CredFree` ownership, environment snapshot
  before every validation early exit, non-throwing temporary cleanup, a public
  native type with no compiler warning on stdout, and absence of network or
  Official Worker behavior outside the bounded child test.
- `OnPremiseData8ConnectorClientFactoryTests` must inject an offline fee and
  stor-lesson page that exceeds `MaximumPageBytes` while remaining below the
  cumulative budget, then assert both branches fail closed and dispose their
  fake service exactly once.
- `LivePackage01Data8ReadEvidenceTests` must remain skipped unless all explicit
  opt-in variables exist, execute each fixed operation at most once per lane,
  assert the CE 9.1 profile, and dispose the runtime/logger owner before
  emitting its marker.
- The focused and full Dynamics/ChurchReport suites, Release build, `git
  diff --check`, and byte-level encoding checks are required before commit.

### 7. Wrong vs Correct

#### Wrong

```text
Read CRM_PASSWORD from a command-line parameter, print dotnet stderr, accept a
skipped live test as success, or let a fixture exception decide the next
profile/connector dynamically.
```

#### Correct

```text
fixed sunnyvalechback + typed fixture environment
  -> fixed local Generic Credential, temporary process-only password
  -> bounded opt-in child test
  -> six allowlisted Data8 reads
  -> dispose runtime/logger, clear/restore process environment and native buffers
  -> one sanitized JSON line
```

This keeps real CE evidence separate from registry/executor/consumer state and
preserves the permanent Data8 contract for both Embedded and Gateway hosts.

## Scenario: Archived P7 coverage-validator repository discovery

### 1. Scope / Trigger

This contract applies to a P7 task-local validator that regenerates or validates
coverage artifacts after its Trellis task has moved from an active task path to
`archive/<year-month>`. The archive move changes directory depth but must not
change the validator's source-of-truth repository or cause it to touch CE.

### 2. Signatures

```text
python <p7-task-directory>/validate_coverage.py --build
python <p7-task-directory>/validate_coverage.py
```

The validator discovers the repository root by walking upward to the directory
that contains both `.trellis` and `.trellis/tasks`; it does not derive the root
from a fixed `Path.parents[n]` offset.

### 3. Contracts

- `--build` may read only repository-versioned source and task-local JSON, then
  rewrite only the validator's task-local artifacts with deterministic UTF-8,
  CRLF JSON.
- The same invocation must work before and after task archival. Archive year,
  month, and task-name depth are not inputs to repository discovery.
- If no structural `.trellis/tasks` anchor exists, discovery fails before any
  source scan, artifact write, network call, credential lookup, or CE action.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Active task path | Locate the enclosing repository root and produce the normal deterministic report. |
| Archived task path | Locate the same enclosing repository root and preserve identical validation semantics. |
| Fixed parent offset resolves to `.trellis/tasks` | Reject that implementation; it is not a repository root. |
| No `.trellis/tasks` ancestor | Raise a deterministic local error before reading or writing artifacts. |

### 5. Good / Base / Bad Cases

- Good: an archived P7.0 validator runs `--build`, reads current approved source
  under the repository root, writes its own manifest, and emits a valid report.
- Base: the task is still active; anchor discovery selects the same root without
  depending on the active path's depth.
- Bad: `TASK_DIRECTORY.parents[2]` is treated as permanent; archival shifts the
  offset and source scans look under `.trellis/tasks` instead of the repository.

### 6. Tests Required

- A direct contract test asserts the discovered root contains `.trellis` and a
  known repository source after the task resides under `archive/<year-month>`.
- The full validator test suite runs `--build` from the archived task directory
  and verifies the worker allowlist and P7.2 activation candidate contracts.
- A normal validation run remains deterministic and reports no errors for the
  committed P7.0 matrix.

### 7. Wrong vs Correct

#### Wrong

```python
REPOSITORY_ROOT = TASK_DIRECTORY.parents[2]
```

#### Correct

```python
REPOSITORY_ROOT = find_repository_root(TASK_DIRECTORY)  # locate `.trellis/tasks`
```

The structural anchor keeps archive portability deterministic, prevents an
incorrect source baseline from being generated, and preserves the P7 gate's
offline, fail-closed boundary.

## Scenario: P7.2 aggregate parity and ephemeral live-evidence handoff

### 1. Scope / Trigger

This contract applies when a typed Data8 capability projects a Dynamics
aggregate whose legacy business result excludes records with a null grouping
attribute, or when an opt-in live test must return one sanitized evidence
record to a Windows PowerShell handoff and the test runner does not reliably
preserve that record in TRX standard output. It prevents a null aggregate row
from changing legacy counts and prevents missing test-runner output from being
misreported as either Green evidence or an unknown CE failure.

### 2. Signatures

The current aggregate template and evidence boundary are:

```text
memberinfo.contact.count.ungrouped.commitment
  -> contact.customertypecode not-null
  -> contact.customertypecode != <server-resolved closed status>
  -> group by contact.customertypecode as commitmenttype
  -> count contactid as rowcount

P7_2_B2_EVIDENCE_PATH=<OS-temp-root>/speechmessage-p7-2-profile-<nonce>/P72Data8B2Evidence.json
```

The evidence file contains exactly one bounded, sanitized object:

```json
{
  "schemaVersion": 1,
  "outcome": "go|no-go",
  "reason": "fixed-sanitized-category",
  "operationId": "memberinfo.contact.count.ungrouped.commitment",
  "profileAlias": "sunnyvalechback",
  "deploymentProfileAlias": "crm91",
  "ceVersion": "9.1",
  "connector": "Data8",
  "preflightOnly": false,
  "operationExecuted": true,
  "parityState": "confirmed|mismatch|unknown",
  "rowCount": 0,
  "featureFlagChanged": false
}
```

### 3. Contracts

- If legacy projection ignores a null grouping value, the server-owned Data8
  template must add an explicit `not-null` condition before the `groupby`.
  Projection must still reject a row whose required alias is absent or whose
  aliased value is null; the filter prevents the known semantic mismatch but
  does not weaken fail-closed response validation.
- Aggregate parity compares bounded raw OptionSet value/count pairs. It does
  not compare translated labels, accept caller-supplied FetchXML, or silently
  turn a missing alias into a synthetic zero/null bucket.
- TRX remains suitable for test outcome and sanitized diagnostics, but a live
  lane may use an ephemeral evidence file when its stdout marker is not
  deterministically retained. A zero child exit code without a valid evidence
  object is still `evidence-result-unavailable`, never Green.
- The handoff creates the parent directory under the OS temporary root and
  supplies the exact path through a process-scoped environment variable. The
  test accepts only the exact file name, an existing nonce-prefixed parent
  that is not a reparse point, and create-new semantics. The path is never
  caller-selected through the product or Gateway contract.
- The file is UTF-8 without BOM, CRLF-terminated, at most 32 KiB, and contains
  no password, token, cookie, endpoint, Organization ID, contact ID, account
  name, CRM payload, raw exception, baseline value, or feature-flag mutation.
- The PowerShell owner validates every field, consumes the file once, and
  removes the entire task-created temporary directory in `finally`. Environment
  variables are restored in `finally`; no path, evidence object, stream,
  process, task, or buffer survives the bounded handoff.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Legacy excludes null status but aggregate template omits `not-null` | Contract/parity test fails; do not publish CE evidence. |
| CE returns a row without `commitmenttype` or `rowcount` | Projection fails closed; do not synthesize a value or partial result. |
| Evidence path is outside the OS temp root, has a wrong file name, nonce parent mismatch, reparse parent, or pre-existing file | Live test fails before writing evidence. |
| Evidence exceeds 32 KiB, is malformed, has an unexpected field value/type, or reports Green without `confirmed` parity | Handoff returns `evidence-result-unavailable`; no flag changes or retry occurs. |
| Child exits zero but neither a valid TRX marker nor the required evidence file exists | Treat as missing evidence, not success. |
| Cleanup or environment restoration fails | Return a sanitized cleanup failure and preserve fail-closed rollout state. |

### 5. Good / Base / Bad Cases

- Good: the aggregate template excludes null `customertypecode`, Data8 and
  legacy return the same bounded value/count pairs, the test atomically writes
  one sanitized temporary object, and the handoff validates then deletes it.
- Base: a normal offline test has no opt-in evidence path. It performs no CE
  operation and remains skipped; it does not create a temporary file.
- Bad: accept a missing aggregate alias as zero, infer Green from a child exit
  code, write evidence into the repository, reuse a prior file, or retain the
  file/environment after the handoff ends.

### 6. Tests Required

- `OnPremiseData8ConnectorClientFactoryTests` asserts that the exact aggregate
  FetchXML contains both `customertypecode not-null` and the server-resolved
  closed-status exclusion, then proves the projected result remains bounded.
- ProductClient and bridge parity tests cover matching, mismatch, empty, fault,
  cancellation, and deterministic disposal paths without returning raw CRM
  objects or translated metadata.
- `Invoke-Package02Data8ContactProfileEvidence.Tests.ps1` proves the fixed
  temporary path contract, 32 KiB/schema validation, sanitized output,
  create-new behavior, environment restoration, and recursive cleanup.
- The opt-in live lane must dispose the Data8 runtime, parity store, logger,
  connector lease, WCF service, and temporary evidence owner before reporting
  completion.

### 7. Wrong vs Correct

#### Wrong

```text
group by nullable customertypecode
  -> accept a missing alias as a null/zero bucket
  -> infer live success from dotnet exit code or unreliable TRX stdout
```

#### Correct

```text
server-owned not-null + closed-status filters
  -> strict bounded aggregate projection
  -> Data8/legacy raw value-count parity
  -> strict one-use OS-temp evidence file when TRX stdout is insufficient
  -> validate, consume, restore environment, delete in finally
```

The correct path preserves legacy aggregate semantics, keeps CE evidence
explicit, and gives every temporary resource one bounded owner and deterministic
cleanup path.

## Scenario: Child-process evidence trust and cleanup precedence

### 1. Scope / Trigger

This contract applies to every live Data8 handoff that launches a child test
process and receives sanitized evidence through an OS-temporary file. It was
added after a child could leave a structurally valid evidence file and still
exit non-zero, which would otherwise let a parent mistake an incomplete or
unclean execution for a usable result.

### 2. Signatures

```text
ExecuteFixture child -> strict evidence file + ExitCode
parent runner -> child-process-failed handoff + optional diagnosticCategory
```

The process lifecycle result is an input to evidence validity; the JSON file
cannot replace it. The optional category is a diagnosis projection, never a
replacement for the process result or a CE operation result.

### 3. Contracts

- The parent must drain stdout and stderr, read the final child `ExitCode`, and
  return `outcome=no-go` / `reason=child-process-failed` for every non-zero
  exit. It never accepts a child success, operation result, read-back,
  cleanup, descriptor publication, or retry decision after that exit.
- A non-zero child exit returns `outcome=no-go` with the fixed reason
  `child-process-failed`; an execute lane conservatively reports that an
  operation may have executed and must not be retried automatically.
- ExecuteFixture may expose one optional `diagnosticCategory` only when the
  evidence path is the fixed `P72Data8ListManagementEvidence.json` directly
  beneath the parent-owned, non-reparse temporary root, the complete strict
  Slice C parser accepts the file, and its result is an allowlisted `no-go`
  reason. The only allowed values are `runtime-failure`, `cleanup-failure`,
  `fixture-precondition-failed`, and `live-evidence-incomplete`. A `go`,
  malformed, stale, reparse, misplaced, or unknown-category file exposes no
  category.
- A reconciliation lane remains mutation-free and must not inherit stale
  evidence from the failed child.
- Cleanup failure has precedence over ordinary baseline classification. It
  returns `reason=cleanup-failure`, sets `readOnlyProbeExecuted=false`, and
  preserves the fail-closed rollout state.
- The parent owns the final retry decision; child-supplied retry fields are not
  trusted or propagated.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Child exits non-zero after writing strict `go` evidence | Return `child-process-failed` no-go without `diagnosticCategory`. |
| Child exits non-zero after writing strict allowlisted `no-go` evidence in the owned root | Return `child-process-failed` no-go and project only `diagnosticCategory`; never accept the file as CE evidence. |
| Child exits non-zero after malformed, stale, reparse, misplaced, or unknown-category evidence | Return `child-process-failed` no-go without `diagnosticCategory`. |
| Child exits zero but evidence is absent or malformed | Return `evidence-result-unavailable`; do not infer success. |
| Reconciliation cleanup fails | Return `cleanup-failure` with `readOnlyProbeExecuted=false`; do not downgrade it to baseline no-go. |
| Parent cannot restore process-scoped environment state | Return sanitized cleanup failure and retain fail-closed state. |
| Any failed child leaves an old evidence file | Do not parse or trust the stale file; remove it through the bounded owner. |

### 5. Good / Base / Bad Cases

- Good: the parent drains both streams, checks `ExitCode`, validates one fresh
  evidence file after a zero exit, or—after a non-zero ExecuteFixture exit—
  projects at most one fixed no-go category while retaining child failure.
- Base: an offline or skipped lane produces no child evidence and no mutation.
- Bad: parse a valid-looking file before checking `ExitCode`, reuse a previous
  file, turn a child `go` record into success after a non-zero exit, expose
  operations/CRM data from a failed child, or convert cleanup failure into an
  ordinary baseline-unprovable result.

### 6. Tests Required

- Inject a child that writes strict `go` evidence and exits non-zero; assert
  `child-process-failed`, no category, no Green result, and no automatic retry.
- Inject a child that writes each strict allowlisted `no-go` category and exits
  non-zero; assert the same terminal failure and the single matching category
  only. Inject malformed/path/reparse evidence and assert no category.
- Inject reconciliation cleanup failure; assert `cleanup-failure`,
  `readOnlyProbeExecuted=false`, and preservation of the fixed operation set.
- Assert both stdout/stderr are drained and process-scoped environment values
  are restored in `finally` after success and failure.
- Run the focused PowerShell contract suite and the corresponding C# live-lane
  assertion tests without contacting CE.

### 7. Wrong vs Correct

#### Wrong

```text
read child `go` evidence -> declare result -> inspect child exit code later
```

#### Correct

```text
drain child streams -> check ExitCode
  -> zero: validate one fresh evidence file normally
  -> non-zero ExecuteFixture: optionally project one strict no-go category only
  -> retain child-process-failed, classify cleanup separately, and deny retry
```

This ordering makes the process lifecycle, evidence file, and cleanup owner a
single fail-closed trust boundary.

## Scenario: Optional weekly-report transfer graph propagation and diagnostic staging

### 1. Scope / Trigger

This contract applies whenever the ChurchReport list-transfer capability changes
the cardinality or lookup semantics of its descriptor-bound weekly report. The
same rule must be propagated through fresh preflight, fixture provision, the
Data8 connector, the live fixture store, execute/reconciliation evidence, and
cleanup. A layer that still assumes "exactly one" after another layer accepts
"zero or one" is a cross-layer release blocker even when its own unit tests pass.

### 2. Signatures

The connector and fixture-store resolvers use the same closed nullable shape:

```csharp
Guid? ResolveWeeklyReport(
    IOrganizationService service,
    Guid targetListId,
    DateTimeOffset weekStartDate);

Guid? ResolveWeeklyReport(
    Guid targetListId,
    DateTimeOffset weekStartDate);
```

The present-record projection also preserves that optional identity:

```text
TransferPresentRecord.WeeklyReportId : Guid?
P72PresentRecord.WeeklyReportId      : Guid?

zero rows        -> null
one valid row    -> exact method-local weekly-report ID
two rows/paging/malformed/missing response -> fail closed
```

Reconciliation evidence uses the existing closed `probeStage` value
`transfer-read`. It is assigned immediately before entering the composite
transfer read, so a failure inside membership, weekly-report, present-record,
primary-list, or owner projection cannot remain mislabeled as the prior
`contact-owner-read` boundary.

### 3. Contracts

- The weekly-report query is fixed to the descriptor-bound target list,
  `statecode=0`, exact UTC Sunday, `TopCount=2`, and the ID-only projection. It
  never searches by name, scans another list, selects the first row, or creates,
  repairs, disables, merges, or deletes a weekly report.
- Zero complete rows are a normal `zero-active` state. The new present record
  omits `new_group_present_weekly_report_prese`; read-back must prove that the
  lookup is absent by nullable exact equality.
- One valid row is `exactly-one-active`. Create and read-back both use that exact
  ID; a different lookup is a partial/ambiguous graph, never a compatible row.
- In the zero-active branch, the present-record query deliberately omits the
  weekly-report filter while retaining exact contact/date/state and `TopCount=2`.
  This makes an existing wrongly linked record visible so the baseline fails
  closed instead of being mistaken for absence.
- Duplicate rows, paging continuation, malformed rows, missing responses,
  multiple present records, or malformed lookups fail before the first transfer
  mutation. They do not fall back to the zero-active branch.
- Cleanup re-runs the same zero-or-one resolver and present-record projection.
  It deletes only the exact record ID that the expected graph already proved,
  with absent lookup for zero-active or exact lookup for exactly-one-active.
- Resolver results, SDK entities, and query objects remain method-local. They
  are not cached, stored in session/static state, written to evidence, or reused
  across a request, profile, user, tenant, or connector generation.
- A cardinality change is complete only when every owning layer and its tests
  have changed together. Updating preflight/provision while leaving execute or
  reconciliation on the old assumption is forbidden change propagation.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Exact target-list/date query returns zero complete rows | Continue with nullable `null`; create/read back a present record with the weekly-report lookup absent. |
| Query returns exactly one valid row | Carry the exact ID through create, reconciliation, and cleanup; require exact lookup equality. |
| Query returns two rows or `MoreRecords=true` | Fail before any membership, present-record, contact, or owner mutation; do not choose a row or downgrade to zero-active. |
| Weekly-report row has wrong logical name or an empty ID | Fail closed before mutation. |
| Zero-active present-record query finds an existing row with another weekly lookup | Preserve the row ID in the request-local snapshot, classify it as non-matching, and reject the baseline; do not delete it. |
| Present-record query returns paging, multiple rows, a wrong logical name, or a malformed lookup | Fail closed; no cleanup guess and no automatic retry. |
| Cleanup re-read no longer matches the expected nullable lookup/record ID | Stop cleanup as ambiguous, retain the cleanup owner, and keep the slice No-Go. |
| Transfer read throws after owner projection | Emit `probeStage=transfer-read`; never misreport the failure as `contact-owner-read` or expose a raw exception. |

### 5. Good / Base / Bad Cases

- Good: the exact query returns zero rows, the baseline has no present record,
  the connector creates one unlinked present record, read-back proves the lookup
  absent, and cleanup proves then removes only that record.
- Base: the exact query returns one row. The connector and fixture store carry
  the same ID through create, read-back, reconciliation, and cleanup without
  placing it in evidence or shared state.
- Bad: preflight accepts zero rows but the execute fixture store still requires
  exactly one, causing every operation to remain not-run and reconciliation to
  report the previous owner-read stage. Also bad: add a weekly lookup filter in
  the zero-active present-record query, which hides an existing wrong relation.

### 6. Tests Required

- A fixture-store regression must first fail against an exactly-one-only
  resolver, then prove zero-active produces an absent-record baseline with no
  mutation calls.
- Exactly-one tests assert the present-record query contains the exact weekly
  filter and the projected lookup equals the same ID.
- Duplicate, paging, malformed weekly-row, multiple present-record, and malformed
  lookup tests assert rejection before Create/Update/Delete/Execute.
- A zero-active wrong-lookup test asserts the row remains visible, its ID is
  request-local, `PresentRecordMatches=false`, and no mutation occurs.
- A zero-active cleanup test asserts exact record re-proof, one Delete, one
  primary-list rollback, the required membership rollback only, and no extra
  mutation.
- Connector, fixture-store, PowerShell strict-evidence, focused P7.2, Release
  build, serial solution, isolation/lifecycle, and byte-level UTF-8/CRLF gates
  must all pass before the one permitted live verification cycle.

### 7. Wrong vs Correct

#### Wrong

```csharp
var weeklyReportId = ResolveExactlyOneWeeklyReport(targetListId, sunday);
query.Criteria.AddCondition(
    "new_group_present_weekly_report_prese",
    ConditionOperator.Equal,
    weeklyReportId);
```

This rejects the valid zero-active business state and hides wrongly linked
present records when the lookup should be absent.

#### Correct

```csharp
Guid? weeklyReportId = ResolveZeroOrOneWeeklyReport(targetListId, sunday);
if (weeklyReportId is Guid exactWeeklyReportId)
{
    query.Criteria.AddCondition(
        "new_group_present_weekly_report_prese",
        ConditionOperator.Equal,
        exactWeeklyReportId);
}

// The projected nullable lookup must equal `weeklyReportId` exactly.
```

The correct form preserves the established ChurchReport zero-active behavior,
detects conflicting present records, keeps duplicate data fail-closed, and
prevents the fixture/evidence layer from drifting away from the connector.

## Scenario: Session-cached legacy manager and operation-local CRM service bridge

### 1. Scope / Trigger

This contract applies whenever a ChurchReport Session-cached manager, a legacy
partial class, or a facade accepts an `IOrganizationService` supplied by a
Gateway/Data8 lease. A borrowed service is mutable transport state; it is never
made safe by clearing a field later. It must not cross from an operation into a
Session object, `ToolUtility`, Factory singleton, static field, cache,
`AsyncLocal`, closure, queue, timer, or background task.

### 2. Signatures

```csharp
void SetupIntegrateData(
    string account,
    string password,
    string loginType,
    DateTime downloadDate,
    string listId,
    string weeklyReportId,
    ref ListSmallGroupWeeklyReport report,
    IOrganizationService organizationService);
```

The final service argument is borrowed synchronously. Its outer lease owner is
the only owner allowed to evict, return, close, abort, or dispose it.

### 3. Contracts

- An operation-local entry validates every supported immutable branch before
  its first CRM call. A legacy or unvalidated login type fails closed before
  login, list, weekly-report, metadata, or chart I/O; it must not first inspect
  the borrowed service and then fall back to `ToolUtility`.
- Each service-aware helper receives the service explicitly and uses it only in
  its current stack frame. It does not store, wrap, return, dispose, or pass it
  to an API that can retain it.
- Read-only output is assembled in a new local report. Assign it to the caller
  only after every allowed CRM read succeeds. A fault, cancellation, or timeout
  preserves the caller's prior report reference.
- A Session-cached `ListManager` overload that lacks a complete immutable,
  server-validated operation context fails before reading its instance fields
  or performing CRM I/O. It is not a compatibility path for forwarding a
  borrowed service to a legacy downloader.
- Legacy mutation paths remain isolated from the service-aware read-only route
  until each partial has explicit parameter propagation and the required
  rollback/read-back evidence. Delay construction of a legacy mutation
  connector during read-only DTO construction, but keep creation at the actual
  mutation boundary so ownership is unchanged.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Login type is not an explicitly verified operation-local read path | Reject before the first borrowed-service CRM call; do not alter output or dispose the service. |
| Session manager would need account/password/date/login fields | Reject before reading those fields; require a new immutable context API. |
| Any service-aware helper would use Factory/ToolUtility fallback | Reject the operation; never mix borrowed and shared services. |
| CRM read faults, cancels, or times out | Propagate the bounded failure; preserve the prior output reference and leave lease eviction/disposal to the outer owner. |
| DTO is created only for a read operation | Do not initialize a legacy mutation connector or acquire its shared CRM service. |

### 5. Good / Base / Bad Cases

- Good: two interleaved A/B calls each receive a different fake service; each
  response contains only its own marker, neither service is retained or
  disposed, and a failed B call leaves B's prior response untouched.
- Base: an unsupported login type returns a bounded error before one CRM call.
- Bad: put the service into a `ListManager` field, call `ToolUtility` after a
  borrowed-service lookup, dispose the service in a helper, or use `finally` to
  clear a temporary shared field. Each permits cross-operation state reuse.

### 6. Tests Required

- Interleave A/B marker services through the public service-aware entry; assert
  no foreign marker, no instance/static retained reference, and zero helper
  `Dispose` calls.
- Inject the first CRM call fault; assert prior report reference and content are
  unchanged.
- Invoke every unsupported login type branch with a call-counting fake; assert
  zero CRM calls, zero `Dispose`, and no output mutation.
- Invoke the Session-manager service overload with uninitialized session state;
  assert it fails before field read/CRM I/O and does not retain either marker.
- Construct a read-only report and assert its legacy mutation connector is not
  created. Run focused isolation tests, Release build, encoding/CRLF checks,
  and `git diff --check` before the CE evidence gate.

### 7. Wrong vs Correct

#### Wrong

```csharp
_service = organizationService;
LoadHeader();                 // may use Session fields
ToolUtility.LoadMembers();    // shared service fallback
_service = null;
```

#### Correct

```csharp
if (!IsVerifiedLeader(loginType))
{
    throw new InvalidOperationException("Operation is not isolated.");
}

var localReport = new ListSmallGroupWeeklyReport();
LoadHeader(account, password, listId, ref localReport, organizationService);
LoadMembers(listId, ref localReport, organizationService);
report = localReport; // only after all reads succeed
```

The correct form preserves the full user/profile/generation boundary, makes
the caller the single resource owner, and gives later P7.4/P7.5 work a
testable condition instead of an unsafe migration shortcut.

## Scenario: Evidence-pending local-only capability gate

### 1. Scope / Trigger

This scenario applies when a P7.x capability has complete local contracts and
tests but does not yet have the governed CE evidence required for a product
rollout. It applies equally to Central Gateway, Dedicated Gateway, Embedded,
Data8 and both Official Worker connector kinds. A green local test suite is not
a replacement for CE read-back, reconciliation and deterministic cleanup.

### 2. Signatures

The local capability metadata and the executable boundary expose both gate
values explicitly:

```csharp
public sealed class LocalCapabilityDefinition
{
    public required string OperationId { get; init; }
    public bool CeExecutorEnabled { get; init; }
    public bool ConsumerEnabled { get; init; }
}

Task<OperationExecutionResult> ExecuteAsync(
    OperationExecutionRequest request,
    CancellationToken cancellationToken = default);
```

The two flags are server-owned immutable metadata. They are never accepted from
a request, configuration reload, browser, IPC frame or product caller.

### 3. Contracts

- An evidence-pending capability may create only an operation-local immutable
  plan. It may not call `Create`, `Update`, `Delete`, `Assign`, `Associate`,
  `Disassociate`, a CRM action, a feature flag, or a product consumer.
- Its `CeExecutorEnabled` and `ConsumerEnabled` values both remain `false`.
  The executor rejects its operation with `operation.not-supported` before
  profile resolution, admission, lease acquisition, connector/client creation
  or outbound I/O.
- An executor rejection must be observable in tests through zero acquire,
  release, create and dispose counters. A late rejection after a lease/client
  was made is not an equivalent safety result.
- P7.4 Gateway cutover and P7.5 ToolUtility removal stay fail closed until the
  operation has its independently governed CE fixture, exact read-back,
  reconciliation, deterministic cleanup and approved rollout evidence.
- A local plan is not a queue, retry record or deferred command. It does not
  retain CRM identities, owner IDs, profiles, credentials, tokens, endpoints,
  `HttpContext`, Session, principals, SDK entities, connector clients or mutable
  collections beyond the current caller's stack.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Local plan has no CE evidence | Retain both gates as `false`; do not enqueue or dispatch it. |
| Caller tries a local-only operation through the executor | Return `operation.not-supported` before admission/lease/client work. |
| A test observes an admission acquire or client creation | Fail the test and treat the capability as rollout-ineligible. |
| CE write returns timeout, ambiguous, no-go, read-back mismatch or uncertain cleanup | Stop that write family; never convert the local plan into a retry. |
| P7.4/P7.5 is proposed while any required CE evidence is absent | Reject the rollout/removal gate; retain the legacy safe path. |

### 5. Good / Base / Bad Cases

- Good: D–H local reducers accept only bounded, deidentified input, return an
  immutable plan, and an executor test proves all candidate operation IDs are
  rejected before allocation.
- Base: a local test suite and Release build pass, but release notes still say
  `CE evidence pending` and no Gateway configuration or ToolUtility path
  changes.
- Bad: add an operation ID to a catalog and assume it may reach Data8, enable a
  consumer because its unit test passed, or call a CE write merely to make a
  candidate version look complete.

### 6. Tests Required

- Enumerate every evidence-pending operation and assert both metadata gates are
  false.
- Send every operation through the executor using counting admission and client
  fakes; assert `operation.not-supported` and all allocation counters are zero.
- Interleave two local plans with different synthetic markers; assert each plan
  is a defensive immutable snapshot and does not retain the other marker.
- Run timeout, partial-completion, duplicate, unavailable, no-replay and
  cleanup-order tests appropriate to the capability family.
- Before candidate delivery run the full Dynamics suite, relevant product
  isolation tests, Release build, UTF-8/CRLF audit and `git diff --check`.

### 7. Wrong vs Correct

#### Wrong

```csharp
if (localPlan.Succeeded)
{
    return await executor.ExecuteAsync(request, cancellationToken);
}
```

This turns a local validation result into an ungoverned external write and can
allocate a client containing operation state before the CE safety gate exists.

#### Correct

```csharp
if (!definition.CeExecutorEnabled || !definition.ConsumerEnabled)
{
    return OperationExecutionResult.Failure("operation.not-supported");
}

// The allocation and dispatch path is reachable only after independent CE evidence.
```

The correct form preserves cross-user/profile isolation, prevents accidental
rollout and leaves P7.4/P7.5 blocked by evidence rather than optimism.

## Scenario: Deterministic negative deployment validation without TestHost disposal races

### 1. Scope / Trigger

This scenario applies when a Gateway test verifies that deployment-owned
configuration is rejected before the listener accepts traffic. It specifically
covers the .NET 10 interaction between top-level `app.Run()` and
`WebApplicationFactory`: when startup deliberately throws, the application can
dispose its provider before `DeferredHost` finishes reading it. That test-host
lifecycle race must never replace the intended fail-closed validation result or
motivate a production change.

### 2. Signatures

The direct unit boundary must be the same concrete validator that production
startup materializes:

```csharp
new ConfigurationGatewayOperationAuthorizer(
    IConfiguration configuration,
    IReadOnlyCollection<string> knownProfileAliases);

GatewayRequestBodyLimitOptions.BindAndValidate(
    IConfiguration configuration);
```

The normal positive HTTP boundary remains the existing
`WebApplicationFactory<Program>` / TestHost or Kestrel integration fixture.

### 3. Contracts

- A negative deployment-configuration test creates one fresh in-memory or
  JSON-stream `IConfiguration` snapshot for its own case, then directly invokes
  the validator that startup invokes. It does not need to create a Host,
  listener, executor, admission permit, connector, socket, timer, background
  task, reload subscription, or shared service provider.
- The direct test must preserve production's fail-closed contract: invalid
  binding selectors, malformed binding sets, unknown aliases/operations and
  invalid body limits throw the same bounded validation exception before any
  runtime resource is materialized.
- `WebApplicationFactory` is retained for positive HTTP authorization,
  request-body, TestHost and Kestrel coverage. It must not be used merely to
  assert an intentionally thrown top-level startup exception when the direct
  validator is the actual contract under test.
- Tests must not change `Program`, weaken startup validation, serialize the
  whole suite, catch `ObjectDisposedException` as a compatible result, or add a
  retry. Those options hide a framework-lifecycle race instead of proving the
  Gateway's deployment boundary.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Invalid deployment binding selector, set shape, profile alias or operation | Direct authorizer construction throws the expected fail-closed validation exception; no Host or outbound resource exists. |
| Request body maximum exceeds the hard ceiling | `BindAndValidate` throws the bounded configuration validation exception before Kestrel is configured. |
| Positive authorized HTTP / Kestrel body-boundary case | Continue using the integration fixture and assert the real response and zero unexpected executor work. |
| A test sees `ObjectDisposedException` from a provider while expecting startup validation | Treat it as a test-host lifecycle defect; replace that negative assertion with the direct validator contract, never accept or retry the disposal exception. |

### 5. Good / Base / Bad Cases

- Good: each invalid selector case builds a private configuration snapshot and
  directly constructs the authorizer; the expected configuration exception is
  observed without creating a listener.
- Base: normal requests still pass through `WebApplicationFactory` or Kestrel,
  so routing, authentication, request ownership and response behavior retain
  integration coverage.
- Bad: alter `Program` shutdown ownership, disable suite parallelism, or accept
  an `ObjectDisposedException` because a negative `CreateClient()` assertion is
  intermittently masked by `app.Run()` disposal.

### 6. Tests Required

- Cover missing, blank, wildcard, delimiter-bearing, unknown, scalar-only,
  scalar-plus-children and childless binding-set forms by direct authorizer
  construction; assert only the expected configuration validation category.
- Cover a request-body hard-ceiling setting by direct
  `GatewayRequestBodyLimitOptions.BindAndValidate` invocation.
- Retain focused Gateway HTTP/TestHost/Kestrel tests and run the complete
  `SpeechMessage.Dynamics.Tests` suite after the change. The test report must
  distinguish explicitly skipped live dependencies from passing local tests.
- Assert changed test files are UTF-8 without BOM, CRLF-only and final-CRLF;
  run `git diff --check` before commit.

### 7. Wrong vs Correct

#### Wrong

```csharp
using var factory = CreateInvalidFactory();
Action start = () => factory.CreateClient();
start.Should().Throw<InvalidOperationException>();
```

This makes a configuration assertion depend on a test host that may already
have been disposed by top-level startup cleanup.

#### Correct

```csharp
var configuration = CreateCaseLocalConfiguration(invalidValues);
Action materialize = () => new ConfigurationGatewayOperationAuthorizer(
    configuration,
    knownProfileAliases);

materialize.Should().Throw<InvalidOperationException>();
```

This tests the same startup validation boundary with deterministic ownership,
while separate positive HTTP and Kestrel tests continue to prove the hosted
pipeline.

## Scenario: P7 capability rebaseline evidence artifact

### 1. Scope / Trigger

When a migration task needs to schedule or release-gate a set of legacy
Dynamics call sites, create a deterministic offline matrix from the canonical
call-site inventory. This applies before any P7 capability cutover, P7.5
legacy removal claim, or P8 handoff. It does not grant a connector operation,
CE mutation, feature-gate change, or deployment action.

### 2. Signatures

The task-owned analyzer has only a bounded local build/validate surface:

```text
python build_rebaseline.py --output authoritative-gap-matrix.json
python build_rebaseline.py --validate authoritative-gap-matrix.json
```

It has no endpoint, credential, identity, profile, connector, network or CE
arguments.

### 3. Contracts

- The canonical phase0 inventory owns the immutable 70 call-site IDs and its
  current file checksum. A derivative coverage artifact may provide family
  classification only after its ID set and operation IDs exactly match phase0;
  its embedded historic hash cannot replace the current canonical checksum.
- A row records registry, Data8 executor, typed ProductClient, ChurchReport
  consumer, CE evidence, host evidence, rollout and rollback as independent
  finite states. Static declarations, disabled gates, unit tests and
  local-only plans do not imply real execution or cutover.
- Static symbol scanning first removes C# comments and quoted literals; an
  `OperationIds` spelling in documentation or a diagnostic string is not
  implementation evidence.
- A local-only row must stay executor-rejected, client-unimplemented,
  consumer-unmigrated and CE-not-executed until a separate capability child
  proves a fresh, allowed evidence family.
- Output is UTF-8 without BOM, CRLF-only with a final CRLF, deterministic in
  call-site order and contains only bounded de-identified classifications.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Phase0/coverage row count, unique IDs or operation IDs disagree | Fail closed; do not emit a green matrix. |
| Matrix checksum differs from the current canonical phase0 file | Validator reports a source-hash error. |
| Local-only row claims executor, ProductClient, consumer or CE success | Validator rejects the row. |
| Disabled consumer is represented as enabled without CE/Dedicated evidence | Validator rejects the row. |
| Requested output contains identity, routing, credential or raw upstream fields | Test/validator rejects the artifact before delivery. |

### 5. Good / Base / Bad Cases

- Good: the analyzer reads fixed repository files, compares exact symbols and
  immutable IDs, and returns a short-lived JSON snapshot with no connector.
- Base: a typed client may be `implemented` while the consumer remains
  `not-migrated` or `migrated-disabled`; the matrix shows that gap explicitly.
- Bad: infer CE 9.1, Dedicated, product traffic or ToolUtility removal from a
  registry entry, a client type, a local reducer or a feature flag string.

### 6. Tests Required

- Assert exactly the canonical 70 IDs, canonical checksum and deterministic
  ordering.
- Assert a client-only operation remains `not-migrated`, an explicitly gated
  consumer remains `migrated-disabled`, and multiline C# constants are not
  omitted from static implementation detection.
- Assert a comment-only and literal-only `OperationIds` reference produces no
  implementation evidence.
- Fault-inject local-only CE success and disabled-consumer enabled state; both
  must fail closed.
- Byte-check generated JSON for UTF-8 no BOM, CRLF-only and final CRLF.

### 7. Wrong vs Correct

#### Wrong

```text
Package01 registry exists -> claim CE/Dedicated consumer cutover complete
```

#### Correct

```text
registry=declared, executor=implemented, client=implemented,
consumer=migrated-disabled, ce91=succeeded, dedicated=evidence-pending
```

The correct form preserves the missing evidence so later P7/P8 work cannot
accidentally remove a legacy path or change traffic before its actual proof.

## Scenario: P7.5 offline prerequisite evidence and zero-reference gate

### 1. Scope / Trigger

Use this scenario when P7 planning needs a repeatable answer to whether
ChurchReport still has direct legacy CRM/ToolUtility dependencies. It applies
to an offline prerequisite report before selecting the next capability family
and before a future P7.5 removal claim. It is not a ToolUtility removal,
consumer migration, CE evidence, feature-gate, traffic or P8 deployment
authorization.

### 2. Signatures

The task-owned analyzer has only fixed local input paths and these commands:

```text
python build_p75_prerequisite_evidence.py --report p75-prerequisite-evidence-report.json
python build_p75_prerequisite_evidence.py --validate p75-prerequisite-evidence-report.json
python build_p75_prerequisite_evidence.py --enforce-p75
```

It accepts no caller-selected source root, profile, endpoint, identity,
credential, connector, network or CE parameter. The only positive static state
is `prerequisite-ready`; it is deliberately not named `ready` and does not
replace the independent P7.5 removal/P8 gates.

### 3. Contracts

- The analyzer reads only the immutable gap matrix, allowlisted ChurchReport
  production `.cs`, one project file and checked-in `appsettings*.json` key
  names. It excludes tests, docs, generated/output/log paths, reparse points
  and root escapes; invalid UTF-8, file-size excess, unknown lexical forms and
  malformed metadata fail closed.
- C# lexical scanning may count legacy tokens only while in code state. It
  masks comments, character/regular/verbatim/interpolated literals and a
  whitespace-prefixed line-start preprocessor directive such as `#region` or
  `#pragma`. Raw strings, unmatched delimiters and unsupported interpolation
  must reject the report rather than yield a false zero-reference result.
- Settings scanning decodes only object key strings. JSONC comments are allowed
  outside strings, while all string/array/object/scalar values are syntax-
  checked and skipped without materializing, logging, hashing, classifying or
  publishing them. Invalid escapes, unterminated comments and trailing syntax
  fail closed.
- Matrix temporary-legacy, consumer/CE/host gaps, every non-`none` matrix
  blocker, production source reference, project dependency and legacy settings
  key are independent no-go dimensions. Passing one dimension never offsets
  another.
- Output is a deterministic, de-identified count/category report. It contains
  no path, line, source fragment, CRM identifier, name, endpoint, secret,
  credential, token, cookie, setting value or raw exception.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| `#region`/`#pragma` label contains a quote or legacy word | Treat the whole directive as non-code; do not enter string parsing or count a reference. |
| Raw C# string, unclosed literal/comment or unsupported interpolation | Return fixed scanner-input-invalid classification; do not emit zero-reference success. |
| JSONC comment appears outside a setting string | Skip it and continue key-only structural parsing. |
| JSONC value has an invalid escape, malformed number or incomplete structure | Reject the input; do not produce partial key counts. |
| Any matrix non-`none` blocker, project dependency, settings key or source finding remains | `--enforce-p75` returns sanitized `no-go`/nonzero. |
| All static dimensions are clear | Return only `prerequisite-ready`; separately require real consumer, CE/host, parity, soak, drain, rollback, commit/archive and immutable handoff evidence before P7.5/P8. |

### 5. Good / Base / Bad Cases

- Good: an offline scanner finds a production legacy category, project reference
  and matrix gap, reports only their fixed counts, and blocks P7.5 while a
  separate local P7 child proceeds from the ordered capability-family backlog.
- Base: a `#region` label contains natural-language quotes, and a checked-in
  `appsettings` file contains JSONC comments; both scan deterministically
  without exposing values or changing a no-go result.
- Bad: parse a whole settings object then publish/debug it, scan comment/string
  text as code, ignore a malformed lexical shape, call a clean static result
  `ready`, or use the report to remove ToolUtility, write CE or start P8.

### 6. Tests Required

- Assert comment/literal/character/preprocessor-only legacy tokens produce no
  source finding while actual code tokens produce only fixed categories.
- Assert raw strings, invalid UTF-8, root escape/reparse point, malformed
  JSONC/comment/escape and report tampering fail closed.
- Assert JSONC nested values are not materialized or returned, while matching
  object keys still count correctly.
- Assert matrix, production references, project dependencies and settings keys
  each independently prevent `prerequisite-ready`.
- Run report generation, strict validation and the expected current nonzero
  `--enforce-p75`, then byte-check UTF-8 no-BOM/CRLF/final-CRLF and run the
  complete Release solution tests/build before task completion.

### 7. Wrong vs Correct

#### Wrong

```text
json.loads(appsettings) -> log/debug the object -> source count is zero -> declare P7.5 ready
```

#### Correct

```text
key-only JSONC scanner + fail-closed C# lexer + immutable matrix/dependency gate
-> prerequisite-ready or sanitized no-go
```

The correct path has no credential-like value retention and cannot turn a
static inspection into a product migration, CE or deployment claim.

## Scenario: P7.4 bounded UTC display projection

### 1. Scope / Trigger

Apply this scenario when a Data8/Gateway response adds a nullable UTC
`DateTimeOffset` display field that ChurchReport must convert into a legacy
local `DateTime` view model. It covers the connector → wire record →
ProductClient DTO → request-local projection path. It does not authorize a
feature-gate enablement, CE request, ToolUtility removal, P7.5 or P8.

### 2. Signatures

```csharp
private static DateTime ToLegacyDisplayDateTime(DateTimeOffset? utcValue);

public sealed record StorLessonRecordDto
{
    public DateTimeOffset? ClassStartDate { get; init; }
}
```

The connector owns conversion of CRM `DateTime` aliases into UTC
`DateTimeOffset`; the ProductClient copies the immutable value; the consumer
owns only a request-local legacy-display conversion.

### 3. Contracts

- An absent CRM value remains `null` through the wire record and DTO. The
  consumer returns the established `DateTime.MinValue` view-model sentinel;
  it must not substitute `DateTimeOffset.MinValue` and convert it to local
  time.
- A UTC-minimum value is also an unavailable legacy-display value. It maps to
  `DateTime.MinValue`, regardless of host timezone. A positive offset must not
  turn it into a plausible `0001-01-01 08:00` timestamp; a negative offset
  must not cause an underflow exception.
- Before invoking `DateTimeOffset.LocalDateTime` for other values, the
  conversion checks `TimeZoneInfo.Local.GetUtcOffset(utcValue.UtcDateTime)` and
  clamps only an otherwise unrepresentable lower/upper boundary to
  `DateTime.MinValue`/`DateTime.MaxValue`.
- The conversion must not cache `TimeZoneInfo`, DTOs, response models,
  `HttpContext`, profile state or CRM objects. Each projection is request-local
  and the connector/pool remains the sole owner of external resources.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| CRM alias is absent | Connector returns null; consumer returns `DateTime.MinValue`. |
| UTC-minimum DTO value | Consumer returns `DateTime.MinValue`; no local-time conversion occurs. |
| Non-minimum value is representable in host local time | Consumer returns its `LocalDateTime`. |
| Host offset would make a non-minimum UTC value unrepresentable | Clamp to the corresponding `DateTime` bound; do not throw or retain a partial projection. |
| CRM alias has the wrong runtime type | Connector fails closed before DTO/projection creation. |

### 5. Good / Base / Bad Cases

- Good: connector projects an aliased UTC date to `DateTimeOffset?`; a
  request-local consumer maps null/minimum to the existing UI sentinel and
  converts ordinary dates once.
- Base: an extreme but non-minimum UTC value is clamped to the closest
  representable local `DateTime`; the UI does not receive an exception or a
  prior request's result.
- Bad: `(dto.Date ?? DateTimeOffset.MinValue).LocalDateTime`, a static timezone
  or projection cache, SDK `RetrieveEntity` enrichment, or a local-time
  conversion before connector alias validation.

### 6. Tests Required

- Assert null and UTC-minimum values both result in exactly
  `DateTime.MinValue`, including on a host with a positive local offset.
- Assert an ordinary UTC value preserves the existing local-time display
  behavior.
- Fault-inject an incompatible CRM alias type and assert connector fail-closed
  behavior before a ProductClient DTO is emitted.
- Interleave two identifiable DTO responses and assert no response model,
  timezone conversion state or display field leaks between requests.

### 7. Wrong vs Correct

#### Wrong

```csharp
DisplayDate = (dto.ClassStartDate ?? DateTimeOffset.MinValue).LocalDateTime;
```

This turns the missing/minimum sentinel into a timezone-dependent plausible
date or can become unrepresentable on a negative-offset host.

#### Correct

```csharp
DisplayDate = ToLegacyDisplayDateTime(dto.ClassStartDate);
```

The helper preserves absence semantics, bounds timezone conversion, and keeps
all data request-local without changing the connector, profile or traffic path.

## Scenario: Cross-assembly WorkerTestHost process-boundary tests

### 1. Scope / Trigger

Apply this scenario whenever a test class starts `SpeechMessage.Dynamics.WorkerTestHost`,
or asserts that its product startup created no `WorkerTestHost`. xUnit collection
parallelization is assembly-local; it cannot serialize a Dynamics testhost against a
ChurchReport testhost. A test must never weaken its process/listener/cleanup assertion
just because another test assembly can create the same process name.

### 2. Signatures

The test-only source linked into each participating test assembly exposes one collection
and one `IDisposable` fixture:

```csharp
[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
public sealed class WorkerBoundaryTest
{
}

public sealed class WorkerTestHostProcessBoundaryLease : IDisposable
{
    public WorkerTestHostProcessBoundaryLease();
}
```

The default fixture path is derived as follows; the SHA-256 output is only a
same-worktree partition key, never a product identifier or routing input:

```text
%TEMP%/speechmessage-worker-testhost-process-boundary-v1-{sha256(solution-root)[0..16]}.lock
```

### 3. Contracts

- The lease source is linked only into test projects. No product, ProductClient,
  Gateway, connector, worker runtime or deployment assembly may reference it.
- The fixture owns exactly one `FileStream` opened with `FileShare.None` for the
  whole xUnit class collection lifetime. It writes no contents and retains no process
  ID, profile, session, principal, credential, endpoint, payload or test result.
- Same-worktree testhosts derive the same path and therefore serialize before either
  takes a process baseline. Different checkout roots derive different opaque path
  partitions, avoiding unrelated worktree contention.
- Acquisition has a fixed deadline. Only Win32 sharing violation (32) and lock
  violation (33) may poll; any other I/O error is rethrown unchanged. On deadline,
  throw a bounded `TimeoutException` and do not execute the ambiguous observer class.
- `Dispose` closes the sole stream exactly once. If a testhost aborts, Windows owns
  closing the process handle. A remaining empty lock-file name is not a held lease.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Same worktree already holds lease | Wait only until fixed deadline, then fail closed with bounded timeout. |
| Different checkout/worktree | Derive a different hash partition; do not cause irrelevant test serialization. |
| Path, permission, disk or other non-contention I/O error | Rethrow immediately; never recategorize as contention timeout. |
| Fixture dispose / testhost abort | Release `FileStream` directly or through OS process-handle cleanup; next bounded acquire can proceed. |
| ChurchReport disabled test observes a worker after lease acquisition | Preserve the original process/listener/cleanup failure; do not suppress it. |

### 5. Good / Base / Bad Cases

- Good: a Dynamics worker-producing class and the ChurchReport zero-worker observer
  share the source-linked collection. The latter waits before its baseline, then still
  proves no worker was created by its own startup.
- Base: a prior aborted testhost leaves an empty path on disk but no open handle; the
  next fixture obtains the lease and continues safely.
- Bad: rely on two identically named assembly-local collections, use a fixed `%TEMP%`
  file shared by every checkout, catch every `IOException` as a timeout, or remove the
  ChurchReport process assertion to make concurrent tests pass.

### 6. Tests Required

- Contract-test contention timeout, dispose release, non-contention I/O failure and
  same-worktree/different-worktree path derivation without CRM, Gateway or network I/O.
- Run the worker soak/process-boundary tests and the ChurchReport disabled-boundary
  test; then run the complete solution suite and assert no `WorkerTestHost` remains.
- Verify source is linked only by test `.csproj` files, both modified sources are
  UTF-8 without BOM with CRLF/final CRLF, and `git diff --check` is clean.

### 7. Wrong vs Correct

#### Wrong

```csharp
[Collection("Worker tests")]
public sealed class DynamicsTest { }

[Collection("Worker tests")]
public sealed class ChurchReportTest { }
```

Those matching names do not cross an xUnit assembly boundary, so a valid Dynamics
worker can still contaminate the ChurchReport process baseline.

#### Correct

```csharp
[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
public sealed class DynamicsTest { }

[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
public sealed class ChurchReportTest { }
```

Both test assemblies compile the same test-only fixture source and therefore acquire
the same worktree-partitioned OS lease before observing the shared process namespace.

## Scenario: Worker PID evidence publication/read race

### 1. Scope / Trigger

Apply this scenario when `SpeechMessage.Dynamics.WorkerTestHost` or a test-only child
process writes a run-unique PID evidence file and a bounded test reader observes that
file. On Windows, `File.Exists` can become true before the writer releases an exclusive
handle; immediate `ReadAllTextAsync` can therefore fail with sharing violation even
though the test-owned writer is still publishing its one scalar PID.

### 2. Signatures

```csharp
private static async Task<int> ReadCapturedProcessIdAsync(string evidencePath);
private static bool IsExpectedEvidenceContention(IOException exception);
```

The path remains derived only from the test's run-unique validated generation. The
content contract remains one positive invariant-culture decimal PID; it is not a
product identifier, request field, endpoint, credential or routing input.

### 3. Contracts

- The reader has one fixed, monotonic deadline and uses condition polling; it does not
  sleep after a successful read or keep a `FileStream`, process handle, background task,
  timer or static cache.
- It may catch and retry only Windows sharing violation (32) or lock violation (33).
  Path, permission, disk, malformed-content and all other `IOException` cases remain
  observable failures immediately; they must not be recategorized as normal startup.
- A parsed PID must be greater than zero before it is returned. The caller remains the
  sole owner of any `Process` handle it later acquires and of final evidence-file deletion.
- This is a test-only publication race fix. It does not authorize Worker startup,
  CE access, profile selection, feature enablement or retry of an external operation.

### 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Evidence file absent | Continue bounded condition polling. |
| Evidence file exists but writer still holds exclusive handle (32/33) | Continue polling only until the same fixed deadline. |
| Evidence contains non-positive/non-numeric text after readable | Do not accept it; continue only until deadline, then fail bounded. |
| Permission/path/disk/unexpected I/O error | Rethrow immediately; do not hide an isolation or cleanup defect. |
| Deadline expires | Throw fixed evidence-not-captured failure; do not infer a PID or inspect unrelated processes. |

### 5. Good / Base / Bad Cases

- Good: a test opens a run-unique evidence file with `FileShare.None`, starts the reader,
  writes the decimal PID, then releases the handle; the reader is incomplete while the
  handle is held and returns the exact PID afterward.
- Base: the writer has already closed before the first reader poll; the reader returns the
  validated PID without creating retained state.
- Bad: catch every `IOException`, use an unbounded retry, read another test's path,
  enumerate processes to guess a PID, or treat the file's appearance as proof that a
  Worker is ready or safely drained.

### 6. Tests Required

- Hold an exclusive writer handle after the file exists; assert the reader remains pending,
  then returns exactly the written positive PID after release.
- Run the OfficialWorker control-plane/profile focused suites and the complete solution
  suite to prove the cross-assembly test path remains clean.
- Verify changed test sources are UTF-8 without BOM, CRLF-only with final CRLF, and
  `git diff --check` is clean.

### 7. Wrong vs Correct

#### Wrong

```csharp
if (File.Exists(evidencePath))
{
    return int.Parse(await File.ReadAllTextAsync(evidencePath));
}
```

This confuses name visibility with completed publication and flakes when the writer has
not released its exclusive handle.

#### Correct

```csharp
try
{
    var text = await File.ReadAllTextAsync(evidencePath);
    // Parse strictly and return only a positive PID.
}
catch (IOException exception) when (IsExpectedEvidenceContention(exception))
{
    // Continue the pre-existing bounded polling deadline.
}
```

The correct form tolerates only the proven publication race while preserving fail-closed
behavior for every unknown filesystem failure.
