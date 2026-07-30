ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: churchreport-local-gateway-session-lifecycle-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# ChurchReport Local Gateway Session Lifecycle Final Review

## Role

Review the current repository working-tree changes as a high-risk lifecycle, isolation, authentication-boundary, and Local Gateway integration change. Do not modify files. Do not print configuration values, credentials, tokens, CRM endpoints, Session identifiers, or secret references.

## Approved architecture

- Central Gateway remains the production target.
- Local Gateway is the immediate Visual Studio and ChurchReport integration path.
- Embedded remains present but deferred.
- CE 8.2 and CE 9.1 share only the product-facing Gateway contract. Their clients, authentication state, credentials, tokens, workers, and physical pools remain isolated.
- The checked-in Data8 `PowerPlatform.Dataverse.Client` project remains present. Phase 6 removal gates are not met.
- `DynamicsAccess:Package01FeeReadsEnabled` must remain `false`.
- ChurchReport must not create a second Dynamics HTTP/provider pool outside its primary DI-owned process host.

## Primary review scope

Production:

- `SpeechMessageProducts.ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs`
- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- `SpeechMessageProducts.ChurchReport/Services/DynamicsGatewayPreflightHostedService.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`

Tests and documentation:

- `ChurchReport.MemberInfo.Tests/SessionLifecycle/`
- `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
- `ChurchReport.MemberInfo.Tests/DynamicsGatewayPreflightHostedServiceTests.cs`
- `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`
- `.ccg/tasks/dynamics-connection-compatibility/review.md`

## Required invariants

1. A Session resource generation has one bounded owner, request ref-counted leases, and deterministic drain/disposal.
2. Scope creation/lookup and generation plus request-lease publication are atomic against logout/re-login identity reset.
3. A no-slot drain cannot remove a generation published after its linearization point.
4. Stale cache detection cannot publish on a slot already removed from the coordinator dictionary.
5. Cleanup failure cannot produce a false zero baseline or lose the exact resource owner. Later host disposal may retry through one serialized cleanup owner.
6. Host stop after factory creation but before cache publication still owns and cleans or retains the rejected resource.
7. Response `OnCompleted` and `RegisterForDispose` share one idempotent request lease. The singleton coordinator must not retain HttpContext, Controller, Session, user identity, credential, or token state.
8. Logout and re-login call drain before `Session.Clear`; failures are fail-closed and do not silently clear identity state first.
9. Manager and processor dispose only self-created LINE/semaphore resources, not Factory/DI-owned CRM utilities or workflows.
10. Gateway preflight is a strict no-op when the feature is disabled or mode is Embedded. Gateway mode uses the production executor pipeline, bounded timeout, sanitized errors, and no spoofed identity headers or second HttpClient.
11. The process host is terminal after disposal; concurrent callers observe the same provider cleanup and cannot recreate a generation.
12. New or substantially modified Production/Test code has deep Traditional Chinese comments covering applicable trust boundaries, ownership, races, fail-closed behavior, cancellation/timeouts, drain/disposal order, and performance/memory trade-offs.
13. Scoped files are UTF-8 without BOM, CRLF only, and end with CRLF.

## Fresh local evidence before this review

- ChurchReport full tests: 366 passed, 0 failed, 0 skipped.
- Dynamics non-live tests: 228 passed, 0 failed, 1 skipped live SQL test.
- Solution Release build: 0 warnings, 0 errors.
- Session lifecycle focused tests: 23 passed, 0 failed.
- Authentication resource-drain tests: 4 passed, 0 failed.
- Scoped `dotnet format --verify-no-changes`: passed for product and test projects.
- Strict UTF-8/no-BOM/CRLF/final-CRLF gate: passed for 21 scoped files.
- `git diff --check`: passed.
- Sensitive literal assignment scan: 0 matches.

## Review instructions

Inspect the actual code and `git diff`; do not trust the evidence list without checking implementation logic. Prioritize cross-request/cross-user isolation, orphaned resource graphs, cleanup retry races, lock ordering/deadlocks, response-callback lifetime, host-shutdown ownership, preflight fail-closed behavior, endpoint/credential disclosure, and spec/document consistency.

Output exactly:

1. `PASS` or `FAIL`.
2. Findings grouped as `Critical`, `Warning`, and `Info`, each with file and line references plus a concrete failure sequence.
3. Verification gaps that still block real Local Gateway or production enablement.
4. Explicit confirmation whether `Package01FeeReadsEnabled=false`, Embedded retention, and Data8 retention are preserved.

Do not classify missing real CE/browser/soak evidence as a code defect if the documentation accurately leaves those gates open. Any credible isolation or resource-retention defect in the implemented local slice is a release blocker.


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