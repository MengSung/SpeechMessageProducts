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
