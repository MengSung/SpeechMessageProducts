# CCG reviewer Task: p2-hosting-solution-registration

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
READ-ONLY narrow review. Patch only adds SpeechMessage.Hosting and SpeechMessage.Hosting.Tests to SpeechMessageProducts.sln using dotnet sln add. Original 30 entries preserved unchanged, now32, both new entries exactly once, Debug/Release ActiveCfg and Build.0 present. Hosting.Tests Release build --no-restore has 0 warnings/0 errors. Full Hosting feature tests NOT passing as baseline; do not claim P2-01 or P2 complete. Inspect git diff -- SpeechMessageProducts.sln and the two project files ONLY if needed. Do not read configs, secrets, credentials or unrelated files; do not change any file. Report Critical/Warning/Info for solution integration patch only, including config mapping issues or incorrect framework dependency if evidenced.

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.