# Global Session and Memory Safety Guardrails

## Purpose

Add durable personal Codex guidance so session isolation and memory safety do not need to be repeated in every prompt.

## Chosen approach

Add one concise, unmanaged section to the global `C:\Users\Administrator\.codex\AGENTS.md`, after the existing CCG-managed block. Global `AGENTS.md` is the primary surface because it is loaded as personal guidance across repositories. A conditionally triggered Skill is not sufficient for zero-tolerance invariants.

## Required policy

The new section will establish these rules:

1. Cross-session, cross-user, and cross-tenant exposure or reuse of data, credentials, state, identifiers, caches, or resources is a zero-tolerance security defect and release blocker.
2. Known, reproducible, or credibly evidenced memory leaks are zero-tolerance correctness and reliability defects and release blockers.
3. Code that creates or retains subscriptions, timers, background tasks, caches, collections, streams, handles, buffers, or disposable resources must have explicit ownership, bounded lifetime where applicable, cancellation, unsubscription, eviction, or deterministic cleanup.
4. Changes with credible isolation or retention risk require targeted tests, stress checks, profiling, or equivalent evidence before completion.
5. Speed and memory efficiency are default engineering goals, but they may never weaken isolation, correctness, deterministic cleanup, or required verification.

## Scope boundaries

- Preserve the entire existing CCG-managed block without modification.
- Do not modify the repository's existing `.codex/config.toml` change.
- Do not claim that arbitrary software can be mathematically proven leak-free. Instead, prohibit shipping with any known, reproducible, or credibly evidenced leak and require risk-based verification.
- Do not add language- or framework-specific enforcement in this task.
- Do not create a Skill or Hook in this task; those can be added later as secondary enforcement mechanisms.

## Verification

1. Confirm the new text is outside the CCG-managed block.
2. Confirm both session leakage and memory leakage are explicitly marked zero tolerance and release blocking.
3. Confirm the policy covers prevention, review, and verification.
4. Confirm unrelated global and repository configuration remains unchanged.
5. Run the required high-risk dual-model review and resolve any Critical findings before completion.

## Rollback

Remove only the newly added unmanaged section from the global `AGENTS.md`. The CCG-managed block and repository configuration remain untouched.
