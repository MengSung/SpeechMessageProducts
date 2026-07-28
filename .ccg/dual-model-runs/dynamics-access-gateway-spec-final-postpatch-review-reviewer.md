# CCG reviewer Task: dynamics-access-gateway-spec-final-postpatch-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# CCG reviewer task: Dynamics Access Gateway architecture SPEC

## Scope

Review the planning artifacts only. Do not modify production code and do not
review unrelated working-tree changes.

Files to review:

- .trellis/tasks/07-23-dynamics-connection-compatibility/prd.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/design.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/implement.md
- docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md

## User objective

Design a new shared Dynamics 365 Organization access solution for five current
products and future products. It must support CE on-premises 8.2 and 9.1 through
direct HTTP/OData v4 Web API, without CRM SDK DLLs or the GitHub-derived
PowerPlatform.Dataverse.Client implementation. The new solution owns
Connection Pool management. The requested final state forbids every solution
project from referencing or using the user-specified external Dynamics 365 SDK
DLL directory (`D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL`), any direct CRM SDK
DLL HintPath, or equivalent CRM SDK package/type dependency.

The user additionally requires that every product may use its own trusted JSON
configuration to choose the shared Gateway Web Service or an in-process
Embedded host adapter. Visual Studio development must be able to observe/test
either mode. This must not create a per-user CRM connection/session pool or
persist user account, LINE ID, JWT, browser session, credential, or token data.
Gateway remains the production default, but an embedded product must still use
the same no-SDK core, operation controls, durable audit/idempotency semantics,
and shared organization-wide admission budget.

Hard quality requirements:

- centralized Gateway Web Service must be justified, not assumed;
- zero-tolerance release gate for session/profile/token/credential/cache
  leakage and memory/resource leakage;
- high performance using safe connection reuse and bounded concurrency;
- JSON named profiles with secrets resolved by reference;
- safe explicit version routing plus validation/detection that never silently
  changes organization/version;
- products must not hold CRM secrets or use an unrestricted CRM proxy;
- no CRM 2011 OrganizationData.svc/OData v2 fallback;
- the migration plan must recognize broad existing Microsoft.Xrm/
  IOrganizationService coupling rather than pretending it is a single DLL swap.

## Review questions

1. Does the proposed Gateway + private no-SDK WebApi library give a technically
   sound answer for five-to-ten products, and are the Library-only and
   transparent-proxy alternatives rejected for concrete reasons?
2. Are HTTP handler/HttpClient, Windows credentials, OAuth token cache,
   metadata cache, retry/circuit state, queue/concurrency state, and reload
   lifecycle isolated by a sufficient immutable profile-generation key?
3. Does the design leave any path for cross-profile routing, secret leakage,
   caller-provided endpoint/header/profile escape, retention leak, stale
   runtime mutation, or unsafe automatic retry?
4. Are the CE 8.2/9.1 API-version and authentication constraints described
   safely, without assuming on-premise client-secret support or WS-Trust
   fallback?
5. Are performance and high-availability claims bounded, testable, and
   compatible with Dynamics service protection?
6. Are migration scope, no-SDK enforcement checks, and test/release gates
   sufficiently concrete?
7. Identify contradictions, missing explicit decisions, or dangerous
   assumptions. Do not request product decisions that can be safely deferred
   behind a stated feasibility gate.
8. Does the Gateway/Embedded host-mode JSON design preserve the central safety
   properties, allow safe Visual Studio development, forbid dynamic/user-driven
   selection, and correctly coordinate capacity across host modes?
9. Does the safe warm-up design accelerate cold/login-adjacent paths without
   retaining user-specific Dynamics connections, sessions, LINE IDs, or tokens?
10. Does the plan require an Organization-call coverage matrix before migration,
    so each current `IOrganizationService`/`OrganizationRequest`/SDK helper use
    is mapped to a bounded Web API capability, temporary legacy item, or explicit
    out-of-scope status?
11. Are migrated-product CI/startup gates strong enough to prevent legacy
    SDK/pool bypasses through `ICrmConnectionPool`, `ToolUtilityFactory`,
    Microsoft.Xrm/CrmSdk/Dataverse packages, or raw CRM connection strings?
12. Is the product JSON trust boundary explicit enough that editable JSON cannot
    grant authorization and Embedded bindings must be signed or registry-verified?
13. Are the durable coordinator/ledger/audit ADR, queue fairness algorithm, and
    capacity-owner artifact concrete enough to make performance safe and
    testable?
14. Are cross-environment profiles that point to the same physical Dynamics
    organization forced into one canonical capacity budget rather than separate
    environment-labeled quotas?
15. Is the Embedded signed-manifest / central-registry trust model concrete
    enough: schema, trust anchor, key rotation, TTL, revocation, anti-rollback,
    timeout, stale-cache, and fail-closed behavior?
