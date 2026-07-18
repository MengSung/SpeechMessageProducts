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
failures. It contains ten Critical P0 issues plus the required X04A-PERF-001
configuration-lifecycle prerequisite, for eleven canonical issues across seven
workspaces.

| Sequence | Workspace | Issue subset | Status | Local contract |
|---:|---|---|---|---|
| 1 | X04A-runtime-configuration-secrets | X04A-SEC-001, X04A-SEC-002, X04A-PERF-001 | CONTRACT_REVISION_APPROVED_DEGRADED | Revision 1 admits the 13-consumer migration prerequisite; Claude had no usable output and the documented inline Codex fallback approved the contract |
| 2 | B01-identity-session-access-control | B01-SEC-003 | CONTRACT_APPROVED | `wave_2/` approved through Codex fallback after Claude produced no usable output; repair retains non-production CRM and caller-inventory gates |
| 3 | B02-member-contact-profile-onboarding | B02-SEC-001 | CONTRACT_APPROVED | `wave_2/` approved through Codex fallback after Claude produced no usable output; repair must enforce the pre-hydration Permit Gate |
| 4 | B04B-appointment-equipment | B04B-SEC-001 | CONTRACT_APPROVED | `wave_2/` approved through Codex fallback after Claude produced no usable output; repair includes Schedule selector and stateless SchedulerView gates |
| 5 | B04C-scheduling-qr | B04C-SEC-001, B04C-SEC-002 | CONTRACT_APPROVED | `wave_2/` approved through Codex fallback after Claude produced no usable output; repair retains explicit B01/B04B/X01/security-platform deployment gates |
| 6 | B04A-attendance-present-record | B04A-SEC-001, B04A-SEC-002 | CONTRACT_APPROVED | `wave_2/` approved through Codex fallback after Claude produced no usable output; repair retains route, projection-purity, and staged runtime-proof gates |
| 7 | X05Q-churchreport-legacy-boundary-quarantine | X05Q-SEC-001 | CONTRACT_APPROVED | `wave_2/` approved through Codex fallback after Claude produced no usable output; repair retains local, staging, runtime, and deployment-proof gates |

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

## Wave 2 Execution Record

| Sequence | Workspace | Terminal state | Evidence | Required action before resuming |
|---:|---|---|---|---|
| 1 | X04A-runtime-configuration-secrets | `CONTRACT_REVISION_APPROVED_DEGRADED` (2026-07-18) | The 2026-07-15 blocked repair proved that 13 excluded runtime consumers load only `appsettings.json`; Revision 1 admits X04A-PERF-001 and freezes the bridge contract. Claude had no usable output; the documented inline Codex fallback found no unresolved Critical or Warning. | Complete one allowlisted X04A repair commit, then independently verify it before dispatching B01. |

Wave 2 dispatch is paused at sequence 1. B01 through X05Q remain selected and
contract-approved, but no repair may start until X04A has a committed Revision 1
repair.

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
