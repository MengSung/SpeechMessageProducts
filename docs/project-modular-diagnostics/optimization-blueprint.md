# Optimization Blueprint

## Purpose

This blueprint assigns every known diagnostic record to a global optimization
wave without treating every record as ready for source changes. It is the
planning authority for wave order. A selected workspace receives a local
`wave_<n>/` directory only when that wave is activated.

## Inventory Authority

- Full machine-readable register:
  `optimization-issue-inventory.csv`.
- Canonical source for a record remains its owning workspace `issue.md`.
- Inventory snapshot totals:
  - 163 canonical confirmed issues;
  - 13 runtime observations;
  - 146 rejected, merged, or conditional observations.
- Canonical confirmed issue categories: 65 security, 54 performance, and 44
  extraction.
- Canonical priorities: 15 P0, 98 P1, 47 P2, and 3 P3.
- A rejected/merged/conditional observation is retained in the register for
  traceability but is never implicitly added to a wave plan.

## Wave Model

The previous module map used Wave 0 through Wave 4. This blueprint renumbers
them as global W1 through W5 so every local workspace can use the familiar
directory name `wave_1`, `wave_2`, and so on.

| Global wave | Module-map source | Primary purpose | Included modules | Canonical issues |
|---|---|---|---|---:|
| W1 | Wave 0 | Trusted governance and executable baselines | F01A-F01D, X01, X02A-X02C, X04A-X04B | 35 |
| W2 | Wave 1 | Shared foundation contracts | F02, F03A, F03B, F03Q, F04, F05A, F05B, F06-F09 | 56 |
| W3 | Wave 2 | Identity and core business data | B01-B03, B06A-B06C | 32 |
| W4 | Wave 3 | Integrated business flows | B04A-B04C, B05, B07 | 27 |
| W5 | Wave 4 | Shared UI, end-to-end validation, and quarantine resolution | X02Q, X03, X05Q | 13 |

The order is dependency-first. A high priority issue may enter only its
assigned wave or an approved prerequisite wave. A P0 issue with an unresolved
gate becomes a prerequisite candidate, not an exception to wave order.

## First Executable Wave

The first global wave is W1. The only current draft candidates are the 12
canonical issues that are `APPROVED_DEGRADED`, have a `READY` module gate, and
have no issue-level gate block. They remain draft candidates until local
measurement contracts are reviewed.

| Workspace | Local directory when activated | Issue subset |
|---|---|---|
| F01B-ai-agent-workflow-governance | `wave_1/` | F01B-SEC-001, F01B-SEC-002, F01B-PERF-001, F01B-PERF-002, F01B-EXT-001 |
| F01C-documentation-tooling-history | `wave_1/` | F01C-SEC-001, F01C-PERF-001, F01C-PERF-002, F01C-EXT-001 |
| F01D-shared-test-harness-governance | `wave_1/` | F01D-SEC-001, F01D-PERF-001, F01D-PERF-002 |

`F01D-EXT-001` is not selected because its issue-level gate is blocked. F01A,
X01, X02A-X02C, X04A, and X04B remain assigned to W1 but require a human,
runtime, provider-review, or dependency/baseline prerequisite before a local
wave directory can be created.

## W1 Execution Status

This table is the live progress source for the first executable wave. A
workspace advances only through the declared lifecycle:
`QUEUED -> PREFLIGHTED -> PLAN_WRITING -> PLAN_REVIEW -> READY_FOR_REPAIR ->
REPAIRING -> VALIDATING -> REVIEWING -> COMMITTED`. A failed prerequisite uses
`BLOCKED`; an unmet accepted target uses `FAILED_GOAL`.

| Workspace | Issue subset | Status | Current owner | Baseline and result | Verification evidence | Review | Commit |
|---|---|---|---|---|---|---|---|
| F01B-ai-agent-workflow-governance | F01B-SEC-001, F01B-SEC-002, F01B-PERF-001, F01B-PERF-002, F01B-EXT-001 | QUEUED | Main session | Not started | Not started | Claude-only support approved; local plan review pending | Pending |
| F01C-documentation-tooling-history | F01C-SEC-001, F01C-PERF-001, F01C-PERF-002, F01C-EXT-001 | QUEUED | Main session | Not started | Not started | Not started | Pending |
| F01D-shared-test-harness-governance | F01D-SEC-001, F01D-PERF-001, F01D-PERF-002 | QUEUED | Main session | Not started | Not started | Not started | Pending |

`F01D-EXT-001` remains excluded and `BLOCKED` until its ToolUtility provider
gate is independently unblocked; it must not receive a local W1 contract.

## Readiness Rules

A canonical confirmed issue can be selected by a local wave only when all of
the following are true:

1. Its diagnostic status is `APPROVED` or `APPROVED_DEGRADED`.
2. Its module gate is `READY`.
3. Its issue-level `Gate blocked` field is `false`.
4. `measurements.md` defines a reproducible baseline and observation method.
5. `goals.md` defines a measurable success condition and a no-regression rule.
6. `plans.md` limits product paths, consumer paths, validation, and rollback.

Quarantine modules F03Q, X02Q, and X05Q may be assigned only responsibility
proof, transfer, split, or retirement work. They are not whole-module repair
candidates.

## Activation Rule

Creating a local `wave_<n>/` directory activates work for exactly one workspace
and its listed issue subset. Empty wave directories are forbidden. A global
wave processes selected workspaces sequentially in the order listed by its
approved wave plan.

The execution protocol is defined in
`wave-execution-workflow.md`.
