# Microsoft-official-NuGet Dynamics 365 access gateway

## 2026-08-02 official NuGet direction correction

This correction supersedes every older statement that makes direct Web API,
CRMWeb/IFD repair, or a universal no-SDK transport the delivery path or a
Phase 4 blocker.

- The primary Dynamics transport is a pair of separately version-pinned,
  out-of-process .NET Framework 4.8 workers using Microsoft-published NuGet
  packages and `CrmServiceClient` / Organization Service semantics.
- CE 8.2 and CE 9.1 use different worker projects and dependency graphs until
  real compatibility evidence proves that any consolidation is safe. Their CRM
  SDK assemblies must never load into the .NET 10 product or Gateway process.
- Products and the .NET 10 Gateway remain SDK-free. SDK DTOs, connections,
  credentials, WCF channels, and mutable authentication/session state cannot
  cross the worker boundary.
- Direct Web API is not a supported route, fallback, or future adapter in this
  task. The existing WebApi project and smoke artifacts are legacy replacement
  inputs only and are removed from the build/routing surface after the official
  workers replace their remaining test dependencies. The D365APP01 CRMWeb/IFD
  HTTP 500 investigation is closed as a Gateway prerequisite.
- "Real-server validation" now means deploying the actual website/Gateway and
  official worker path on the intended Windows host and exercising the approved
  Organization operation matrix against CE 8.2 and CE 9.1. It does not mean
  establishing a D365APP01 management channel, reopening the IFD wizard, or
  repeating direct Web API `WhoAmI` diagnostics.
- Data8 remains temporary only for existing legacy traffic. New Gateway work
  targets the official workers first; Data8 is removed after the corresponding
  official worker passes compatibility, lifecycle, rollback, and no-leak gates.

The executable contract is
`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`. Older
Web-API-specific material remains historical evidence only and cannot override
this correction.

## 2026-07-29 direction amendment (historical)

This amendment supersedes the strict no-SDK requirement wherever the older text conflicts with it.

- Central Gateway remains the production target for the multi-product estate.
- Local Gateway is the immediate Visual Studio/development and isolated-deployment path. It is the same `Gateway` execution mode pointed at a localhost endpoint, not a new execution-mode enum value.
- Embedded is retained but deferred until Central/Local, CE 8.2/9.1, isolation, and lifecycle validation passes.
- Microsoft official SDK components are allowed only behind a Gateway adapter or out-of-process compatibility worker. Products still must not reference CRM/Dataverse SDK components directly.
- CE 9.1 and CE 8.2 use their separately pinned official
  `CrmServiceClient` workers. Direct Web API is removed from the route set.
- CE 8.2 may temporarily use the checked-in Data8 WS-Trust bridge only for
  existing unmigrated traffic while its official worker is proven.
- Data8 remains temporary and removable only after replacement, real-server, lifecycle, isolation, rollback, and dependency-removal gates pass.

The executable contract is `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`. The full Traditional Chinese decision history and explanation is `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`.

## Goal

Design and implement the future shared Dynamics 365 Organization integration
solution for five current products and at least ten future products. The
solution uses separately version-pinned, recyclable Microsoft-official-NuGet
workers for Customer Engagement 8.2 and 9.1 rather than loading CRM SDK/WCF
assemblies into products or the .NET 10 Gateway, or making the GitHub-derived
Data8 project the permanent transport.

The recommended default deliverable is an internal Dynamics Access Gateway Web
Service backed by process-isolated official NuGet workers. Each product
uses a product-owned JSON file to select one deployment-owned execution mode:
`Gateway` (the production default) calls the controlled REST API, while
`Embedded` remains deferred. Local Visual Studio development uses a separately
running Local Gateway. This is a host-mode choice, not a caller/runtime switch.
Only explicit worker projects may reference Microsoft CRM SDK packages; neither
products nor Gateway may retain a per-user CRM connection/session.

## User value

- One governed connection, authentication, version-validation, observability,
  and connection-pool implementation instead of duplicated logic in every
  product.
- Safe concurrent access to both 8.2 and 9.1 organizations through named,
  configuration-driven profiles.
- A migration path that removes all use of the external Dynamics 365 SDK DLL
  directory and all production CRM SDK type dependencies.
- A service boundary that can grow from five products without copying CRM
  credentials, token caches, metadata caches, or version-specific workarounds.
- A product can declare `Gateway` or `Embedded` in its own versioned JSON
  configuration without duplicating connector code. The embedded exception
  keeps an isolated local HTTP pool but still participates in the same
  organization-wide admission budget as every Gateway and embedded host that
  reaches that Dynamics organization.

