ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p7-post-runtime-health-reconciliation

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 post-runtime-health matrix reconciliation analysis

Review this repository-only planning task. We need to run the fixed archived
`build_rebaseline.py` directly into a new task-owned output after the local-only
`runtime.health.whoami` ProductClient implementation was committed and archived.

Determine whether this approach preserves the canonical 70-row matrix, keeps historical
P7.2 Slice C no-go closed, and avoids false promotion of consumer/CE/host/rollout/P7.5/P8.
Identify any source-derived caveats when selecting the next independent P7 capability.

Strict scope: no CE, network, credentials, user/profile/Owner selection, fixtures, consumer
wiring, feature-gate/traffic change, ToolUtility removal, P7.5, or P8. Output only concrete
Critical/Warning/Info findings based on source. Do not suggest replaying historical Slice C.


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