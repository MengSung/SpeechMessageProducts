# CCG reviewer Task: p2-culture-timestamps

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
Active task: .trellis/tasks/11-07-p2-01-hosting
Read-only review. Do not modify files, read appsettings/User Secrets/credentials/environment values, call external services, or examine other worktrees.
Review only current changes in AGENTS.md, .trellis/spec/frontend/progress-page-contract.md, tools/RepoQuality/Tests/Test-ProgressPage.cjs and the DATA of 文件/規劃設計書/實作計畫-3.3/實作進度.html; plus tools/I18nCheck/{CultureCompletenessGenerator.cs,CommandRunner.cs}, tools/I18nCheck.Tests/CultureCompletenessTests.cs, SpeechMessage.Localization/CultureCompleteness.g.cs, SpeechMessage.Hosting/StartupGuards.cs, SpeechMessage.Hosting.Tests/StartupCulturePolicyTests.cs, SpeechMessageProducts.ChurchReport/Program.cs and ChurchReport.MemberInfo.Tests/Contracts/ChurchClockStartupTests.cs. Use git diff and read the named new files. Exclude unrelated .ccg P0 metadata.
Check secret-free errors, fail-closed parsing and generated completion, no fallback as translation, all resx groups counted, bounded ownership and no static state retention; Main diagnostics->ICU->locale policy->business Host order. Also preserve one P2 card and historical text, every entry visible date/time Asia/Taipei, explicitly label unknown historical event time as supplementation. Tests actual: tool179/179; Hosting Release70/70; ChurchReport Debug/Release90/90 each; Localization182 net48 host targeting net462 and209 net10; page18/18; file docs1265/0; I18n verify passed. No real deployment or network validation. P2 remains0/12 with R2/R3/R9 pending. Return Critical/Warning/Info; don't claim passing live acceptance.

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
