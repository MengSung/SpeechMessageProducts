[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p74-package03-contact-image-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 Package03 contact-image read candidate analysis

Review whether the following narrowly-scoped local-only ChurchReport consumer cutover is safe.

Current facts:

- P7.3 already implements the fixed `memberinfo.contact.retrieve.image` Data8/typed ProductClient operation.
- `MemberInfoController.GetContactImage` first validates `contactId` and calls `CanViewContact(contactGuid)` before its current legacy CRM retrieve.
- The existing action reads `contact.entityimage`, optionally performs a request-local thumbnail transform, stores only the processed result in a private response cache, and falls back to the existing neutral SVG/avatar behavior on failure.
- The existing P7.3 `IPackage03SpecialResourceClient` returns a defensive-copy `ContactImageResult` with a closed PNG/JPEG media kind. It does not return CRM Entity or Stream.
- Existing Package01 flag must remain false. No CE request/mutation, feature enablement, traffic switch, P7.5, P8, push, or PR is allowed.

Proposed scope:

1. Add a separate deployment-owned `Package03SpecialResourcesEnabled`, default false.
2. With false, preserve the legacy path and prove no ProductClient/process host/HTTP handler/connector work is created.
3. With true in local fakes only, after `CanViewContact` succeeds, invoke only `RetrieveContactImageAsync` with server-owned ProfileAlias/workload and `RequestAborted`; transform the defensive-copy bytes request-locally; do not cache typed image bytes because profile/generation cache partitioning is not proven.
4. Cancellation must rethrow; ProductClient failure must use the existing neutral fallback without legacy retry; no image write or batch endpoint change.

Find Critical/Warning/Info issues. Focus on authorization ordering, profile/generation isolation, image media/content response correctness, cancellation, resource lifetime, old cache interactions, and whether this is genuinely a DTO-only read candidate.

Output a concise categorized report, citing current files/methods where possible.


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
  PID: 42740
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-42740.log
