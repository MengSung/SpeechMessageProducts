# Implementation plan: no-SDK Dynamics Access Gateway

## 2026-07-29 execution amendment

- Preserve completed Phase 4/5 abstractions, operation registry, ProductClient, admission, isolation, and lifecycle work; do not restart the implementation from zero.
- Validate Local Gateway first by running the existing Gateway host beside ChurchReport and pointing `ExecutionMode=Gateway` to localhost.
- Central Gateway remains the production deployment topology using the same REST contract.
- Do not add `CentralGateway` or `LocalGateway` enum values unless a separate contract change is approved; endpoint topology selects Central versus Local.
- Keep Embedded code but defer further Embedded rollout.
- Keep Data8 temporarily for the known-working CE 8.2 path, but isolate it behind a bounded/recyclable worker boundary before treating it as a Gateway runtime dependency.
- Evaluate proven Web API v8.2 and an official .NET Framework 4.8 `CrmServiceClient` worker as Data8 replacements. Keep 8.2 and 9.1 SDK workers separately version-pinned until real-server evidence proves consolidation safe.
- `Package01FeeReadsEnabled` remains false until the existing rollout gates pass.

The implementation must follow `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` and the decision explanation in `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`.

## Status — updated 2026-07-31

This plan is actively executing. The local Gateway foundation, runtime/policy
boundaries, deterministic isolation/lifecycle checks, and opt-in smoke harness
have implementation and verification evidence. Phase 4 is not complete: the
real CE 9.1 CRMWeb Claims/IFD gate, CE 8.2/9.1 real-server matrix,
cross-process capacity/fault proof, and soak/performance baselines remain open.
No product traffic is enabled while those gates remain open:
`Package01FeeReadsEnabled=false`, Phase 5 migration, and Phase 6 SDK removal
remain locked. The current Claims/IFD wizard evidence and the exact once-only
post-apply verification sequence are recorded in
`phase4-ifd-wizard-evidence-2026-07-31.md`.

## Preconditions

- Obtain non-production CE 8.2 and CE 9.1 environments with approved test
  accounts and a named data-cleanup strategy.
- Confirm each target's hosting/authentication feasibility:
  Windows/IWA for AD, or AD FS OAuth for IFD. A target that cannot pass the
  direct Web API probe is not silently routed to SOAP/WCF.
- For every Windows/IWA profile, approve and test one hosting mode before Phase
  1: Windows hosting with the required service identity/gMSA, or a target-like
  Linux Kerberos/keytab configuration. Without this proof the profile remains
  unavailable and cannot reach production.
- Confirm that every IFD profile has an approved non-password service-workload
  OAuth flow proven against the exact target: issuer/audience, `WhoAmI` service
  identity, cold-start/expiry/renewal behavior, and no browser cookie/user
  password/refresh-token/session persistence. The Gateway will not accept/store
  end-user passwords or enable ROPC as an implicit fallback.
- Define the first product's operation catalog, table/field classification,
  read/write needs, page sizes, write idempotency rules, and expected load.
- Select the deployment/identity/secret platform for the internal Gateway:
  workload identity, TLS/mTLS/JWT validation, secret provider, metrics sink,
  and at least two-replica production topology.
- Select a durable, shared `IRuntimeHostSlotCoordinator` with atomic conditional
   create/renew/fenced-release semantics and fault-test it before production. It
   must scope every Gateway, Embedded, blue/green/canary revision that sends to
   the same validated organization into the same canonical capacity entry and
   its approved `RuntimeHostSlotLeaseNamespace`. Define the global AdmissionEpoch, slot TTL, expiry-fence
  margin, maximum outbound-work lifetime, and quarantine interval before
  implementation.
- Before Phase 2 starts, write an ADR selecting the durable coordinator,
  idempotency ledger, and audit-retention backend. The ADR must define store
  technology, transaction/atomicity primitives, clock source, fencing-token
  semantics, acquire/renew/release, idempotency create-or-read, audit intent
  reservation, TTL/quarantine formulas, outage fail-closed behavior,
  backup/restore behavior, and deterministic test harness.
- Define the product-mode JSON schema, permitted Visual Studio development
  profiles, all Gateway/Embedded deployment replica maxima, and each
  organization plan's `MaximumRuntimeHosts`. The sum of all possible host
  replicas must be included in every conservative fallback calculation.
- Define the Embedded manifest/registry verification source and timeout policy.
  Embedded startup must fail closed when the source is unavailable, times out,
  is stale, or signature/policy verification fails; local JSON is not sufficient
  authorization to resolve a production profile.
  The manifest schema must include schema version, product ID, workload subject,
  permitted profile binding, permitted coordinator reference, environment,
  expiry, monotonic version, and signing key ID; registry responses must also
  include revocation state and policy decision version.
- Establish the current production-traffic baseline and CRM service-protection
  budget. Performance targets are validated against this baseline.
- Rotate existing CRM credentials as part of the first production profile
  enablement; do not copy the current secret material into the new solution.

## Phase 0 — Baseline and safety inventory

1. Record every SDK package/reference/type consumer, including production,
   tests, direct DLL HintPaths, SDK-shaped interfaces, and plaintext/fallback
   credential locations. Treat the current inventory as a migration manifest.
