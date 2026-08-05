## 審查結論：ChurchReport 錯誤復原與 ToolUtility 生命週期修正

已直接讀取三個變更檔案的完整 diff、追蹤 `IToolUtilityProvider` → `ToolUtilityProvider` → `ToolUtilityFactory`（process-wide static singleton）的擁有權鏈，並實際執行 `dotnet build` 與聚焦測試（4/4 passed，無新增警告），確認编码（UTF-8 no BOM、CRLF）與 `git diff --check` 主張屬實。

### Critical 🔴
無。核心契約（AJAX/錯誤頁不回顯 exception.Message、TempData 失效不覆蓋原始例外、Controller 不釋放 Provider 持有的共享 ToolUtility）皆已正確實作並有對應測試證實。

### Warning 🟡
- **`SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:750`**（行為破壞）
  `DisplayErrorView` 修正後完全忽略路由參數 `ErrorMessage`，只讀 `TempData["ErrorMessage"]`。但 `DedicationController.cs:747` 與 `AuthenticationController.LineBinding.cs:43` 目前都是用 `RedirectToAction("DisplayErrorView", "Home", new { ErrorMessage = "缺少 LIFF 參數，請從 LINE 入口開啟。" })` 傳遞**寫死、安全、可操作**的提示訊息（非例外文字）。這兩處呼叫從未寫入 TempData，因此修正後使用者一律只會看到通用訊息「系統暫時無法完成操作，請稍後再試。」，喪失原本明確可操作的提示。這是本次變更直接造成、且未被新增 4 個測試涵蓋的**功能性回歸**，雖然檔案不在宣告的變更範圍內，但成因就是本次 diff。
  建議：讓 `DisplayErrorView` 保留一個「僅限白名單/固定文字」的路由參數 fallback，或修改這兩個呼叫端改用 TempData 傳遞提示訊息。

- **`SpeechMessageProducts.ChurchReport/Services/DonationContactCreationService.cs:82`**（潛在資訊外洩，僅靠下游行為間接緩解）
  `return _redirectToAction("DisplayErrorView", new { ErrorMessage = exception.Message })` 仍把原始例外文字塞進轉址 URL。目前之所以不會外洩到畫面，完全是因為 `HomeController.DisplayErrorView` 這次改成忽略該參數——屬於**副作用式防護**，而非從源頭修正。302 的 `Location` header 與後續 GET 請求 URL 仍會帶著原始例外文字，殘留在伺服器存取記錄、代理記錄與瀏覽器歷史紀錄中。且若日後有人為了修復上述 LIFF 提示訊息回歸問題而讓 `DisplayErrorView` 重新採用 `ErrorMessage` 路由參數作為 TempData 為空時的 fallback（很自然的修法），這裡就會立刻恢復把例外文字回顯給瀏覽器。
  建議：此呼叫端也應同步改為傳遞固定安全訊息，不要依賴下游的忽略行為作為唯一防線。

### Info 🟢
- **`BaseChurchController.cs:319` 與 `HomeController.cs:754`**：安全訊息「系統暫時無法完成操作，請稍後再試。」以 `const string` 各自宣告一次，內容目前一致，但缺乏單一來源。建議抽成共用常數，避免未來只改到一處造成 AJAX／錯誤頁訊息不一致。
- `StoreSafeErrorMessage` 與 `DisplayErrorView` 對 TempData 例外採用 `catch (Exception ...)` 全捕捉，範圍稍寬（會吞下如 `OutOfMemoryException` 之類），但考量寫入/讀取的都是固定安全字串、且已用 `Debug.WriteLine` 記錄型別名稱以利除錯，屬可接受的防禦性寫法，非必須修正。
- `Dispose()` 移除 `ToolUtility?.Dispose()` 後已核實 `ToolUtility` 全鏈路（`_toolUtilityProvider.GetToolUtility()` → `ToolUtilityFactory.GetInstance()`）為 process-wide static singleton，Controller 不再釋放屬實且正確；專案中 `Tools/LineUtilityClass.cs:139` 與 `Tools/RecurringDonationPaymentProcessor.cs:104` 仍有 `m_ToolUtilityClass.Dispose()`（建構子注入），若注入來源同樣是共享 singleton，可能是同類問題，但屬本次變更範圍外，僅供後續追蹤參考。

### 測試涵蓋度
四個新測試對應到宣告的四個 RED 場景，斷言精準（如逐一驗證 payload 不含 `"internal CRM connection detail"`、以反射讀取 `_disposed` 旗標驗證 Provider 擁有權），且測試替身（Fake Provider/Pool/Context）均無外部資源、無跨測試共享狀態。實測 `dotnet test --filter BaseChurchControllerErrorRecoveryTests` 4/4 passed，建置無新增 warning，與回報的驗證結果一致。唯一缺口是上述 Warning 中提到的 `DisplayErrorView` 對既有安全路由訊息的回歸，未被涵蓋。

### 總結
本次三個變更檔案本身**沒有 Critical 問題**，核心安全與生命週期契約修正正確且有測試佐證。但 `HomeController.DisplayErrorView` 的修法方式（整段忽略 `ErrorMessage` 參數）在專案其他既有呼叫端造成了未被測試發現的行為回歸，且 `DonationContactCreationService.cs` 的既有洩漏路徑只是被動獲救而非根治。建議在合併前一併處理，或至少建立追蹤項目再合併。

---
SESSION_ID: c76b93b6-daf2-425c-ae3d-ab5d2bc62739
