# Live Dual-Model Analysis Validation

Analyze the current planning specification for the task `global-isolation-memory-leak-guardrails`.

Read these files from the repository:

- `.ccg/tasks/global-isolation-memory-leak-guardrails/task.json`
- `.ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md`
- `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/task.json`
- `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/prd.md`
- `AGENTS.md`
- `.trellis/spec/guides/ccg-external-review-thinking-guide.md`

Objectives:

1. Determine whether the proposed global Codex guidance is the correct durable enforcement surface for zero-tolerance cross-session, cross-user, cross-tenant, and memory-leak guardrails.
2. Identify missing requirements, ambiguous wording, precedence hazards, managed-block risks, verification gaps, or unintended effects on ordinary development work.
3. Confirm that performance and memory-efficiency guidance is subordinate to isolation, correctness, deterministic cleanup, and verification.
4. Keep the task in planning. Do not edit files, start implementation, or execute destructive commands.

Output a substantive analysis with these sections:

- Verdict
- Confirmed strengths
- Critical issues
- Warnings
- Recommended planning changes
- Acceptance readiness

Explicitly state whether your backend completed the analysis with a usable final report.