2. Add a CI report-only scan for banned SDK DLL paths/packages/types. Change it
   to a failure gate only after the migration removes each consumer.
   Record the actual current Microsoft.Crm.Sdk.Proxy HintPath as well as the
   user-named Dynamics 365 SDK DLL root; do not rely on one literal absolute
   path string to detect the violation.
   For every migrated workload/product root, promote this from report-only to a
   mandatory gate against `Microsoft.Xrm*`, `Microsoft.CrmSdk*`,
   `Microsoft.PowerPlatform.Dataverse*`, `IOrganizationService`,
   `ICrmConnectionPool`, `ToolUtilityFactory`, `ToolUtilityClass` CRM helpers,
   and raw CRM connection strings, except exact files/use cases still listed in
   the temporary-legacy matrix with owner/removal deadline.
3. Build the Organization-call coverage matrix for every current
   `IOrganizationService`, `OrganizationRequest`, SOAP/SVC, SDK helper, and CRM
   pool call site. Required columns: product, file/member, legacy SDK/SOAP entry
   point, current `OrganizationRequest`/helper shape, table/entity touched,
   operation kind (read/write/action/function/metadata/batch), data
   classification, side-effect/idempotency class, proposed
   `capabilityOperationId`, server-owned Web API route/action/function/FetchXML
   template, named typed parameter schema, XML/OData encoding context for each
   parameter, v8.2 metadata evidence, v9.1 metadata evidence, non-production
   smoke-test evidence, audit requirement, migration status, temporary-legacy
   owner, and removal deadline. CI fails a migrated source root when any current
   call site lacks a matrix row or when a row is missing one of these columns.
   Store this matrix as versioned machine-readable data. CI loads it with the
   generated `OperationDefinition` registry and fails when a migrated row's
   capability operation ID, typed parameter list, XML/OData/URI/multipart
   encoding context, version evidence, or audit/idempotency classification does
   not exactly match the registered operation.
4. Define the operation registry for the first consumer. Each operation must
   state its `capabilityOperationId`, product capability, logical alias, fixed
   server-owned CRM template, named bounded parameter schema, data classification, timeout,
   page/write limit, immutable `OperationDefinitionRevision`/template hash,
   idempotency behavior, and CE 8.2/9.1 capability requirement.
5. Create a security threat model covering caller-controlled routing, SSRF,
   credential/token/cookie leakage, user impersonation, query abuse, data
   exfiltration, and profile reload/secret rotation.

## Phase 1 — New projects and contracts in the existing solution

**Design mapping:** this is the design's **Foundation** stage. The design's
**Gateway and host control plane** spans Phase 2 plus Phase 3 below; **Prove** is
Phase 4; **First consumer / product-by-product migration** is Phase 5; and
**Removal / enforcement** is Phase 6. Phase 0 ADR/capacity prerequisites are a
hard gate before Phase 2 begins.

1. Create the Dynamics project group and add every project to the existing
   `SpeechMessageProducts.sln`; do not create a mandatory separate
   `SpeechMessage.Dynamics.sln`:
   - SpeechMessage.Dynamics.Abstractions
   - SpeechMessage.Dynamics.WebApi
   - SpeechMessage.Dynamics.Gateway
   - SpeechMessage.Dynamics.Embedded
   - SpeechMessage.Dynamics.Tests
   - SpeechMessage.Dynamics.SmokeTests
2. Keep all project files free of Microsoft.Xrm, Microsoft.CrmSdk,
   Microsoft.PowerPlatform.Dataverse.Client, WCF CRM, and external Dynamics SDK
   DLL references.
3. Define DTO-only abstractions. Do not expose Entity, QueryBase, ColumnSet,
   OrganizationRequest, IOrganizationService, or a generic Execute equivalent.
4. Define one product-facing execution abstraction with two trusted adapters:
   Gateway REST and Embedded in-process. Define a versioned, duplicate-aware
   product JSON schema with exactly one `ExecutionMode` branch. Gateway mode
   accepts only the internal Gateway endpoint/alias; Embedded mode accepts only
   a product profile binding/coordinator reference. Reject raw CRM URI,
   credentials/tokens, user/LINE/session fields, inactive mode branch, unknown
   or duplicate properties, and per-request mode switching. Add
   `appsettings.Development.json` guards that reject production secrets and
   production organization identities.
    Treat product JSON as a startup binding, not authorization truth: Gateway
    derives `WorkloadSubjectId` from authenticated workload identity and Embedded
    verifies its binding/coordinator reference against a signed manifest or
    central registry before resolving any secret/runtime/admission slot. If the
    manifest/registry cannot be reached within the bounded startup timeout, is
    stale, or verification fails, Embedded remains NotReady and never falls back
    to trusting the local JSON values.
    Define a separate Visual Studio development trust anchor: an approved local
    development registry or signed Development manifest may authorize only fake
    endpoints and designated non-production organization identities. It cannot
    use production signing keys/registry/secrets, and unavailable/invalid/
    expired development trust artifacts also leave Embedded NotReady.
