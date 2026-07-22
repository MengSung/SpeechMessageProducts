# Global Session Isolation and Memory Leak Guardrails

## Goal

Stop requiring repeated per-prompt reminders by adding durable personal Codex guidance.

## Mandatory invariants

1. Cross-session, cross-user, and cross-tenant leakage is zero tolerance and a security release blocker.
2. Memory leaks are zero tolerance and a correctness/reliability release blocker.
3. Performance and memory-efficiency work must never weaken isolation, correctness, cleanup, or verification.

## Intended surface

- Put the durable personal defaults in the global `C:\Users\Administrator\.codex\AGENTS.md`.
- Add content outside the existing CCG-managed block.
- Do not depend on a conditionally triggered Skill for these mandatory invariants.

## Verification intent

- Require explicit ownership and cleanup for subscriptions, timers, background tasks, caches, collections, streams, handles, and disposable resources where relevant.
- Treat known or reproducible isolation or memory leaks as blockers rather than deferred optimization work.
- Require targeted tests, stress checks, or profiling when the changed code creates a credible leakage risk.
