# Diagnostic Convergence Implementation Plan

> **For agentic workers:** Read-only research peers may gather evidence. Lead
> Codex performs all edits and final checks. No worker may spawn another agent.

**Goal:** Complete diagnostic convergence steps 1-6 and stop immediately before
the optimization-map step.

**Architecture:** Apply six ordered, independently audited documentation and
validation stages. Each stage updates this checklist and `progress.md` before
the next stage begins.

**Tech Stack:** Markdown, PowerShell, Git, Trellis task/channel runtime, CCG
self-healing dual-model runner, repository-native .NET test/build commands only
where a reviewed runtime validation contract permits them.

---

### Task 1: Canonicalize all diagnostic issue schemas

**Files:**
- Modify: `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`
- Modify: `docs/project-modular-diagnostics/*/issue.md`
- Modify: `.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/progress.md`

- [x] Add the canonical hash derivation to the workflow.
- [x] Normalize all seven header fields across 35 issue documents.
- [x] Reclassify X05Q from approved to runtime pending because its completed
      reviewer emitted two `NEEDS_RUNTIME_VALIDATION` verdicts.
- [x] Compute and write all canonical issue hashes.
- [x] Run a 35-file schema/hash audit; expected: 35 valid, 0 missing, 0 invalid.
- [x] Normalize every ranked item to a stable `<LeafID>-SEC/PERF/EXT-NNN` ID.
- [x] Populate every mandatory per-issue field or move unconfirmed/runtime-only
      observations out of `Ranked Confirmed Issues`.
- [x] Run a full per-issue schema audit in addition to header/hash validation.
- [x] Record corrected Step 1 evidence in `progress.md` and update Trellis.

### Task 2: Converge 17 pending CCG reviews

**Files:**
- Modify: `docs/project-modular-diagnostics/<pending-workspace>/issue.md`
- Modify: `docs/project-modular-diagnostics/<pending-workspace>/review-log.md`
- Create/modify: `.ccg/dual-model-runs/<module>-convergence-review-*`
- Modify: `.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/progress.md`

- [x] Freeze each current canonical issue hash before review.
- [x] Start the sequential self-healing queue; stop after B02 returned zero
      usable backends rather than repeating the same provider block 16 times.
- [x] Reflect every usable backend verdict without inventing missing output;
      this pass produced no usable verdict.
- [x] Record a truthful approved, runtime-pending, human-decision, or
      provider-blocked disposition per module.
- [x] Verify that no issue edit was accepted, all 17 canonical hashes remain
      unchanged, and each frozen hash/prompt/disposition is linked in
      `review-log.md`.
- [x] Record Step 2 evidence in `progress.md` and update Trellis.

### Task 3: Execute bounded runtime validation

**Files:**
- Modify: `docs/project-modular-diagnostics/B06A-list-reference-data/**`
- Modify: `docs/project-modular-diagnostics/B06B-fee-management/**`
- Modify: `docs/project-modular-diagnostics/B06C-church-hierarchy-register/**`
- Modify: `docs/project-modular-diagnostics/X05Q-churchreport-legacy-boundary-quarantine/**`
- Modify: `.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/progress.md`

- [x] Review exact commands, fixtures, credentials, expected outputs, and
      generated paths before each run.
- [x] Capture tracked and ignored filesystem baselines.
- [x] Run only validations that do not mutate production or external systems.
- [x] Record pass/fail/blocked evidence and generated-path cleanup.
- [x] Keep unavailable or unsafe validations pending with exact blockers.
- [x] Record Step 3 evidence in `progress.md` and update Trellis.

### Task 4: Recover F01A without write-scope contamination

**Files:**
- Modify: `docs/project-modular-diagnostics/F01A-solution-build-ci-governance/**`
- Create/modify: `.ccg/dual-model-runs/f01a-write-scope-recovery-*`
- Modify: `.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/progress.md`

- [x] Preserve the original invalid run in history.
- [x] Capture a complete tracked/ignored baseline.
- [x] Run a review-only CCG prompt that prohibits repository commands/writes.
- [x] Compare before/after filesystem state and record exact evidence.
- [x] Assign the truthful recovery status from runner and write-scope evidence.
- [x] Record Step 4 evidence in `progress.md` and update Trellis.

### Task 5: Resolve replacement-worker topology

**Files:**
- Modify: `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`
- Modify: affected `docs/project-modular-diagnostics/*/review-log.md`
- Modify: `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/diagnostic-run-ledger.md`
- Modify: `.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/progress.md`

- [x] Enumerate every replacement/final-retry workspace and attempt evidence.
- [x] Apply the narrow recovery exception only where prior output was empty,
      workers did not overlap, and one final accepted author is explicit.
- [x] Mark any unprovable case `INVALID_AGENT_TOPOLOGY`.
- [x] Record superseded attempts and final accepted author in review logs and
      ledger without deleting history.
- [x] Run topology audit; expected: 0 nested agents and 0 unrecorded recovery
      exceptions.
- [x] Record Step 5 evidence in `progress.md` and update Trellis.

### Task 6: Full compliance audit and diagnostic closure

**Files:**
- Modify: `docs/project-modular-diagnostics/diagnostic-run-current-state.md`
- Modify: `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/{prd.md,task.json,diagnostic-run-ledger.md}`
- Modify: `.ccg/tasks/project-modular-analysis-diagnosis-optimization/task.json`
- Modify: `.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/{prd.md,progress.md,task.json}`
- Modify: `.ccg/tasks/diagnostic-convergence-first-wave-prioritization/task.json`

- [x] Verify 35 workspaces and 245 required files.
- [x] Verify schema/hash, CCG evidence, runtime dispositions, F01A write scope,
      topology, status totals, and no product-code changes.
- [x] Update the authoritative ledger and current-state summary from audit
      output rather than hand-maintained counts.
- [x] Mark only proven acceptance criteria complete.
- [x] Keep the parent and convergence tasks active because Step 7 remains under
      the owner gate; record every provider/runtime/human blocked disposition
      rather than archiving an unfinished prioritization task.
- [x] Record Step 6 evidence and stop before Task 7.
