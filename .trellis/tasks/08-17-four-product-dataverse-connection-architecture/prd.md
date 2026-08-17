# Design four-product Dataverse connection architecture

## Goal

Design a four-product ASP.NET Core architecture that preserves the team's familiar
`ToolUtility`-style programming model while giving each independently hosted IIS
product an explicit, high-performance, and leak-free Dataverse connection boundary.

## Requirements

- Each product must own its own `PowerPlatform.Dataverse.Client` integration and
  must not share mutable request, user, tenant, credential, token, or profile state
  with another product or session.
- Avoid creating and authenticating a completely new Dataverse connection for every
  operation when safe reuse is available.
- Connection reuse, pooling, caching, expiration, health checks, concurrency limits,
  and disposal ownership must be explicit and bounded.
- Session leakage, cross-user leakage, cross-tenant leakage, connection leakage,
  task leakage, timer leakage, and unmanaged resource leakage are release blockers.
- Determine whether the current `ToolUtility` layer manages connections, whether
  `Microsoft.PowerPlatform.Dataverse.Client` / the local
  `PowerPlatform.Dataverse.Client` project manages reuse internally, or whether the
  application currently creates and disposes clients per call.
- Compare viable lifetime models before choosing an architecture, including
  per-operation clients, one long-lived client per product/identity boundary, and a
  bounded keyed pool.
- Retain a simple application-facing API so product code does not need to understand
  authentication, pooling, retries, health checks, or disposal details.
- Use current Microsoft-supported Dataverse client behavior and official lifecycle
  guidance as the basis for the final recommendation.

## Confirmed Repository Facts

- `ToolUtility.ConnectionOperations.CrmConnectionPool` is the current explicit
  bounded pool. ChurchReport registers it as a singleton per application process.
- The local `PowerPlatform.Dataverse.Client.OnPremiseClient` creates one underlying
  WS-Trust/WCF service channel; it does not implement a connection pool.
- `OnPremiseClient` currently exposes mutable `CallerId` and `Timeout` properties
  and does not implement `IDisposable`, so its underlying channel is not closed by
  the current pool's `IDisposable` check.
- `ToolUtilityProvider` and `ToolUtilityFactory` also expose a process-wide singleton
  `ToolUtilityClass` that creates and stores a separate long-lived
  `m_Crm2011OrganizationService`; many call paths use this connection directly and
  bypass `CrmConnectionPool`.
- The current application therefore has two connection-lifetime models at once:
  manually acquired pooled clients and a directly shared singleton client.
- The production configuration uses a `ConnectionPool` section, while Startup reads
  pool sizing from `CrmConnection`; those production pool-size overrides are not
  currently consumed by the registration code.
- An IIS Application Pool isolates worker processes, not user sessions. Each worker
  process gets its own DI singletons, statics, and connection pool; web gardens or
  multiple instances multiply the total connection count.
- Plaintext CRM and payment credentials are present in tracked configuration/source
  and must be treated as compromised, rotated, and moved to a secret provider.

## Product Boundaries Discussed

- Product A: `SpeechMessageProducts.ChurchReport`, product name "好牧人 1.5";
  planned as the first cloud product and expected to connect to Dynamics 365 9.1.
- Product B: planned "好牧人 2.0".
- Product C: planned construction-company maintenance system.
- Product D: planned church-member management system.
- The current discussion is clarifying whether ASP.NET user sessions have dedicated
  Dataverse connections, when pooled connections are borrowed and returned, and
  whether a connection previously used by user A can safely be reused for user C.

## Acceptance Criteria

- [ ] The current connection construction, reuse, caching, and disposal paths are
      documented from repository evidence.
- [ ] The four product/process boundaries and their credential/tenant isolation
      boundaries are explicitly documented.
- [ ] At least three lifecycle approaches are compared with latency, throughput,
      concurrency, failure recovery, resource ownership, and leakage trade-offs.
- [ ] The recommended design defines DI lifetimes, connection keys, maximum
      concurrency, acquisition/release behavior, invalidation, shutdown, and token
      refresh ownership.
- [ ] No mutable request/session state is stored in singleton or pooled objects.
- [ ] The design includes focused isolation, disposal, load, and recovery verification
      criteria before any implementation is accepted.
- [ ] The user reviews and approves the design before implementation begins.

## Out of Scope

- Product code implementation before the architecture is approved.
- Producing a finalized technical design or implementation plan during the current
  discussion stage; first clarify architecture concepts and current behavior.
- Sharing live client instances or mutable authentication state across IIS products.
- Suppressing lifecycle or security warnings instead of resolving their causes.

## Open Questions

- Whether requests within a product always use one fixed Dataverse environment and
  application identity, or can vary by tenant, organization, or end-user credential.
- The exact four product boundaries and IIS application-pool topology.
- Required peak concurrency, latency target, and acceptable connection warm-up time.

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
