# Diagnostic convergence and first-wave optimization prioritization

## Goal

Converge the 35 module diagnostic results into trustworthy readiness states and
produce an evidence-based first-wave optimization proposal without changing
product code.

## Confirmed Facts

- The work is isolated to the `1.0.0.1.EvenVersion` worktree and branch.
- All 35 fixed diagnostic workspaces contain the required seven-file package.
- The current ledger contains 14 `APPROVED_DEGRADED`, 17
  `DEGRADED_REVIEW_PENDING`, 3 `RUNTIME_VALIDATION_PENDING`, and 1
  `INVALID_WRITE_SCOPE` result.
- Pending and invalid states are complete diagnostic deliverables, but they are
  not equivalent to optimization approval.
- The authoritative ownership and dependency map is
  `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`.
- The authoritative run state and ledger are
  `docs/project-modular-diagnostics/diagnostic-run-current-state.md` and
  `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/diagnostic-run-ledger.md`.
- Product-code optimization has not been authorized.

## Requirements

- Keep the 35 existing isolation-zone ownership boundaries unchanged.
- Define an explicit optimization-readiness gate for every diagnostic status.
- Apply strict first-wave admission: only `APPROVED`, `APPROVED_DEGRADED`,
  `NO_ACTION_REQUIRED`, or a runtime-pending module that subsequently records a
  passing validation and approved terminal status may be considered ready.
- Treat diagnostic status as necessary but not sufficient. Admission also
  requires the module-map optimization gate to be ready, all required provider
  and consumer baselines to be executable, and the module not to be a
  quarantine leaf.
- Exclude `DEGRADED_REVIEW_PENDING`, `RUNTIME_VALIDATION_PENDING`,
  `INVALID_WRITE_SCOPE`, `INVALID_AGENT_TOPOLOGY`, and
  `HUMAN_DECISION_REQUIRED` from optimization implementation until resolved.
- Exclude F03Q, X02Q, and X05Q from whole-module optimization. They may only
  receive responsibility-proof, split, transfer, or approved retirement work.
- Resolve or assign a documented follow-up for F01A `INVALID_WRITE_SCOPE`.
- Define the controlled CCG retry path for the 17
  `DEGRADED_REVIEW_PENDING` workspaces.
- Define runtime validation scope, write boundaries, expected evidence, and
  pass/fail criteria for B06A, B06B, and B06C before running validation.
- Build a cross-module issue inventory that preserves the source module, issue
  identifier, evidence confidence, severity, dependencies, expected value,
  implementation risk, and validation cost.
- Rank modules using explicit criteria rather than document order or issue
  count alone.
- Produce a proposed first optimization wave with dependency order, module
  scope, acceptance gates, and rollback expectations.
- Require explicit user approval before activating any optimization task or
  modifying product source, configuration, tests, deployment files, or data.
- Keep the RichMenu Word manual task separate from this architecture-governance
  task.

## Constraints

- Planning is documentation-only.
- Existing diagnostic findings may be summarized but not silently rewritten.
- CCG calls must use the project self-healing entrypoint and truthfully record
  full, degraded, quota-blocked, or unusable outcomes.
- A module with unresolved Critical evidence cannot be treated as ready merely
  because another module depends on it.
- Any runtime command that may write build, cache, generated, lock, or test
  output requires an approved write boundary and cleanup/rollback plan.

## Acceptance Criteria

- [x] Every one of the 35 workspaces has one recorded convergence disposition.
- [x] F01A has a documented recovery decision and verifiable write-scope result.
- [x] Each pending CCG review has either usable review evidence or an explicit
      blocked disposition that excludes it from optimization.
- [x] B06A, B06B, and B06C have reviewed runtime validation contracts and
      recorded outcomes before becoming optimization-ready.
- [ ] A normalized issue inventory can trace every ranked item to its source
      `issue.md` and evidence files.
- [ ] The prioritization method documents weights, tie-breakers, dependency
      ordering, and exclusion rules.
- [ ] Eligibility is computed as diagnostic status AND module gate readiness
      AND non-quarantine ownership, never from diagnostic status alone.
- [ ] The first-wave proposal contains independently approvable module tasks and
      does not authorize implementation by itself.
- [x] A final audit confirms no product-code changes were made by this planning
      and convergence work unless separately approved.

## Out of Scope

- Implementing any diagnosed optimization.
- Changing the 35-module ownership map.
- Combining unrelated RichMenu documentation work with this task.
- Declaring quota-blocked CCG review to be full dual-model approval.

## Decisions

- 2026-07-13: The user selected strict admission. Pending, invalid, and
  human-decision states are excluded from optimization until they reach an
  eligible terminal state. The previously approved `APPROVED_DEGRADED` policy
  remains valid.

## External Analysis

- CCG run
  `20260713-095124-diagnostic-convergence-first-wave-prioritization-analysis-analyzer`
  completed with Claude output and Gemini provider-balance HTTP 403.
- Result: degraded single-model fallback, not full dual-model analysis.
- Claude verdict: `ACCEPT_WITH_CHANGES`.
- Locally validated retained change: combine diagnostic terminal status with
  the module map's optimization gate and quarantine rules.
- Other retained guidance: dependency gates override scores; issue count is at
  most a tie-breaker; blocked modules with Critical findings remain urgent but
  are not optimization-ready.

## Notes

- This is a complex planning task. It requires `design.md` and `implement.md`
  plus user review before `task.py start`.
