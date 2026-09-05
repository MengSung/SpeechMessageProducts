以下是針對本次變更（相對於 `HEAD`）的完整 UI / Backend Controller / Code Quality 審查報告。

---

# 審查總結 (Executive Summary)

* **評估結果**: **PASS / 建議合併 (Approved)**
* **整體評分**: **94/100**
* **重置崩潰問題修復**: **全面通過**
* **資料隔離與 Session 安全性**: **全面通過**
* **測試涵蓋率與品質**: **良好，具備真實行為驗證**

本次變更成功解決了從 Layout 導覽選單進入 `DedicationFeeAuditViewWeb` 時，因 Request-scoped `DonationPaymentManager.m_Contact` 為 `null` 導致的 `ArgumentNullException` 當機問題。同時確保了無登入或身分遺失時的表單模型完全清空，避免跨 Request / Session 資料洩漏。

---

# 詳細檢查項目報告

### 1. `DedicationFeeAuditViewWeb` Layout 導覽崩潰問題
* **狀態**: **已修復 (Passed)**
* **程式碼行號**: `Controllers/DedicationAuditController.cs:74-87`
* **分析**:
  * 原先程式碼在 View Action 中直接呼叫 `SetDedicationFeeList(m_Contact)`，若使用者從 Layout 主導覽直接點擊進入，Request-scoped `m_Contact` 為 `null`，會直接觸發 `ArgumentNullException`。
  * 修正後統一改為呼叫 `BuildAuditWebFormModel()`，先從 `InMemoryContext.PersonalInfomationModel?.m_LoginContact` 取得伺服器端登入身分；若無登入，則安全建構重置過後的 `DonationPaymentFormModel`，徹底消除 Layout 導覽崩潰隱患。

### 2. `BuildAuditWebFormModel` Null-Safe Fallback 與 Session 資料隔離
* **狀態**: **完全正確且安全 (Passed & Secure)**
* **程式碼行號**: `Controllers/DedicationAuditController.cs:134-156`
* **分析**:
  * 當 `loginContact == null` 時，`BuildAuditWebFormModel()` 執行了完整欄位重置邏輯：
    ```csharp
    model.FullName = string.Empty;
    model.Mobile = string.Empty;
    model.DedicationNumber = string.Empty;
    model.NationId = string.Empty;
    model.LastSixDigit = string.Empty;
    model.DedicationFeeList.Clear();
    model.SameNameList.Clear();
    model.TotalAmount = 0;
    ```
  * 包含姓名、手機、奉獻編號、身分證字號、卡號後六碼、奉獻清單、同名清單、總金額等 **8 項敏感個人資訊與計算欄位皆被明確清空**。
  * 由於 `DonationPaymentManager` 為 Request-scoped，此設計可防範在上一個 Request 或前一次操作遺留殘餘狀態時的資料遺洩問題。

### 3. Manager 表單模型在 AJAX / Grid 路由的 Null 處理
* **狀態**: **完全防護 (Passed)**
* **程式碼行號**:
  * `Controllers/DedicationAuditController.cs:166-174` (`EnsureAuditFormModel`)
  * `Controllers/DedicationAuditController.cs:211` (`LoadDedicationFeeList`)
  * `Controllers/DedicationAuditController.cs:241` (`LoadSameNameList`)
  * `Controllers/DedicationAuditController.cs:407` (`GetFeesByContactId`)
* **分析**:
  * 引入 `EnsureAuditFormModel` 輔助函式，當 `manager.m_DonationPaymentFormModel` 為 `null` 時自動實例化，並使用 `??=` 空值覆合運算子初始化 `DedicationFeeList` 與 `SameNameList` 為空 List。
  * 即使前端或第三方直接對 AJAX/Grid 端點發起請求，`DataSourceLoader.Load` 亦會收到空集合而正常傳回 JSON，不會引發 `NullReferenceException` 500 錯誤。

