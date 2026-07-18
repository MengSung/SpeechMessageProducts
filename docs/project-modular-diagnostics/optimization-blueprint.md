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
| 1 | X04A-runtime-configuration-secrets | X04A-SEC-001, X04A-SEC-002, X04A-PERF-001 | READY_FOR_REVISION_2_REPAIR | Revision 1 remains committed as `ab9993e8`; owner approved Revision 2 and its degraded contract review reproduced comments=3, aliases=6, original scanner tests 2/2, and artifact leaks=0 |
| 2 | B01-identity-session-access-control | B01-SEC-003 | BLOCKED | `wave_2/` remains contract-approved, but repair is blocked by missing non-production CRM row-version/route-probe evidence and deployed ToolUtility caller inventory |
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
| 1 | X04A-runtime-configuration-secrets | `READY_FOR_REVISION_2_REPAIR` (2026-07-18; Revision 1 commit `ab9993e8` retained) | Revision 1 passed its frozen `0/21` scan, eight Production controls, 13-consumer bridge migration, focused suite, and build. The Revision 2 contract now covers the three raw-comment and six legacy-alias residuals with a two-path allowlist. Claude produced no usable contract-review output; owner approval and inline redacted audit found no unresolved Critical or Warning. | Execute the approved two-path repair, verify `0/21`, `0/6`, comments=0, then return X04A to `COMMITTED` before resuming B01. |
| 2 | B01-identity-session-access-control | `BLOCKED` (2026-07-18, no commit) | The repair agent captured a two-location direct-comparison baseline and a 56-location password-flow baseline. Its isolated verifier tests reached a temporary 12-test green state, but the contract's mandatory non-production CRM row-version conditional-update success/conflict proof, synthetic `ProcessLogin -> SetupSystemData` route probe, and deployed ToolUtility caller inventory were unavailable. No final validation, Claude review, or commit occurred; all uncommitted candidate product/test paths were cleaned. | F03A/CRM and non-production environment owners must supply redacted row-version capability evidence, route-probe evidence, and caller path/owner/key-or-raw inventory. Re-activate B01 only after those prerequisites are available. |

Wave 2 is paused at reopened sequence 1 while X04A Revision 2 closes the
residual scanner gap. B01 also retains its external evidence blockers. B01 and
all later workspaces remain queued until X04A returns to `COMMITTED`; B02 through
X05Q additionally may not bypass B01.

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