## Confirmed facts

- The checked-in PowerPlatform.Dataverse.Client project is a WS-Trust/WCF
  implementation of IOrganizationService, not a direct REST/Web API client.
- The live path is ChurchReport -> ToolUtility -> local
  PowerPlatform.Dataverse.Client. ToolUtility's CrmConnectionService creates the
  SOAP client and CrmConnectionPool pools IOrganizationService objects.
- Startup registers one singleton ICrmConnectionPool from one CRM configuration.
  That structure is not a safe multi-profile ownership boundary.
- SpeechMessageProducts.ChurchReport has a direct external
  Microsoft.Crm.Sdk.Proxy DLL HintPath whose actual external path contains
  Dynamics 365 SDK DLL. It violates the requested boundary even though the
  literal user-supplied absolute root is not present verbatim. Production
  projects also depend on Microsoft.Xrm /
  Microsoft.PowerPlatform.Dataverse.Client APIs.
- Existing configuration contains secret material. The new design must not copy
  or expose it; migration includes secret rotation and secret-store references.
- Microsoft documents the Dynamics Web API as an OData v4 HTTP API that does
  not require a language-specific assembly. CE 8.2 and 9.1 have materially
  different compatibility/capability constraints and must be validated against
  real servers.

## Architecture decisions made during discovery

| Decision | Chosen rule | Why it satisfies the request |
| --- | --- | --- |
| Transport | Separately version-pinned .NET Framework 4.8 workers using Microsoft-published `Microsoft.CrmSdk.XrmTooling.CoreAssembly` / `CrmServiceClient`; no direct Web API route or fallback. | Uses the supported on-premises Organization client while keeping SDK/WCF version conflicts and lifecycle state outside every .NET 10 product/Gateway process. |
| Default host | Shared Gateway Web Service. | Centralizes secrets, observability, pool lifecycle, audit, compatibility, and organization-wide limits for five-to-ten products. |
| Product exception | Per-product JSON may select `Embedded`. | Enables Visual Studio in-process development/testing or an intentionally isolated deployment without copying connector code. |
| Pooling | Reuse a bounded pool of profile-generation-owned worker processes/official clients; coordinate all hosts by organization admission key and recycle workers by age, operation count, health, and memory. | Removes repeated connection setup without pooling user CRM sessions and gives WCF/SDK state a deterministic process cleanup boundary. |
| Login acceleration | Service-identity warm-up at host/profile readiness; a login may join only its existing bounded single-flight task. | Reduces cold-call connection/metadata cost while retaining no account, LINE ID, user token, password, browser session, or CRM session. |
| Version selection | Explicit configured CE 8.2 or CE 9.1 worker kind and pinned package set. | Prevents unsafe SDK assembly mixing, binding-redirect guesses, or request-time transport switching. |
| IFD | Authentication is owned by the selected Microsoft connector worker and proven by that worker's real-server operation matrix. | Avoids making direct Web API/CRMWeb diagnostics an unrelated prerequisite. |

## Requirements

### Functional

- Support CE on-premises Dynamics 365 8.2 and 9.1 through separately pinned
  official Microsoft NuGet worker profiles.
- Use explicit named JSON profiles for production routing. Each profile includes
  a configured API version, organization endpoint, authentication mode,
  organization identity expectation, and bounded runtime settings.
- Provide an operator-only validation/discovery workflow. It may identify a
  compatible API endpoint on the configured host, but it must never silently
  change a production route, host, profile, or version.
- Manage the complete profile-keyed connection/runtime pool inside the new
  Dynamics solution: HTTP handler/socket pool, HttpClient, authentication-token
  cache, metadata cache, retry state, concurrency limit, health state, and
  shutdown/reload disposal.
- Expose a product-facing REST API with versioned, capability-controlled
  pre-registered query shapes and commands. It must not expose an unrestricted
  transparent proxy or generic query surface for arbitrary CRM URL, headers,
  OData text, FetchXML, credentials, filters, or profile.
- Before migrating any workload, produce an Organization-call coverage matrix
   for every current `IOrganizationService`, `OrganizationRequest`, SOAP/SVC,
   SDK helper, and CRM pool call site. Each row must map to an approved Web API
   capability with v8.2/v9.1 metadata/smoke proof, a temporary legacy item with
   owner/removal deadline, or explicit out-of-scope. Generic Execute parity is
   not an acceptable row status. The matrix is versioned machine-readable data;
   CI must verify its operation ID, typed parameters, encoding contexts, version
   evidence, and audit/idempotency class against the generated operation registry.
