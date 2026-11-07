ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: p2-01-resume

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
Active task: .trellis/tasks/11-07-p2-01-hosting
Analyze the P2-01 Hosting implementation and required architecture against the exact formal plan in 文件/規劃設計書/實作計畫-3.3/03_階段2-4_共用送訊_Webhook_金流.md, P2-01 only. Read AGENTS.md and applicable specs. READ ONLY. Do not edit, spawn agents, run services, read appsettings values, User Secrets, credentials, logs containing personal data, or connect D365/LINE. Do not quote any secret even if accidentally present in source.
Current uncommitted work: SpeechMessage.Hosting and SpeechMessage.Hosting.Tests, Directory.Packages.props. Existing ToolUtility/Diagnostics owner is retained; existing ChurchReport Program/sender/logger/middleware not yet integrated. Inspect lifecycle issues (sync DI Dispose with IAsyncDisposable, sender drain/dispose order, singleton ownership, duplicate registrations, attach failure, token clearance), test fixtures and Production/Development localization completeness. Identify exact gaps versus move sender/options requirements and explain a no-cycle architecture that satisfies them without a second owner. Need R2/R3/R9 architecture tests, source scanning without skipping legacy behavior silently. Existing ChurchReport is not a thin host; explicitly account migration-only exceptions rather than weakening checks. No coding. Output concise Traditional Chinese Critical/Warning/Info report, recommended next red tests and integration order. Restrict reads to named code/spec/plan and required non-secret dependency source; never read deployment configuration values.


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