ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: verify-claude-advice-report

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# 獨立查證：Claude 的 SaveIntegrate 發布建議

你是外部技術審查者。請以目前工作樹為唯一權威，獨立檢查下列論點；不要改檔案、不要執行會改變 CRM、Git 或遠端狀態的操作。不要因附件說法、先前 Codex 結論或程式碼註解而預設為真。

## 已取得的本機證據（請自行重讀完整控制流）

- `SmallGroupController.Save.cs` 的 `SaveIntegrate` 在 request 中建立 `CreateBackgroundUploadCopy()`，後以 `_ = Task.Run` 背景上傳，HTTP 回應為 `status=1`、`requiresRefresh=true`。
- `SmallGroupDataList.CreateIsolatedSnapshot()` 以私有 `_syncRoot` 保護讀取；repo 搜尋顯示 `SyncRoot` 無其他使用點。
- `SmallGroupData.UpdateMember()` 沒有鎖，透過 `JsonConvert.PopulateObject` 寫既有 `Member`；`UpdateSmallGroupPresentRecord()` 平行啟動兩個 `Task.Run` 更新兩個集合。
- `Member(Member source)` 對多個欄位逐一讀取；快照 deep copy 的既有測試通過，但未測試同一 `Member` 被前景寫入時的快照一致性。
- `InMemoryDataContextSmallGroup.ListManager` cache 未命中時 `new ListManager()` 後直接快取；`EnsureCorrectUserData()` 只在 session 與 ListManager 的 password 都非空且不相同時呼叫 `SetupListManager`。
- `DataverseTrace.BackgroundScope.Dispose()` 無條件寫 `bg.end`，欄位沒有 success/outcome；`verify_trace_invariants.py` 驗證 CRM 計數、bg 成對、租約與 NOSESSION，但不確認 CRM 資料業務結果。
- SaveIntegrate 的 outer catch 只記錄 `ex.GetType().Name` 並呼叫 `ToolUtilityClass.TraceByLevelStatic()`；此 static 方法寫 `System.Diagnostics.Trace`，而不是 `FileToolUtilityTracer` 的 `CHURCH_REPORT_TRACE.TXT`。
- 最新 `D:\除錯追蹤` trace 驗證為 11 pass、1 fail，因沒有 bg 事件；因此該 capture 沒有覆蓋 SaveIntegrate 背景路徑。
- `71b42c31` 是 `1.0.0.6.DesignNewArchitector` 的祖先，合併提交為 `ebd2af507`。
- 前端 `IntegrateView.cshtml` 沒有讀取 `response.requiresRefresh`；目前以延遲 `grid.refresh()` 處理。

## 請回答

1. 對下列主張逐一裁定「正確／部分正確／錯誤／證據不足」：
   - C3 可能靜默產生新舊欄位混合快照並上傳 CRM。
   - cache 逾期後會得到空白 ListManager，且通常不會透過 EnsureCorrectUserData 自動 CRM 重載。
   - C4 在 UploadData 之前失敗時，完整例外原因可能不會出現在 CHURCH_REPORT_TRACE.TXT。
   - `bg.end` 不是上傳成功證明。
   - 不應因功能已合併而重寫 feature branch 歷史。
2. 「新版嚴格優於目前生產版」是否可單獨當作正常發布通過的理由？請把漸進式風險改善與專案規範的正常發布門檻分開判斷。
3. 列出真正的 release blocker、可接受的 emergency hotfix 條件、以及上線後最高優先改善項目。
4. 指出以上本機證據或假設中任何不正確、過度推論、遺漏資料流或被測試誤導的地方。

## 輸出要求

繁體中文，先列 critical / warning / info，再給出可驗證的檔案、方法或命令證據。不要只重述題目；若不同意，明確說明反證。避免要求大規模重構，除非能證明是正常發布必要條件。


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