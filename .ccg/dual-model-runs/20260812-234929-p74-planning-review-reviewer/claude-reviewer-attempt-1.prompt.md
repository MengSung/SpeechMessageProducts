ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p74-planning-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
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


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
