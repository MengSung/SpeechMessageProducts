ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: f07-issue-review-r1

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
# F07 Issue Review Round 1

You are reviewing a diagnosis-only artifact set for module F07 LINE RichMenu Engine.

## Repository Scope

Workspace root: `D:\音訊科技產品\系統平台\SpeechMessageProducts`

Owned F07 paths:

- `LineMessagingProcessor.RichMenus/**`
- `LineMessagingProcessor.RichMenus.Tests/**`

Diagnosis artifacts to review:

- `docs/project-modular-diagnostics/F07-line-richmenu-engine/issue.md`
- `docs/project-modular-diagnostics/F07-line-richmenu-engine/review-log.md`
- `docs/project-modular-diagnostics/F07-line-richmenu-engine/evidence/scope-manifest.md`
- `docs/project-modular-diagnostics/F07-line-richmenu-engine/evidence/security-analysis.md`
- `docs/project-modular-diagnostics/F07-line-richmenu-engine/evidence/performance-analysis.md`
- `docs/project-modular-diagnostics/F07-line-richmenu-engine/evidence/extraction-analysis.md`
- `docs/project-modular-diagnostics/F07-line-richmenu-engine/evidence/runtime-validation-plan.md`

Read-only context only:

- `Line.Messaging/**`
- `LineMessagingProcessor/**`
- `LineMessagingProcessor.AspNetCore/**`
- `SpeechMessageProducts.ChurchReport/**` where RichMenu integration is referenced

## Hard Constraints

This is a diagnosis-only review.

Do not modify files.
Do not spawn agents or use nested agents.
Do not run or recommend running commands that write generated/ignored/cache/lock/test-output files during this review.
Do not run `dotnet restore`, `dotnet build`, `dotnet test`, package restore, code generation, formatting, migrations, benchmarks, or coverage.
Do not write under product source, tests, project/solution files, config, CI, docs outside the F07 workspace, other module workspaces, `.trellis` task files, ledger files, package/cache/lock/test output, `bin/**`, or `obj/**`.

You may inspect files and reason from source text.

## Review Request

Review the F07 diagnosis for factual accuracy, severity, missing evidence, and false positives.

Focus on these retained findings:

1. `F07-001`: RichMenu TTL is modeled by `RichMenuDecision.Ttl` but not persisted by assignment, so expiry sweep cannot act on policy TTL.
2. `F07-002`: same-menu assignment trusts local state and skips provider reconciliation.
3. `F07-003`: provisioning can reuse a provider RichMenu created during a failed upload because reuse is based on versioned name.
4. `F07-004`: F07 cancellation tokens do not reach provider calls.
5. `F07-005`: default in-memory state store is unbounded and expiry sweep scans all states.
6. `F07-006`: legacy `DeleteLinkedRichMenuAsync` deletes a provider RichMenu id discovered from one user's current link, while the workflow remains publicly registered.

For each finding, decide:

- Confirmed, downgrade, upgrade, merge, or reject.
- Whether the file:line evidence is sufficient.
- Whether the issue belongs to F07 or should be delegated to F04/F05A/F05B/B07.
- Whether any security/performance/extraction issue is missing from the artifacts.

Also review:

- Whether the scope manifest accurately respects the prompt boundaries.
- Whether `issue.md` has no draft/initialized status.
- Whether every retained confirmed issue has file:line evidence and CCG round history placeholders.
- Whether the extraction recommendations are based on module seams rather than file size.
- Whether the runtime validation plan avoids forbidden commands for this diagnosis-only phase.

## Output Format

Return a concise Critical / Warning / Info report.

For every Critical or Warning:

- Identify the artifact path and source evidence path/line.
- State exactly what should change in the diagnosis artifacts.
- Call out false positives explicitly when a finding should be rejected.

End with one of:

- `APPROVED`
- `APPROVED_WITH_WARNINGS`
- `REJECTED_NEEDS_REVISION`



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