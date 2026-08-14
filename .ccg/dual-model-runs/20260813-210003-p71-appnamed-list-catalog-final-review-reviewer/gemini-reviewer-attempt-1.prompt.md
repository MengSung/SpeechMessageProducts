ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p71-appnamed-list-catalog-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.1 App-named list catalog final review

Review only the task-owned P7.1 changes for `ORG-CALL-00014` / `list.catalog.retrieve.app.named`.

Scope:

- Fixed server-owned Data8 `QueryExpression` for active `list` records with purpose `小組名單` and `new_app_named=true`.
- Closed response union and immutable wire/DTO records.
- Bounded paging and response bytes; fail closed projection.
- ProductClient mapping, cancellation forwarding, invalid routing zero-I/O, defensive copies, A/B isolation tests and DI registration.
- Phase 0 matrix and authoritative rebaseline matrix updates.

Required review:

1. Find correctness, boundary, resource-lifetime, isolation, security, performance and regression issues.
2. Confirm no caller-controlled entity/query/profile/credential routing, CRM Entity leakage, mutable shared state, retry/fallback, CE dispatch, consumer cutover, feature enablement, ToolUtility removal or P8 work was introduced.
3. Classify findings as Critical, Warning or Info, with file and line reference and an evidence-based rationale.

Known local evidence:

- Focused tests: 98 passed, 0 failed.
- Full Dynamics tests: 786 passed, 7 live-SQL skipped, 0 failed.
- Full solution tests passed.
- Full solution Release build: 0 warnings, 0 errors.
- Rebaseline tests: 13 passed; authoritative matrix validator: valid.
- UTF-8 without BOM, CRLF-only and final CRLF verified for all changed C# files; `git diff --check` passed.

The review must not treat local implementation as CE/host/consumer evidence.


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