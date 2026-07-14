# Diagnostic Run Current State

As of 2026-07-13, all 35 isolation-zone workspaces have complete diagnostic
folders and one explicit convergence disposition under
`docs/project-modular-diagnostics/`.

## Status Counts

- APPROVED_DEGRADED: 13
- DEGRADED_REVIEW_PENDING: 17
- RUNTIME_VALIDATION_PENDING: 4
- HUMAN_DECISION_REQUIRED: 1

## Completion Evidence

Final local verification covers:

- Ledger rows: 35
- Top-level diagnostic workspace folders: 35
- Required seven-file package present for every workspace:
  - `issue.md`
  - `review-log.md`
  - `evidence/scope-manifest.md`
  - `evidence/security-analysis.md`
  - `evidence/performance-analysis.md`
  - `evidence/extraction-analysis.md`
  - `evidence/runtime-validation-plan.md`
- Forbidden placeholder scan: pass for all diagnostic folders.
- Product-code write-scope check: no unexpected product/code/config/test changes
  were introduced by convergence work.
- Canonical metadata/hash validation: 35/35.
- Strict per-issue schema validation: 35/35.
- Pending CCG dispositions: 17/17, optimization eligible: 0.
- Runtime dispositions: B06A, B06B, B06C, and X05Q recorded without unsafe
  external execution.

## Status Meaning

- `APPROVED_DEGRADED`: at least one backend produced usable output and failed backend state was quota/session related.
- `DEGRADED_REVIEW_PENDING`: CCG ran but no backend produced usable output.
- `RUNTIME_VALIDATION_PENDING`: diagnostics exist and require runtime validation before final approval.
- `HUMAN_DECISION_REQUIRED`: the diagnostic package is complete, but owner or
  later external-review evidence is required before status can change.

## Remaining Follow-Up

No workspace is missing its diagnostic folder or seven-file package. Remaining
statuses are explicit blocked dispositions, not missing diagnostic deliverables.
The 17 provider-blocked modules, four runtime-pending modules, and F01A are all
excluded from optimization admission.

## CCG Pending Convergence

- B02 was retried through the self-healing CCG entrypoint against its frozen
  canonical hash; neither backend produced usable output.
- The controlled sequential queue stopped after that zero-backend result.
- B02 records `PROVIDER_BLOCKED_NO_USABLE_BACKEND`; the other 16 frozen retry
  packets record `PROVIDER_BLOCKED_RETRY_DEFERRED`.
- Provider-blocked or deferred review is not approval and did not change any
  ranked issue.

## F01A Recovery

- The original restore/write violation remains preserved.
- The accepted recovery measurement found zero product deltas and zero
  unexpected deltas after classifying CCG task-turn metadata inside the already
  approved `.ccg/tasks/**` orchestration boundary.
- Both recovery backends produced no usable output. F01A therefore remains
  `HUMAN_DECISION_REQUIRED` and is not optimization-eligible.

## Worker Topology Recovery

- Worker recovery exceptions: 5.
- Affected workspaces: B04A, B04C, X04A, X04B, X05Q.
- Each exception records one accepted final package author, every superseded
  empty attempt, zero overlap, and nested agent count zero.
- The exception is non-retroactive: it does not claim that only one dispatch
  occurred. It accepts the final package only because superseded attempts
  produced no diagnostic deliverable or usable CCG finding.
- The X04A unavailable-model launch is recorded as
  `DISPATCH_FAILED_MODEL_UNAVAILABLE`, not as a diagnostic author.

## Final Compliance Audit

- Evidence:
  `.trellis/tasks/07-13-diagnostic-convergence-first-wave-prioritization/research/diagnostic-convergence-compliance-final.json`.
- Result: `pass=true`, checks `14`, failed `0`.
- Step 7 optimization inventory, scoring, and wave planning have not started and
  remain under the project owner's explicit gate.