- Define one product integration abstraction with two operator-selected host
  modes. `Gateway` is the default and uses the internal REST contract;
  `Embedded` references only a supported no-SDK host adapter and runs the same
  capability registry, validation, runtime lifecycle, and bounded admission in
  the product process. A product JSON file may choose its mode at deployment
  time, but a user request, LINE identity, browser session, or runtime request
  cannot change it. The setting is validated on startup and takes effect only
  through a replace-and-drain restart/reload.
- Allow Visual Studio development to select an embedded fake-server or a local
   Gateway integration profile through `appsettings.Development.json`. It must
   use non-production secret references or no secret at all; it must not make a
   developer workstation silently use production CRM credentials. Embedded fake
   mode still requires a separate Development trust anchor (approved local
   registry or signed Development manifest) and must remain NotReady if it is
   missing, invalid, expired, or attempts to authorize a production endpoint,
   identity, secret, registry, or signing key.
- Support the approved Organization operation matrix, including common CRUD,
  QueryExpression or server-owned FetchXML, paging, metadata, actions/requests,
  and bounded batch operations through typed worker commands. Products cannot
  submit arbitrary FetchXML, SDK types, organization URLs, or generic Execute.
- Support the Microsoft connector's documented on-premises authentication
  modes for the exact CE target. Credential material is resolved inside the
  worker from an approved secret reference and never appears in process
  arguments, product JSON, logs, or persisted IPC. Browser/end-user sessions are
  never a pool or cache key. Future Dataverse OAuth is a separately declared
  adapter mode, not an implicit compatibility claim for CE on-premises.
- A Windows/IWA profile is unavailable until each concrete runtime hosting mode
  (Gateway or Embedded on Windows service/IIS/gMSA, or target-like Linux
  Kerberos/keytab) passes a real environment smoke test.
- Windows authentication configuration is a strict union: a validated host
  identity (service/IIS/gMSA/Kerberos) has no credential-secret fields, while a
  separately approved non-human service account uses secret references only.

### Security and isolation

- A product identity is mapped server-side to permitted organization aliases,
  profiles, tables, fields, operations, and rate/concurrency limits.
- Product callers cannot submit an endpoint, credential, authorization header,
  or unapproved profile name that changes outbound CRM routing.
- The policy layer derives a bounded server-side `WorkloadSubjectId` from the
  authenticated product workload and carries it with the operation revision in
  queued work. It must never retain a raw JWT, end-user identity, LINE ID,
  browser/session ID, user token, or Dynamics credential in a runtime key,
  queue, audit record, metric tag, cache, or correlation ID.
- Product JSON is never the source of authorization truth. Gateway mode derives
  workload identity from the authenticated internal service principal. Embedded
  mode must verify product/profile/admission binding against a signed manifest or
  central registry before resolving any secret, runtime, or queue slot. If the
  manifest/registry is unavailable, times out, is stale, or verification fails,
  Embedded fails closed / remains NotReady; local JSON can never grant
  production access by itself.
- Secrets are resolved at runtime from an approved secret provider by reference;
  no password, client secret, token, or certificate private key is stored in
  JSON, logs, telemetry, exceptions, source, or test fixtures.
- The design treats session/profile/token/credential/cache leakage as a
  zero-tolerance release blocker. Profile state must never be shared without an
  immutable profile-generation key.
- Every multi-field runtime/admission/ledger key uses typed structural equality
  and a versioned length-prefixed canonical encoding at store boundaries; direct
  string concatenation is prohibited.
- Secret version changes must create a validated replacement profile generation
  through a secret-provider version signal/poll. They cannot mutate active
  credentials in place.
- A rejected/revoked replica lease, or a lease that reaches TTL expiry before
  bounded renewal succeeds, is fail-closed: the affected runtime host
  immediately stops admitting new outbound CRM work, becomes NotReady, and
  force-cancels any work that has not finished by expiry. A new work lease is
  admitted only when its complete maximum lifetime fits before the slot-expiry
  fence. The coordinator quarantines an expired/revoked slot before reuse for
  the maximum outbound-work lifetime plus a settlement margin, so an old host
  and replacement can never exceed the aggregate Dynamics budget. A transient
  renewal error within a still-valid lease is retried only inside that TTL;
  there is no local expiry extension or admission "grace period" after lease
  loss.
- Every organization admission plan is versioned by a durable global
  configuration/admission epoch. A runtime host must hold the current epoch to
  become Ready; an epoch change uses the smaller safe capacity during handoff,
  fences stale hosts, and fails closed on confirmed credential revocation.

