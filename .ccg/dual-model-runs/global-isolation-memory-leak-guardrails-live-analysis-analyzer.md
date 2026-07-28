# CCG analyzer Task: global-isolation-memory-leak-guardrails-live-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
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


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.