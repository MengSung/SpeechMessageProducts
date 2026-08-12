# P7.4 Authorized Fee Contact Read Implementation Plan

## Preconditions

- [x] Read AGENTS.md, parent P7.4 artifacts, the authoritative matrix, and the mandatory cross-user isolation contract.
- [x] Keep all deployment feature flags false and run no CE/traffic/P7.5/P8 operation.
- [x] Record the Gemini-only architecture result and the Claude 45-second timeout as a degraded analysis state.

## TDD order

1. [x] Add `DonationFeeAuditAccessResolverTests` with a real `Entity` snapshot: valid accounting snapshot succeeds;
       null/empty/no-role snapshots fail. Run the focused test and observe compile failure before adding the resolver.
2. [x] Add `DonationFeeAuditReadResult` and the pure resolver with full Traditional Chinese lifecycle/isolation documentation.
       Re-run the resolver test green.
3. [x] Add a failing `DonationFeeQueryServiceAsyncTests` case for the new typed audit query. It must prove the client
       receives the contact operation, null contact name and caller cancellation; returned rows/total are new request-local
       values. Add a second interleaved A/B case and an overflow/cancellation case. Run RED.
4. [x] Add the minimal typed audit query in `DonationFeeQueryService`, service/manager forwarding only, and no target
       `Entity` retrieval. Run the focused service tests green.
5. [x] Add a failing source contract test for `DedicationAuditController`: authorization precedes the parsed target;
       true-gate dispatches the typed audit method; false-gate retains legacy call; no raw exception detail, no target
       `RetrieveEntity`, and cancellation does not enter catch-all. Run RED.
6. [x] Update the controller with the resolver, fixed bounded error outcome and true/false gate split. Run the controller
       contract test green.

## Verification

7. [x] Run targeted resolver, fee-query and controller contract tests.
8. [x] Run `ChurchReport.MemberInfo.Tests` Release suite and the solution Release build; run full solution tests at the
       child boundary.
9. [x] Byte-check every modified/new `.cs` for UTF-8 no BOM, CRLF-only and final CRLF; run `git diff --check` and a
       scope/forbidden-pattern scan.
10. [x] Run CCG dual-model implementation review through `Start-CcgDualModelRun.ps1`, wait at most 45 seconds, and
        record any degraded result accurately.
11. [x] Update parent matrix/task records without promoting local work to CE/cutover evidence; scope-only commit and
        archive this child.
