# P7.2 Slice C Read-only Reconciliation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a bounded, explicit `-ReconcileFixture` lane that reads the existing Slice C fixture state without sending any CE mutation, always returns `no-go / baseline-unprovable / safeToRetry=false` after a complete probe, and prevents a missing pre-execution baseline from being treated as permission to retry.

**Architecture:** A small test-local reconciler converts fixed `P72Data8ListManagementFixtureStore` read snapshots into allowlisted categories without serializing GUIDs, field values, endpoints, exceptions, or credentials. The existing opt-in C# live child writes one guarded temporary evidence file; the PowerShell parent validates and projects it, restores its process environment, and deletes the owned nonce directory before emitting its one JSON line.

**Tech Stack:** .NET 10 / xUnit / FluentAssertions, existing Data8 `EmbeddedData8Runtime`, Windows PowerShell 5.1, existing `CredRead`/`CredFree` helper, UTF-8 no-BOM CRLF files.

---

### Task 1: Define and test the pure read-only reconciliation classifier

**Files:**

- Create: `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureReconciler.cs`
- Create: `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureReconcilerTests.cs`

- [x] **Step 1: Write failing classifier tests**

Create synthetic `P72MembershipSnapshot`, `P72SmallGroupFixedFieldsSnapshot`, and `P72TransferGraphSnapshot` values. Assert the normal observed shape produces exactly:

```csharp
result.Outcome.Should().Be("no-go");
result.Reason.Should().Be("baseline-unprovable");
result.ReadOnlyProbeExecuted.Should().BeTrue();
result.SafeToRetry.Should().BeFalse();
result.States.AddMembership.Should().Be("baseline-absent");
result.States.RemoveMembership.Should().Be("baseline-present");
result.States.SmallGroup.Should().Be("not-expected-baseline-unproven");
result.States.ContactOwner.Should().Be("non-target-baseline-unproven");
result.States.Transfer.Should().Be("baseline-shape-unproven");
```

Add a second test whose list membership, owner, and transfer observations differ, and assert only fixed `unexpected-*` / `*-baseline-unproven` categories are returned—never a GUID or raw CRM value.

- [x] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --filter "FullyQualifiedName~P72Data8ListManagementFixtureReconcilerTests"
```

Expected: compile failure because `P72ListManagementFixtureReconciler` does not exist.

- [x] **Step 3: Implement the minimal test-local reconciler**

Implement an internal pure type with a closed result schema. It accepts only already-read snapshots and the in-memory WhoAmI target owner. It must not take an `IOrganizationService`, endpoint, credential, raw `Entity`, `QueryExpression`, or mutable cache. The only result values are:

```text
ownerBinding: matches-service-identity | unavailable
addMembership: baseline-absent | unexpected-present | unavailable
removeMembership: baseline-present | unexpected-absent | unavailable
smallGroup: not-expected-baseline-unproven | expected-baseline-unproven | unavailable
contactOwner: non-target-baseline-unproven | target-baseline-unproven | unavailable
transfer: baseline-shape-unproven | unexpected-shape-unproven | unavailable
```

Its complete-read result must always be `no-go / baseline-unprovable / safeToRetry=false`; it must not expose a method that authorizes retry or cleanup.

- [x] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2. Expected: all classifier tests pass with no CE connection.

### Task 2: Add a separate opt-in C# read-only child lane

**Files:**

- Modify: `ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementEvidenceTests.cs`
- Test: `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureReconcilerTests.cs`

- [x] **Step 1: Write a failing test for the explicit reconciliation evidence contract**

Extend the existing offline PowerShell contract test first (Task 3) to require a distinct `P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH`, a distinct fixed file name, `-ReconcileFixture`, and no legacy TRX marker. It must reject callers that pass both `-ExecuteFixture` and `-ReconcileFixture` before credential access.

- [x] **Step 2: Implement the C# reconciliation method**

Add a second `[P72Data8SliceCReconcileFact]` method named `Reconcile_package02_data8_list_management_emits_sanitized_reconciliation`. Its distinct attribute rejects the execute-mode environment flag, so a read-only command cannot accidentally discover the existing mutation lane. It must:

1. Read the existing fixed descriptor and development configuration exactly as the execution lane does.
2. Read the existing Windows Generic Credential only from the short-lived child environment supplied by the parent.
3. Build `EmbeddedData8Runtime`, validate WhoAmI with `ResolveFixtureTargetOwnerIdAsync`, then build exactly one `P72Data8ListManagementFixtureStore`.
4. Call only `ReadMembership`, `ReadSmallGroupFields`, `ResolveSmallGroupExpected`, `ReadOwnerId`, and `ReadTransferGraph`; it must not call any `Execute*`, `Restore*`, `Update`, `Delete`, or `Assign` method.
5. Dispose store, runtime, and logger in the existing reverse order before writing evidence.
6. Write one exact temporary evidence object with `operationExecuted=false` and `featureFlagChanged=false`; the parent, not the child, owns and injects `safeToRetry=false` after strict parsing.

Use a private shared evidence-writer implementation called only with hard-coded environment-variable/file-name pairs. It must validate direct OS-temp nonce parent, fixed file name, non-reparse parent, `FileMode.CreateNew`, UTF-8 no-BOM CRLF, and 32 KiB maximum.

- [x] **Step 3: Verify no accidental live execution path is introduced**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --filter "FullyQualifiedName~P72Data8ListManagementFixtureReconcilerTests|FullyQualifiedName~P72Data8ListManagementFixtureOwnerResolverTests"
```

