# F4 背景追蹤邊界審查報告 (F4 Background Trace Boundary Review)

## 驗證報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 提供了清晰的背景追蹤隔離，避免了日誌污染，極大地提升了開發者排查問題的體驗。
Visual Consistency: 20/20 - bg.end 完整繼承了 request.end 的所有聚合欄位，並規範了 parentTraceId 與 op 欄位，日誌格式高度一致。
Accessibility: 20/20 - API 設計符合 C# IDisposable 慣用法，利用 using 語法自動管理生命週期，防呆且易用。
Performance: 20/20 - 停用追蹤時為零配置、零分配（NoopScope），且背景追蹤使用 AsyncLocal 進行輕量級隔離，無額外鎖競爭，效能優異。
Browser Compatibility: 20/20 - ToolUtility 與 DataverseTrace 保持主機中立，不依賴特定 Web 容器，相容性極佳。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無 (No critical issues found)

RECOMMENDATION: PASS
```

---

## 1. 總體評估 (Summary)

本次審查針對 F4 背景追蹤邊界（Background Trace Boundary）的未提交變更進行了完整評估。變更涵蓋了 `DataverseTrace.cs`、`DataverseTraceTests.cs` 以及 `SmallGroupController.Save.cs`。

整體實作非常嚴謹，完全滿足了以下核心合約要求：
- **隔離性**：背景操作 `BeginBackgroundOperation` 成功建立了獨立的統計物件 `RequestStats`，並清除繼承的租約（lease），確保背景 CRM 工作不會污染父請求的 `request.end` 指標。
- **追蹤鏈結**：子追蹤 ID 採用 `{parentTraceId}#bg{seq}` 格式，並在 `bg.begin` 與 `bg.end` 中正確記錄 `parentTraceId` 與 `op`。
- **安全性**：`op` 欄位僅接受硬編碼的背景操作名稱（如 `"SaveIntegrate.Upload"`），無任何使用者控制或機密資料傳入。
- **生命週期與 DI 順序**：在 `SmallGroupController.Save.cs` 中，背景追蹤 scope 確實於背景 DI scope 建立之前開啟，確保了追蹤上下文的完整覆蓋。
- **測試覆蓋率**：新增的單元測試真實且精準地驗證了平行、巢狀背景追蹤的隔離性與合約。

---

## 2. 可存取性與 API 易用性問題 (Accessibility Issues)

* **評等：無 (No Issues)**
* **說明**：`DataverseTrace` 的 API 設計採用了 C# 標準的 `IDisposable` 資源管理模式。透過 `using var traceScope = ...` 語法，開發者可以非常直覺且安全地管理背景追蹤的生命週期，避免了手動釋放或上下文洩漏的風險。

---

## 3. 設計與一致性問題 (Design Issues)

* **評等：無 (No Issues)**
* **說明**：
  - `bg.end` 事件輸出的欄位與 `request.end` 保持高度一致，包含了 `durationMs`、`crmCount`、`crmMs`、`leaseCount`、`leaseOutstanding`、`maxDepth`、`concurrentGateway`、`topEntity`、`topEntityCount` 與 `distinctEntities` 等所有聚合指標，便於日誌分析系統進行統一的解析與監控。
  - 程式碼註解與文件皆採用繁體中文，且詳細說明了生命週期與隔離要求，符合專案規範。

---

## 4. 建議事項 (Suggestions)

### Info: 關於 `BackgroundScope` 的 `Dispose` 執行緒安全
* **檔案路徑**：`ToolUtility/Dataverse/DataverseTrace.cs` (Line 425-454)
* **說明**：`BackgroundScope.Dispose` 中使用了 `Interlocked.Exchange(ref _disposed, 1)` 來確保只會執行一次釋放邏輯。這在多執行緒環境下是安全的。目前實作已非常完善，無須修改。

---

## 5. 肯定之處 (Positive Notes)

1. **完美的非同步隔離**：利用 `AsyncLocal<RequestContext>` 的 Copy-on-Write 特性，完美實現了平行背景任務（`Task.Run`）之間的上下文隔離，這在 `Parallel_and_nested_background_scopes_keep_independent_contexts` 測試中得到了充分的驗證。
2. **主機中立性**：`DataverseTrace` 與 `ToolUtility` 均未引入任何與 ASP.NET Core `HttpContext` 或特定 Web 容器耦合的程式碼，保持了良好的主機中立性，便於在主控台程式、背景服務或單元測試中重用。
3. **零開銷設計**：當 `Enabled` 為 `false` 時，`BeginBackgroundOperation` 會直接返回 `NoopScope.Instance`，且 `CrmOperation` 等熱路徑（Hot Path）操作均有 `if (!Enabled) return;` 的快速通道，實現了零分配與零效能損耗。