### Performance and lifecycle

- The gateway supervisor must reuse bounded profile-generation-owned worker
  processes and each worker's official client instead of creating a process,
  client, WCF channel, discovery request, or metadata request per call.
- It must use bounded concurrency, backpressure, cancellation, timeout,
   transient-failure retry, and Retry-After behavior. It must not use unbounded
   parallel requests as a performance strategy.
- Automatic compression remains disabled unless a profile-gated implementation
  first proves a safe real-target throughput/CPU/p95 benefit and applies bounded
  streaming decompression, decompressed-size/expansion-ratio limits, and
  malformed/stacked-content rejection.
- The aggregate concurrent work sent by every Gateway or Embedded runtime host
  to one Dynamics organization must remain below an organization-level budget.
  A failed distributed limiter must fall back to a fixed conservative per-host
  allocation, not unlimited throughput.
- Runtime caches and credentials are generation-isolated, but outbound admission
  is shared across all concurrently draining generations and aliases that target
  the same validated Dynamics organization. A configuration reload must never
  multiply that organization's concurrency budget.
- Profiles in different deployment-environment labels that resolve to the same
  physical Dynamics organization by expected organization ID and/or normalized
  base URI must share one explicit cross-environment `OrganizationAdmissions`
  entry. Otherwise startup fails closed so environment labels cannot accidentally
  double the Dynamics budget. The budget key is the canonical physical
  organization capacity key, not the environment label by itself.
- Profile configuration must fail validation unless
  `AggregateMaxInFlight >= MaximumRuntimeHosts >= 1`, the derived
  `LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumRuntimeHosts)` is
  at least one, and connection/queue/timeout values are finite and within
  deployment-owned hard bounds. `MaxConnectionsPerServer` cannot exceed the
  local outbound-work limit, and a queue cannot outlive its request deadline.
- Equal per-host allocation is the Phase 1 conservative default. Host-role
  weights may be introduced later only as a versioned `OrganizationAdmissions`
  field with CI proof that Gateway, Embedded, blue/green, and canary hosts remain
  below the aggregate budget even when the distributed limiter is unavailable.
- Deployment CI/HPA limits and a runtime-host slot lease must enforce the
  organization admission plan's maximum host count; an excess instance remains
  NotReady. Gateway and Embedded hosts targeting the same organization share
  this coordinator even though their physical HTTP pools remain process-local.
- Every organization admission plan has an owner, measurement date, approved
  Gateway and Embedded host maxima, HPA/IaC maxima, fairness policy, and rollout
  policy. CI must detect drift between that artifact and deployment limits.
- Queue fairness is measurable: per-workload queue share, weighted/deficit fair
  dispatch, rejection order, and starvation bound are declared before release.
- A graceful termination retains its replica slot until existing outbound work
  drains, then releases it atomically. Rollout policy must use a capacity-aware
  handoff and may not demand a new replica become Ready while every safe slot is
  still held.
- Retryable writes require a CRM alternate-key/upsert semantic or a durable
  cross-replica idempotency ledger. If the ledger is unavailable, the write
  fails before outbound CRM traffic.
- The idempotency ledger uses an atomic bounded-format key, fixed retention,
  per-workload/global quota, and an HMAC request fingerprint. An outcome that is
  uncertain after CRM dispatch is retained as non-replayable rather than retried.
- A queued dispatch envelope and idempotency record bind the immutable
  capability operation definition revision/hash. A registry rollout cannot
  silently apply a changed template to a request already queued or to an
  idempotency-key retry.
- Memory, timers, cancellation registrations, response streams, handlers, and
  background workers must have deterministic lifecycle ownership and disposal.
- Every capability, service document, and metadata response has declared finite
  request/response/parser limits; queue, cache, ledger, telemetry, and error
  payloads are bounded and cannot retain raw credentials, tokens, HTTP context,
  or unbounded CRM bodies.
- Gateway operation responses are private/no-store. Telemetry must use an
  allowlisted redaction path before export, rather than relying on a later sink
  to suppress URLs, query strings, headers, bodies, or serialized exceptions.
- Audit retention has hard entry/byte bounds and reserves capacity before CRM
  dispatch. The reservation is a durable atomic audit intent with recovery and
  terminal states, not an in-memory counter. Retention failure alerts
  immediately; at hard quota the Gateway fails the operation before CRM traffic
  rather than retaining audit data unboundedly or dropping it silently.
