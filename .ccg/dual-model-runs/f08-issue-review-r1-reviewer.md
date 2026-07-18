# CCG reviewer Task: f08-issue-review-r1

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
# F08 Issue Review R1 Prompt

Role: reviewer

Review the F08 Payment Provider Core diagnostic draft. This is diagnosis-only. Do not modify files.

## Hard Constraints

- Do not spawn agents or request nested agents.
- Do not modify product source, tests, project/solution files, config, CI, docs outside `docs/project-modular-diagnostics/F08-payment-provider-core/**`, other module workspaces, `.trellis` files, ledger files, package/cache/lock/test output, `bin/**`, or `obj/**`.
- Do not run `dotnet restore`, `dotnet build`, `dotnet test`, package restore, code generation, formatting, migrations, benchmarks, coverage, or any command that writes generated/ignored/cache/lock/test-output files.
- Prefer read-only commands only, such as `rg`, `Get-Content`, and `git diff`.

## Module Scope

Owned:

- `SpeechMessage.Payments/**`
- `LinePayCSharp/**`
- `SpeechMessage.Payments.Tests/**`, except `SpeechMessage.Payments.Tests/Workflows/**`

Read-only consumers:

- Payment ASP.NET integration.
- ChurchReport payment host flows.

Exclude:

- MVC route/session/CRM/donation decisions.
- Post-payment workflow orchestration.
- Neutral order/ack mapping owned by F09.
- LINE notification decisions owned by B05/B07/F06.

## Draft Files To Review

- `docs/project-modular-diagnostics/F08-payment-provider-core/issue.md`
- `docs/project-modular-diagnostics/F08-payment-provider-core/evidence/scope-manifest.md`
- `docs/project-modular-diagnostics/F08-payment-provider-core/evidence/security-analysis.md`
- `docs/project-modular-diagnostics/F08-payment-provider-core/evidence/performance-analysis.md`
- `docs/project-modular-diagnostics/F08-payment-provider-core/evidence/extraction-analysis.md`
- `docs/project-modular-diagnostics/F08-payment-provider-core/evidence/runtime-validation-plan.md`

## Review Goals

1. Verify whether each retained issue is supported by file:line evidence.
2. Identify false positives, overclaims, or issues that belong to another module.
3. Identify missing high-value F08 findings in the required security/performance/extraction categories.
4. Check that issue severities are reasonable.
5. Check that no forbidden implementation or runtime validation claim is made.

## Required Output

Return a concise `Critical / Warning / Info` review report.

For each item:

- State whether it is a blocker for approving the F08 diagnostic.
- Cite file paths and lines when disagreeing with the draft.
- Distinguish confirmed source evidence from inference.

Do not provide patches.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.