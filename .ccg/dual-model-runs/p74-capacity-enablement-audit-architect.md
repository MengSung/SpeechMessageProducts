# CCG architect Task: p74-capacity-enablement-audit

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 feature-gate capacity enablement audit

Review the repository evidence for whether the deployment-owned ChurchReport
`DynamicsAccess:Package01FeeReadsEnabled` feature gate may safely be enabled.

Required contract: before enablement, either (a) legacy ToolUtility and the
Gateway/Data8 path demonstrably share a durable organization admission/host-slot
authority, or (b) an operationally verified drain-first non-overlap runbook
proves that both paths never receive the same Organization traffic concurrently.

Current local-only scope: no CE request or mutation, no configuration change,
no feature-gate enablement, no traffic switch, no P7.5/P8 work.

Inspect these sources:

- `.trellis/tasks/08-12-churchreport-productclient-cutover/design.md`
- `.trellis/tasks/08-12-churchreport-productclient-cutover/implement.md`
- `.trellis/spec/backend/cross-user-isolation-and-performance.md`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
- `SpeechMessage.Dynamics.ControlPlane/`
- `SpeechMessage.Dynamics.Tests/SqlRuntimeHostSlotCoordinatorTests.cs`
- `SpeechMessage.Dynamics.Tests/CrossProcessSqlRuntimeHostSlotCoordinatorTests.cs`
- current ChurchReport ToolUtility-related consumer paths.

Output exactly:
1. `GO` or `NO-GO`.
2. Concrete repository evidence for every required condition.
3. Any critical or warning finding, differentiated from assumptions.
4. The smallest safe next local deliverable. Do not recommend enabling the gate
   based only on unit tests of an unbound coordinator.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.