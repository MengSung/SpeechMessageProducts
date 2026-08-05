# ChurchReport 錯誤復原與 CRM 生命週期加固審查報告

本審查針對 `SpeechMessageProducts.ChurchReport` 專案中，關於共享資源生命週期管理、例外處理安全性、以及 `TempData` 容錯機制的修正進行評估。審查範圍涵蓋 `BaseChurchController`、`HomeController`、`AuthenticationController`、`DedicationController`、`DonationContactCreationService` 以及相關單元測試。

---

## 審查結論摘要

本次提交的修正與 commit `d47bb43f` 完整解決了核心生命週期與資安缺陷：
1. **生命週期擁有權導正**：移除了 `BaseChurchController.Dispose()` 中對共享單例 `ToolUtility` 的釋放呼叫，避免了單一 Request 結束導致全域 CRM 連線癱瘓的 Use-After-Dispose 錯誤。
2. **防禦性例外處理**：在 `BaseChurchController.HandleError` 與 `HomeController.DisplayErrorView` 中對 `TempData` 存取實施了 `try-catch` 保護，確保在測試環境或背景管線中 `TempData` 不可用時，不會因二次 `NullReferenceException` 遮蔽原始 CRM 異常。
3. **敏感資訊防洩漏**：AJAX 回應與錯誤頁面重導向皆不再傳遞或回顯原始的 `exception.Message`，改由伺服器端白名單錯誤代碼（`errorCode`）進行安全對照，防止 CRM 連線字串、內部實體結構或憑證洩漏至瀏覽器端。
4. **測試有效性**：新增的 5 個單元測試精確覆蓋了上述邊界條件，且使用無副作用的測試替身，無跨測試共享狀態或資源洩漏風險。

---

## 詳細審查發現

### Critical 🔴
* **無**。核心契約（不越權釋放單例、不遮蔽原始例外、不外洩敏感資訊）皆已正確實作。

---

### Warning 🟡

#### 1. 跨控制器重導向路由遺失控制器名稱導致 404 錯誤
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationContactCreationService.cs` (第 83 行)
* **程式碼片段**：
  ```csharp
  return _redirectToAction("DisplayErrorView", new { errorCode = "contact-create-failed" });
  ```
* **風險說明**：
  在 `DonationPaymentManager.cs` 中初始化 `DonationContactCreationService` 時，傳入的 `_redirectToAction` 委派為 `DonationPaymentManager` 控制器自身的 `RedirectToAction` 方法。
  當 `DonationContactCreationService` 發生異常並執行上述重導向時，由於未指定控制器名稱，ASP.NET Core MVC 預設會導向當前控制器（即 `DonationPaymentManager/DisplayErrorView`）。然而，`DonationPaymentManager` 並未實作 `DisplayErrorView` Action，這將導致使用者端收到 **HTTP 404 Not Found** 錯誤，而非預期的安全錯誤提示頁面。
* **建議修正**：
  應修改 `DonationPaymentManager.cs` 中傳遞給該服務的委派，或在服務內改用接受三個參數的重載以明確指定目標控制器為 `"Home"`：
  ```csharp
  // 建議在呼叫端或委派定義中確保導向 "Home" 控制器
  return _redirectToAction("DisplayErrorView", "Home", new { errorCode = "contact-create-failed" });
  ```

---

### Info 🟢

#### 1. 安全錯誤提示訊息重複定義
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs` (第 319 行)
  * `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs` (第 754 行)
* **說明**：
  安全錯誤提示字串 `"系統暫時無法完成操作，請稍後再試。"` 在上述兩處分別被宣告為 `const string safeUserMessage` 與 `safeFallbackMessage`。雖然目前內容一致，但未來若需調整文字或進行多國語言語系（Localization）支援時，容易產生不一致。建議將此字串收攏至全域常數類別或資源檔中。

#### 2. 舊有路由參數相容性保留
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs` (第 749-750 行)
* **說明**：
  `DisplayErrorView` 保留了 `[Route("/Home/DisplayErrorView/{*ErrorMessage}")]` 路由與 `string? ErrorMessage` 參數以維持向後相容性。實作中已透過「僅讀取 `TempData` 與受控 `errorCode`，不回顯 `ErrorMessage` 內容」的方式進行安全隔離，此防禦性設計符合安全規範。

---

## 測試覆蓋度評估

新增的單元測試類別 `BaseChurchControllerErrorRecoveryTests` 設計嚴謹：
* **`HandleError_WhenTempDataIsUnavailable_ReturnsErrorRedirectWithoutThrowing`**：驗證無 `TempData` 基礎設施時不崩潰。
* **`HandleError_WhenAjaxRequest_DoesNotExposeTheOriginalExceptionMessage`**：驗證 AJAX 請求僅回傳安全訊息。
* **`Dispose_DoesNotDisposeProviderOwnedToolUtility`**：利用反射讀取私有欄位 `_disposed`，精確驗證 Controller 釋放時不影響共享單例。
* **`DisplayErrorView_WhenTempDataProviderFails_UsesSafeFallbackMessage`**：透過自訂 `ThrowingTempDataProvider` 模擬 TempData 載入失敗，驗證錯誤頁面的安全降級。
* **`DisplayErrorView_WhenRecognizedErrorCodeIsProvided_UsesMappedSafeMessage`**：驗證白名單錯誤代碼的對照機制。

測試執行結果為 **5/5 Passed**，無新增編譯警告，編碼格式（UTF-8 no BOM, CRLF）亦符合規範。
