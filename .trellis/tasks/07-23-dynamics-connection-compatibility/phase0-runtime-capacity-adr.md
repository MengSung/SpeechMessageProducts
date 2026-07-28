# Phase 0 ADR: runtime capacity, durable coordination, idempotency, and audit prerequisites

## Status

Accepted for Phase 0 planning and implementation gating on 2026-07-24. This ADR does not select a production storage vendor yet because this repository does not currently prove which durable store and deployment/IaC platform are available. Phase 1 and Phase 2 production implementation cannot dispatch to Dynamics until the decision gates below are satisfied.

## Context

- The Dynamics project group is added to the existing SpeechMessageProducts.sln solution.
- Gateway mode is the default production boundary.
- Embedded mode is a startup-time exception for approved products and must use the same no-SDK core plus the same organization-capacity coordinator.
- Connection pool means profile-generation-owned SocketsHttpHandler, HttpClient, authentication-token state, metadata cache, retry/backoff state, and bounded admission state.
- Connection pool never means pooling per-user CRM sessions, LINE IDs, passwords, browser cookies, HttpContext objects, raw tokens, or CRM SDK service objects.

## ADR-001: durable runtime host slot coordinator

Decision: introduce an IRuntimeHostSlotCoordinator abstraction before any Gateway or Embedded host can dispatch to Dynamics.

Required semantics:

- Atomic acquire, renew, fenced release, and quarantine of runtime-host slots.
- Monotonic fencing token returned on acquire/renew and attached to every local admission epoch.
- Fail closed when the coordinator is unavailable, stale, split-brained, or returns a lower fencing token.
- RuntimeHostSlotLeaseNamespace is distinct from OrganizationAdmissionKey and CanonicalOrganizationCapacityKey.
- Gateway, Embedded, blue/green, canary, and old/new profile generations targeting the same physical Organization consume the same canonical capacity budget.

Technology selection gate:

1. Identify the durable store already approved for this deployment.
2. Prove conditional create/renew/release primitives and TTL/quarantine behavior with deterministic fault tests.
3. Document clock source, maximum clock skew, lease TTL, expiry-fence margin, maximum outbound-work lifetime, and quarantine interval.
4. Provide backup/restore behavior and degraded-mode behavior.
5. Provide an IaC/deployment artifact or operations runbook proving the store is provisioned before production Gateway readiness can pass.

## ADR-002: canonical capacity ownership

Decision: one CanonicalOrganizationCapacityKey owns the physical Organization budget. No raw tuple such as deploymentEnvironment plus expectedOrganizationId may independently own a budget, queue, permit, or lease.

| Namespace | Purpose | Must not contain |
| --- | --- | --- |
| CanonicalOrganizationCapacityKey | Physical Organization capacity budget and aggregate in-flight limit. | Profile generation, raw endpoint string, user identity, LINE ID, session, token, password. |
| OrganizationAdmissionKey | Entry-resolved queue/permit namespace that maps back to exactly one canonical capacity key. | Independent capacity values or duplicate budgets. |
| RuntimeHostSlotLeaseNamespace | Durable host-slot lease namespace for Gateway/Embedded/blue-green participants. | Request payloads, credentials, user/session state, or raw CRM response data. |

Readiness gate:

- Sum of maximum Gateway replicas, Embedded replicas, blue/green overlap, and canary overlap must be less than or equal to MaximumRuntimeHosts.
- LocalQueueCapacity, MaxConnectionsPerServer, MaxInFlightPerHost, and AggregateMaxInFlight must be finite, explicitly configured, and observable.
- The system must fail startup/readiness rather than infer fallback capacity from a profile or environment name.

## ADR-003: idempotency ledger

Decision: every write capability must declare an idempotency class before it can enter the operation registry.

Required ledger behavior:

- Create-or-read by workload subject, profile alias, capabilityOperationId, operation revision, and caller idempotency key when required.
- Store a bounded request fingerprint and terminal outcome metadata, not CRM credentials, raw user sessions, JWTs, cookies, or unbounded request bodies.
- Reject the same idempotency key with a different operation revision, fingerprint, profile alias, or workload subject.
- Keep retry policy tied to the operation definition. Non-idempotent writes cannot automatically retry after an unknown CRM outcome.

Technology selection gate:

1. Prove atomic create-or-read and terminal-state update.
2. Prove expiration/retention behavior and backup/restore handling.
3. Prove crash recovery between audit intent reservation, CRM dispatch, and terminal ledger update.

## ADR-004: audit retention backend

Decision: audit intent is reserved before CRM dispatch for auditable operations. Audit completion must not depend on process memory after CRM traffic has left the host.

Required audit behavior:

- AuditIntent Reserved is durable before dispatch for write, security, and financial operations.
- Completion records include sanitized operation identity, profile alias, workload subject, capabilityOperationId, operation revision, terminal status, idempotency identity when present, and capacity/admission metadata.
- Audit records never contain passwords, tokens, raw cookies, browser session data, LINE ID as a pool key, or arbitrary CRM payload dumps.
- Retention period, redaction policy, restore behavior, and export path are deployment-owned decisions.

Technology selection gate:

1. Prove write durability and retention policy.
2. Prove redaction/sanitization tests for secrets and user/session data.
3. Prove outage behavior: either fail closed before CRM dispatch or preserve a recoverable reserved audit intent.

## ADR-005: measurement, HPA, and leak guardrails

Decision: connection-pool performance is accepted only with observable bounded resources, not with optimistic pooling assumptions.

Required metrics:

- Runtime generations by profile and state.
- Active handlers/HttpClients, active requests, queued requests, queue age, rejected requests, renew failures, fencing failures, admission epoch, and capacity-key usage.
- Socket/connection counters, DNS refresh/recycle counters, token refresh counts, metadata cache size, retry counts, timeout counts, and Retry-After backpressure events.
- Process memory, managed heap, timer count, stream disposal count where testable, and sustained soak-test deltas.

Release gate:

- Deterministic unit/fault tests prove cancellation, timeout, reload, secret rotation, lease expiry, and failed smoke-test paths dispose or quarantine all owned resources.
- Soak tests prove no sustained memory/socket/timer/queue growth under bounded load.
- HPA/autoscale configuration cannot scale replicas beyond MaximumRuntimeHosts for the same CanonicalOrganizationCapacityKey.

## Phase 0 follow-up

1. Select the first read-heavy ChurchReport or ToolUtility use case only after the Organization-call matrix has a normalized row with bounded data classification and CE 8.2/9.1 evidence needs.
2. Keep all SDK references in report-only inventory until their owning source root has migrated rows and registry agreement.
3. Do not create SpeechMessage.Dynamics.sln as the default boundary; add the new project group to SpeechMessageProducts.sln when Phase 1 begins.

