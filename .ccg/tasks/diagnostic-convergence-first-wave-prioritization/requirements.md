# Diagnostic Convergence and First-Wave Prioritization Requirements

## Objective

Convert the complete but mixed-confidence 35-workspace diagnostic set into a
governed optimization-readiness matrix and a proposed first optimization wave.

## Scope

- Reconcile one terminal or explicitly blocked disposition per workspace.
- Repair or revalidate F01A write-scope status.
- Define and execute only separately approved review/runtime convergence work.
- Normalize and rank retained issues with source traceability and dependency
  awareness.
- Produce an approval proposal; do not change product code.

## Current State

- 14 `APPROVED_DEGRADED`
- 17 `DEGRADED_REVIEW_PENDING`
- 3 `RUNTIME_VALIDATION_PENDING`
- 1 `INVALID_WRITE_SCOPE`

## Required Gates

- Use strict first-wave admission. Eligible states are `APPROVED`,
  `APPROVED_DEGRADED`, `NO_ACTION_REQUIRED`, or a later approved terminal state
  reached after required runtime validation.
- Require the module-map optimization gate to be ready and all required
  provider/consumer baselines to be executable. Diagnostic status alone is not
  sufficient.
- Treat F03Q, X02Q, and X05Q as quarantine ownership; allow only proof, split,
  transfer, or approved retirement work rather than whole-module optimization.
- Pending, invalid-scope, invalid-topology, and human-decision states are
  ineligible until resolved.
- CCG outcomes are represented truthfully.
- Runtime validation has bounded writes and explicit evidence requirements.
- Pending or invalid modules are not silently promoted to ready.
- Every ranked issue links back to its module diagnostic evidence.
- Product implementation starts only under a separately approved child task.

## External Analysis State

- Run: `20260713-095124-diagnostic-convergence-first-wave-prioritization-analysis-analyzer`
- Claude: usable output, verdict `ACCEPT_WITH_CHANGES`
- Gemini: no output, provider balance/quota HTTP 403
- Classification: degraded single-model fallback
