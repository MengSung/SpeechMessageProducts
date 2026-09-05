# LINE 奉獻收費清單登入與 HttpClient 生命週期 程式碼審查報告

本報告方針對工作樹相對於 HEAD 的變更（包含 `Startup.cs` 之 HttpClient 註冊、`DedicationController` 的 LINE LIFF ID Token 驗證流程、`AuthenticationController.LineLoginOAuth`、`DedicationAuditController`、`DonationDedicationFeeFormService` 與相關單元測試）進行完整的架構、安全性、資源生命週期與 UX/UI 資料流審查。

---

## 綜合審查評估 (Summary)

* **審查結論**：**PASS / 通過**
* **整體品質評估**：本次變更修復與架構重構嚴謹，成功落實 `IHttpClientFactory` 管理 HTTP 連線與 Socket 生命週期，並在 LINE LIFF ID Token 驗證層、CRM Contact 關聯與 Session 表單模型層建立了嚴密的多層 Fail-Closed 機制與資料清空隔離機制，確保個資與跨租戶/跨 Session 資料不洩漏。

---

## 核心審查要點逐項分析

### 1. `Startup.cs` `LineLoginApi` HttpClient 註冊與生命週期
* **驗證結果**：**[PASS] 完全正確與規範一致**
* **檔案與行號**：`SpeechMessageProducts.ChurchReport/Startup.cs` (Line 227–232)
* **分析依據**：
  - **有界 Timeout**：明確設定 `client.Timeout = TimeSpan.FromSeconds(10)`，防止遠端 LINE API 延遲或網路異常時無限期掛起 HTTP Request 執行緒。
  - **BaseAddress 設值與相對路徑**：基底位址設定為 `new Uri("https://api.line.me/")`。經查呼叫端：
    - `DedicationController.cs:1064` 傳入 `"oauth2/v2.1/verify"`（無前導斜線，組合為 `https://api.line.me/oauth2/v2.1/verify`）。
    - `AuthenticationController.LineLoginOAuth.cs:424` 傳入 `"oauth2/v2.1/token"`。
    - `AuthenticationController.LineLoginOAuth.cs:468` 傳入 `"v2/profile"`。
    相對端點組合邏輯完全正確。
  - **`IHttpClientFactory` 使用一致性**：呼叫端透過 `factory.CreateClient("LineLoginApi")` 取得 transient `HttpClient` 實例，未將其儲存於 `static` 或單例欄位中，交由 `HttpClientFactory` 自動管理底層 `HttpMessageHandler` 集區與 Socket 輪替，完全消除 TIME_WAIT 與 Socket Exhaustion 風險。

---

### 2. LINE LIFF ID Token 驗證與奉獻收費清單跨使用者/Session 隔離
* **驗證結果**：**[PASS] 無資料洩漏風險，Fail-Closed 機制完備**
* **檔案與行號**：
  - `SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs` (Line 963–1083)
  - `SpeechMessageProducts.ChurchReport/Services/DonationDedicationFeeFormService.cs` (Line 40–95, Line 157–177)
* **分析依據**：
  - **身分權限驗證 (VerifyLineIdTokenAsync)**：
    1. 前端傳入之 `UserLineId` 先經過 `IsValidLineUserId` 格式檢驗。
    2. 經由 `VerifyLineIdTokenAsync` 向 LINE 官方 API (`oauth2/v2.1/verify`) 驗證 `id_token` 的真實性。
    3. 嚴格比對 Token 的三要素：`Issuer == "https://access.line.me"`、`Audience == channelId`（確定租戶/頻道）、`Subject == expectedUserId`（確定使用者身分相符），並檢查 `ExpiresAt > UtcNow`。
    4. 若上述任一驗證失敗，立即觸發 `ClearLineDonationState` 並回傳失敗 JSON，防止未授權或偽造 LINE ID 的請求存取奉獻資料。
  - **Missing Contact 的清空與隔離 (Fail-Closed)**：
    `DonationDedicationFeeFormService.FillFromLineId` 當 CRM 查無 `lineLoginContact` 時，呼叫 `ClearModelForMissingContact`，顯式重置 `FullName`、`Mobile`、`DedicationNumber`、`NationId`、`LastSixDigit` 為 `string.Empty`，並將 `DedicationFeeList` 與 `SameNameList` 執行 `Clear()`、`TotalAmount` 歸零。此舉徹底避免了同一個 Session 切換帳號時殘留上一位使用者個資的風險。

---