5. Define one product-facing execution shape for Gateway mode:
   `POST /v1/organizations/{alias}/operations/{capabilityOperationId}`. Its
   request contains only typed bounded named parameters and an idempotency key
   where required. Define errors, correlation IDs, and operator-only diagnostics.
   Do not accept a CRM schema target, CRM action/function identifier, query text,
   FetchXML text/fragment/flag, endpoint, profile, URL, header, or low-level
   connector reference from a product. A capability that uses FetchXML owns a
   fixed server-side template and receives only typed bounded named parameters.
6. Add contract tests before implementation so any accidental transparent
   proxy input is rejected by the contract. Add parity tests proving Gateway and
   Embedded adapters expose the same bounded capability DTOs/errors while a
   product cannot reference the low-level WebApi project.

## Phase 2 — Profile runtime and no-SDK Web API connector

1. Implement a duplicate-aware streaming JSON parse and versioned schema
   validation before normal options binding. Validate profile IDs, normalized
   HTTPS `OrganizationBaseUri` values (including approved virtual-directory
   paths, no user-info/query/fragment), explicit v8.2/v9.1 API routes, auth mode, secret references,
   organization IDs, cache sizes, timeouts, and concurrency bounds. Reject a
   profile unless `AggregateMaxInFlight >= MaximumRuntimeHosts >= 1`; derive
   `LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumRuntimeHosts)` and
   reject it if below one. Require finite deployment-capped
   `LocalQueueCapacity`/timeouts and positive bounded
   `MaxDispatchEnvelopeBytes`; reject an envelope before queueing if its
   canonical serialized size exceeds that manager-owned limit.
    `1 <= MaxConnectionsPerServer <= LocalMaxInFlight`; do not bind an
    independently configurable local concurrency value. Require every Gateway
    and Embedded deployment/HPA maximum to be included in
    `MaximumRuntimeHosts`; Gateway-dependent production additionally reserves at
    least two ready-capable Gateway hosts in the shared organization-capacity
    domain. Treat this equal per-host derivation as the Phase 1 conservative
    default; if `HostRoleWeights` is introduced later, validate the weighted
    Gateway/Embedded/blue-green/canary sum against `AggregateMaxInFlight` and
    keep the no-distributed-limiter fallback bounded.
    Require exactly one `OrganizationAdmissions` entry, keyed by
    `CanonicalOrganizationCapacityKey` (validated expected organization GUID
    plus normalized organization base URI), for every shared physical Dynamics
    organization.
    If two `deploymentEnvironment` labels resolve to the same physical
    organization by expected organization GUID and/or normalized organization
    base URI, reject startup unless one explicit cross-environment
    `OrganizationAdmissions` entry merges their budgets. Implement distinct
    `CanonicalOrganizationCapacityKey`, `RuntimeHostSlotLeaseNamespace`, and
    queue/permit `OrganizationAdmissionKey` types so environment labels cannot
    accidentally become independent capacity budgets. Delete any helper that can
    derive queue/semaphore/permit/lease capacity directly from
    `tuple(deploymentEnvironment, expectedOrganizationId)`; all such runtime
    keys must be resolved from the validated `OrganizationAdmissions` entry.
   Resolve aggregate/runtime-host limits, local queue capacity, envelope byte
   limit, queue-admission deadline policy, and admission-drain timeout only from
   that manager-owned entry; reject missing entries and all profile-local
   overrides. Validate the global worst-case queued payload bound as
   `MaximumRuntimeHosts * LocalQueueCapacity * MaxDispatchEnvelopeBytes`. Derive and validate the exact
   `ApprovedWebApiRoot` from base URI plus API version; test URI normalization,
   base-path preservation, duplicate names, unknown fields, and schema-version
   transitions before accepting a replacement generation.
   Require every `OrganizationAdmissions` entry to record owner, measurement
   date, CE target/version, Gateway host maximum, Embedded host maximum,
   deployment/HPA maxima, rollout policy, and fairness policy. CI must compare
   this artifact with IaC/HPA before a profile can become Ready.
2. Implement secret resolution by reference. Reject plaintext password, token,
   client secret, embedded URI credentials, duplicate profile IDs, and unknown
   fields in production configuration.
   Implement a non-secret secret-version monitor (provider notification plus
   bounded version-stamp polling fallback) that publishes a new profile
   generation for rotation/revocation; never mutate active credentials in place.
   Bind authentication as a strict tagged union: Windows `HostIdentity` has no
   username/password/domain fields; Windows `SecretReference` permits only
   non-human service-account secret references; AD FS OAuth has its own exact
   validated shape with only authority/client-ID/target-specific
   feasibility-evidence/credential references and no
   password/ROPC/client-secret/certificate/private-key fields. Reject mixed
   credential-source fields and block an IFD profile until its recorded
   cold-start service-flow proof succeeds.
