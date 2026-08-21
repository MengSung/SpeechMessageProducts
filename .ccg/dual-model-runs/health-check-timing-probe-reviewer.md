# CCG reviewer Task: health-check-timing-probe

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# 健康檢查耗時觀測點審查

請只審查目前 git diff 中的三個 C# 檔案：

- `ToolUtility/Dataverse/DataverseTrace.cs`
- `ToolUtility/Dataverse/BoundedClientPool.cs`
- `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs`

任務契約：為既有 `pool.health` JSONL event **只新增** `ms` 欄位，以 `Stopwatch` 包住既有 `_healthCheck` 委派，成功與失敗都記錄耗時；不得改變 pool 行為、健康檢查時機、ensureMin、建線、資源生命週期或現有欄位語意。trace disabled 時不可新增 Stopwatch 成本，且不能記錄 CRM 回應、使用者、身分、tenant 或認證資料。只新增 observation，沒有任何效能最佳化。

請依 Critical / Warning / Info 分級，並逐項核對：

1. 成功、false 回傳與委派拋例外三條路徑的 elapsed 是否正確。
2. trace disabled 是否零新增量測成本，且 trace schema 是否只加欄位。
3. 是否引入 session、tenant、credential、CRM response 或資源生命週期風險。
4. 測試是否真的驗證成功和失敗健康檢查均有正 `ms`，且避免測量精度造成非決定性。
5. 註解是否準確且不過度宣稱。

本回合的使用者硬性指示：審查後不可自行修正任何新發現；僅輸出報告。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.