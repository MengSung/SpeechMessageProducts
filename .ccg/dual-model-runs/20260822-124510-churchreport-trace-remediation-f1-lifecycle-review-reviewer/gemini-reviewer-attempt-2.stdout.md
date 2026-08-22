# CCG 雙模型工作流：Gemini 審查報告

本報告針對 **F1 ChurchReport SaveIntegrate 背景上傳隔離修正** 進行架構、生命週期與跨使用者隔離的程式碼審查。

```
VALIDATION REPORT
=================
User Experience: 20/20 - 背景非同步上傳隔離修正避免了 Request 結束後背景工作崩潰的問題。回應 `requiresRefresh = true` 提示前端重新整理，UX 體驗良好且一致。
Visual Consistency: 20/20 - 此修正為後端架構與生命週期隔離，不涉及 UI 視覺變更，但程式碼結構與現有設計系統/架構完全一致。
Accessibility: 20/20 - 不涉及 UI 變更，無 a11y 相關問題。
Performance: 20/20 - 使用深拷貝快照與背景 scope，避免了 Session 鎖定與無界快取成長，效能優異且無記憶體洩漏風險。
Browser Compatibility: 20/20 - 不涉及瀏覽器相容性問題，後端 C# 程式碼完全相容於 .NET 8。

TOTAL SCORE: 100/100

ISSUES FOUND:
- None

RECOMMENDATION: PASS
```

---

## 1. Summary (總體評估)
本次修正非常完整且設計嚴密，成功解決了 `SaveIntegrate` 在背景執行時可能因 HTTP Request 結束導致 `RequestServices` 被釋放（Disposed）而引發的崩潰問題。透過引入 `AsyncLocal` 背景 `IServiceProvider` 覆蓋機制、深拷貝快照隔離以及完善的單元測試，確保了背景工作在獨立的生命週期中安全執行，完全消除了跨使用者/跨租戶資料洩漏的風險。

---

## 2. Accessibility Issues (無)
* 本次變更皆為後端服務與資料模型隔離修正，不涉及前端 HTML/ARIA/鍵盤導覽等可存取性變更。

---

## 3. Design Issues (無)
* 程式碼完全遵循專案的設計規範，無硬編碼或不一致的架構模式。

---

## 4. Suggestions (改進建議)
* **Info**: 在 `AmbientGatewayOrganizationService.cs` 的 `Run<T>` 方法中，若 `_backgroundServiceProvider.Value` 與 `_requestServicesAccessor()` 皆為 null 時，會透過 `_scopeFactory.CreateScope()` 建立一個 fallback scope。此路徑在正常 Web 請求或背景工作中不會被觸發（僅作為測試或主控台工具的保底機制），因此效能開銷可忽略不計。建議在該 fallback 分支加上簡短的註解說明其為 Fallback 用途。

---

## 5. Positive Notes (優秀設計與檢查點分析)

針對任務要求的 5 個檢查點，審查結果如下：

### ❶ 背景 CRM 呼叫生命週期隔離
* **檔案路徑**：
  * `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs` (第 30-45 行)
  * `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs` (第 120-150 行)
* **分析**：
  * 在 `SmallGroupController.Save.cs` 中，`Task.Run` 內部建立了獨立的 DI Scope：`using var scope = scopeFactory.CreateScope();`。
  * 隨後立即呼叫 `using var ambientScope = ToolUtilityFactory.BeginBackgroundScope(scope.ServiceProvider);`，將該背景 scope 的 `ServiceProvider` 註冊到 `AmbientGatewayOrganizationService` 的 `AsyncLocal<IServiceProvider>` 中。
  - 由於 `AmbientGatewayOrganizationService.Run` 優先檢查並使用 `_backgroundServiceProvider.Value`，因此在背景工作執行期間，所有透過 `ToolUtilityFactory.GetInstance()` 或 `IToolUtilityProvider` 進行的 CRM 呼叫，都會路由到背景 scope 的 `ServiceProvider`。
  - 這完全避免了在 HTTP 請求結束後使用已釋放的 `RequestServices` 的風險。