3. Implement the immutable `ProfileRuntimeKey` and `ProfileRuntimeManager`.
   ProfileRuntime owns the handler, HttpClient, credentials/token provider,
   metadata cache, retry/circuit state, health state, and deterministic disposal.
    Implement `CanonicalOrganizationCapacityKey`, entry-resolved
    `OrganizationAdmissionKey`, and entry-resolved
    `RuntimeHostSlotLeaseNamespace` as distinct non-secret types. The canonical
    key owns the physical organization budget; `OrganizationAdmissionManager`
    owns the local queue/semaphore and optional distributed permits only for the
    entry-resolved admission key across all Gateway/Embedded hosts, aliases, and
    old/new runtime generations mapped to that organization. It must not hold any
    credential, token, cache, user/LINE/session data, or caller session. Give every envelope a bounded
   server-derived `WorkloadSubjectId`, immutable `OperationDefinitionRevision`/
   template hash, and
   deadline; cancellation atomically removes undispatched work and fairness
   bounds one workload's queue share. Reject conflicting aggregate-budget/host
   settings for one key and require a new profile/alias for a different
   organization ID.
   Implement explicit fairness: per-workload queue caps plus deficit/weighted
   fair dispatch with an aging/starvation bound. Reject before enqueue in order:
   expired deadline, oversized envelope, stale/unauthorized operation revision,
   per-workload cap, then organization cap; expose wait/share/reject metrics.
   Use typed tuple records for every composite key in memory. Define
   `CanonicalKeyV1` for process/store boundaries: fixed key kind and field
   order, UTF-8 byte-length prefixes, and base64url encoding; never concatenate
   component strings. Bound every source field and version the encoding.
4. Implement the durable `AdmissionEpoch` control-plane record and
   `IRuntimeHostSlotCoordinator`: atomic acquire/renew/fenced-release,
   current-epoch acknowledgement, expiry-fence admission calculation, and
   expired/revoked-slot quarantine. Require a work lease's maximum request plus
   cleanup lifetime to fit before slot expiry. On lease loss, cancel work at the
   fence; do not reissue the capacity until quarantine ends or zero active work
   is durably acknowledged. Publish capacity/configuration changes at the
   smaller safe old/new budget and fence stale Gateway/Embedded hosts.
5. Use one long-lived SocketsHttpHandler and HttpClient per active profile
   generation. Configure no cookies, no automatic redirect, `UseProxy = false`, bounded
   MaxConnectionsPerServer, pooled lifetime/idle timeout, normal TLS
   validation, and request-level authorization/OData headers. Keep
   `PreAuthenticate` disabled unless an exact target-like Windows/IWA smoke test
   compares disabled/ enabled behavior, proves no cross-profile signal for
   connection-bound auth, and shows a measured benefit for that specific profile.
   Do not use ambient/system proxy settings or `HttpClient.DefaultRequestHeaders`
   for Dynamics authorization or caller data; create and dispose one request
   message per outbound call. A proxy capability is out of scope until separately
   security-reviewed as a profile-owned boundary.
   Make ProfileRuntime the single owner of both objects and construct
   `HttpClient` with `disposeHandler: true`; no factory/caller may share or
   dispose its handler. Verify exactly-once handler disposal under normal drain,
   cancellation, and replacement failure.
   Implement keyed asynchronous single-flight refresh for each profile token
   cache and each profile metadata refresh. A caller cancellation stops only its
   wait and cannot remove shared refresh work; only completion or runtime-drain
   cancellation removes the matching attempt identity. Lock cleanup must be
   cancellation/timeout safe and cannot become an unbounded dictionary.
6. Implement atomic replace-and-drain configuration reload. Cap every logical
   profile at one active plus one draining generation; serialize/coalesce rapid
   changes rather than accumulating handlers/token caches. The shared
   `OrganizationAdmissionManager` owns the only organization queue; entries are
   bounded typed dispatch envelopes with `WorkloadSubjectId`, immutable
   `OperationDefinitionRevision`/template hash, and policy-decision version, and no
   HttpContext/principal/JWT/headers/cookies/streams/credential/user/LINE/session/
   generation reference. On drain, stop only old-generation dispatch/retry and
   warm-up callbacks; dequeue rechecks current authorization but resolves the
   then-active compatible generation only for the identical
   `OperationDefinitionRevision`.
   Reject an item that cannot safely rebind before CRM traffic. Await background
   loops and single-flight owners before disposing handlers; retain the shared
   queue until its final compatible-generation reference is released. Add tests for malformed
   updates, secret rotation, cancellation, shutdown, repeated reload, and reload
   while traffic is in flight.
