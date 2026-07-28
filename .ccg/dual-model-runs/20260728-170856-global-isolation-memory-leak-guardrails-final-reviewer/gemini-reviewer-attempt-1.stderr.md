[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: global-isolation-memory-leak-guardrails-final

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
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
  PID: 43776
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-43776.log
