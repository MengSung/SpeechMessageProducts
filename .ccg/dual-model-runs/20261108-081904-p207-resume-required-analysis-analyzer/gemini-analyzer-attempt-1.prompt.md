ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: p207-resume-required-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
Active task: .trellis/tasks/11-07-p2-07-outbound
Analyze P2-07 only, read-only. Existing untracked CAS files are OutboundTaskRecord.cs, D365OutboundTaskStore.cs, OutboundTaskClaimTests.cs. Read task prd/design/implement and plan 3.4 P2-07. Propose minimal Hosting BackgroundService design and tests for 5s polling, <=50 claims, CAS/stale reclaim, retry 1/2/4/8/16 min for 429/5xx/timeout, terminal notification using real Exception, per-church isolated disposable scope, 30s shutdown/drain and no detached work. Reuse existing shared notifier and SDK lease contracts; do not change files, build, run commands, contact services, or read secrets. Identify Critical/High correctness/security gaps and exact source locations, distinguishing confirmed bugs from design choices. Output concise Traditional Chinese findings and implementation plan. No nested agents.


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