7. Implement direct HTTP/OData request composition, error mapping,
   cancellation, timeout budget, read retry/Retry-After, circuit behavior,
   paging, metadata discovery, approved FetchXML, actions/functions, and
   controlled batch parsing. Do not automatically retry writes or arbitrary
   multipart content. Define finite capability-specific request/response/page
   byte limits and page count. Set default request headers: request-scoped
   authorization only,
   `Accept: application/json`, `OData-Version: 4.0`, `OData-MaxVersion: 4.0`,
   and `Content-Type: application/json` for JSON bodies. Allow `Prefer` only
   through operation-approved values such as `return=representation`,
   `odata.include-annotations`, and `odata.maxpagesize`; never forward a caller
   supplied `Prefer` header blindly. Reject excessive Content-Length and enforce the
    same limit while streaming chunked bodies; disable automatic decompression
    and ambient Accept-Encoding for the first release, and reject every received
    `Content-Encoding` before parsing. Add a later profile-gated compression
    path only after real-target throughput/CPU/p95 evidence shows a safe benefit;
    use allowlisted encodings, bounded streaming decompression, decompressed-byte
    and expansion-ratio caps, a finite encoding-chain policy, and malformed/
    stacked-content rejection before parsing. Follow a returned nextLink only after
   validating HTTPS, same approved organization origin **and base path**, and
   exact `ApprovedWebApiRoot`/configured API-version path; reject any other link
   before a credential-bearing request.
    Apply separate finite service-document/metadata byte limits and parse CSDL
    with DTD/external-entity resolution disabled and bounded XML
    depth/character/name counts before caching the validated model.
    Implement operation builders as metadata-typed templates with
    context-specific XML attribute/text, OData literal, URI component, and
    multipart encoders. Do not concatenate typed parameter values into FetchXML,
    OData filters, URLs, headers, or multipart boundaries. Reject a capability at
    startup if any parameter lacks a declared encoding context or if a template
    placeholder can alter entity/attribute names outside the allowlist.
    Implement write idempotency per operation: use CRM alternate-key/upsert
   semantics when possible, otherwise a bounded retained idempotency ledger
   keyed by workload subject/logical profile/expected organization/capability
   operation ID/idempotency key. Store the immutable
   `OperationDefinitionRevision`/template hash and never blindly replay a write.
   The durable idempotency ledger is shared across Gateway and Embedded hosts,
   has atomic create-or-read semantics with a fixed TTL, and stores only a
   fingerprint hash/minimal redacted result reference, not a token, credential,
   raw request body, or unbounded response. If it is unavailable or cannot
   atomically record/read, fail a ledger-dependent write before any outbound
   request; sticky sessions are not an allowed substitute. An equal identity
   under a different `OperationDefinitionRevision` is a typed conflict, never a
   rebind.
    Persist `Pending` atomically before dispatch and retain `OutcomeUnknown`
    after any post-dispatch cancellation/transport/process uncertainty. A
    duplicate must return its recorded, in-progress, or outcome-unknown result;
    it must not replay the write. Bounded recovery turns expired pending records
    into outcome-unknown rather than deleting them for reuse. Automatic retry
    after uncertainty is allowed only for an operation with proven CRM
    alternate-key/upsert idempotency.
    Require a 1–128-character URL-safe key, per-workload/global ledger
    entry/byte quotas, and a versioned HMAC-SHA-256 canonical-envelope
    fingerprint including `OperationDefinitionRevision`. Reject quota exhaustion before CRM
    dispatch and return a typed conflict for key reuse with a different
    fingerprint. Resolve its HMAC key by a separate secret-provider reference,
    retain prior verification material through the entire ledger window, require
    coordinator acknowledgement before removal, fail ledger-dependent writes
    closed if verifier/KMS material is unavailable, and never log key/input.
8. Implement capability validation using service document/CSDL metadata and
   expected organization identity. Label this as configured-route/capability
   validation, not CE release detection; obtain/record Discovery-service release
   data separately whenever exact product-release proof is required. Treat v8.2
   unsupported features as explicit capability failures.
9. Implement profile-generation warm-up as a low-priority, service-identity-only
   single-flight action: bounded service document/CSDL fetch plus read-only
   `WhoAmI`, using the same admission permit, runtime-host expiry fence, audit
   intent, deadline, cancellation, and disposal paths as ordinary work. Allow a
   login only to join that existing bounded task by static profile binding;
   prohibit user/LINE/session/token/credential keys or retention. Disable IFD
   warm-up until its target-specific noninteractive service proof exists.

## Phase 3 — Gateway/Embedded policy and controlled operations

1. Implement internal TLS plus workload authentication. Derive product identity
   only from a validated workload credential, never an untrusted header/body.
   For Embedded mode, derive the identical bounded workload subject from trusted
   startup configuration; never from a user, LINE ID, browser session, or raw
   JWT claim.
2. Implement server-owned product -> alias -> profile -> capability mapping.
   Apply field, operation, page, write, rate, and concurrent-request policy
   before a profile runtime is acquired.
3. Implement an operation registry. Map each product capability to a fixed
   Web API template/projection/action and named bounded parameters rather than
   accepting arbitrary OData, generic filters, dynamic URLs, headers, caller
   identity, or raw batch payloads. Version/hash every definition and carry that
   immutable revision through queueing, audit, and idempotency behavior.
4. Implement sanitized structured telemetry, audit, health/readiness, and
   profile lifecycle metrics through an allowlisted adapter. Remove/redact raw
   URL/query/header/body and exception-object serialization before any exporter.
   Set `Cache-Control: no-store, private` for all organization-operation
   responses/errors and exclude those routes from output/shared response cache.
   Require a distinct operator workload identity/policy for profile-health
   diagnostics; ordinary product identities cannot call that endpoint. Add
   redaction, cache-header, and operator-authorization regression tests.
   Implement audit retention as a hard bounded durable store with pre-dispatch
   `AuditIntent(Reserved)` capacity claim. For a ledger-dependent write,
   transactionally create the audit intent with idempotency `Pending`; transition
   the intent to `Dispatching` immediately before CRM traffic, then transactionally
   finalize both records. Recover every crash/post-dispatch failure as retained
   `OutcomeUnknown`; release capacity only through durable terminal/recovery
   state. Retention-job failure alerts immediately; reservations throttle at a
   high-water mark and fail closed at hard entry/byte quota before CRM traffic.
   Do not create an unbounded retry queue or silently drop audit events; warm-up
   uses the same low-priority intent path and non-audit telemetry has a separate
   bounded, drop-counted low-priority path.
