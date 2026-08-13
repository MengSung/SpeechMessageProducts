# P7.4 authorized fee contact read implementation review

Review only the current uncommitted P7.4 implementation for `ORG-CALL-00005`
(`fee.dedication.retrieve.by.contact`). Do not edit files.

## Goal

Move `DedicationAuditController.GetFeesByContactId` behind a server-authorized,
request-local, DTO-only Package01 read path while keeping the deployment-owned
`Package01FeeReadsEnabled` flag false. This is local implementation only: no CE
request/mutation, feature enablement, traffic switch, ToolUtility removal, P7.5,
P8, push, or PR.

## Changed boundary

- `DonationFeeAuditAccessResolver` accepts only server-resolved login `Entity`;
  valid non-empty login contact plus existing accounting role is required.
- Controller calls `EnsureCorrectUserData`, validates that login snapshot before
  parsing browser GUID or manager access, then treats GUID only as a locator.
- false gate keeps existing legacy manager route; true gate uses
  `RetrieveFeeAuditByContactAsync` and a new request-local
  `DonationFeeAuditReadResult`.
- typed route sends fixed deployment profile / server workload subject, null
  contact name, no target CRM Entity retrieve or DTO-to-Entity rehydration.
- typed result must not mutate `DonationPaymentFormModel`, use fallback/retry,
  or retain request/session/entity/profile/DTO in static/cache/background state.
- cancellation must escape generic controller handling and the manager semaphore
  must be released exactly once in finally.
- raw exception details must not be returned to browser JSON.

## Review focus

Check current git diff for Critical / Warning / Info findings with exact paths
and line numbers in authorization order, IDOR, A/B isolation, mutable state,
async cancellation, resource cleanup, arithmetic fail-closed behavior,
feature-gate rollback boundary, Traditional Chinese documentation, and accidental
scope expansion. Do not demand CE evidence for this local-only change.

## Evidence already run

- targeted P7.4 tests: 13 pass.
- full ChurchReport.MemberInfo.Tests Release: 556 pass / 14 skipped.
- solution Release build: 0 warnings / 0 errors.
- solution test run had one unrelated Kestrel HTTP/1.1 transport premature-response
  failure in `GatewayRequestBodyBoundaryTests`; rerunning that exact test passed.
- changed C# files verified UTF-8 no BOM, CRLF-only, final CRLF; `git diff --check` passes.
