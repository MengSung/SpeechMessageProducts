# CCG analyzer Task: p2-progress-phase-summary

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
請只讀分析：使用者要求實作進度.html同階段一般資訊合併，P2兩張info合成一張穩定id與phaseId的摘要，保留所有原證據於收合steps與歷史，P1歷史與warn/error獨立。只修改HTML DATA、tools/RepoQuality/Tests/Test-ProgressPage.cjs與frontend progress-page-contract.md。修正測試綁定P1 branch／全todo／目前階段固定P1的過時快照，但必須保留83包、53計畫、逐包規格marker與實機授權證據。請指出最小安全改動及回歸風險；不可修改檔案，不讀任何appsettings或秘密。

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