5. Implement high availability behavior for Gateway and Embedded runtime hosts:
   readiness, bounded local limits, aggregate organization budgets, current
   `AdmissionEpoch`, and no cross-host token/client/credential/user-session
   sharing. Share each local admission controller across concurrent runtime
   generations for the same validated organization so a reload cannot double the
   budget. Count every Gateway/Embedded deployment replica in
   `MaximumRuntimeHosts`; configure the fixed local allocation that remains safe
   if the distributed permit limiter is unavailable and never autoscale past that
   count without a new validated organization plan.
   Enforce the maximum twice: CI/IaC rejects the aggregate HPA/deployment maxima
   above the organization plan, and each process acquires a renewable
   `RuntimeHostSlotLease` at the current epoch before becoming Ready. A process
   without a slot is NotReady. A single renewal RPC error uses bounded retry only
   while the current lease TTL remains valid. Admit work only if its maximum
   request/cleanup lifetime fits before the expiry fence. If the coordinator
   rejects/revokes/fences the lease or the TTL expires before renewal, stop new
   CRM admission/retries immediately, become NotReady, cancel remaining work at
    the fence, and quarantine the expired/revoked slot before reissue. Lease keys
    are `RuntimeHostSlotLeaseNamespace`, resolved from the canonical capacity
    entry and shared across Gateway, Embedded, blue, green, and canary hosts
    rather than scoped to an individual release.
   On graceful termination, become NotReady and close new admission, retain the
   slot until existing outbound-work leases drain to zero and runtime teardown
   completes, then fenced-release it. CI rejects rollout policies that require a
   surge host to become Ready before a slot holder can terminate; use a
   capacity-aware handoff that never exceeds aggregate admission.

## Phase 4 — Verification before any consumer migration

1. Run deterministic two-endpoint fake-server tests at high concurrency across
   at least five product workloads, Gateway plus Embedded hosts, and two profile
   generations. Prove no endpoint, auth, token, cookie, cache, retry, user/LINE/
   session, or correlation cross-talk.
   Include token/metadata single-flight stampede tests and unauthorized
   query/endpoint/impersonation escape attempts. Include a same-organization
   old/new generation overlap and prove their combined local and runtime-host
   outbound work never exceeds `AggregateMaxInFlight`.
   Validate strict Gateway/Embedded product JSON parsing, duplicate keys,
   inactive branches, Visual Studio development secret/organization guards, and
   the prohibition on direct WebApi project references from products.
2. Run profile reload/drain tests repeatedly under load. Assert at most one
   active plus one draining generation, retired runtime count, timer count,
   handler creation/disposal count, and live request lease count return to zero
   after the deadline.
3. Run allocation/GC/handle/socket/thread-pool soak tests with bounded caches
   and queues. Investigate any sustained post-warm-up growth before release.
