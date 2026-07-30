[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: local-gateway-development-config-adfs-probe-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Local Gateway Development Configuration And Retired ADFS Probe Final Review

## Role

Review the current working-tree changes for this bounded Phase 4 slice. Do not modify files. Do not print or quote credentials, tokens, passwords, Session identifiers, client identifiers, callback values, private VM addresses, full CRM/ADFS endpoints, or secret-reference values.

## Approved architecture and scope

- Central Gateway remains the production target.
- Local Gateway is the immediate Visual Studio and ChurchReport Development path.
- Embedded remains present but deferred; Data8 and `PowerPlatform.Dataverse.Client` remain present because Phase 6 gates are open.
- `DynamicsAccess:Package01FeeReadsEnabled` must remain false. This slice aligns hosting and configuration only; it must not enable consumer traffic or preflight traffic.
- Development Gateway must use the explicitly provisioned, same-Windows-user LocalDB control-plane database for durable host-slot/epoch/fencing coordination. It must not connect to a Dynamics native SQL database or auto-create schema.
- Development CRM transport target must remain the checked-in non-routable fail-closed target. No silent fallback to production, Embedded, Data8, or another profile is allowed.
- The historical `Invoke-AdfsTokenProbe.ps1` must be a fail-closed retired entrypoint. It must not accept credentials, read product appsettings, call identity/CRM endpoints, write result artifacts, or recommend enabling Package 1.
- The supported interactive local path is the existing ChurchReport Public Client authorization-code diagnostics route; the retired script must not duplicate a second token/cache/resource owner.

## Primary changed files

- `SpeechMessage.Dynamics.Gateway/appsettings.Development.json`
- `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
- `docs/scripts/Invoke-AdfsTokenProbe.ps1`
- `SpeechMessage.Dynamics.Tests/SqlRuntimeHostSlotCoordinatorTests.cs`
- `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`
- `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`

Inspect the actual `git diff` and relevant startup/binding code. Do not assume the evidence below proves correctness.

## Required invariants

1. ASP.NET Core Development precedence selects the fixed LocalDB instance, dedicated control-plane database, integrated authentication, bounded connection pool, and bounded connect timeout.
2. The connection string contains no SQL username/password and is owned only by Gateway Development configuration.
3. The Development CRM Web API target remains non-routable and fail closed.
4. ChurchReport Development selects Gateway mode, the exact Local Gateway profile alias/version, HTTPS loopback endpoint, and `/v1` prefix while Package 1 remains disabled.
5. Feature disabled means ChurchReport does not create ProductClient, HTTP handler/pool, token cache, timer, or Dynamics operation traffic.
6. The retired PowerShell script has no credential/token/result parameters or code paths, creates no file/network/background resources, and immediately fails with fixed guidance.
7. New or modified tests/config/script contain substantive Traditional Chinese comments explaining ownership, trust boundaries, fail-closed behavior, resource lifetime, and why Package 1 remains off.
8. Scoped files are UTF-8 without BOM, CRLF only, and end with CRLF.

## Fresh evidence

- TDD RED was observed before both Development JSON overrides and before script retirement.
- Configuration contract tests now pass for Gateway LocalDB/fail-closed target and ChurchReport Local Gateway/flag-off precedence.
- Live LocalDB durable coordinator contract was explicitly run against the provisioned database and passed.
- Real Development Local Gateway started with SQL durable readiness. `/health` and `/ready` returned 200; anonymous `/v1` returned 401; current Windows workload catalog returned 200; wrong alias and unauthorized operation returned 403; the only allowed operation against the non-routable Development target returned a controlled 400 with no fallback.
- ChurchReport and Local Gateway ran concurrently. ChurchReport root returned 200; in-app browser loaded the login page to `readyState=complete` with zero JavaScript errors. Two existing DevExtreme deprecation warnings were observed and are not part of this slice.
- Both test hosts were stopped and both listeners were released.
- Remote AD FS read-only marker verification found one Public Client, one callback, and all shared-IFD/Gateway/fail-closed description markers without printing their values.
- Dynamics tests: 230 passed, 0 failed, 1 environment test skipped in the ordinary run; the skipped LocalDB contract was run separately and passed.
- ChurchReport tests: 367 passed, 0 failed.
- Solution Release build: 0 warnings, 0 errors.
- Changed-file format, strict UTF-8/no-BOM/CRLF/final-CRLF, `git diff --check`, and added-line sensitive literal scans passed.

## Output

Return exactly:

1. `PASS` or `FAIL`.
2. Findings grouped as `Critical`, `Warning`, and `Info`, each with file/line references and a concrete failure sequence.
3. Any remaining verification gaps that block real CE 8.2/9.1 or Phase 5 enablement, without misclassifying already documented open gates as defects in this slice.
4. Explicit confirmation that Package 1 remains false and Embedded/Data8 remain retained.

Any credible credential disclosure, cross-session/cross-user state retention, socket/task/handler/connection leak, unbounded resource, silent transport fallback, or production-target exposure is a release blocker.


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
  PID: 30428
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-30428.log