- The connector must implement a bounded, service-identity-only, profile
  generation warm-up. Startup/profile readiness pre-warms service document,
  bounded metadata, and a read-only identity probe through the same admission,
  lease, audit, and cancellation rules. A login may join an existing
  single-flight warm-up only by static product/profile binding; no user account,
  LINE ID, user token, credential, browser session, or user-specific CRM state
  may be used as a warm-up/pool key or retained in a cache, queue, audit record,
  metric tag, or correlation ID.
- Leak verification must use runtime-owned counters and collection/disposal
  sentinels: after every controlled drain, retired-generation handlers, timers,
  workers, request leases, registrations, streams, queues, and strong runtime
  references return to zero/baseline within the declared deadline. Any
  unexplained retained object or sustained post-warm-up resource trend blocks
  release.
- A profile configuration change creates a new immutable generation, drains
  requests using the old generation, then disposes it. It cannot mutate a live
  profile runtime in place.

### SDK-isolation and legacy-removal end state

- No project in the solution may reference or load a DLL from
  D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL.
- No product, Gateway, ProductClient, Embedded, Abstractions, or ordinary test
  project may reference Microsoft.Xrm.*, Microsoft.CrmSdk.*,
  Microsoft.Crm.Sdk.*, Microsoft.PowerPlatform.Dataverse.Client,
  IOrganizationService, OrganizationServiceProxy, or DiscoveryServiceProxy.
  Only explicitly allowlisted CE 8.2/9.1 worker projects and their worker-only
  tests may reference the pinned Microsoft packages/types.
- The repository's Data8 `PowerPlatform.Dataverse.Client` is temporary legacy
  only. Final acceptance requires removing it from `SpeechMessageProducts.sln`,
  removing every ProjectReference to it, and deleting or moving it out of
  buildable source after all consumers migrate to the official worker path.
- The final SDK-boundary gate must scan solution, project, package, props,
  targets, source, and test artifacts. A passing build is not enough if an SDK
  reference exists outside the explicit worker allowlist or if a worker exposes
  an SDK type across IPC.
- CRM 2011 OrganizationData.svc/OData v2 is not a supported fallback.

## Acceptance criteria

- [ ] A technical design describes the gateway-versus-library alternatives and
      records why Gateway is the default for the five-to-ten product scenario,
      while an approved embedded host adapter remains available through a
      product-owned JSON execution-mode setting.
- [ ] The design specifies a new Dynamics project layout inside the existing
      `SpeechMessageProducts.sln`, clear ownership
      boundaries, REST contract direction, and a migration sequence.
- [ ] The design includes a safe JSON named-profile schema, safe discovery
      rules, and CE 8.2/9.1 compatibility limits.
- [ ] The connection-pool design owns all profile runtime state and documents
      zero-tolerance isolation, deterministic disposal, reload/drain, and
      performance behavior.
- [ ] The design specifies security controls that prevent caller-directed
      outbound CRM routing and secret exposure.
- [ ] The design contains measurable unit, integration, worker crash/recycle,
      IPC isolation, soak, fault-injection, performance, official-worker
      real-server smoke-test, and SDK-boundary gates.
- [ ] The design defines a safe maximum-throughput target: connection reuse and
      bounded admission improve speed without exceeding the validated Dynamics
      organization budget, and coordinator failure stops new CRM admission.
- [ ] The design defines a bounded service-identity warm-up and proves that a
      login cannot create or retain a per-user CRM session, token, LINE ID, or
      connection-pool entry.
- [ ] The design specifies a strict, versioned, duplicate-aware product JSON
      schema for Gateway versus Embedded mode, Visual Studio development, and
      a shared organization-admission coordinator across both modes.
- [ ] The design includes the Organization-call coverage matrix, phased
      migrated-product CI gates against legacy SDK/pool bypasses, a selected
      durable coordinator/ledger/audit backend ADR, and a measurable queue
      fairness/capacity artifact.
- [ ] The final source inventory identifies the current SDK/DLL coupling and
      keeps the scope honest; no production source behavior is changed in this
      planning task.

## Out of scope for this planning task

- Implementing the gateway, changing existing product behavior, or deleting
  existing SDK dependencies.
- Claiming 8.2 or 9.1 support without deploying and testing the selected
  official worker against the target server.
- Reopening the D365APP01 IFD wizard, repeating direct Web API `WhoAmI`, or
  treating CRMWeb/ASP.NET diagnostics as a prerequisite for the official worker
  implementation. Those actions belong to a separate operations issue only if
  the selected worker later produces evidence that requires them.
- Turning the gateway into a generic unauthenticated CRM/OData forwarding proxy.
- Replacing product business rules with gateway business rules. The gateway owns
  Organization connectivity; products retain their product-specific behavior.
