[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p71-list-member-count-source-no-go-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.1 ORG-CALL-00047 source-only no-go final review

Review only task-record changes for `.trellis/tasks/08-14-p71-list-member-count-typed-read/` and
`.ccg/tasks/p71-list-member-count-typed-read/`.

## Intended result

- The child records a local design no-go for direct migration of `list.members.count.by.listid`.
- It cites the legacy static `listmember` query, dynamic CRM `list.query` -> `FetchExpression` execution,
  mutable login/list workflow, and shared ToolUtility service fallback.
- It forbids static-only partial migration, caller-supplied listId as authority, raw CRM objects/queries,
  CE, feature gates, traffic, P7.5 and P8 changes.
- It lists the required future authorization/template/isolation/lifecycle conditions without implementing them.

## Required review

1. Find only actual Critical/Warning defects in the task records: source accuracy, authorization/isolation,
   scope control, accidental upgrade claims, or missing recovery condition.
2. Verify that no proposed change turns stored CRM FetchXML into Gateway executable input.
3. Do not recommend production code, CE operation, gate enablement, traffic change, P7.5 removal or P8.
4. Treat text encoding as a finding only when the raw file bytes prove invalid UTF-8/BOM/replacement/mixed line endings;
   the local byte-level check has already established UTF-8 without BOM, CRLF, final CRLF and no U+FFFD.

OUTPUT: Critical / Warning / Info findings with exact file references. State explicitly if no Critical/Warning finding exists.


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
  PID: 44532
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-44532.log