### 3. Resource Leakage 檢查 (Memory, Socket, Stream, Timer, Task, Cancellation)
* **驗證結果**：**[PASS] 資源釋放確定且無洩漏**
* **分析依據**：
  - **Socket / Connection Leakage**：一律由 `IHttpClientFactory` 統一池化管理。
  - **Stream & HttpContent 釋放**：
    - `DedicationController.cs:1058-1065`：`FormUrlEncodedContent` 與 `HttpResponseMessage` 均加上 `using var` 宣告，確定性 (deterministic) 釋放 Stream 與 Socket 資源。
    - `AuthenticationController.LineLoginOAuth.cs:415-424, 468-471`：`requestData`、`request` 與 `response` 均有 `using var` 妥善管理。
  - **Cancellation 傳遞**：
    `SetupUserLineId` 與 `VerifyLineIdTokenAsync` 均正確接收並向下傳遞 `CancellationToken`，當 Client 端取消 HTTP 請求時能即時中斷非同步 I/O 運算。
  - **Task 與 ThreadPool 濫用**：
    註解與程式碼已將先前的非同步混亂修正為直接在同一個 Request pipeline async/await 處理，未發現懸空 `Task.Run` 或未監聽之背景 Timer。

---

### 4. 相關 Controller、Model、Service 與 Razor/Tests 正確性與回歸風險
* **驗證結果**：**[PASS] 邏輯完整，防衛強化**
* **檔案與行號**：
  - `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 74–87, 134–174)
  - `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (Line 217–340)
* **分析依據**：
  - **`DedicationAuditController` Null 防衛**：`BuildAuditWebFormModel` 預設檢查登入狀態，無 login contact 時安全 fallback 至空白隔離模型，`EnsureAuditFormModel` 保證 AJAX/Grid 端點在 `m_DonationPaymentFormModel` 為 null 時依然能回傳空 DataGrid 物件而非 500 錯誤。
  - **單元測試保護**：測試專案包含針對 `Startup` 設定 `AddHttpClient("LineLoginApi"` 的反射驗證與 4 項邊界情境單元測試，真實驗證了模型重置與 Grid null 安全行為。

---

### 5. 效能評估與安全的加速機會
* **驗證結果**：**[PASS] 無顯著效能瓶頸，無需投機性重構**
* **分析依據**：
  - `IHttpClientFactory` 實現了 DNS 輪替與 TCP Connection Pooling，大幅降低了連線建立與 TLS 握手延遲。
  - 陣列與 List 操作均使用 `.Clear()` 與高效率內嵌物件初始化，未有重複數據查詢或大量 GC 記憶體配置。
  - 目前架構已具備優良之效能表現，不建議加入任何會犧牲隔離性（如跨 Request 全域物件快取）的投機性重構。

---

## 審查發現分類與建議 (Findings & Recommendations)

### 🔴 Critical (必須修正的問題)
* **無 (None)**

---

### 🟡 Warning (建議修正或需明確限制的風險)

#### 1. `DedicationController.GetFeesByContactId` 解參考潛在風險
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`
* **行號**：Line 413
* **問題說明**：
  在 `GetFeesByContactId` 行 413 中：
  ```csharp
  return Json(new { status = "1", DedicationFeeList = feeList, TotalAmount = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.TotalAmount });
  ```
  若該傳入之 contactId 異常或 manager 尚未初始化表單模型，直接讀取 `m_DonationPaymentFormModel.TotalAmount` 可能觸發 `NullReferenceException`（雖然下方有外層 catch 攔截，但會導致前端收到 `status = "0"` 的錯誤 JSON，而非空集合列表）。
* **修正建議**：
  建議改用 `EnsureAuditFormModel` 確保模型非空：
  ```csharp
  var totalAmount = EnsureAuditFormModel(InMemoryContext.DonationPaymentManager).TotalAmount;
  return Json(new { status = "1", DedicationFeeList = feeList, TotalAmount = totalAmount });
  ```

---

### 🔵 Info (觀察與可選優化)

#### 1. 單元測試之 DataGrid 斷言可進一步強化
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (Line 280–303)
* **說明**：
  目前 Grid 測試使用 `action.Should().NotThrow()` 驗證不當機。未來可進一步斷言其回傳之 `LoadResult` 筆數為 `0`，提升測試防禦精準度。

---

## 總結結論 (Decision)

**審查結果：通過 (PASS)**

變更完全符合專案 Coding Style 與架構規範：
1. `Startup.cs` 的 `LineLoginApi` HttpClient 配置正確、有界 (10s) 且生命週期管理安全。
2. LINE ID Token 驗證及奉獻查詢流程具備完善的 Session / 租戶 / 個資隔離防衛與 Fail-Closed 設計。
3. 無 Stream、Socket、Timer、Memory 或 Task 資源洩漏。
4. 繁體中文註解完整，檔案格式均維持 UTF-8 without BOM 與 CRLF 規範。
