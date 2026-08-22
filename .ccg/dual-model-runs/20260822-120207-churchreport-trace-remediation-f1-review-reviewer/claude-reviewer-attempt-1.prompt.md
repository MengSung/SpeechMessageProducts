ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: churchreport-trace-remediation-f1-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# F1 背景上傳狀態隔離程式審查

請審查目前 `git diff` 中 F1 的變更，範圍是：

- `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
- `SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs`
- `SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs`
- `SpeechMessageProducts.ChurchReport/Models/Member.cs`
- `ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs`

背景：三個 `Members` 集合的產品可執行使用點（包含 `?.Members`）為 44 個，超過計畫 30 處門檻。因此選擇唯讀退路：request 期間以短鎖建立深層副本，背景上傳和清理只改副本、不得回寫 Session／IMemoryCache，共用圖維持前景所有權，回應附加 `requiresRefresh=true`。不可建議整份回寫，因為約 14 秒的陳舊快照會覆蓋同期 CRUD。

請重點檢查：

1. `Task.Run` 是否沒有捕獲 Controller、`InMemoryContext`、`weeklyReportRef` 或共享 Members。
2. `IServiceScope` 與背景 trace scope 的所有權、例外與釋放是否正確；不得出現跨 request 資源或 session 泄漏。
3. `Member` 深拷貝是否涵蓋所有公開可變欄位，且不會保留父週報引用。
4. 背景週報副本是否只帶上傳需要的資料，並使用新的 uploader，沒有與前景共用可變模型或 CRM Entity 圖。
5. 回應相容性、敏感資訊日誌、測試的競態／隔離覆蓋。
6. 請以 `Critical`、`Warning`、`Info` 分級，標出檔案與行號；沒有問題也要明確說明。


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