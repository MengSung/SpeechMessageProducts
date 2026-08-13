[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-static-list-membership-action-consumer-boundary-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 static-list membership action consumer boundary final review

Review only the current no-go task artifacts and parent-record updates. This child deliberately makes no runtime,
configuration, feature gate, CE, fixture, ToolUtility, or product-data change. It records that ChurchReport's
`ListManagementDataManager` interleaves two membership actions with legacy contact/list/attendance mutations, so
partial ProductClient wiring would introduce a split-brain composite without common authorization, read-back,
reconciliation, cleanup, or rollback ownership.

Verify that the artifacts accurately preserve `temporary-legacy`, do not claim CE/cutover/P7.5/P8 success, have
clear recovery conditions, and do not accidentally authorize a partial migration. Return Critical/Warning/Info and
PASS/FAIL. Do not request or perform external operations.


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
  PID: 22780
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-22780.log
