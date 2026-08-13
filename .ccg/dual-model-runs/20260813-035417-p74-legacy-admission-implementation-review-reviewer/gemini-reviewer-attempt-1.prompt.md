ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-legacy-admission-implementation-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 legacy Gateway admission implementation review

Review only the current uncommitted P7.4 change set. Do not edit files.

## Goal
A host-owned local legacy drain controller, optional Package01 fee ingress accounting,
host shutdown drain, no-secret validator and drain-first/non-overlap runbook.

## Security invariants
- It is operation-level metering only, never Organization-level capacity proof.
- Package01FeeReadsEnabled remains false; no CE writes, traffic switch, P7.5 or P8.
- Synchronous ToolUtility CRM work cannot be cancelled/fenced, unknown legacy coverage and
  cross-host non-durable topology are no-go.
- No controller retention of request/session/CRM entity/profile/endpoint/credentials/responses.
- Lease double-dispose, timeout, cancellation and shutdown must fail closed with bounded cleanup.

## Verify
Inspect git diff and relevant tests. Report Critical/Warning/Info. Flag any security issue,
false claim of deployment evidence, resource/session leakage, lifecycle race, test gap,
legacy behavior regression, or documentation/encoding issue. Do not request external operations.


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