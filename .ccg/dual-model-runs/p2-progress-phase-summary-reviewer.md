# CCG reviewer Task: p2-progress-phase-summary

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
請只讀審查本次進度頁更動，不得修改檔案或讀取appsettings／秘密。僅檢查 git diff -- tools/RepoQuality/Tests/Test-ProgressPage.cjs .trellis/spec/frontend/progress-page-contract.md 文件/規劃設計書/實作計畫-3.3/實作進度.html。使用者要求同階段一般資訊只一張，P2兩卡已合成phase-2-summary，steps保留原歷史，P1 alert未變，warn/error仍獨立。原3個P1固定快照斷言已替換為動態目前階段與計數一致性，加合成分支及四狀態切換，不刪83包、53項逐包規格、實機授權與清理證據。真實P2-01補回ExceptionOutputOptions明確名稱，最新Hosting6通過4失敗仍未完成。node進度頁17/17、檔案說明1257檔0失敗，主會話會另做結構與位元組檢查。檢查是否遺失證據、放寬驗收、HTML注入或範圍擴張，輸出Critical/Warning/Info。

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.