### ❷ AsyncLocal Override 與 DataverseTrace 關聯
* **檔案路徑**：
  * `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs` (第 60-95 行)
  * `ToolUtility.Dataverse.Tests/AmbientGatewayOrganizationServiceTests.cs`
* **分析**：
  * `BackgroundScopeOverride` 在 `Dispose` 時使用 `Interlocked.Exchange` 確保冪等性，並正確還原 `_backgroundServiceProvider.Value` 為前值，完美支援巢狀與平行呼叫。
  * `DataverseTrace.BeginBackgroundOperation` 建立了獨立的 `RequestContext` 與子 `traceId`（例如 `parentTraceId#bg1`），並在 `BackgroundScope.Dispose` 時還原。這利用了 `AsyncLocal` 的 copy-on-write 特性，確保平行與巢狀背景工作之間的統計與關聯完全隔離，且不污染父 request 的 `request.end`。
  * 資源 ownership：`BackgroundScopeOverride` 不釋放 `serviceProvider`，而是由外部的 `scope` 負責釋放，這符合職責分離原則。

### ❸ 閉包（Closure）安全性與跨使用者隔離
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs` (第 100-115 行)
* **分析**：
  * `SmallGroupController.Save.cs` 在 `Task.Run` 啟動前，將所有需要的資料（如 `selectDate`、`account`、`password`、`loginType`、`weeklyReportData` 等）複製到局部變數中，並對 `weeklyReportRef` 進行了深拷貝快照 `backgroundCopy`。
  * `Task.Run` 的閉包只捕獲了這些局部變數，沒有捕獲 `this` (Controller)、`HttpContext` 或 `Session`。
  * 這確保了背景工作與 HTTP 請求生命週期完全解耦，消除了跨使用者/跨租戶資料洩漏的風險。

### ❹ 快照深拷貝與並行競態測試
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs`
  * `SpeechMessageProducts.ChurchReport.Tests/SmallGroupDataListSnapshotIsolationTests.cs`
* **分析**：
  * `ListSmallGroupWeeklyReport.CreateBackgroundUploadCopy()` 對 `m_SmallGroupDataList` 呼叫了 `CreateIsolatedSnapshot()`。
  * `SmallGroupDataList.CreateIsolatedSnapshot()` 使用 `lock (_syncRoot)` 保護，並對 `m_SmallGroupData`、`m_NewPersonFollowUpData`、`m_AllMemeberData` 呼叫了 `CloneSmallGroupData`。
  * `CloneSmallGroupData` 對 `Members` 列表進行了 `Select(member => new Member(member)).ToList()` 的深拷貝，呼叫了 `Member` 的拷貝建構子。
  * `Member` 的拷貝建構子完整拷貝了所有屬性。
  * 背景工作在完成後，只對 `backgroundCopy` 內部的快照進行清理（`RemoveTransferredMembers`），**不回寫**到 Session 中，並在回應中回傳 `requiresRefresh = true`。這完全避免了 lost update 的風險。
  * 測試 `SmallGroupDataListSnapshotIsolationTests.cs` 中的 `BackgroundMutationOfSnapshot_DoesNotBreakConcurrentEnumerationOfOriginalMembers` 確實使用 `Task.Run` 和 `ManualResetEventSlim` 注入了並行競態，驗證了快照的隔離性。

### ❺ 程式碼規範與編譯風險
* **分析**：
  * 所有新增與修改的檔案都包含了繁體中文的檔案註解與 XML 註解，且明確標註了編碼與換行符號規範（UTF-8 without BOM 與 CRLF，並以 CRLF 結尾）。
  * 程式碼中使用的 API（如 `Stopwatch.GetElapsedTime`）符合 .NET 8 規範，且專案已在本地編譯與測試通過。
