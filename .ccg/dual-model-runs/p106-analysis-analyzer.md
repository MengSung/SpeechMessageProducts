# CCG analyzer Task: p106-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# 唯讀分析：P1-06 教會時鐘

Active task: .ccg/tasks/p106-church-clock

僅分析，不編輯、不執行外部服務、不讀 secrets 或設定憑證。依據 task requirements.md／plan.md、正式 02 P1-06／90 C.1、Localization csproj 與既有契約。
請檢查雙目標 UTC/local Kind、DST invalid/ambiguous fail closed、生日純日期、TimeProvider 借用、net10 ICU 啟動拒絕、CLDR 釘選與 deterministic generator 的漏項。
提出 Critical/Warning/Info 與可執行測試建議。不得把未知 VM／實機狀態當測試通過。
本次不需要 Gemini/Claude reviewer；此 analyzer 由現行執行層規則要求嘗試，供應商阻擋須明確保存，不冒稱雙模型完成。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
