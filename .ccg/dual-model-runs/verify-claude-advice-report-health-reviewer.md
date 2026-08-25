# CCG reviewer Task: verify-claude-advice-report-health

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# 第二次獨立審查：SaveIntegrate 發布判定

請只做唯讀審查、以目前程式碼為準，繁體中文輸出 Critical／Warning／Info。不要改檔案或 CRM。

請自行查證下列五點並各給「正確／部分正確／錯誤／證據不足」：

1. `SmallGroupDataList.CreateIsolatedSnapshot()` 用 `_syncRoot`，但 `SmallGroupData.UpdateMember()` 用 `JsonConvert.PopulateObject` 原地寫 `Member` 且不使用同一鎖；這是否可產生靜默混合欄位快照？
2. `InMemoryDataContextSmallGroup.ListManager` cache miss 會 `new ListManager()`；`EnsureCorrectUserData()` 是否會在新物件 password 為空時重載 CRM？
3. `SaveIntegrate` 背景 outer catch 是否只記例外型別，且 `TraceByLevelStatic` 是否不是 `CHURCH_REPORT_TRACE.TXT` writer；因此 pre-upload fault 是否缺完整可觀測性？
4. `DataverseTrace.BackgroundScope.Dispose()` 寫出的 `bg.end` 是否只代表 scope 結束，不代表 CRM 成功？
5. 新版較舊版安全，是否足以讓含已知靜默資料一致性風險的版本通過「正常發布」？請分開回答正常發布與緊急 hotfix。

限制：不得建議記錄 `ex.ToString()`、stack 或成員／帳密；請提出可安全記錄的 outcome/error-class/operation-id 方向。另請指出 `requiresRefresh=true` 是否被 `IntegrateView.cshtml` 實際消費。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.