4. Run fault injection for 401/token refresh, 429, 503, timeout, cancellation,
   malformed metadata, DNS/connection reset, invalid replacement config, and
   replica termination.
   Run a Gateway-plus-Embedded aggregate-permit test with and without the
   distributed limiter and prove all runtime hosts combined stay below the
   organization's Dynamics service-protection budget.
    Test OrganizationAdmissions owner/measurement/HPA maxima drift detection,
    per-workload queue caps, weighted/deficit dispatch, starvation bound,
    deterministic rejection order, and metrics for queue share/wait/reject reason.
    Test RuntimeHostSlotLease atomic create/renew/fenced-release,
    exhaustion/coordinator outage/HPA misconfiguration, current/old
    AdmissionEpoch, and Gateway/Embedded/blue/green/canary contention in the
    same canonical capacity entry / `RuntimeHostSlotLeaseNamespace`. Prove excess hosts remain
    NotReady, coordinator failure stops new CRM admission/retries immediately,
    each outbound work lifetime fits before its expiry fence, late work is
    cancelled, and a quarantined old slot cannot overlap replacement capacity.
     Test a transient renewal RPC failure within a valid TTL versus actual lease
     rejection/expiry, confirmed credential revocation, and graceful
     termination/rolling handoff: the old host must retain the slot until active
     outbound leases reach zero, then fenced-release it so the new host may
     become Ready without an aggregate-budget spike.
     Test cross-environment same-organization collision detection: two environment
     labels pointing at the same expected organization GUID and/or normalized
     organization base URI must fail startup unless one cross-environment
     `OrganizationAdmissions` entry explicitly merges the budget. Test Embedded
     manifest/registry unavailable, timeout, stale data, invalid signature, and
     policy-denied cases; every case must remain NotReady without resolving
     secrets or admission slots. Test equal allocation and any future
     `HostRoleWeights` path so the weighted host-role sum never exceeds
     `AggregateMaxInFlight`.
     Test server-owned FetchXML/OData parameter encoding with values containing
     single quote, double quote, less-than, greater-than, ampersand, percent,
     CR/LF, XML comment/CDATA terminators, and attempted filter/entity/operator
     injection. Prove the generated request remains structurally identical except
     for encoded values, or that startup rejects the operation definition.
     Test the machine-readable coverage matrix against the generated operation
     registry: every migrated call site must have exactly one row whose
     capability ID, typed parameter names, encoding contexts, version evidence,
     and audit/idempotency class match the compiled operation definition.
     Test Visual Studio Embedded fake profiles with a valid development manifest/
     registry and with unavailable, expired, invalid, production-key, production
     endpoint, and production-organization cases; only the valid non-production
     case may become Ready. Test optional compression only after its profile gate:
     allowed small compressed responses succeed within decompressed-byte and
     expansion-ratio bounds, while zip-bomb, malformed, stacked, unsupported, and
     over-limit content fails before parsing.
    Test nextLink validation with relative, valid same-base-path, cross-origin,
    wrong-base-path, wrong-version, and malformed values; only the first two may
    be followed. Test received Content-Encoding rejection in the initial release.
    Test the idempotency ledger state machine across Gateway/Embedded hosts,
    including duplicate fingerprint mismatch, immutable operation-revision
    conflict, in-progress, completed, post-dispatch timeout/cancellation,
    process-loss recovery, bounded TTL, HMAC verifier/KMS outage/rotation, and
    no replay of `OutcomeUnknown` writes.
    Test `UseProxy = false`, absence of authorization/default-header bleed,
    per-request disposal, caller-cancelled single-flight waiters, and a runtime
    drain that safely rebinds or rejects queued-undispatched requests, stops
    retry callbacks, and awaits background teardown before handler disposal. Test fixed response
    byte/page/decompression limits with Content-Length and chunked over-limit
    fake responses. Test service-document/CSDL declared/chunked overages and
    malicious DTD/external-entity/deep XML input. Test telemetry/exporter
    allowlisting, exception redaction, no-store response headers, output-cache
    exclusion, idempotency key length/quota/fingerprint-conflict behavior, and
    a queued request that rebinds or rejects safely during a generation drain.
    Test exact Windows authentication tagged-union validation, `HostIdentity`
    without secret fields, service-account-only `SecretReference`, and
    PreAuthenticate disabled/enabled target-like comparison before any enablement.
    Test HMAC fingerprint-key version rotation without logging key/input and
    without accepting a conflicting retained ledger fingerprint.
    Test `CanonicalKeyV1` with delimiter/Unicode/null-containing values,
    ambiguous-concatenation pairs, maximum permitted field lengths, version
    transition, and exact typed tuple equality across runtime/admission/ledger
    maps.
    Test caller attempts to submit FetchXML text/fragment/flag and assert the
    contract rejects them, while a registered server-owned FetchXML template
    accepts only its typed named parameter envelope. Test missing/duplicate
    `OrganizationAdmissions` entries or a profile-local admission override fail
    configuration validation.
    Inject retention-job failure, audit-store high-water/quota exhaustion, and
    recovery. Test durable audit intent reservation/pending/dispatching/
    completed/failed/outcome-unknown states and crashes at every ledger/audit/
    dispatch boundary. Prove an operation without an audit intent fails before
    CRM traffic, audit capacity cannot leak or disappear after dispatch, audit
    data never queues unboundedly in memory, alerts and drop/failure metrics
    fire, and non-audit telemetry remains bounded.
    Test safe warm-up on host start, first login joining an existing warm-up,
    queue pressure, cancellation, drain, and lease loss. Prove warm-up cannot
    retain a user/LINE ID/JWT/session/token/credential or create a per-user
    connection-pool entry.
5. Run the real CE 8.2 and CE 9.1 smoke matrix described in design.md. Capture
   configured API-route/auth/capability results, and record Discovery-service
   release data separately when exact CE product-release proof is required,
   without recording secrets.
6. Benchmark direct Web API, Gateway-added, and Embedded-added overhead under
   representative concurrency. Pin actual SLOs only after real-server evidence
   is available; neither host mode may weaken admission/lifecycle guards.
7. Run a full security review before a profile contains production credentials.

## Phase 5 — Strangler migration

1. Select one bounded, read-heavy ChurchReport workflow whose behavior can be
   verified against the legacy path. Define comparison data and rollback
   trigger.
2. Integrate its production path through Gateway mode behind a feature flag.
   Independently prove the product's Visual Studio Embedded fake-server/local
   development mode without production secrets. Use shadow read/compare only
   where data classification and side effects permit.
3. Migrate product use cases by operation catalog, not by bulk replacement of
   IOrganizationService. Gateway remains the production default; allow Embedded
   only after the product's host-count, admission, audit, secret, lifecycle, and
   smoke-test evidence matches the shared design. Metadata/option-set, Assign, SetState,
   ExecuteMultiple, marketing-list, and custom action paths require individual
   Web API parity decisions.
4. Maintain an explicit temporary-legacy list with owner, reason, target
   replacement operation, and removal deadline. It must never be selected
   automatically by version detection.
5. After every migrated product path, rerun contract, isolation, real-server,
   performance, and regression tests.

## Phase 6 — Final SDK removal

1. Remove the ChurchReport direct Microsoft.Crm.Sdk.Proxy HintPath.
2. Remove SpeechMessageProducts.sln's PowerPlatform.Dataverse.Client project
   entry after all consumers have migrated.
3. Remove ToolUtility's ProjectReference to
   `..\\PowerPlatform.Dataverse.Client\\PowerPlatform.Dataverse.Client.csproj`.
4. Delete or move the local PowerPlatform.Dataverse.Client project out of
   buildable source. It is a temporary legacy dependency, not a wrapper to keep
   or refactor.
