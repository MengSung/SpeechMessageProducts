ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: global-isolation-memory-leak-guardrails-live-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Live Dual-Model Review Validation

Perform a final planning-specification review for the task `global-isolation-memory-leak-guardrails`.

Review these files from the repository:

- `.ccg/tasks/global-isolation-memory-leak-guardrails/task.json`
- `.ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md`
- `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/task.json`
- `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/prd.md`
- `AGENTS.md`

Review criteria:

1. The intended global `C:\Users\Administrator\.codex\AGENTS.md` policy is concise, durable, and placed outside managed blocks.
2. Cross-session, cross-user, and cross-tenant leakage is an explicit zero-tolerance security release blocker.
3. Memory leaks are an explicit zero-tolerance correctness and reliability release blocker.
4. Lifecycle ownership and deterministic cleanup cover subscriptions, timers, background tasks, caches, collections, streams, handles, and disposable resources when relevant.
5. Risk-based verification requires targeted tests, stress checks, or profiling where credible leakage or retention risk exists.
6. Performance optimization cannot weaken isolation, correctness, cleanup, verification, or maintainability.
7. The specification does not require implementation during this review and does not authorize unrelated changes.

Do not edit files. Return a structured report with:

- Overall verdict: PASS or FAIL
- Critical findings
- Warning findings
- Info findings
- Required changes before user approval

Every finding must cite the relevant file and text or explain why no finding exists. Explicitly state whether your backend completed the review with a usable final report.


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