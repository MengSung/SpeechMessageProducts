# Optimization Blueprint

## Purpose

This document records completed or active optimization waves. Canonical issue
definitions remain in each workspace `issue.md`; the cross-workspace inventory
remains `optimization-issue-inventory.csv`.

Wave membership is selected by product risk and priority. Dependency layers in
`module-boundaries-and-optimization-map.md` describe execution order only and
do not assign issues to waves.

## Inventory Authority

- 163 canonical confirmed issues;
- 13 runtime observations;
- 146 rejected, merged, or conditional observations.

Only canonical confirmed issues can enter a wave. Runtime and non-actionable
records remain evidence and do not become implementation work implicitly.

## Wave 1 Archive

Wave 1 contains the repository-governance repairs implemented in
`F01B-ai-agent-workflow-governance/wave_1`.

| Workspace | Issue subset | Result | Verification |
|---|---|---|---|
| F01B-ai-agent-workflow-governance | F01B-SEC-001, F01B-SEC-002, F01B-PERF-001, F01B-PERF-002, F01B-EXT-001 | Implemented and locally validated | 0 indexed raw CCG/Serena paths; 0 durable synthetic-token matches; 15 Python and 5 OpenCode tests passed |

The local implementation contract is preserved in:

```text
F01B-ai-agent-workflow-governance/wave_1/
  plans.md
  measurements.md
  goals.md
```

## Wave 2 Selection

Wave 2 addresses the highest-priority confirmed product-runtime security
failures. It contains ten Critical P0 issues across seven workspaces.

| Sequence | Workspace | Issue subset | Status | Local contract |
|---:|---|---|---|---|
| 1 | X04A-runtime-configuration-secrets | X04A-SEC-001, X04A-SEC-002 | SELECTED | `wave_2/` not created |
| 2 | B01-identity-session-access-control | B01-SEC-003 | SELECTED | `wave_2/` not created |
| 3 | B02-member-contact-profile-onboarding | B02-SEC-001 | SELECTED | `wave_2/` not created |
| 4 | B04B-appointment-equipment | B04B-SEC-001 | SELECTED | `wave_2/` not created |
| 5 | B04C-scheduling-qr | B04C-SEC-001, B04C-SEC-002 | SELECTED | `wave_2/` not created |
| 6 | B04A-attendance-present-record | B04A-SEC-001, B04A-SEC-002 | SELECTED | `wave_2/` not created |
| 7 | X05Q-churchreport-legacy-boundary-quarantine | X05Q-SEC-001 | SELECTED | `wave_2/` not created |

Every selected workspace must first receive an approved `wave_2/plans.md`,
`measurements.md`, and `goals.md`. Product repair cannot begin until all seven
local contracts are approved. Repairs then run sequentially in the table order.

The Wave 2 lifecycle is:

```text
SELECTED -> CONTRACT_WRITING -> CONTRACT_REVIEW -> READY_FOR_REPAIR ->
REPAIRING -> VALIDATING -> REVIEWING -> COMMITTED
```

An unresolved prerequisite uses `BLOCKED`; it does not remove the issue from
Wave 2 or permit the next workspace to bypass it. Wave 2 is complete only when
all seven workspaces are `COMMITTED`.

## Future Wave Admission

A future wave must:

1. select issues horizontally across all workspaces by product risk and
   priority;
2. prefer runtime identity, authorization, secret, payment, and data-integrity
   failures over development-tool governance;
3. create one local `wave_<n>/` contract for every selected workspace before
   product code changes begin;
4. retain blocked issues in the selected wave while recording the prerequisite
   needed to execute them;
5. reach a truthful terminal state for every selected workspace before the
   global wave is complete.

The reusable execution protocol remains in `wave-execution-workflow.md`.