5. Remove all Microsoft.Xrm/Microsoft.CrmSdk/Microsoft.PowerPlatform.Dataverse
   Client packages from production and test projects.
6. Remove WCF CRM adapters, SOAP pool/services, SDK-shaped interfaces, test
   fakes, and source types. Replace tests with Gateway contract/fake-server
   tests.
7. Remove legacy plaintext credentials/fallbacks, rotate credentials, and
   verify the secret provider is the sole runtime source.
8. Change SDK scans to mandatory CI gates. Verify that
   `SpeechMessageProducts.sln` no longer includes the legacy connector project
   after product migrations remove it.
9. Perform an independent dual-model architecture/code review and a final
   security/performance verification pass before declaring the migration done.

## Validation commands

The exact test command names are added with the new projects. The final
repository checks include:

~~~powershell
dotnet build SpeechMessageProducts.sln --configuration Release
dotnet test SpeechMessageProducts.sln --configuration Release
powershell .\eng\Verify-NoDynamicsSdk.ps1 -SourceRootManifest .\eng\no-sdk-source-roots.json
~~~

`no-sdk-source-roots.json` is generated/maintained from the solution/project
graph and contains every production/test project directory; it may exclude only
approved historical artifact roots such as `.trellis`, `.ccg`, and `docs`.
`Verify-NoDynamicsSdk.ps1` runs the three banned DLL/package/type scans inside
those roots and includes a PowerShell Select-String fallback for Windows agents,
so a missing ripgrep executable or planning documentation cannot turn a forbidden
SDK reference into either a skipped gate or a broad allowlist.

Smoke, soak, and load commands must use an explicit non-production profile
selector and fail closed if the target profile is not marked safe for testing.

CI gate matrix:

| Phase | Gate | Command / workflow | Fail condition | Artifact |
| --- | --- | --- | --- | --- |
| 0 | Legacy SDK inventory | `eng/Verify-NoDynamicsSdk.ps1 -Mode Inventory` | Source root missing from manifest or unclassified SDK hit | `artifacts/dynamics-sdk-inventory.json` |
| 1 | Product JSON contract | `dotnet test SpeechMessage.Dynamics.Tests --filter ProductModeJson` | Duplicate/unknown field accepted, production secret reachable from development JSON, or Embedded authorization trusted from local JSON | `TestResults/ProductModeJson.trx` |
| 2 | Runtime isolation | `dotnet test SpeechMessage.Dynamics.Tests --filter RuntimeIsolation` | Profile/session/token/credential/cache leak, unbounded queue, handler/timer/stream not disposed | `TestResults/RuntimeIsolation.trx` |
| 2 | Capacity and lease safety | `dotnet test SpeechMessage.Dynamics.Tests --filter AdmissionCapacity` | Same physical org double-budgeted, host-role sum exceeds aggregate, expired/revoked slot admits work | `TestResults/AdmissionCapacity.trx` |
| 3 | Web API compatibility | `dotnet test SpeechMessage.Dynamics.SmokeTests --filter WebApi82Or91` | v8.2/v9.1 route, metadata, auth, paging, batch, action, or FetchXML guard fails on approved non-production target | `artifacts/dynamics-smoke/*.json` |
| 4 | Performance/leak soak | `dotnet test SpeechMessage.Dynamics.Tests --filter SoakPerf` | Sustained memory/socket/timer/queue growth, SLO regression, or Retry-After/backpressure violation | `artifacts/dynamics-soak/*.json` |
| 6 | Final no-SDK enforcement | `eng/Verify-NoDynamicsSdk.ps1 -SourceRootManifest eng/no-sdk-source-roots.json` | Any production/test SDK package, HintPath, SDK type, legacy pool, or raw CRM string outside temporary-legacy matrix | `artifacts/no-sdk-scan.json` |

## High-risk rollback points

| Change | Rollback shape |
| --- | --- |
| Gateway rollout | Route product traffic back through its feature flag; preserve audit/correlation evidence. |
| Product Gateway/Embedded mode change | Reject invalid JSON before readiness. Roll back by restoring the prior trusted mode configuration and replace-and-drain the host; never change mode per request or retain a user connection/session. |
| Profile config/credential rotation | Keep the last validated generation while replacement validation fails; emergency-disable profile if credentials are revoked. |
| Admission epoch/host-slot failure | Fence the affected hosts, preserve slot quarantine, and accept bounded unavailability rather than bypassing the aggregate organization budget. |
| Product migration | Re-enable the documented temporary legacy path only for the migrated bounded use case, not for arbitrary profiles. |
| SDK deletion | Restore only the pre-removal migration commit while the root cause is addressed; never reintroduce an SDK fallback silently. |

## Review gates

- Before Phase 1: user/spec review, CCG dual-model architecture review, and
  security feasibility review.
- Before Phase 2: approved durable coordinator/ledger/audit ADR and capacity
  artifact/IaC drift check.
- Before Phase 4 completion: deterministic isolation/lifecycle tests,
  performance/soak evidence, and CE 8.2/9.1 real-server smoke evidence.
- Before Phase 6 completion: dual-model code review, all production/test SDK
  scans clean, secret rotation confirmed, and no temporary-legacy items open.
