ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-auth-contact-lookup-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 authentication contact lookup boundary final review

Review only the current uncommitted diff for active Trellis child
`.trellis/tasks/08-13-p74-auth-contact-lookup-boundary/`.

Scope: ORG-CALL-00055 and ORG-CALL-00056 disabled-by-default, local-only Data8/ProductClient typed contact reads. No CE I/O, feature enablement, traffic change, login/session wiring, P7.5, P8, push, or PR is authorized.

Verify the actual diff and report Critical / Warning / Info only:
- no password/hash/token/cookie/raw Entity/raw exception crosses wire/DTO/client;
- fixed account/LINE QueryExpression only, active condition, TopCount=2, no generic CRUD/caller query;
- false gate returns before bind/options/profile/host/client/I/O;
- profile/workload validation, cancellation, no retry/fallback;
- zero/duplicate/secret/mismatch fail closed;
- A/B request-local isolation, resource ownership and encoding consistency;
- matrix/schema/registry agreement.

Do not propose expanding scope. Distinguish verified facts from speculation. Answer in Traditional Chinese.

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