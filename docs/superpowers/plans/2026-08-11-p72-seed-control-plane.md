# P7.2 Seed Control Plane Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the disposable-descriptor prerequisite with a permanent, current-user-bound seed so a cleaned Slice C fixture can start a new safe cycle.

**Architecture:** A strict seed is the static authority for fresh preflight/provision; the existing active descriptor pair remains a per-cycle published graph output. A one-time legacy bootstrap proves seed data read-only before atomic local publication; cleanup only removes fresh outputs.

**Tech Stack:** Windows PowerShell 5.1, xUnit/.NET, Data8 CE 9.1 test harness, strict JSON file control plane.

---

### Task 1: Define seed contract with failing PowerShell tests

**Files:**
- Modify: `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`
- Modify: `docs/scripts/Invoke-Package02Data8ListManagementFreshFixture.Tests.ps1`

- [ ] **Step 1: Write failing tests**

Add synthetic cases with a valid fixed-path seed and absent active pair. Assert the preflight starts only the read-only child, publication leaves the seed byte-identical, cleanup leaves the seed byte-identical, and any extra `targetOwnerId` is rejected before Credential Manager access.

- [ ] **Step 2: Run tests to verify failure**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`

Expected: assertion failure because the runner still requires the active pair at the common gate.

- [ ] **Step 3: Implement minimum parent control-plane split**

Add strict seed input and change fresh preflight/provision/cleanup to use it. Keep execute/reconcile/repair on the active pair; retain no-retry, descriptor publication, environment restoration, and cleanup boundaries.

- [ ] **Step 4: Run tests to verify pass**

Run the command from Step 2 and the fresh-fixture suite.

### Task 2: Verify C# request / read-only integration

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/P72Data8ListManagementFreshFixtureProvisionerTests.cs`
- Modify only if required: `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixturePreflightProbe.cs`

- [ ] **Step 1: Write failing tests**

Assert a seed-derived request exposes only fixed static IDs, rejects an invalid baseline owner relationship before mutations, and preserves zero/one/duplicate weekly-report behavior.

- [ ] **Step 2: Run focused test to verify failure**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~P72Data8ListManagementFreshFixtureProvisionerTests`

Expected: failure until the seed request boundary is available.

- [ ] **Step 3: Implement minimum support**

Add only the immutable request conversion or test helper necessary for the parent/child boundary; do not add a generic CRM discovery API.

- [ ] **Step 4: Run focused tests to verify pass**

Run the command from Step 2 plus live-gate/evidence contract tests.

### Task 3: Quality gates and controlled execution

**Files:**
- Modify: task records only for results

- [ ] Run focused C#, both PowerShell suites, validator, Release build, serial solution tests, encoding/CRLF, `git diff --check`, scope check, and CCG review with 45-second maximum model wait.
- [ ] Only if all local gates pass, execute one bootstrap read-only proof, one preflight, one provision, Slice C, exact read-back/reconcile, and cleanup. Stop at the first no-go or ambiguity; then record the sanitized outcome.
