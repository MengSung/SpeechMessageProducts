[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-authorized-fee-contact-read-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG reviewer Task: p74-authorized-fee-contact-read-final-review

## Repository

`D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree`

## Request

Review the current uncommitted P7.4 implementation for `ORG-CALL-00005`
(`fee.dedication.retrieve.by.contact`). Do not edit files.

The last implementation review completed before the following final hardening:

- `DonationFeeAuditRow` is now immutable.
- `DonationFeeAuditReadResult` copies its rows and publishes them through a
  read-only wrapper rather than exposing its backing array.
- A regression first proved that the backing array was publicly castable, then
  now proves callers cannot replace a published audit row.
- All changed C# files were normalized to UTF-8 without BOM, CRLF-only and a
  final CRLF.

## Required boundary

- Server-resolved login contact and accounting role must be authorized before
  browser GUID parsing, manager access or any dispatch.
- The browser GUID is a locator only; no target CRM `Entity` retrieval,
  DTO-to-Entity rehydration, request-time fallback or retry is allowed.
- `Package01FeeReadsEnabled=false` retains legacy compatibility; the flag stays
  false in every checked-in deployment setting.
- The true branch uses only the typed Package01 operation, fixed deployment
  profile and server workload subject, request-local immutable DTO rows, and
  checked integer totals.
- Cancellation must escape generic controller error handling; semaphore/lease
  owners must release deterministically.
- This is local-only: no CE request/mutation, flag enablement, traffic switch,
  ToolUtility removal, P7.5, P8, push or PR.

## Review output

Classify findings as Critical, Warning or Info, with exact file and line. Focus
on IDOR, A/B isolation, mutable result exposure, async cancellation, resource
cleanup, overflow, rollback-boundary semantics, documentation and scope drift.
Do not require CE evidence for this disabled local-only change.

## Evidence already run

- RED: exposed backing-array regression failed as expected.
- GREEN: its focused test passed; combined new/changed P7.4 tests passed 13.
- Before the final immutable-wrapper hardening: complete Release solution tests
  passed (ChurchReport 556 passed / 14 environment skips; Dynamics 736 passed /
  7 environment skips); Release build passed with 0 warnings / 0 errors.
- Fresh full verification will run again after this review.


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
  PID: 14012
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-14012.log