Expected: offline tests pass; the live methods remain skipped without their explicit process-only variables.

### Task 3: Add the mutually exclusive PowerShell handoff mode

**Files:**

- Modify: `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`
- Modify: `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`

- [x] **Step 1: Write failing PowerShell contract tests**

Add fixture JSON for a valid reconciliation result and import a new strict parser. Assert:

```powershell
$parsed.outcome -eq 'no-go'
$parsed.reason -eq 'baseline-unprovable'
$parsed.readOnlyProbeExecuted
-not $parsed.operationExecuted
-not $parsed.featureFlagChanged
-not $parsed.safeToRetry
```

Also assert an extra property, an unknown state, a missing file, and both live switches fail closed. The test must prove a `-ReconcileFixture` run with a missing Generic Credential returns `credential-unavailable` without starting `dotnet` or changing a flag.

- [x] **Step 2: Run the PowerShell test and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Invoke-Package02Data8ListManagementEvidence.Tests.ps1
```

Expected: failure because reconciliation parser/mode does not exist.

- [x] **Step 3: Implement the closed parent contract**

Add `-ReconcileFixture`; reject it together with `-ExecuteFixture` before any Credential Manager call. Snapshot and restore the two reconciliation environment variables in the existing `finally`. Reuse the owned temporary directory and `Complete-HandoffResult`, but provide a strict reconciliation parser and a reconciliation-specific cleanup-failure projection. The parent must launch only the new C# method, wait at most 180 seconds, never retry, and return `safeToRetry=false` on every result.

- [x] **Step 4: Run the PowerShell test and verify GREEN**

Run the command from Step 2. Expected: all static/offline checks pass; no CE operation, browser action, feature-flag change, or password output occurs.

### Task 4: Update the operator handoff and verify the safe live read

**Files:**

- Modify: `.trellis/tasks/08-07-churchreport-write-action-function-migrations/operator-handoff-p7.2-slice-c.md`
- Modify: `docs/superpowers/plans/2026-08-09-p7-2-slice-c-read-only-reconciliation.md` (tick completed plan steps only after evidence)

- [x] **Step 1: Document the one-command read-only action**

Add a copy/paste command using `-ReconcileFixture`. State in Traditional Chinese that it performs CE reads through the existing local credential but sends no mutation, emits no GUID/password/endpoint, returns `safeToRetry=false`, and does not authorize a retry.

- [ ] **Step 2: Run verification**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Invoke-Package02Data8ListManagementEvidence.Tests.ps1
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --filter "FullyQualifiedName~P72Data8ListManagementFixtureReconcilerTests|FullyQualifiedName~P72Data8ListManagementFixtureOwnerResolverTests"
$root = (Get-Location).Path
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Invoke-Package02Data8ListManagementEvidence.ps1 -RepositoryPath $root -ReconcileFixture -Json
```

Expected final live command: one sanitized `no-go / baseline-unprovable` JSON line with `readOnlyProbeExecuted=true`, `operationExecuted=false`, and `safeToRetry=false`. It is not permission to invoke `-ExecuteFixture`.

- [ ] **Step 3: Run byte-level and scope checks**

Run `git diff --check` and a byte-level UTF-8 no-BOM / CRLF-only / final-CRLF scan over every modified P7.2 file. Do not run Gemini, Claude, `task.py start`, `-ExecuteFixture`, a feature flag change, commit, archive, push, P6, or P8.

---

## Plan self-review

- Spec coverage: the plan preserves the P7.2 capability-scoped write contract by introducing a distinct read-only lane, never infers a retry baseline, and keeps cleanup/resource ownership deterministic.
- Placeholder scan: every task lists its exact files, tests, command, and fixed outcome categories.
- Type consistency: the C# classifier produces only the fixed categories accepted by the PowerShell parser; both use `baseline-unprovable`, `operationExecuted=false`, and `safeToRetry=false`.

## Execution decision

The user has already authorized continuing this active task. Execute this plan inline; do not create a new Goal, commit, archive, or run a CE write.
