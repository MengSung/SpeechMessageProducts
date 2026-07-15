# Wave 2 Product Security Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` to execute this plan task by task.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete ten Critical P0 product-runtime security issues across seven
workspaces without allowing the main session to modify product code.

**Architecture:** The global blueprint owns Wave 2 membership and order. Each
workspace owns exactly one local `wave_2` contract composed of `plans.md`,
`measurements.md`, and `goals.md`. All local contracts are approved before any
repair begins; fresh repair subagents then execute one workspace at a time and
commit independently.

**Tech Stack:** Markdown, CSV, Git, repository-native test commands, one fresh
planning or repair subagent at a time, Claude read-only review, and a read-only
Codex fallback when Claude has no usable output.

---

## Hard Boundaries

- The main session may update orchestration documents and dispatch, wait for,
  inspect, and close subagents. It must never modify product source, tests, or
  runtime configuration for Wave 2.
- Planning subagents may write only their workspace's three `wave_2` files.
- Repair subagents must be different agents from planning subagents and may
  write only paths explicitly allowed by the approved local `plans.md`.
- No subagent may spawn another agent.
- At most one mutating subagent may be active. Workspaces execute in the exact
  order recorded below.
- A blocked prerequisite pauses dispatch after its evidence and required owner
  action are recorded. It never removes the issue, permits a later workspace to
  bypass it, or counts toward Wave 2 completion.
- One workspace produces one independently revertible commit with a Traditional
  Chinese subject and body.

## Wave 2 Manifest

| Sequence | Workspace | Canonical issue IDs |
|---:|---|---|
| 1 | X04A-runtime-configuration-secrets | X04A-SEC-001, X04A-SEC-002 |
| 2 | B01-identity-session-access-control | B01-SEC-003 |
| 3 | B02-member-contact-profile-onboarding | B02-SEC-001 |
| 4 | B04B-appointment-equipment | B04B-SEC-001 |
| 5 | B04C-scheduling-qr | B04C-SEC-001, B04C-SEC-002 |
| 6 | B04A-attendance-present-record | B04A-SEC-001, B04A-SEC-002 |
| 7 | X05Q-churchreport-legacy-boundary-quarantine | X05Q-SEC-001 |

### Task 1: Freeze The Global Wave 2 Manifest

**Files:**

- Modify: `docs/project-modular-diagnostics/optimization-issue-inventory.csv`
- Modify: `docs/project-modular-diagnostics/optimization-blueprint.md`
- Modify: `docs/project-modular-diagnostics/optimization-blueprint-implementation-plan.md`

- [x] Set `ProposedWave=W2` and `Disposition=WAVE_2_SELECTED` for exactly the
  ten manifest issue IDs.
- [x] Record exactly seven selected workspaces and their execution order in the
  blueprint.
- [x] Exclude AI, CCG, Trellis, documentation, and test-governance issues from
  Wave 2.
- [x] State that blocked and quarantine gates affect execution evidence, not
  membership.

### Task 2: Approve Every Local Wave 2 Contract

**Files created by planning subagents:**

- `docs/project-modular-diagnostics/X04A-runtime-configuration-secrets/wave_2/{plans,measurements,goals}.md`
- `docs/project-modular-diagnostics/B01-identity-session-access-control/wave_2/{plans,measurements,goals}.md`
- `docs/project-modular-diagnostics/B02-member-contact-profile-onboarding/wave_2/{plans,measurements,goals}.md`
- `docs/project-modular-diagnostics/B04B-appointment-equipment/wave_2/{plans,measurements,goals}.md`
- `docs/project-modular-diagnostics/B04C-scheduling-qr/wave_2/{plans,measurements,goals}.md`
- `docs/project-modular-diagnostics/B04A-attendance-present-record/wave_2/{plans,measurements,goals}.md`
- `docs/project-modular-diagnostics/X05Q-churchreport-legacy-boundary-quarantine/wave_2/{plans,measurements,goals}.md`

- [ ] Dispatch one fresh planning subagent per workspace, sequentially in
  manifest order, with write access limited to its three local files.
- [ ] Require each `plans.md` to list exact product/test/configuration paths,
  excluded paths, repair boundaries, validation commands, and rollback.
- [ ] Require each `measurements.md` to define a reproducible security baseline,
  fixtures, evidence location, and observation method for every selected issue.
- [ ] Require each `goals.md` to define measurable success, authorized
  no-regression behavior, and explicit rollback conditions.
