# CCG analyzer Task: dynamics-local-central-82-91-continuation

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics Local/Central Gateway 8.2/9.1 continuation analysis

Repository: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree`

Active task: `.trellis/tasks/07-23-dynamics-connection-compatibility`

## Objective

Continue implementation under the new hosting/version-routing specification. Preserve Phase 4 through Phase 6, validate Local Gateway first, retain Central Gateway as the production topology, defer Embedded, support CE 8.2 and CE 9.1 behind one product contract, configure/validate the lab DC and D365 VMs through WinRM, prove no session/resource leakage, maximize safe sustained performance, perform browser validation, and produce an execution report.

## Authoritative design

- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`

## Current evidence

- `dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore`: 97 passed, 0 failed.
- `dotnet build SpeechMessageProducts.sln --configuration Release --no-restore`: succeeds with 0 errors and 10 NU1903 warnings.
- The warnings come from checked-in Data8 `PowerPlatform.Dataverse.Client` and `ToolUtility` via explicit `System.Security.Cryptography.Xml` 10.0.9. NuGet reports five high-severity advisories. Treat this as a release blocker for any new Gateway use of Data8.
- Both `192.168.50.10` and `192.168.50.20` currently answer TCP 5985 and HTTPS 443; `Test-WSMan` reports Stack 3.0. Port 5986 is closed.
- Local process identity is `LENOVO-LEGION\Administrator` at Medium Integrity; Administrators is deny-only. Hyper-V/WinRM local administrative configuration is not yet proven available.
- Existing code has ProductClient, Gateway, WebApi, Embedded, SQL runtime-host coordinator, workload authentication boundary, admission tests, socket soak tests, and Phase 4 isolation tests.
- Current Gateway host loads one `DynamicsWebApiOptions` profile only. It does not yet provide a multi-profile CE 8.2/9.1 router.
- ProductClient can point to any endpoint, but Gateway product-option validation only checks non-empty values; it does not yet enforce absolute HTTPS, bounded API prefix, inactive-branch rejection, or local/central parity as an explicit contract.
- No isolated CE 8.2 Data8 Legacy Worker project exists yet.
- Embedded product options still contain raw organization/authentication details and are retained only as deferred legacy scaffolding.
- `Package01FeeReadsEnabled` remains false.

## Requested analysis

Produce a concrete, risk-ordered implementation recommendation for the next execution plan. Include:

1. The first TDD increment that makes Local/Central Gateway topology safer without inventing new execution-mode enum values.
2. The exact minimal files/tests to change first.
3. How to evolve from one Gateway profile to isolated `crm82` and `crm91` profile generations without sharing token/WCF/SDK state.
4. How to introduce the temporary Data8 CE 8.2 worker boundary with deterministic process/resource cleanup and no per-user/session pool.
5. Whether and how to remediate the vulnerable `System.Security.Cryptography.Xml` dependency before using Data8 under Gateway load.
6. Safe VM/WinRM steps that can proceed without exposing credentials, including what must fail closed when authenticated remote administration is unavailable.
7. Required red/green tests, soak/performance gates, browser checks, and rollback order.
8. Any spec drift or current code behavior that is a Critical/Warning blocker.

Do not modify files. Return actionable findings grouped as Critical / Warning / Recommended sequence, with exact repository paths and commands where possible.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.