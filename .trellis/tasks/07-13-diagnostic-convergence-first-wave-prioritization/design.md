# Diagnostic Convergence Execution Design

## Objective

Close the carpet-diagnostic program truthfully before any optimization map or
product-code planning begins. The execution repairs diagnostic metadata,
obtains or truthfully records review/runtime evidence, resolves the F01A scope
violation, documents worker-recovery exceptions, and performs a final audit.

## Boundaries

- Product source, product configuration, project files, solution files,
  deployment files, and tests remain read-only.
- Normal writes are limited to:
  - `docs/project-modular-diagnostics/**`;
  - `.ccg/dual-model-runs/**`;
  - `.ccg/tasks/project-modular-analysis-diagnosis-optimization/**`;
  - `.ccg/tasks/diagnostic-convergence-first-wave-prioritization/**`;
  - `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/**`;
  - `.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/**`.
- Runtime validation may create generated or test output only after recording a
  full pre-command filesystem baseline. Every generated path must be listed,
  inspected, and removed only when it is proven to have been created by that
  validation run. Existing user files are never removed or reverted.
- Step 7 (optimization map and per-issue implementation measurements) is an
  explicit stop boundary and is not part of this execution.

## Agent Model

Five peer workers perform read-only evidence collection for schema, CCG
pending reviews, runtime validation, F01A recovery, and topology recovery.
Workers may not edit files, run writing commands, or spawn agents. Lead Codex
owns all integration edits and final verification so write ownership never
overlaps.

## Ordered Convergence

### 1. Schema convergence

Every `issue.md` receives the seven mandatory header fields with canonical
values. Gate status is one of `READY`, `BLOCKED`, or `QUARANTINE` and is derived
from the authoritative module map, not from current issue severity.

The issue hash is SHA-256 over canonical UTF-8/LF content after replacing the
first `Issue document SHA-256:` line with an empty-value form. This avoids a
self-referential hash while detecting every other content change. The workflow
will document this derivation before hashes are populated.

### 2. CCG pending convergence

Each of the 17 pending modules is reviewed through
`docs/scripts/Start-CcgDualModelRun.ps1`. Runs are sequential because current
provider quota/session state makes parallel retries wasteful. A module may be
promoted only when at least one backend produces usable output and every
completed-backend verdict is reflected. No usable backend remains a truthful
blocked/pending disposition and is not reported as approval.

### 3. Runtime validation convergence

B06A, B06B, B06C, and the contradictory X05Q findings receive the smallest
safe measurement defined by their evidence plans. Validation that requires
production credentials, external mutations, or unavailable fixtures is not
simulated. Such findings remain pending with an exact blocker and future
measurement contract.

### 4. F01A recovery

F01A receives a review-only CCG rerun. The prompt prohibits commands and all
repository writes. A fresh before/after tracked-and-ignored filesystem audit
must prove no write. The original violating run remains recorded and is never
rewritten as compliant.

### 5. Worker-recovery exception

Replacement history cannot be erased. A narrow recovery exception may accept
one final author only when prior attempts produced no diagnostic deliverable,
workers never overlapped, nested count stayed zero, and all superseded attempt
IDs remain visible. Any workspace that cannot prove those facts becomes
`INVALID_AGENT_TOPOLOGY`.

### 6. Final audit and closure

The final audit verifies 35 workspace folders, 245 required files, canonical
schema and hashes, truthful status/reviewer evidence, runtime dispositions,
F01A write scope, topology records, no product changes, ledger consistency,
and Trellis/CCG task consistency. Only then can the parent diagnostic task be
closed. The session stops before Step 7.

## Failure Handling

- Provider quota/session block: retain pending status and artifact evidence.
- Runtime prerequisite unavailable: retain runtime pending with exact blocker.
- Any unexpected write: stop the affected run, preserve evidence, and mark the
  run invalid; do not revert unknown user changes.
- Metadata contradiction: prefer workflow evidence and ledger history; never
  promote status mechanically.

## Rollback

Documentation edits are independently reviewable by step. No product rollback
is expected because product files are not changed. Generated runtime outputs
are removed only from a verified post-baseline delta and are recorded in the
step progress log.
