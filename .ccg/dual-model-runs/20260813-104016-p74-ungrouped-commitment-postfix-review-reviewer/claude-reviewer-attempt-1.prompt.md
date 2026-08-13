ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p74-ungrouped-commitment-postfix-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00024 final review after local corrective pass

Review only the current P7.4 child changes for `ORG-CALL-00024` in this repository. This is a disabled-by-default, local-only ChurchReport consumer boundary for non-empty ungrouped commitment aggregate counts. It must not claim or perform CE write, traffic enablement, ToolUtility removal, P7.5 completion, P8 deployment, or a retry of the historically closed P7.2 Slice C cycle.

Verify from the current diff:
- both checked-in gates stay false and the base/sub-gate remains fail closed;
- the specialized Package02 factory validates a deployment-owned non-empty ProfileAlias before host/provider/pool resolution;
- enabled typed count has fixed workload/profile, request cancellation propagation, no caller-controlled routing, no retry, and no legacy aggregate fallback;
- enabling the typed non-empty count bypasses the legacy three-minute grouped-contact cache for the same page request, while introducing no cache/session/resource leak;
- typed result is defensive, request-local, malformed DTOs fail closed, and A/B isolation is tested;
- public action and modified test have adequate Traditional-Chinese boundary documentation;
- scope is limited to this child and makes no CE/cutover/P7.5/P8 claim.

Return only Critical / Warning / Info findings with exact paths and lines. Do not modify code.


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