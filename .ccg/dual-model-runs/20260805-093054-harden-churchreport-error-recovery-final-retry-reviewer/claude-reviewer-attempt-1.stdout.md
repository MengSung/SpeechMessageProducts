## 審查結論（harden-churchreport-error-recovery-final-retry）

審查範圍：目前未提交變更 + commit `d47bb43f`，聚焦 6 個列管檔案。已實際 `dotnet build`（成功，0 警告 0 錯誤）並執行新增/既有回歸測試（`BaseChurchControllerErrorRecoveryTests`，5/5 通過）。

### Critical

**1. `AuthenticationController.LineBinding.cs:392-393` — AJAX JSON 仍直接回傳原始 CRM 例外訊息**
```csharp
private IActionResult HandleCrmServiceException(FaultException<OrganizationServiceFault> ex)
    => Json(new { status = "0", message = $"系統服務異常: {ex.Detail?.Message ?? ex.Message}" });
```
`/Authentication/ProcessLineBinding` 的 CRM Fault 分支會把 `OrganizationServiceFault` 的原始訊息（可能含 CRM 端點、欄位、內部型別資訊）直接序列化進瀏覽器可見的 JSON。這與本次任務明訂的契約「Browser responses, AJAX JSON... must not expose raw exception... CRM endpoint... data」直接牴觸。此行**未被本次 diff 修改**，但該檔案是本次任務明列的 6 個 in-scope 檔案之一，且正是這次強化工作要消除的同一類洩漏——`HandleError`/`ResolveSafeErrorMessage` 已經建立好白名單模式，這裡卻仍是舊的直通模式，形成一個未被涵蓋的殘留缺口，建議在同一輪修正中一併處理（改用固定 `errorCode`/安全訊息，原始 `ex.Detail` 僅記錄於伺服器端）。

### Warning

**2. 缺少「未知 `errorCode` fail-closed」的回歸測試**
`HomeController.cs` 新增的 `ResolveSafeErrorMessage`（`SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:790-799`）是本次白名單機制的核心防線：任意未列於 `switch` 的代碼一律回傳 `safeFallbackMessage`。但 `BaseChurchControllerErrorRecoveryTests.cs` 只新增了「已知代碼 `missing-liff-parameter` → 對應訊息」這一種情境（第 136-156 行），沒有任何測試對「未知 / 惡意 errorCode（例如任意字串或嘗試注入的 payload）」斷言其會 fail closed 到通用訊息。`errorCode` 是可由使用者透過 querystring 直接觸達的參數（`/Home/DisplayErrorView?errorCode=xxx`），這正是白名單防線實際會被攻擊面測試到的路徑，建議補一個 `DisplayErrorView_WhenErrorCodeIsUnrecognized_FallsBackToGenericMessage` 測試。

**3. `DonationContactCreationService.cs:83` 的 redirect 目標未明確指定 controller，與其他兩處修正不一致**
```csharp
return _redirectToAction("DisplayErrorView", new { errorCode = "contact-create-failed" });
```
`_redirectToAction` 綁定的是 `DonationPaymentManager.RedirectToAction`（`DonationPaymentManager` 本身繼承 `Controller`，見 `Models/DonationPaymentManager.cs:44`，於 `Models/InMemoryDataContextSmallGroup.cs:1216` 以 `new DonationPaymentManager(...)` 手動建構，未見任何 `ControllerContext` 賦值）。相較於同一批修正中 `AuthenticationController.LineBinding.cs:44` 與 `DedicationController.cs:748` 都明確傳入 `"Home"` 控制器名稱，這裡的 2 參數多載沒有指定 controller，導向目標依賴這個手動建立、非經 MVC action invocation 解析的 `ControllerContext`。此行為在本次 diff 前後相同（只是把 `ErrorMessage = exception.Message` 換成 `errorCode = ...`），非新增風險，但既然此檔案是本輪「MVC route compatibility」重點列管對象，建議至少加一個測試/手動驗證確認導向不會 404 或丟出例外。

### Info

**4. `HomeController.DisplayErrorView` 的 `ErrorMessage` 路由參數已成為死參數**
`HomeController.cs:750` 簽章仍保留 `string? ErrorMessage = null`，且 `[Route("/Home/DisplayErrorView/{*ErrorMessage}")]` 仍會綁定該值，但方法本體已完全不讀取它（改用 `errorCode` 白名單）。目前不構成安全風險（值從未被回顯），但屬於死程式碼，容易誤導後續維護者以為它仍會影響輸出，建議之後清理或加註說明。

**5. 安全訊息常數重複宣告**
`BaseChurchController.cs:318` 的 `safeUserMessage` 與 `HomeController.cs:756` 的 `safeFallbackMessage` 是同一字串「系統暫時無法完成操作，請稍後再試。」的兩份獨立宣告。commit message 已自陳「建議將安全訊息抽為常數並預留多語系支援」尚未落實，屬已知技術債，不影響本次安全契約。

### 未發現問題（已驗證符合契約）

- `BaseChurchController.Dispose()`（`BaseChurchController.cs:1261-1266`）已移除 `ToolUtility?.Dispose()`，不再釋放 Provider 擁有的共享實例；`Dispose_DoesNotDisposeProviderOwnedToolUtility` 測試通過驗證。
- `StoreSafeErrorMessage` / `DisplayErrorView` 對 `TempData` 存取都以 `try/catch` 包裹，寫入或讀取失敗只會降級為固定訊息，不會用第二個例外蓋掉原始錯誤處理結果；對應的兩個防禦性測試皆通過。
- `AuthenticationController.LineBinding.cs:44`、`DedicationController.cs:748` 的 LIFF 缺參情境已改為只傳固定 `errorCode`，不再把可能含使用者輸入拼接文字的訊息放進 redirect route。
- 新增/既有 5 項測試皆為 deterministic（無真實 CRM/計時器/共享靜態狀態），`dotnet test` 全數通過，非 flaky。

---

**關於「Required Recovery Behavior」**：本次審查是以此工作階段內建的檔案讀取／`git diff`／`dotnet build`／`dotnet test` 工具直接完成，並未呼叫外部的 CCG self-healing entrypoint 或 Gemini/Claude 雙後端腳本（此環境未提供該編排層的可呼叫介面，只能存取其歷史產物目錄 `.ccg/dual-model-runs`）。若需要嚴格依 CCG 雙模型/健康檢查/重試流程留存 artifacts，需由外層編排腳本（非本次對話可觸及）執行；以上為本次能在此環境內完成之範圍的完整、可驗證結果。

---
SESSION_ID: 366e65da-bb8d-4204-9dd5-7ccbc19f02f1
