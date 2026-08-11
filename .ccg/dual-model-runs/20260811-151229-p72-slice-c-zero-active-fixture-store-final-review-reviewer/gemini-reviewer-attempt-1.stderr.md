[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p72-slice-c-zero-active-fixture-store-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice C zero-active fixture-store final review

Review the current repository changes for correctness, security, isolation,
resource lifecycle, performance, and test quality. Return a Traditional Chinese
Critical / Warning / Info report with a PASS or FAIL verdict. Do not modify files
and do not expose credentials, endpoints, CRM identities, GUID values, tokens,
cookies, or raw upstream exceptions in the report.

## Root cause and required behavior

The product connector, fresh preflight, and fresh provision paths already treat
an exact target-list / active-state / UTC-Sunday weekly-report query as a
zero-or-one relation:

- zero rows: valid `zero-active`; create a present record without
  `new_group_present_weekly_report_prese` and prove the lookup is absent;
- one valid row: use that exact weekly-report ID and prove exact read-back;
- duplicate rows, paging, malformed rows, missing responses, multiple present
  records, or malformed lookups: fail closed before mutation;
- never create, select, repair, disable, merge, or delete a weekly report.

`P72Data8ListManagementFixtureStore` still required exactly one weekly report.
That cross-layer drift caused ExecuteFixture to stop at
`fixture-precondition-failed` before all five operations and made reconciliation
leave a misleading `contact-owner-read` stage. Historical CE cycles are
permanently non-retryable.

## Files in the corrective scope

- `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStore.cs`
- `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStoreTests.cs`
- `ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementEvidenceTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/spec/guides/cross-layer-thinking-guide.md`

Also inspect the surrounding P7.2 task diff for contract conflicts, but keep the
findings tied to this corrective scope and the existing Slice C fail-closed
boundary.

## Review questions

1. Does the nullable weekly-report resolver preserve exact list/state/date and
   `TopCount=2`, returning null only for a complete zero-row response?
2. Does the zero-active present-record query intentionally omit only the weekly
   lookup filter while keeping exact contact/date/state and the row bound, so an
   existing wrongly linked record is visible and rejected?
3. Do read-back and cleanup use exact nullable equality, exact record ID, and
   deterministic ordered rollback without guessing or deleting unrelated data?
4. Are duplicate, paging, malformed, multiple-record, or wrong-lookup states
   rejected before mutation, without retry or weekly-report repair?
5. Can any request/user/tenant/profile/credential/session/CRM Entity state leak
   through static state, cache, logs, exceptions, evidence, or test doubles?
6. Can any service, WCF channel, lease, process, stream, buffer, timer,
   cancellation registration, background task, temporary data, or other
   resource leak or be reused after an uncertain transport state?
7. Are the tests non-tautological and sufficiently strict about query shapes,
   mutation counts, exactly-one behavior, zero-active behavior, ambiguity, wrong
   lookup, and cleanup?
8. Is setting `probeStage=transfer-read` before the composite read the correct
   bounded diagnostic behavior, without turning the stage into retry authority?
9. Are the new/modified C# regions documented with substantive Traditional
   Chinese ownership, isolation, fail-closed, cleanup, and fault-injection
   explanations?
10. Is the implementation efficient: bounded O(1) weekly/present queries, no
    unbounded scan, no shared mutable state, and no expensive new runtime per
    ordinary product request?

## Fresh local evidence

- TDD RED: 15 fixture-store tests produced exactly 3 failures at the old
  exactly-one resolver; duplicate/paging tests remained fail-closed.
- GREEN: fixture-store tests 16/16 passed after the minimal fix.
- P7.2 focused ChurchReport tests: 109 passed, 9 explicit opt-in/Windows
  privilege skips, 0 failed.
- Data8 connector/ProductClient focused tests: 44 passed, 0 failed.
- Main PowerShell evidence contracts: 258 checks passed.
- Fresh-fixture PowerShell contracts: 533 checks passed.
- P7.2 validator tests: 6 passed. The offline coverage report intentionally
  remains no-go only for four `matrix-evidence-pending` Slice C live-evidence
  rows; it is not a local-code failure.
- Release solution build: 0 warnings, 0 errors.
- Serial Release solution tests: 1,281 passed, 21 explicit live/environment
  skips, 0 failed.
- Modified C# files: strict UTF-8 without BOM, CRLF-only, final CRLF, no U+FFFD;
  `git diff --check` passed.

## Terminal rule

After review and final local gates, at most one final task-owned CE verification
cycle is permitted. If it is no-go or cleanup becomes uncertain, the live lane
must be closed as unreleasable without another cycle. Slice D-H must remain
closed until Slice C has complete evidence.


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
  PID: 48740
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-48740.log