- [ ] Obtain read-only review approval for each contract and close its planning
  subagent before starting the next one.
- [ ] Confirm all seven local contracts are approved before Task 3 begins.

## Sequential Repair Contract

Tasks 3 through 9 use the same mandatory sequence. For each workspace, the main
session must:

1. dispatch one fresh zero-trust repair subagent with the exact issue IDs and
   approved local contract paths;
2. require baseline capture before modification and test-first repair within
   the `plans.md` allowlist;
3. require every measurement and goal to pass without weakening the frozen
   contract;
4. obtain read-only review approval, using Codex fallback only when Claude has
   no usable output;
5. require the repair subagent to create one Traditional Chinese commit, then
   independently verify its allowlisted diff and evidence;
6. update the blueprint terminal evidence, close the subagent, and only after
   `COMMITTED` dispatch the next workspace.

### Task 3: Repair X04A Runtime Configuration Secrets

**Contract:**
`docs/project-modular-diagnostics/X04A-runtime-configuration-secrets/wave_2/`

- [x] Reached the truthful `BLOCKED` terminal result on 2026-07-15. No product
  repair commit was created because the approved allowlist excludes runtime
  consumers that must migrate before externally injected secrets are safe.

**Blocking evidence:** The Claude-only runner produced no usable output, so the
single permitted read-only Codex fallback review was used. It confirmed that
`ChurchReportLineAdminNotificationService` and `LineUtilityClass` load only
`appsettings.json`; clearing committed secrets would leave those Production
paths without injected values. The required next action is a separately
approved X04A contract that includes the `X04A-PERF-001` consumer migration or
another safe compatibility design. Per the sequential rule, Tasks 4 through 9
must not start.

### Task 4: Repair B01 Identity And Login Password Handling

**Contract:**
`docs/project-modular-diagnostics/B01-identity-session-access-control/wave_2/`

- [ ] Complete B01-SEC-003 through the sequential repair contract and record
  `COMMITTED` or a truthful `BLOCKED` result.

### Task 5: Repair B02 Contact Ownership Enforcement

**Contract:**
`docs/project-modular-diagnostics/B02-member-contact-profile-onboarding/wave_2/`

- [ ] Complete B02-SEC-001 through the sequential repair contract and record
  `COMMITTED` or a truthful `BLOCKED` result.

### Task 6: Repair B04B Appointment LINE Identity Binding

**Contract:**
`docs/project-modular-diagnostics/B04B-appointment-equipment/wave_2/`

- [ ] Complete B04B-SEC-001 through the sequential repair contract and record
  `COMMITTED` or a truthful `BLOCKED` result.

### Task 7: Repair B04C QR And Scheduler Mutation Boundaries

**Contract:**
`docs/project-modular-diagnostics/B04C-scheduling-qr/wave_2/`

- [ ] Complete B04C-SEC-001 and B04C-SEC-002 through the sequential repair
  contract and record `COMMITTED` or a truthful `BLOCKED` result.

### Task 8: Repair B04A Attendance Authorization And Query Side Effects

**Contract:**
`docs/project-modular-diagnostics/B04A-attendance-present-record/wave_2/`

- [ ] Complete B04A-SEC-001 and B04A-SEC-002 through the sequential repair
  contract and record `COMMITTED` or a truthful `BLOCKED` result.

### Task 9: Resolve X05Q Legacy Session Identity Fallback

**Contract:**
`docs/project-modular-diagnostics/X05Q-churchreport-legacy-boundary-quarantine/wave_2/`

- [ ] Complete X05Q-SEC-001 through the sequential repair contract and record
  `COMMITTED` or a truthful `BLOCKED` result.

### Task 10: Close Wave 2

**Files:**

- Modify: `docs/project-modular-diagnostics/optimization-blueprint.md`
- Read: every selected workspace `wave_2/{plans,measurements,goals}.md`
- Inspect: all seven workspace commits and validation outputs

- [ ] Verify all ten issue IDs appear exactly once in Wave 2 and no unselected
  issue entered any local contract or source diff.
- [ ] Verify all seven workspaces are `COMMITTED`; a `BLOCKED` workspace keeps
  Wave 2 open and prevents later workspace dispatch.
- [ ] Verify each committed workspace satisfied its frozen measurements, goals,
  no-regression checks, review gate, and allowlist.
- [ ] Run the broadest repository tests required by the seven local contracts.
- [ ] Mark Wave 2 complete only when the blueprint truthfully records all seven
  terminal results and commit identifiers.
