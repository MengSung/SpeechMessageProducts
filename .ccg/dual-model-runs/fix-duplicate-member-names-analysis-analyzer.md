# CCG analyzer Task: fix-duplicate-member-names-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.8.FixDuplicateName.Worktree

## Request
# 角色

請以資深 ASP.NET Core/.NET 10 架構與效能審查者身分，分析目前分支的小組回報重複姓名問題。

# 目前已確認的程式事實

- `SpeechMessageProducts.ChurchReport/Models/ListManager.cs` 的 `SetupIntegrateData` 會直接修改共享 `m_ListSmallGroupWeeklyReport`，並重用欄位 `m_DownloadIntegrateData`。
- `DownloadIntegrateData.SetupHeaderData` 在其他 CRM 載入完成前設定 `LoadFlag = true`。
- `LoadIntegrate`、`LoadNewPersonFollowUp`、圖表、日期切換與 LINE 登入會讀寫同一 ListManager。
- LINE 登入以 `Task.Run` + `Task.WhenAll` 同時操作同一 `InMemoryContext`。
- UI row key 為 `PresentRecordId`；合法同名人員不可按姓名合併。

# 擬採設計

1. ListManager instance 擁有一個 SemaphoreSlim；該 instance 已由 Session 隔離的 holder/cache 擁有，不新增 static keyed dictionary。
2. gate 內依完整載入 key 重新檢查；每次使用新的 DownloadIntegrateData 與 candidate report，全部完成、驗證 row key 後才發布。
3. 讀取 API 取得深複製 detached snapshot 後才交給 DataSourceLoader。
4. exact duplicate `PresentRecordId` fail closed；同 FullName 不同 key 全部保留。
5. LINE 登入移除同一 request state 的平行 Task.Run，按依賴順序執行。

# 請輸出

請分 Critical / Warning / Info，特別檢查：session/cross-user leakage、memory/resource leakage、Semaphore/Task/取消生命週期、同步 CRM I/O、deadlock、scope key 完整性、失敗後重試、效能配置量與測試缺口。不得建議以姓名 Distinct 掩蓋問題。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.