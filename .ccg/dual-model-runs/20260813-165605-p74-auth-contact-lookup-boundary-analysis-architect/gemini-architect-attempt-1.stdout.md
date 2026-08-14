# P7.4 帳號與 LINE ID 聯絡人查詢邊界分析報告
(p74-auth-contact-lookup-boundary-analysis)

本報告針對權威矩陣中的 `ORG-CALL-00055` (`auth.contact.retrieve.by.account`) 與 `ORG-CALL-00056` (`auth.contact.retrieve.by.lineid`) 本機唯讀設計進行安全邊界與架構審查。

---

## 審查發現與風險分類

### Critical (危急)

1. **明文密碼比對與敏感欄位洩漏風險 (Credential Leakage & Plaintext Comparison)**
   * **檔案路徑**：`ToolUtility\ContactOperations\ContactService.cs` (Legacy 進入點) 及新設計邊界 `auth.contact.retrieve.by.account`
   * **合理依據**：Legacy 實作中，`RetrieveByAccountNumber` 會將 CRM 中的 `new_app_pass` 明文拉回記憶體進行比對。在新設計的強型別 API 邊界中，必須嚴格禁止將 `new_app_pass` 欄位包含在 Data8 DTO 或 wire 傳輸中。若新 API 僅作為 DTO-only 查詢，它絕不能回傳密碼或雜湊至客戶端。密碼驗證邏輯必須在伺服器端（Dynamics 內部或專屬安全邊界內）進行，或者新 API 僅回傳聯絡人基本資訊，而比對邏輯完全與此強型別讀取邊界隔離。若直接在 DTO 中包含 `new_app_pass`，將違反「No password, hash, token, cookie, Entity, raw exception, endpoint, credential... may cross the new API boundary」的絕對限制。
   * **建議**：新 DTO 必須明確排除 `new_app_pass` 欄位。密碼比對不得在新 API 邊界內外傳遞明文密碼或雜湊。

2. **多筆結果 (Ambiguity) 未 Fail-Closed 的潛在風險**
   * **檔案路徑**：`ToolUtility\ContactOperations\ContactService.cs` (Legacy 參考) 及新設計邊界 `auth.contact.retrieve.by.lineid`
   * **合理依據**：在 LINE ID 查詢中，若 CRM 中因歷史資料髒亂而存在多個關聯相同 `new_lineid` 的 active 聯絡人，Legacy 實作僅使用 `TopCount = 1` 並回傳第一筆 (`result.Entities[0]`)。這在身分驗證情境下極度危險（可能導致登入錯誤帳號）。新設計必須在發現多筆結果時，明確判定為 `ambiguous` 並 fail closed，絕不能沿用 legacy 的 `TopCount = 1` 容錯邏輯。
   * **建議**：新 Data8 查詢與 Executor 必須檢索所有匹配項（或不限制 TopCount 為 1，而是檢查回傳數量是否大於 1），一旦數量大於 1，必須立即拋出或回傳固定分類 `ambiguous`，拒絕回傳任何聯絡人 DTO。

### Warning (警告)

1. **Gate=false 狀態下的資源延遲載入與 I/O 隔離**
   * **檔案路徑**：`SpeechMessage.Dynamics.WorkerHost` 相關初始化與 `IGatewayOperationAuthorizer` 綁定處
   * **合理依據**：當 `Gate=false` 時，系統必須確保不進行任何與此二 operation 相關的 host/client/pool/handler 構建。若 DI 容器在啟動時即積極初始化 (Eager Initialization) 相關的 Data8 管道或憑證管理員，即使 Gate 為 false，仍可能觸發出站 I/O 或資源分配。
   * **建議**：在 DI 註冊與 Bootstrap 階段，必須使用 Lazy 延遲載入或條件式註冊，確保當 Gate 為 false 時，完全不解析或建立與這兩個 operation 相關的連線池或處理程序。

2. **非同步與 Cancellation 傳遞不完整**
   * **檔案路徑**：新設計之 `auth.contact.retrieve` 相關非同步方法
   * **合理依據**：Legacy 程式碼中常有同步與非同步混用的情況（例如 `ExecuteAsync(() => RetrieveByAccountNumber(...))`）。新 API 必須是原生非同步，且必須將 `CancellationToken` 一路傳遞至底層的 Data8 傳輸通道，避免在等待 Dynamics 回應時造成執行緒阻塞或無法及時釋放連線資源。
   * **建議**：確保新 API 簽章完全為 `Task<T>`，且強制要求傳入 `CancellationToken`，不得在內部使用 `.Result` 或 `.GetAwaiter().GetResult()`。

### Info (提示)

1. **A/B 隔離與 Request-Local 生命週期**
   * **檔案路徑**：新設計之 DTO 與 Context 處理器
   * **合理依據**：為避免身分查詢結果在併發請求間交叉污染，查詢輸入（帳號、LINE ID）與輸出 DTO 必須嚴格限制在 Request-Local 生命週期內，絕不能快取於 static 欄位、HttpContext 共享字典或 Singleton 服務中。
   * **建議**：在單元測試中加入併發測試，驗證不同執行緒/請求傳入不同參數時，回傳的 DTO 實例與內容完全隔離。

---

## 具體本機建議 (Local-only Recommendations)

1. **定義嚴格的 DTO 結構**：
   建立專屬的 `AuthContactLookupResultDto`，僅包含 `ContactId` (Guid)、`AccountLocator` (string)、`DisplayName` (string) 與 `IsActive` (bool)。嚴禁包含 `new_app_pass` 或任何自訂密碼欄位。

2. **實作 Fail-Closed 驗證器**：
   在 Data8 Executor 收到 Dynamics 回傳的 `EntityCollection` 後，進行以下檢查：
   * 若 `Entities.Count == 0` -> 回傳 `not-found` 分類。
   * 若 `Entities.Count > 1` -> 回傳 `ambiguous` 分類。
   * 若回傳的 Entity 屬性中包含 `new_app_pass` 或其他未授權欄位 -> 立即拋出 `secret-present` 異常並 fail closed。

3. **Gate 狀態檢查置頂**：
   在進入任何連線解析前，優先檢查 Feature Gate。若未啟用，直接回傳 `profile-unavailable`，不觸發任何連線池借出動作。

4. **編寫專屬的單元測試**：
   * 撰寫 `RetrieveByLineId_MultipleMatches_FailsClosed` 測試，模擬 Dynamics 回傳兩筆相同 LINE ID 的聯絡人，驗證其回傳 `ambiguous`。
   * 撰寫 `RetrieveByAccount_GateFalse_NoIoPerformed` 測試，驗證在 Gate 關閉時，呼叫 API 不會觸發任何 I/O 或連線建立。
