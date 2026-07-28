# Global isolation and memory-leak guardrails final review

Review the completed global-policy change and its planning/task artifacts.

Files in scope:

- `C:\Users\Administrator\.codex\AGENTS.md`
- `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/prd.md`
- `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/task.json`
- `.ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md`
- `.ccg/tasks/global-isolation-memory-leak-guardrails/task.json`

Verify all of the following against the actual files:

1. The new personal policy is strictly outside the `CCG:START` / `CCG:END` managed block and does not alter or duplicate its markers.
2. Cross-session, cross-user, and cross-tenant data/state leakage is an explicit zero-tolerance security release blocker.
3. Memory/resource leakage is an explicit zero-tolerance correctness/reliability release blocker.
4. Lifecycle ownership covers subscriptions, timers, background tasks, caches, collections, streams, handles, cancellation registrations, connections, and disposables with bounded ownership and deterministic cleanup.
5. Credible-risk changes require targeted isolation/lifecycle tests and proportionate stress, soak, or profiling proof.
6. Performance is framed as maximum safe sustained performance and may not weaken isolation, correctness, cleanup, verification, or maintainability.
7. Wording is concise, enforceable, technically sound, and consistent across PRD and requirements.
8. No unrelated global configuration was changed.

Report Critical, Warning, and Info findings with exact file references. A Critical or Warning must explain the concrete failure mode. Finish with PASS only when there are no Critical or Warning findings.
