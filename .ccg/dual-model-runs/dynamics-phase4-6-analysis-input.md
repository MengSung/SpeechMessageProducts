# Dynamics Phase 4-6 implementation analysis

ROLE: Independent senior security, distributed-systems, .NET performance, and migration analyst.

Repository: the current worktree.
Task artifacts:
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-runtime-capacity-adr.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-isolation-hardening-verification.md`

Objective: complete Phase 4, Phase 5, and Phase 6 of the accepted no-SDK Dynamics access plan. The user explicitly authorized product changes, VM configuration, WinRM probing, service/browser startup, and autonomous validation. Session leakage and memory/resource leakage are zero-tolerance release blockers; maximize safe sustained performance without weakening isolation or lifecycle guarantees.

Fresh infrastructure evidence:
- D365DC01 192.168.50.10: WinRM reachable; AD FS/AD DS/DNS/KDC/ADWS/WID running.
- D365APP01 192.168.50.20: WinRM reachable; Dynamics 9.1, IIS, default SQL Server, SQL Agent, and SSRS running.
- A separate SQL control-plane database named `SpeechMessageDynamicsControlPlane` is proposed. It must never modify MSCRM_CONFIG or any Dynamics organization database.

Known blockers to analyze against actual code:
1. Gateway and Embedded set `RequireDurableHostCoordinator=false`; Gateway uses process-local `InMemoryRuntimeHostSlotCoordinator`.
2. Gateway accepts caller-controlled JSON `WorkloadSubjectId`; ProductClient sends it. Production direction is Negotiate-authenticated Windows workload identity mapped server-side to a bounded workload subject. Unauthenticated/unmapped callers must fail before admission/CRM. Raw principal/session/cookie/token must not enter queues, runtime keys, metrics, or CRM requests.
3. `GatewayHttpClientFactory` owns an unbounded static endpoint-to-HttpClient dictionary. ChurchReport manual bootstrap uses it; standard DI uses `IHttpClientFactory`.
4. Phase 4 requires shared durable coordination, lifecycle/drain, isolation/fault/soak/performance, and honest live CE evidence. An API v8.2 route on a CE 9.1 product is not exact CE 8.2 product proof.
5. Phase 5 requires one bounded ChurchReport read flow behind a feature flag with parity and rollback proof.
6. Phase 6 requires removal of all production/test Dynamics SDK, WCF/SOAP pools/adapters/types/projects, raw CRM fallback credentials, and promotion of `eng/Verify-NoDynamicsSdk.ps1` to a mandatory full-source gate.

Analyze the repository and return:
- Critical/Warning/Info findings, with exact paths and symbols.
- A safe TDD sequence and dependency ordering for Phase 4 through Phase 6.
- A precise SQL Server lease schema/transaction/locking/fencing/quarantine design using server UTC and bounded commands/connections, including stale renew/release behavior and fail-closed outage semantics.
- Gateway authentication/authorization design that derives workload identity server-side and prevents session/tenant leakage.
- Deterministic HttpClient/handler/profile-generation ownership and disposal design; call out retained task/timer/subscription/cache hazards.
- Inventory and migration risk for all remaining SDK/WCF/SOAP consumers, with a bounded Phase 5 candidate and exact Phase 6 removal order.
- Verification gaps that would make a claim of completed Phase 4, 5, or 6 dishonest.

Do not edit files. Do not expose or inspect secrets, cookies, browser/session storage, or credentials.
