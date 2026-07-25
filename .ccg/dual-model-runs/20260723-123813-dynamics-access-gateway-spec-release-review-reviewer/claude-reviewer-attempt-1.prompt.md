ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-access-gateway-spec-release-review

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
project from referencing or using any DLL under:

D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL

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

## Regression checks from prior review passes

Confirm specifically that the revised artifacts now:

- fail closed on ReplicaSlotLease coordinator/renewal failure: immediately stop
  new CRM admission and retries, become NotReady, and only drain already leased
  outbound work; there is no emergency admission grace period;
- expose product invocation only as
  `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` and do
  not accept CRM schema/action identifiers, profile, URL, headers, or query
  grammar from the caller;
- validate `AggregateMaxInFlight >= MaximumGatewayReplicas >= 1`, derive rather
  than accept local concurrency, constrain queue/connection/timeout values, and
  require two ready-capable replicas for production;
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
- use evidence-safe CE 8.2/9.1 language: direct HTTP/OData is viable, versions
  are explicit, on-prem AD/IFD authentication is a feasibility gate, and no
  unproven SDK parity or on-prem client-secret support is claimed.

## Required output

Return a concise report with Critical, Warning, and Info findings. Every
Critical/Warning must cite the relevant file/section and recommend a specific
spec correction. If no finding applies, state why the relevant gate is sound.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.