# UI 與前端/後端整合審查報告 (Gemini UI Reviewer)

本審查針對 LINE LIFF 登入流、奉獻收費清單（Donation Payment View）、奉獻稽核頁面（Dedication Fee Audit View）之安全性、Session 隔離、記憶體/資源管理及前端 UX/資安防護進行完整檢視。

---

## 審查總結 (Executive Summary)

- **總體評估**：整體變更品質優秀。LINE LIFF Token 驗證機制完整（包含 Issuer, Audience, Expiry, Subject 雙重驗證），個資與 Session 綁定隔離機制明確，無跨 Session 洩漏疑慮。後端 controller/context ownership、HttpClient 生命週期與 C# 異步資源處置皆符合最佳實務。
- **Critical 問題**：**無 Critical**（未發現導致系統崩潰、嚴重資安漏洞或死鎖之問題）。

---

## 1. 重點需求驗證

### (1) LINE LIFF 登入至奉獻收費清單資料流
- **ID Token 驗證** (`DedicationController.VerifyLineIdTokenAsync`)：
  - 完整驗證 LINE OAuth2 v2.1 Token API (`https://api.line.me/oauth2/v2.1/verify`)。
  - 正確比對 `Issuer` (`https://access.line.me`)、`Audience` (`ChannelId`)、`ExpiresAt` (> `UtcNow`) 以及 `Subject == expectedUserId`。
- **CRM Contact 與 Session 綁定**：
  - 以 `UserLineId` 正確查詢對應 CRM 聯絡人 (`ToolUtility.RetrieveContactByLineId`)。
  - Session 寫入 `DonationPaymentSessionKeys.LineUserId` 並清空 Web 登入 Session 鍵值 (`WebLoginContactId`)，防止混合驗證狀態混淆。
- **轉址控制**：
  - 前端 AJAX 收到 `status == 1` 時始執行 `window.location.href` 轉址至奉獻收費清單；`status != 1` 則停止轉址並顯示錯誤訊息。

### (2) Null 防禦與 Session/個資隔離
- **稽核頁與收費頁防禦** (`DedicationAuditController.cs`, `DedicationController.cs`)：
  - 當 `m_Contact` 或 Session 為 `null` 時，`BuildAuditWebFormModel` / `EnsureAuditFormModel` 確保回傳具預設值之 Model，不引發 `NullReferenceException`。
  - `FullName`, `Mobile`, `DedicationNumber`, `NationId`, `LastSixDigit`, `DedicationFeeList`, `SameNameList` 等個資欄位均於未登入/驗證失敗時明確清空，避免跨使用者歷史資料殘留。
  - 稽核頁 LineUserId 比對保護：當 Session 中 LINE User ID 與當前 Manager 狀態不符時，強制重新清理與綁定。

### (3) 記憶體與資源洩漏 (Memory & Resource Leakage Check)
- **HttpClient 釋放**：`VerifyLineIdTokenAsync` 使用 `IHttpClientFactory` 建立獨立 Client，`FormUrlEncodedContent` 與 `HttpResponseMessage` 均加上 `using var` 宣告，避免 socket / memory 洩漏。
- **Controller / Context Ownership**：`HomeController.SetupUserLineIdRedirect` 使用 `using (var controller = new DedicationController(...))` 確保包裝調用後控制器被正確 Dispose。
- **Timers / Subscriptions / Background Tasks**：無未釋放之計時器、背景任務或事件訂閱。
- **Cache**：`GetCachedClientIP` 與設定快取無無限增長 Key。

### (4) 前端防護與 Console 資安
- **狀態判斷轉址**：`DediationLineLoginView.cshtml` 之 JavaScript 明確檢查 `String(data.status) === "1"`，非 1 時呼叫 `getLoadPanelInstance().hide()` 並呈現 Toast 提醒，不進行轉址。
- **資安與 Console Log**：`requestLogData` 於送出前明確將 `IdToken` 遮蔽為 `"[REDACTED]"`，瀏覽器 Console 完全無明文 ID Token 印出紀錄。

### (5) 檔案格式與規範
- **格式規範**：修改之 C# 與 `.cshtml` 檔案皆為 UTF-8 without BOM、CRLF 換行符號、註解採標準繁體中文。

---

## 2. 審查發現事項 (Findings)

### Critical
**無 Critical**

---

### Warning

#### 1. 前端 DevExtreme / Toast 元件在連續點擊登入時可能重複彈出
- **檔案**：`SpeechMessageProducts.ChurchReport/Views/Dedication/DediationLineLoginView.cshtml` (Line 508-545)
- **說明**：在點擊 LINE 登入按鈕送出 AJAX 請求期間，雖然有顯示 LoadPanel，但若使用者因網路延遲重複觸發事件或 LoadPanel 隱藏後迅速重試，可能會重複建立 Toast 通知或發送重覆的 SetupUserLineId 請求。
- **修正建議**：可在 `SetupUserLineId` 發起前將按鈕設為 disable 狀態，直到 AJAX `complete` 或失敗後再解鎖。

---

### Info

#### 1. SessionKey 常量統一性與擴充性
- **檔案**：`SpeechMessageProducts.ChurchReport/Payments/DonationPaymentSessionKeys.cs` (Line 1-25)
- **說明**：`DonationPaymentSessionKeys` 結構設計清晰，將 `WebLoginContactId` 與 `LineUserId` 解耦。未來若有第三種登入管道（如 App Token），建議可延續此常數列舉模式。

#### 2. 單元測試案例涵蓋面完整
- **檔案**：`ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`
- **說明**：測試案例包含 `DonationPaymentFormModel` 預設值、Null 聯絡人處理、 Session 隔離測試，能有效確保未來修補時不會破壞此安全防禦機制。

---

## 3. UI Review Checklist 評分表

| 審查項目 | 評分 / 狀態 | 說明 |
| :--- | :---: | :--- |
| **Accessibility (無障礙與語意化)** | PASS | 使用標準 HTML5 / DevExtreme 語意結構與 ARIA 提示 |
| **Design Consistency (設計一致性)** | PASS | Toast 與 LoadPanel 遵循 DevExtreme 統一樣式 |
| **Code Quality (程式碼品質與型態)** | PASS | C# 異步處置完整，TypeScript/JS 邊界判斷與型態轉換嚴謹 |
| **Performance & Resource (效能與資源處置)** | PASS | HttpClient / FormContent 嚴格處置，無記憶體洩漏風險 |
| **Security & Privacy (資安與個資保護)** | PASS | Token 驗證規範完備，無 console 明文 ID Token， Session 隔離落實 |
| **Responsive (響應式設計)** | PASS | LIFF 視窗與流動排版適用行動裝置與 Desktop |

**RECOMMENDATION**: **PASS**
