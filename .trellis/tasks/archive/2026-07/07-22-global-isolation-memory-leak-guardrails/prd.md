# Define global session and memory leak guardrails

## Goal

Add durable global Codex instructions that treat cross-session data leakage and memory leaks as zero-tolerance release blockers, while keeping performance guidance subordinate to security and correctness.

## Requirements

- Add durable personal guidance to the global `C:\Users\Administrator\.codex\AGENTS.md` so the user does not need to repeat these constraints in every prompt.
- Treat cross-session, cross-user, and cross-tenant data or state leakage as a zero-tolerance security defect and release blocker.
- Treat memory leaks as a zero-tolerance correctness and reliability defect and release blocker.
- Require lifecycle ownership and cleanup for relevant subscriptions, timers, background tasks, caches, collections, streams, handles, cancellation registrations, connections, and disposable resources.
- Require targeted tests, stress checks, or profiling when a change creates a credible isolation or memory-retention risk.
- Keep speed and memory-efficiency goals subordinate to security, isolation, correctness, cleanup, verification, and maintainability.
- Add the guidance outside the existing CCG-managed block and preserve all unrelated global and repository configuration.

## Acceptance Criteria

- [x] A concise global policy exists outside the CCG-managed block in `C:\Users\Administrator\.codex\AGENTS.md`.
- [x] The policy explicitly marks session leakage and memory leaks as zero-tolerance release blockers.
- [x] The policy covers prevention, review, and risk-based verification rather than only aspirational wording.
- [x] The policy states that performance optimization cannot trade away isolation, correctness, deterministic cleanup, verification, or maintainability.
- [x] Existing managed instructions and unrelated workspace changes remain untouched.

## Notes

- This is a lightweight instruction change; PRD-only planning is sufficient.
- A Skill is intentionally not the primary enforcement surface because Skills are loaded by task relevance, whereas global `AGENTS.md` guidance is automatically injected as a personal default.
- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