16. Is the implementation plan's CI gate matrix concrete enough for no-SDK
    enforcement, product JSON validation, isolation, capacity, CE smoke, soak,
    and final migration checks?

## Regression checks from prior review passes

Confirm specifically that the revised artifacts now:

- use `RuntimeHostSlotLease` and an AdmissionEpoch across Gateway and Embedded
  hosts: work is admitted only when it fits before the expiry fence, late work
  is cancelled, and the coordinator quarantines an expired/revoked slot before
  reuse so a replacement cannot overlap capacity;
- expose product invocation only as
  `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` and do
  not accept CRM schema/action identifiers, profile, URL, headers, or query
  grammar from the caller;
- validate `AggregateMaxInFlight >= MaximumRuntimeHosts >= 1`, derive rather
  than accept local concurrency, count all Gateway/Embedded deployment hosts,
  constrain queue/connection/timeout values, and reserve two ready-capable
  Gateway hosts when Gateway is a production dependency;
- share `OrganizationAdmissionKey` capacity (including its lease namespace)
  across old/new runtime generations, aliases for one organization, and
  blue/green/canary revisions, so reload or rollout cannot double aggregate
  Dynamics concurrency;
- give the durable cross-replica idempotency ledger an atomic bounded key,
  fixed retention/quota, no raw body/token/credential storage, pre-dispatch
  failure, and no automatic replay of post-dispatch `OutcomeUnknown` writes;
- make handler/proxy/header, single-flight cancellation, shared-queue drain,
  response/metadata parsing bounds, telemetry/output-cache redaction, and
  deterministic leak/disposal gates concrete and testable;
- define `OrganizationAdmissionKey` itself as the immutable shared lease
  namespace across all release revisions, require an atomic durable coordinator,
  and use a strict Windows `HostIdentity` versus `SecretReference` configuration
  union so gMSA/Kerberos hosting never requires password fields;
- define versioned, length-prefixed canonical tuple encoding for all composite
  runtime/admission/idempotency keys, safe organization base URI/API-root
  validation, duplicate-aware versioned JSON parsing, and safe rolling handoff:
  a terminating host reserves its slot through drain, then fenced-releases it;
  a transient renewal RPC failure is distinguished from lease rejection, epoch
  fencing, or TTL expiry.
- forbid all caller-provided FetchXML text/fragments/flags even when a registered
  operation uses a server-owned FetchXML template, and require every shared
  `OrganizationAdmissionKey` to have one conflict-free set of manager-owned
  queue/admission/drain settings.
- use one canonical-organization-keyed `OrganizationAdmissions` map (rather than
  duplicated profile settings), and make audit retention bounded/fail-safe:
  durable atomic pre-dispatch audit intent, ledger ordering/recovery, alert/
  high-water/hard-quota behavior, and no unbounded in-memory audit retry queue.
- define `LocalQueueCapacity` and `MaxDispatchEnvelopeBytes` as manager-owned
  organization-admission settings, reject oversize canonical envelopes before
  queueing, and bound the worst-case aggregate queued payload across all
  `MaximumRuntimeHosts`.
- use evidence-safe CE 8.2/9.1 language: direct HTTP/OData is viable, versions
  are explicit, route/capability validation is not falsely described as exact
  CE-release proof, on-prem AD/IFD authentication is a feasibility gate, and no
  unproven SDK parity or on-prem client-secret support is claimed.
- bind queue envelopes and idempotency semantics to immutable operation
  revision/template hashes, cap active-plus-draining generations, make
  HttpClient/handler ownership testable, and specify a bounded
  service-identity-only warm-up that cannot retain user/LINE/session data.
- require an Organization-call coverage matrix, OData v4 default headers and
  allowlisted `Prefer` behavior, phased migrated-product legacy bypass gates,
  signed/registry-verified product JSON bindings, a durable coordinator/ledger/
  audit backend ADR before Phase 2, per-workload fair queueing, and a capacity
  artifact with owner/measurement/HPA/IaC maxima.
- distinguish `CanonicalOrganizationCapacityKey`,
  `RuntimeHostSlotLeaseNamespace`, and queue/permit `OrganizationAdmissionKey`
  so deployment-environment labels cannot double-budget one physical Dynamics
  organization.
- define Embedded manifest/registry fail-closed semantics including timeout,
  stale cache, revocation, invalid signature, anti-rollback, monotonic version,
  and signing key rotation.
- include a CI gate matrix with commands/workflows, fail conditions, and
  artifacts for no-SDK inventory/enforcement, product JSON, runtime isolation,
  capacity/lease safety, CE 8.2/9.1 smoke, and soak/performance.

## Required output

Return a concise report with Critical, Warning, and Info findings. Every
Critical/Warning must cite the relevant file/section and recommend a specific
spec correction. If no finding applies, state why the relevant gate is sound.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.