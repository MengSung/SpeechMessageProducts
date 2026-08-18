# CCG reviewer Task: run2-toolutility-scoped-review-retry

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
請審查 Run 2 commit HEAD（refactor(toolutility): ToolUtilityClass 改為 request 範圍）。

檢查重點：request scope 與跨請求隔離、IOrganizationService 擁有權與 Dispose、Facade 子服務是否誤釋放共用連線、Factory legacy 路徑是否捕獲 scoped 依賴、DI ValidateScopes、測試覆蓋、繁中 XML 文件、UTF-8/CRLF，以及是否超出 Run 2 白名單。請執行必要的唯讀檢查，輸出 Critical/Warning/Info 分級結果；不要修改檔案。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.