### 4. 單元測試品質與真實性 (Test Coverage & Authenticity)
* **狀態**: **通過（具備真實斷言，無 Tautology 虛假測試）**
* **程式碼行號**: `Tests/DonationPaymentViewDefaultsTests.cs:217-303`
* **分析**:
  * `Dedication_audit_web_form_without_login_contact_returns_isolated_blank_model` 測試：在 `manager` 設定帶有資料的舊模型，測試在 `m_LoginContact` 為 null 時執行 `BuildAuditWebFormModel()`，並嚴格斷言 8 個欄位均被重置為空/0。這證明了測試並非重複實作邏輯（tautology），而是真正測試邊界隔離。
  * `Dedication_audit_web_form_reassigns_new_default_model_to_manager` 測試：驗證 `manager.m_DonationPaymentFormModel` 被重新賦值與關聯。

### 5. 規範合規性 (Session Isolation / Memory / Local / Encoding / Performance)
* **狀態**: **符合規範**
* **分析**:
  * **Session 隔離**: 嚴格符合 Request-scoped 生命週期。
  * **記憶體/資源**: 無 Static 引用洩漏，List 重建/Clear 清晰。
  * **語系與註解**: 新撰寫之 XML 註解均為繁體中文（如 `建立網頁稽核頁所需的表單模型...`）。
  * **檔案編碼與換行**: 經檢視變更區塊，新加程式碼均維持 UTF-8 無 BOM 且採用 CRLF 換行。

---

# 審查發現分類與建議 (Findings & Recommendations)

### 🔴 Critical (嚴重問題)
> **無 (None)**

---

### 🟡 Warning (警告項目)

#### 1. 舊有檔案標頭註解亂碼問題
* **檔案與行號**:
  * `Controllers/DedicationAuditController.cs:2-12`
  * `Tests/DonationPaymentViewDefaultsTests.cs:2-12`
* **說明**: 檔案頂部的歷史註解區塊包含 Big5 / 錯誤編碼留下的亂碼字元（例如 `AI-蝜?銝剜?瑼?閮餉圾...`）。雖然這非本次變更所引起，且不影響功能編譯與執行，但建議在未來的程式碼清理中修復此標頭編碼。
* **建議**: 將歷史註解標頭轉碼為正確繁體中文或予以清理。

---

### 🔵 Info (提示與最佳化建議)

#### 1. Grid 單元測試之斷言強度強化
* **檔案與行號**: `Tests/DonationPaymentViewDefaultsTests.cs:280-303`
* **說明**: 目前 `Dedication_audit_fee_grid_returns_empty_data_when_form_model_is_missing` 與 `Dedication_audit_same_name_grid_returns_empty_data_when_form_model_is_missing` 使用 `action.Should().NotThrow()` 來驗證不會觸發 Exception。
* **建議修正範例**: 除了驗證 `NotThrow()` 外，可進一步斷言傳回值（`JsonResult` / `LoadResult`）中的 `data` 筆數為 `0`，使測試防護力更為堅固：
  ```csharp
  // 建議範例
  var result = action.Invoke() as JsonResult;
  result.Should().NotBeNull();
  ```

#### 2. 單元測試中使用 `RuntimeHelpers.GetUninitializedObject` 之維護注意事項
* **檔案與行號**: `Tests/DonationPaymentViewDefaultsTests.cs:222`, `265`, `282`, `297`
* **說明**: 測試中使用 `RuntimeHelpers.GetUninitializedObject(typeof(DedicationAuditController))` 來繞過 Controller 的建構子 (avoid DI/CRM connection overhead)。 Controller 內部在 `EnsureCorrectUserData()` 處理了 DEBUG 模式下 `HttpContext` 為 null 的情況，確保測試運作順暢。
* **提醒**: 這是單元測試切離外部依賴的有效做法，未來若 `DedicationAuditController` 建構子加入新的必填欄位或方法依賴，需注意此處未初始化的 Controller 物件行為。

---

# 最終建議與決策 (Final Decision)

**決策: Pass (通過)**

本次變更邏輯清晰、防護到位、兼顧個人隱私與 Session 隔離，且已補齊單元測試。無 Critical 級別隱患，建議可直接 Commit / Merge。
