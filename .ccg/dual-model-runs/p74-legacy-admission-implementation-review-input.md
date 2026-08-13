# P7.4 legacy Gateway admission implementation review

Review only the current uncommitted P7.4 change set. Do not edit files.

## Goal
A host-owned local legacy drain controller, optional Package01 fee ingress accounting,
host shutdown drain, no-secret validator and drain-first/non-overlap runbook.

## Security invariants
- It is operation-level metering only, never Organization-level capacity proof.
- Package01FeeReadsEnabled remains false; no CE writes, traffic switch, P7.5 or P8.
- Synchronous ToolUtility CRM work cannot be cancelled/fenced, unknown legacy coverage and
  cross-host non-durable topology are no-go.
- No controller retention of request/session/CRM entity/profile/endpoint/credentials/responses.
- Lease double-dispose, timeout, cancellation and shutdown must fail closed with bounded cleanup.

## Verify
Inspect git diff and relevant tests. Report Critical/Warning/Info. Flag any security issue,
false claim of deployment evidence, resource/session leakage, lifecycle race, test gap,
legacy behavior regression, or documentation/encoding issue. Do not request external operations.
