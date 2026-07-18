# Optimization blueprint and issue workflow

## Goal

Inventory all confirmed diagnostic issues, define a scored optimization blueprint, and design the single-subagent-per-run workflow before any product-code optimization.

## Confirmed Facts

- The diagnostic convergence program has completed Steps 1-6 for all 35 fixed
  isolation zones. Its strict audit covers 35 workspaces, 245 required files,
  canonical issue hashes, topology, current-state, and task governance.
- Current diagnostic status totals are `APPROVED_DEGRADED=13`,
  `DEGRADED_REVIEW_PENDING=17`, `RUNTIME_VALIDATION_PENDING=4`, and
  `HUMAN_DECISION_REQUIRED=1`.
- A diagnostic status is necessary but not sufficient for optimization
  admission. Module-map gates, dependency baselines, quarantine rules, and
  any issue-level measurement prerequisites remain binding.
- F03Q, X02Q, and X05Q are quarantine modules. They can receive only
  responsibility proof, split, transfer, or approved retirement work.
- Product source, configuration, tests, deployment files, and data remain
  read-only until the owner approves a separately scoped optimization task.
- The user wants a reusable workflow in which one assigned subagent executes
  one approved optimization work unit. Nested delegation is prohibited; CCG
  review remains external to that subagent.
- The user selected a module-local dependency bundle as the default execution
  unit. A self-contained canonical issue may form a one-issue bundle; a
  cross-module change is a separate contract bundle only when an atomic
  provider/consumer change is required.
- Every wave is stored in its primary owner's workspace. When a wave needs a
  consumer change, `plans.md` lists that consumer's exact allowed paths rather
  than creating an ownerless cross-module wave directory.
- Every declared module wave lives at
  `docs/project-modular-diagnostics/<workspace>/wave_<n>/` and contains exactly
  `plans.md`, `measurements.md`, and `goals.md`.
- `plans.md` identifies the exact canonical issue-ID subset from the workspace
  `issue.md` and describes the proposed repair boundary. `measurements.md`
  defines how the selected subset is observed. `goals.md` defines the success
  condition for the selected subset. These three documents replace per-issue
  planning files; each selected issue is represented as a concise row or
  section in the shared wave files.
- The eventual execution prompt will tell one subagent to use the wave's
  `goals.md` as its outcome contract. The subagent may modify only the wave's
  approved product scope and must not spawn nested agents.
- A global optimization wave selects an issue subset for one or more module
  workspaces. The wave runs those workspaces sequentially, never concurrently.
- Each selected workspace is processed by exactly two sequential subagents:
  1. a planning subagent creates only the three wave files and revises them
     through Claude review;
  2. after the plan files pass review, the main session assigns a different
     zero-trust repair subagent that uses those files as its authority, performs
     the repair, and runs its own Claude-review/fix loop.
- The repair subagent may read `goals.md`, `measurements.md`, `plans.md`, and
  the necessary owning `issue.md` as planning evidence. It may additionally
  read source/test files only within the approved `plans.md` scope, because
  repairing without inspecting those files is not possible. It must not use
  unrelated diagnostic files to expand its task.
- No commit may be made until the repair subagent's local verification succeeds
  and its Claude review has passed. The commit message body is written in
  Traditional Chinese and identifies the wave, issue-ID subset, measurement
  result, validation result, and rollback boundary.
- The owner selected Claude as the only external reviewer for this workflow.
  Gemini must not be invoked, probed, or treated as required. If Claude is
  unavailable or produces no usable output, the main session invokes one
  independent Codex review agent as the fallback. That fallback is read-only,
  zero-trust, non-nested, and returns the same finding format; its approved
  result is valid for advancing the current review gate.
- The existing module map already orders work as Wave 0 trusted baselines,
  Wave 1 shared foundations, Wave 2 identity/core data, Wave 3 integrated
  business flows, and Wave 4 user experience/end-to-end validation. Priority
  must be reconciled with that dependency order rather than replacing it.
- The full register has 322 records: 163 canonical confirmed issues, 13 runtime
  observations, and 146 non-actionable observations. The current authoritative
  assignment is W1=5 archived issues, W2=11 selected issues, and 147 canonical
  issues with no assigned wave. No W3, W4, or W5 membership currently exists.
- W1 contains only the five F01B governance issues and is archived after commit
  `181c9298`. W2 contains eleven issues across seven product workspaces. X04A
  is committed as `ab9993e8`; B01 is blocked by three external evidence gates,
  and B02 through X05Q remain queued behind B01.

## Requirements

- Build a normalized inventory that can trace every known diagnostic issue to
  its source module, canonical issue ID, current disposition, evidence,
  dependency/gate state, score inputs, expected value, implementation risk,
  validation cost, and rollback boundary.
- Separate implementation candidates from provider-blocked, runtime-pending,
  human-decision, rejected, merged, and quarantine-only observations. No
  blocked item may be presented as ready.
- Define an explicit, repeatable ranking method with weights, tie-breakers,
  dependency ordering, and exclusion rules.
- Produce a proposed optimization blueprint that groups work into independently
  approvable units without changing the established 35-module ownership map.
