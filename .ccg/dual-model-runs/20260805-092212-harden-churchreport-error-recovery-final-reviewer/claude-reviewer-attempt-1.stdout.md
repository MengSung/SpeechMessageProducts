## 最終審查報告：ChurchReport 錯誤復原與 CRM 生命週期硬化

**審查範圍**：commit `d47bb43f` + 目前未提交變更（`BaseChurchController.cs`、`HomeController.cs`、`AuthenticationController.LineBinding.cs`、`DedicationController.cs`、`DonationContactCreationService.cs`、`BaseChurchControllerErrorRecoveryTests.cs`）

### 核心契約驗證結果
- **ToolUtility 生命週期**：`ToolUtilityProvider` 在 DI 中註冊為 `AddSingleton`，`BaseChurchController.Dispose()` 已移除 `ToolUtility?.Dispose()`，正確把釋放權還給 Provider/Factory。✅
- **TempData 防禦**：`StoreSafeErrorMessage` / `DisplayErrorView` 的 `TempData` 存取皆包 `try-catch`，經追蹤程式碼路徑確認：即使 `HttpContext`/`TempData` 為 `null` 或 Provider 拋例外，也只會被內部 catch 吞掉，不會產生第二個 `NullReferenceException` 蓋掉原始錯誤。✅
- **錯誤訊息不外洩**：AJAX JSON、redirect route、view data 全面改用固定 `safeUserMessage`/`errorCode` 白名單，`ResolveSafeErrorMessage` 對未知代碼 fail closed。✅
- **測試有效性**：五個新測試（`GetUninitializedObject` 模擬未完全建構的 Controller、`ThrowingTempDataProvider` 模擬 TempData 失效）皆對應真實故障情境，非虛設斷言；已逐行核對 `_disposed`、`ConnectionPoolStats` 等反射/型別依賴皆存在，可正常編譯執行。✅

### Warning 🟡
1. **`DonationContactCreationService.cs:83`** — `_redirectToAction("DisplayErrorView", new { errorCode = "contact-create-failed" })` 綁定的是 `RedirectToAction(string, object)` 兩參數多載，未指定 controller。此委派實際由 `DonationPaymentManager`（繼承 `Controller` 但從未真正被路由）建立，經 `DedicationController.CreateContact` 呼叫鏈回傳給 MVC pipeline 執行；執行時 `RedirectToActionResult` 使用的是**當前實際請求**（Dedication）的 ambient route values，而非建立時的內容。因此 controller 為 `null` 會解析成 `"Dedication"`，組出 `/Dedication/DisplayErrorView`，但只有 `Home` controller 定義了該 action，最終導致 404，本次要保證的「安全錯誤頁」在此路徑其實沒有真正顯示給使用者。此為既有缺陷（改動前同樣少了 controller 名稱），本次修正未一併補上。

### Info 🟢
2. **測試覆蓋缺口** — `ResolveSafeErrorMessage` 的 fail-closed 分支只被「errorCode 為 null」間接測到，缺少「傳入未知/非法 errorCode 仍回傳通用訊息」的直接測試，建議補上。
3. **`DedicationController.cs:777`（`PaymentError`）** — 既有（非本次改動）端點直接把 `title/message/code/details` 等使用者可控 query 參數原封塞進 ViewBag 並渲染，與本次「不得回顯外部/例外內容」的精神不一致，值得後續一併處理，但不在本次 diff 範圍。
4. **`BaseChurchController.cs:319` / `HomeController.cs:754`** — 安全訊息常數重複宣告兩份，Gemini reviewer 已指出，建議抽成共用常數避免未來只改一處。

### 總結
本次修正正確解決了共享 `ToolUtility` 被 Controller 誤釋放、TempData 失效掩蓋原始例外、瀏覽器回應洩漏例外內容等核心問題，測試設計嚴謹且非虛假斷言。**唯一需要留意的 Warning** 是 `DonationContactCreationService` 的重導向委派仍缺 controller 名稱，導致「聯絡人建立失敗」這條路徑目前實際上是 404 而非顯示安全錯誤頁——建議在後續小改動中修正（例如改用三參數 `RedirectToAction(action, "Home", routeValues)` 委派）。其餘皆為 Info 等級、非阻斷性建議。

**判定：可合併，但建議另開小 PR 補上 `DonationContactCreationService.cs:83` 的 controller 名稱問題。**

---
SESSION_ID: 93b974e6-9e5d-488b-8172-8cd8490cc71a
