[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p7-runtime-health-whoami-productclient

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Architecture analysis request

Review the proposed local-only P7 child `ORG-CALL-00003 runtime.health.whoami`.

Current evidence: the registry and Data8 executor already support the fixed, zero-parameter WhoAmI operation and emit a closed `OperationResponseData.ForWhoAmI` branch. ProductClient has no runtime health client. The legacy source is ToolUtility `CrmConnectionService.ValidateConnection(IOrganizationService)`; no consumer, feature gate, CE, traffic or ToolUtility migration is in scope.

Proposed change: add a stateless typed ProductClient interface/implementation plus additive DI registration. Its sole method accepts bounded deployment-owned profile alias/workload subject scalars, sends exactly `OperationIds.RuntimeHealthWhoAmI` with no parameters/idempotency key through the injected executor, validates exact operation id/CE 9.1/WhoAmI response and non-empty GUID scalars, and returns an immutable DTO. It must retain no request/profile/response/identity state and must never expose SDK, HTTP, endpoint, credential, connector or raw error data.

Assess correctness, isolation, lifecycle, failure behavior, compatibility and minimal test plan. Report Critical/Warning/Info. Do not suggest consumer wiring, CE calls, feature changes, legacy fallback, retry or scope expansion.


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
  PID: 40676
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-40676.log
