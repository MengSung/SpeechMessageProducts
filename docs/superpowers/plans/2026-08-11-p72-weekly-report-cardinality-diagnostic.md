# P7.2 Weekly-Report Cardinality Diagnostic Implementation Plan

> **For agentic workers:** Execute this plan inline in the current P7.2 task. Keep the
> read-only boundary strict; never turn this diagnostic into a mutation path.

**Goal:** Preserve ChurchReport transfer semantics for the exact descriptor-bound
target list and fixed UTC Sunday: zero active weekly reports creates an unlinked
present record, exactly one creates a linked record, and duplicate/unavailable
states fail closed with bounded, deidentified evidence.

**Architecture:** Keep the exact-list/exact-date `RetrieveMultiple` with
`TopCount=2`. Resolve the weekly report to an optional method-local ID: no row
means no lookup on a new present record, one valid row means that exact lookup,
and duplicate/paged/malformed results fail before mutation. Carry only fixed
categories through the C# result, child evidence writer, PowerShell strict
parser, and tests; never emit IDs, names, dates, counts, raw CRM data, or
exceptions.

**Tech Stack:** C#/.NET xUnit + FluentAssertions, Microsoft.Xrm.Sdk query
projection, Windows PowerShell 5.1 strict evidence contract, Trellis/CCG task
artifacts.

---

### Task 1: Persist the corrected contract

**Files:**
- Modify: `.trellis/tasks/08-07-churchreport-write-action-function-migrations/prd.md`
- Modify: `.trellis/tasks/08-07-churchreport-write-action-function-migrations/design.md`
- Modify: `.trellis/tasks/08-07-churchreport-write-action-function-migrations/implement.md`
- Modify: `.ccg/tasks/p7-2-churchreport-write-action-function-migrations/task.json`
- Modify: `.trellis/tasks/08-07-churchreport-write-action-function-migrations/p7.2-slice-c-continuation-2026-08-10.md`

- [ ] State explicitly that cardinality is scoped to `(descriptor-bound transfer target list, active state, fixed UTC Sunday)`, not the whole organization.
- [ ] Define the only allowed categories: `exactly-one-active`, `zero-active`, `duplicate-active`, and `unavailable`.
- [ ] Record that the previous live result `not-exactly-one-active` was intentionally non-diagnostic and that no CE state is changed by this clarification.
- [ ] Record degraded external analysis as `雙模型未完成` because Gemini completed and Claude was provider-quota blocked.
- [ ] Preserve the no-retry rule and state that the updated probe is a new read-only diagnostic only.

### Task 2: Add failing C# classification tests (RED)

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFreshFixtureProvisionerTests.cs`

- [ ] Add separate test scenarios for zero rows and two rows (including `MoreRecords=true`) in the test-only `FreshPreflightProbeScenario` enum and fake service.
- [ ] Add theory assertions that zero rows produce `Outcome=go`, `Reason=fresh-preconditions-proven`, `ReadOnlyProbeExecuted=true`, and `WeeklyReport=zero-active`; two rows and paging produce `Outcome=no-go`, `Reason=fresh-preconditions-not-proven`, and `WeeklyReport=duplicate-active`.
- [ ] Assert the probe keeps zero remote mutations and the fixed query still has the target-list, active-state, UTC-Sunday, and `TopCount=2` constraints.
- [ ] Run the focused test and verify it fails because the implementation still treats `zero-active` as no-go.

### Task 3: Implement minimal C# classification (GREEN)

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixturePreflightProbe.cs`
- Modify: `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFreshFixtureProvisionerTests.cs`

- [ ] Replace the boolean weekly-report helper with a method-local classification helper that returns only the four fixed categories.
- [ ] Keep `TopCount=2`, `NoLock`, exact target-list lookup, `statecode=0`, and exact UTC Sunday equality unchanged.
- [ ] Return `zero-active` only for a complete zero-row response; return `duplicate-active` for two rows or paging; return `unavailable` only for an exception or malformed projection.
- [ ] Make `go` possible for `zero-active` and `exactly-one-active`; keep `duplicate-active` and `unavailable` inside `NotProven`.
- [ ] Run the focused C# tests and verify all new and existing tests pass.

### Task 4: Preserve optional weekly-report transfer behavior (RED/GREEN)

**Files:**
- Modify: `SpeechMessage.Dynamics.Connectors.Data8/Package02Data8ListManagementOperations.cs`
- Modify: the existing focused Data8 transfer test host, or add an exact in-process test host under `SpeechMessage.Dynamics.Tests/`

- [ ] Write failing transfer tests first: a zero-row weekly-report response must permit the fixed transfer and create one exact present record without `new_group_present_weekly_report_prese`; a one-row response must require and read back the exact lookup; two rows, paging, malformed rows, an unexpected existing present record, or a wrong read-back lookup must fail before the first mutation.
- [ ] Run the focused transfer tests and verify the zero-row case fails because the current resolver rejects all non-singleton results.
- [ ] Change only the fixed resolver/projection: represent zero rows as nullable weekly-report ID, preserve the current `TopCount=2` target-list/state/date query, and reject duplicate/paged/malformed results.
- [ ] Change the fixed present-record template and final read-back to require either an absent lookup for null or the exact method-local lookup for one row. Do not add a generic query API, a report mutation, retry, cache, caller-selected report ID, or user selection.
- [ ] Run the focused transfer tests again and verify the three business branches and every no-mutation assertion are green.

### Task 5: Update strict PowerShell evidence contract (RED/GREEN)

**Files:**
- Modify: `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`
- Modify: `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`

- [ ] Add `zero-active` and `duplicate-active` to the only accepted `weeklyReport` values.
- [ ] Add parser contract cases accepting each new category and rejecting an unknown category, CRM IDs, names, raw counts, and extra properties.
- [ ] Keep `preflightOnly=false`, `operationExecuted=false`, and `featureFlagChanged=false`; update the strict no-go/go combination rules so zero-active and exactly-one-active are both valid go values.
- [ ] Run the focused PowerShell contract and verify it passes with the updated C# evidence shape.

### Task 6: Verify and execute one new read-only diagnostic

**Files:**
- No product or CRM data changes are allowed.

- [ ] Run focused C# tests, PowerShell contract tests, P7.2 validator contracts, Release build, serial Release solution tests, encoding/line-ending checks, and `git diff --check`.
- [ ] Run the approved CCG self-healing analysis/review with a maximum 45-second wait; record any quota fallback without calling it full dual-model success.
- [ ] Invoke `-FreshPreflightProbe -Json` once as the updated zero-mutation diagnostic only after all local gates pass.
- [ ] If the category is `zero-active` or `exactly-one-active`, record the materially changed precondition and proceed to a separately governed fresh-fixture cycle; if `duplicate-active` or `unavailable`, keep Slice C blocked and do not modify CRM data.
- [ ] Commit only P7.2-owned source, test, task-record, and CCG artifact changes; do not push, create a PR, or touch D–H.
