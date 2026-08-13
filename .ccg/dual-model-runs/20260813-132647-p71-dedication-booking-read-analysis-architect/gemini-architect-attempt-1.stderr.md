[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p71-dedication-booking-read-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.1 dedication-booking typed-read architecture analysis

Review the planned local implementation for authoritative matrix ORG-CALL-00041.

Current legacy implementation: `ChurchReport.Services.DonationBookingService.FillBookingList` invokes
ToolUtility FetchXML by contact and then performs a per-row `RetrieveEntity` for `new_dedication_booking`.

Proposed scope:
- Add a bounded server-owned Data8/Package01 read capability
  `payments.dedication.retrieve.by.contact`.
- Add a dedicated closed wire response and ProductClient DTO/client; only `contactId` is a required typed input;
  `contactName` is optional compatibility data and cannot influence query scope.
- Fixed projection and bounded RetrieveMultiple; no Entity, EntityCollection, FetchXML, QueryBase, generic query,
  caller-supplied endpoint/profile/connector/credential, cache, retry, consumer migration, feature enablement, CE calls,
  fixture, P7.5, or P8.
- ChurchReport consumer remains unchanged in this child. A later P7.4 task owns server authorization, disabled gate,
  rollback and consumer cutover.

Review for: exact data-flow risks, contract naming/placement, response-boundary/isolation defects, Data8 lease lifecycle,
bounded projection/pagination, cross-profile A/B leakage, backwards compatibility, tests that must fail first, and whether
the proposed boundary might accidentally claim consumer or CE evidence.

Output: Critical / Warning / Info findings only, with exact files and concrete remediation. If no finding, say so.


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
  PID: 30284
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-30284.log