- Define the per-unit optimization workflow: preconditions, exclusive file
  ownership, baseline measurement, implementation, verification, rollback,
  CCG review, and truthful blocked/degraded outcomes.
- Require the planning subagent to modify only the selected workspace's
  `wave_<n>/plans.md`, `measurements.md`, and `goals.md`; its CCG artifacts are
  stored under `.ccg/dual-model-runs/**`.
- Require the repair subagent to be a separate zero-trust agent with a file
  allowlist derived from `plans.md`; it may not delegate or alter wave goals to
  make an unsuccessful repair appear successful.
- Run selected workspaces sequentially within a global wave. Advance to the
  next workspace only after the current one reaches a truthful committed or
  blocked terminal result.
- Provide a self-healing Claude-only review entrypoint or a backend-selection
  parameter before the first wave. The current dual-model entrypoint always
  invokes Gemini and therefore cannot be used by this workflow unchanged.
- Define the Codex fallback review invocation as a main-session action, never
  as a child of either planning or repair subagent. Gemini is excluded from all
  normal and fallback review paths.
- Define a required measurement method and success criterion for every issue
  selected by a wave before any source edit is authorized, using that wave's
  shared `measurements.md` and `goals.md`.
- Build optimization batches/waves from priority plus readiness, dependencies,
  blast radius, measurement feasibility, and rollback safety. A high score by
  itself must not admit a blocked or unmeasurable issue.
- Model a blocked high-priority issue as an explicit prerequisite/unblocking
  work unit, not as a source-edit candidate. Only the prerequisite's own
  approved scope may run before the issue's measurement and implementation
  gates are satisfied.
- Keep planning artifacts separate from later implementation task artifacts;
  the blueprint itself must not authorize source changes.
- Discuss and obtain owner approval for the workflow design before activating
  optimization execution tasks.

## Acceptance Criteria

- [ ] Every known issue/observation has a normalized inventory record or an
      explicit reason it is not an implementation candidate.
- [ ] Every implementation candidate has an evidence source, owner, gate and
      dependency status, measurement method, success criterion, and rollback
      boundary.
- [ ] Ranking is reproducible from documented criteria and does not use issue
      count or document order as a primary determinant.
- [ ] The blueprint identifies independently approvable work units and their
      dependency order.
- [ ] The reusable single-subagent workflow defines no-nesting, exclusive write
      scope, Claude-only external review, and stop/rollback rules.
- [ ] The workflow defines the two-agent sequence per selected workspace:
      planning/docs/review first, then zero-trust repair/review/commit.
- [ ] A successful repair commits only after local verification and Claude
      review pass, or a Claude-unavailable Codex fallback review pass, with a
      Traditional Chinese commit body containing the wave evidence.
- [ ] Each proposed work unit has a measurable baseline and explicit pass/fail
      target in its wave's `measurements.md` and `goals.md` before
      implementation begins.
- [ ] Every wave `plans.md` points to a precise issue-ID subset in the owning
      `issue.md`, with no unlisted issue implicitly added to implementation
      scope.
- [ ] The owner reviews and approves the workflow design before any
      optimization task begins.
- [ ] No product source/configuration/test/deployment/data file is changed by
      this planning task.

## Out Of Scope

- Editing product code or configuration.
- Invoking Gemini for a wave review.
- Combining quarantined modules into whole-module optimization.
- Dispatching optimization implementation agents before the owner approves the
  blueprint and a work unit.
- Running two selected workspaces concurrently inside the same global wave.
- Creating empty wave directories for work that has not entered a declared
  optimization batch.

## Decisions

- A global wave is created only when its exact canonical issue membership is
  admitted in `optimization-issue-inventory.csv`. Deferred issues remain
  unassigned; no placeholder W3-W5 membership or empty local wave directory is
  created.
- W1 is archived with only F01B. W2 is the active product-security wave and
  must complete its seven workspaces in the sequence recorded by the current
  blueprint. A blocked workspace prevents later workspaces from bypassing it.
- A primary-owner workspace stores each local wave. Consumer paths are explicit
  in `plans.md` when a selected issue requires them.
- Claude is the sole external reviewer. A main-session read-only Codex review
  agent is the required fallback for unavailable Claude output. Gemini is
  excluded.

## External Analysis

- CCG run:
  `20260713-141158-optimization-blueprint-workflow-analysis-r1-analyzer`.
- Gemini was provider quota/billing blocked and Claude produced no usable
  output. `completedBackends=[]`, `ok=false`, and this is neither full nor
  degraded analysis approval.
- The planning discussion proceeds from the repository-local issue inventory;
  an external critique must be retried before any optimization execution task
  is approved.

## Fallback Review

- Claude produced no usable analysis output for this task, so the main session
  invoked Codex fallback reviewer Kant (`019f5a4e-74a4-7e30-87d0-092e141b50ba`).
- Verdict: `APPROVE`; Critical findings: none; Warning findings: none.
- The reviewer independently confirmed the inventory and workflow as they
  existed during the original planning review. Later owner-approved revisions
  superseded its draft W1-W5 assignment: the current CSV and optimization
  blueprint are the execution authority.

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
