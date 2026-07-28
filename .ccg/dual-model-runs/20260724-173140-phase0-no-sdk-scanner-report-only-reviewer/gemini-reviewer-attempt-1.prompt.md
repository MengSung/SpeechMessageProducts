ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: phase0-no-sdk-scanner-report-only

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# CCG Review Request: Phase 0 no-SDK scanner report-only gate

Review the current repository changes for the Dynamics 365 no-SDK Phase 0 task.

## Scope

- New report-only scanner:
  - `eng/Verify-NoDynamicsSdk.ps1`
  - `eng/no-sdk-source-roots.json`
- CI visibility:
  - `.github/workflows/toolutility-tests.yml`
- Phase 0 documentation update:
  - `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-verification.md`
  - related Phase 0 task/spec artifacts if needed

## User intent / non-negotiables

- Final state will remove all Microsoft Dynamics CRM/Dataverse SDK references and remove `PowerPlatform.Dataverse.Client`.
- Phase 0 must not delete or break the current legacy SDK path yet.
- The scanner must remain report-only now; it should not fail CI until later migration phases.
- Scanner must still catch:
  - `SpeechMessageProducts.sln` including `PowerPlatform.Dataverse.Client`
  - `ToolUtility/ToolUtility.csproj` ProjectReference to `PowerPlatform.Dataverse.Client`
  - `ToolUtility.Tests/ToolUtility.Tests.csproj` `Microsoft.CrmSdk.CoreAssemblies`
  - `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj` legacy connector / Dataverse SDK package
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj` `Microsoft.PowerPlatform.Dataverse.Client` and `Microsoft.Crm.Sdk.Proxy.dll` HintPath
- The scanner should exclude build output directories `bin` / `obj` so stale generated artifacts do not dominate the report.
- It must run on Windows PowerShell 5.1.

## Local validation already run

- JSON parse:
  - `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json`
  - `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json`
  - `eng/no-sdk-source-roots.json`
- Scanner compact summary:
  - mode: `report-only`
  - findingCount: `1072`
  - binObjFindingCount: `0`
  - all five known critical paths: hit
  - rule counts:
    - LEGACYPROJECT001: 89
    - SDKASM001: 37
    - SDKNS001: 625
    - SDKPATH001: 1
    - SDKPKG001: 7
    - SDKPKG002: 2
    - SDKTYPE001: 269
    - SDKTYPE002: 41
    - SDKTYPE003: 1
- Rejected mandatory separate `SpeechMessage.Dynamics.sln` wording check: no matches outside the verification log command itself.
- `git diff --check`: passed for task/docs/eng/workflow scope.
- UTF-8 no BOM / CRLF check: passed for 18 task/scanner/workflow/design files.

## Requested review output

Return Critical / Warning / Info findings. Focus on correctness, safety, report-only semantics, Windows PowerShell compatibility, CI behavior, false-positive/noise risk, and whether the scanner meaningfully addresses the prior review warning.


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