# P7.4 planning review

Review the current P7.4 planning artifacts and repository evidence for a safe ChurchReport
ProductClient capability-by-capability cutover.

Scope:

- Review `.trellis/tasks/08-12-churchreport-productclient-cutover/{prd,design,implement}.md`.
- Cross-check against the authoritative 70-row matrix and current Package01 consumer code.
- First local batch is read-only Package01 fee/stor consumer work only; every gate remains false.
- Identify Critical/Warning/Info findings in isolation, lifecycle cleanup, capability boundary,
  feature-gate/rollback, evidence claims, and accidental P7.5/P8 scope expansion.

Hard constraints:

- No CE mutation, feature flag enablement, traffic cutover, deployment, request-time fallback,
  dual-write, generic CRM proxy, P7.5, or P8.
- Do not recommend accepting SDK `Entity`/`EntityCollection` bridge as a completed typed migration.
- First actual enablement requires durable shared admission authority or verified drain-first
  non-overlap runbook.

Output a concise Critical / Warning / Info report with concrete file and line references.
