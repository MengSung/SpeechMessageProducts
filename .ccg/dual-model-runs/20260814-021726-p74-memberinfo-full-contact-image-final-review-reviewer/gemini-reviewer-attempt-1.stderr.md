[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-memberinfo-full-contact-image-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00028 final local-only review

Review the current uncommitted change set scoped to `.trellis/tasks/08-14-08-14-p74-memberinfo-contact-image-full-response`.

Required properties:
- `memberinfo.contact.retrieve.image.display` is a server-owned typed operation with one fixed CE 9.1 contact Retrieve projection: `entityimage`, `new_line_picture_url`, `gendercode`.
- Exact CRM entity logical-name/ID matching is required before any image/redirect/avatar projection; mismatched identity fails closed.
- Image > exact HTTPS allowlisted LINE hosts (`profile.line-scdn.net`, `obs.line-apps.com`) > optional gender avatar. No generic URL, non-default port, URL user-info, fragment or legacy fallback.
- Data8 and ChurchReport host validation must agree.
- ChurchReport route is gated false by default and orders gate -> scope -> GUID parse -> CanViewContact -> typed client -> dispatch.
- No SDK Entity, ToolUtility, cache, retry, caller-selected profile/connector/endpoint, CE request/mutation, traffic switch, P7.5 removal or P8 work.
- Cancellation must propagate; A/B request isolation and defensive image copies must hold; no resource/session/memory leak.
- Treat only actual evidence as findings. Return Critical/Warning/Info with exact file/line evidence. Do not propose unsafe shortcuts.

Evidence already run: focused Dynamics display tests 9/9, focused ChurchReport service tests 3/3, controller contract tests 4/4.

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
  PID: 42520
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-42520.log
Gemini CCG adapter error: Gemini adapter reached the turn limit without a final response.

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, backslash, backtick, length>800
gemini exited with status 1
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-42520.log (deleted)
