# 審查報告：ChurchReport SaveIntegrate 背景上傳隔離修正

本報告針對目前工作樹中 **F1 ChurchReport SaveIntegrate 背景上傳隔離修正** 進行審查，特別聚焦於 `request scope`、`session`、`resource lifecycle` 與`跨使用者隔離`。

---

## VALIDATION REPORT
=================
* **User Experience**: 20/20 - 背景上傳隔離修正避免了 Request 結束後背景工作因 `ObjectDisposedException` 崩潰的問題。回應 `requiresRefresh = true` 提示前端重新整理以載入最新資料，UX 體驗良好且符合預期。
* **Visual Consistency**: 20/20 - 本次修正不涉及 UI 視覺變更，但程式碼結構與現有設計系統/架構一致。
* **Accessibility**: 20/20 - 本次修正不涉及 UI 變更，無 a11y 問題。
* **Performance**: 20/20 - 使用深拷貝快照與背景 scope，避免了 Session 鎖定與無界快取成長，效能優異。
* **Browser Compatibility**: 20/20 - 不涉及瀏覽器相容性問題，後端 C# 程式碼相容 .NET 8。

**TOTAL SCORE: 100/100**

**ISSUES FOUND:**
* 無（No critical or warning issues found.）

**RECOMMENDATION: PASS**

---

## 1. Summary (整體評估)
本次變更非常完整且嚴謹，完美解決了背景工作在 HTTP Request 結束後，因存取已釋放的 `RequestServices` 而導致的生命週期崩潰問題。透過 `AmbientGatewayOrganizationService` 的 `AsyncLocal` 路由機制，既保留了舊有 Factory 單例的相容性，又實現了背景 DI Scope 的隔離。同時，透過深拷貝快照與不回寫 Session 的設計，徹底消除了跨使用者資料洩漏與 Lost Update 的風險。

---

## 2. Accessibility Issues (無)
本次變更為純後端與架構層面的修正，不涉及 HTML/CSS/JS 等前端 UI 變更，因此無 Accessibility 相關問題。

---

## 3. Design Issues (無)
程式碼結構、命名規範與現有架構設計高度一致，無設計不一致問題。

---

## 4. Suggestions (改進建議)
* **[Info] 關於 `AmbientGatewayOrganizationService.Run` 的 Fallback Scope 效能**
  * **檔案與行號**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs` (第 205-206 行)
  * **根因**：當既無背景 Scope 也無 Request Scope 時，Fallback 機制會為每次 CRM 操作建立一個新的 DI Scope (`_scopeFactory.CreateScope()`)。
  * **影響**：若在非 Web/背景環境下頻繁呼叫，可能會因為頻繁建立與釋放 Scope 產生微小的效能開銷。
  * **建議**：此路徑為保底（Fallback）冷路徑，正常情況下不會觸及。若未來有大量非 Web 環境的序列呼叫需求，建議呼叫端顯式建立並傳遞 Scope，避免依賴 Fallback 機制。

---

## 5. Positive Notes (優秀設計與亮點)

### 1. 背景 CRM 呼叫生命週期隔離 (Lifecycle Isolation)
* **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs` (第 99-106 行)
* **亮點**：在 `Task.Run` 內部顯式建立背景 DI Scope (`scopeFactory.CreateScope()`)，並透過 `ToolUtilityFactory.BeginBackgroundScope` 將其註冊到 `AmbientGatewayOrganizationService` 的 `AsyncLocal` 中。這確保了背景工作內部的所有 CRM 呼叫都使用獨立的背景 Scope，絕不會在 Request 結束後存取已釋放的 `RequestServices`。

### 2. AsyncLocal Override 與 Trace 關聯保護
* **檔案**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs` (第 111-121 行)
* **亮點**：`BackgroundScopeOverride` 在 `Dispose` 時使用 `Interlocked.Exchange` 確保還原操作的冪等性。同時，`DataverseTrace.BeginBackgroundOperation` 建立了獨立的 `RequestContext` 與子 `traceId`（例如 `parentTraceId#bg1`），並在 `BackgroundScope.Dispose` 時還原。這利用了 `AsyncLocal` 的 copy-on-write 特性，確保平行與巢狀背景工作之間的統計與關聯完全隔離，且不污染父 request 的 `request.end`。

### 3. 閉包無狀態捕獲與跨使用者隔離 (Zero-State Closure Capture)
* **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs` (第 70-90 行)
* **亮點**：`Task.Run` 的閉包僅捕獲了不可變的字串（如 `account`、`password`）與深拷貝的快照 `backgroundCopy`，**沒有捕獲** `this` (Controller 實例)、`HttpContext` 或 `Session`。這完全消除了跨使用者/跨租戶資料洩漏的風險。

### 4. 完整深拷貝快照與 Lost Update 防護
* **檔案**：`SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs` (第 83-129 行)
* **亮點**：`CreateIsolatedSnapshot` 使用 `lock (_syncRoot)` 保護，並對 `Members` 列表進行了 `Select(member => new Member(member)).ToList()` 的深拷貝。背景工作完成後，只對快照進行清理，**不回寫**到 Session，並在回應中回傳 `requiresRefresh = true` 提示前端重新整理。這完全避免了 Lost Update 的風險。

### 5. 嚴謹的並行競態與靜態分析測試
* **檔案**：`ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs` (第 87-133 行)
* **亮點**：測試中確實使用 `Task.Run` 與 `ManualResetEventSlim` 注入了並行競態，驗證了背景工作修改快照時，前台列舉原集合不會拋出 `InvalidOperationException`，證明了快照的隔離性。
* **檔案**：`ToolUtility.Dataverse.Tests/SessionDiagnosticsSwitchTests.cs`
* **亮點**：透過靜態原始碼分析測試，確保 `SessionDiagnosticsSwitch.Enabled` 預設為 `false`，且所有 Session 診斷日誌呼叫點都被正確保護，避免敏感資訊外洩。
