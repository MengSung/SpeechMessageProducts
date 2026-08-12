[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p7-3-special-resource-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.3 Special-resource local implementation review

Review the current uncommitted P7.3 changes in this repository. Do not modify files.

Scope:
- `memberinfo.contact.retrieve.image`
- `memberinfo.contact.update.image`
- `newperson.contact.update.image`
- `metadata.optionset.retrieve.by.attribute`
- `stats.meeting.retrieve.by.sunday`

Important constraints:
- This task is local-only: no CE mutation/evidence, feature flag, traffic switch, Official Worker, P7.4/P7.5/P8 work, push, or PR.
- P7.2 historical Slice C remains closed; do not recommend retrying it.
- Validate strict profile/generation isolation, no CRM SDK/raw stream/cookie/entity crossing boundaries, immutable defensive copies, bounded cache/paging/input, cancellation/fault eviction, exact resource disposal, and fail-closed response contracts.
- Treat unsuccessful connector result as session state not safe for reuse, requiring fault eviction.
- Check matrix/registry/schema consistency and verify no consumer migration or ToolUtility-removal claim is made.

Review the diff and relevant P7.3 source/tests. Return ONLY Critical, Warning, Info findings with file/line and concrete justification. Do not request secrets or make external calls.

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
  PID: 44212
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-44212.log
