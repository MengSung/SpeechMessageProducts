ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p7-remaining-work-rebaseline

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 remaining capability rebaseline review

Review only the local diff for `.trellis/tasks/08-12-p7-remaining-work-rebaseline/` plus required parent planning updates. This task is offline source analysis; it must not perform CE, network, deployment, or secret reads.

Required checks:
1. The 70-row matrix uses immutable archived P7.0 call-site identity and fail-closed hash/count validation.
2. Registry, Data8 executor, typed ProductClient, ChurchReport consumer, CE evidence, host evidence and rollout remain independent states.
3. Three explicit Package01 ChurchReport typed-client paths are `migrated-disabled`, not enabled or Dedicated-success; client-only operations stay `not-migrated`.
4. D-H local-only rows remain executor-rejected, not-consumer and no CE evidence.
5. Package02 multi-line constants are detected accurately.
6. No secrets, endpoint, identity, CRM payload, raw errors, CE/network access, session retention, or shared mutable state are introduced.
7. Tests cover the failure modes and artifacts are deterministic, UTF-8 no BOM, CRLF final CRLF.

Output Critical/Warning/Info findings. Do not request external state or make changes.

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
