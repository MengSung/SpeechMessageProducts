# UI/API 邊界審查報告：P7.4 認證聯絡人查詢邊界 (p74-auth-contact-lookup-mismatch-review)

本報告針對 Trellis 子任務 `.trellis/tasks/08-13-p74-auth-contact-lookup-boundary/` 當前未提交的變更進行程式碼與架構審查。審查範圍嚴格限定於 `ORG-CALL-00055` 與 `ORG-CALL-00056` 的 disabled-by-default、local-only Data8/ProductClient 強型別聯絡人讀取邊界。

---

## 一、 整體評估 (Summary)

本次變更完整且嚴格地實作了 `ORG-CALL-00055`（依帳號定位值讀取聯絡人）與 `ORG-CALL-00056`（依 LINE ID 定位值讀取聯絡人）的唯讀安全投影邊界。所有變更均符合 **disabled-by-default** 原則，未啟用任何生產環境功能閘門（Feature Gate），亦未引入任何 CE I/O、流量變更、登入會話（Session）綁定或 legacy fallback 邏輯。程式碼在防範敏感資料外洩、基數限制（TopCount=2）、Fail-Closed 機制、A/B 請求隔離以及編碼一致性上皆有極為嚴格的防禦性設計。

---

## 二、 審查發現與分類 (Findings)

### 1. Critical (關鍵缺陷)
* **無**。未發現任何會導致憑證外洩、越權存取、記憶體洩漏或破壞隔離邊界的關鍵缺陷。

### 2. Warning (警告事項)
* **無**。程式碼實作高度契合設計規範，無潛在的架構風險。

### 3. Info (說明/建議事項)
* **檔案註解編碼問題**：
  * **檔案路徑**：
    * `SpeechMessage.Dynamics.Abstractions/Operations/AuthenticationContactReadRecord.cs`
    * `SpeechMessage.Dynamics.ProductClient/Authentication/IAuthenticationContactReadClient.cs`
    * `SpeechMessage.Dynamics.ProductClient/Authentication/AuthenticationContactReadDto.cs`
    * `SpeechMessage.Dynamics.ProductClient/Authentication/AuthenticationContactReadClient.cs`
    * `ChurchReport.MemberInfo.Tests/AuthenticationContactReadBootstrapTests.cs`
    * `SpeechMessage.Dynamics.Tests/AuthenticationContactReadClientTests.cs`
    * `SpeechMessage.Dynamics.Tests/AuthenticationContactReadRegistryTests.cs`
  * **說明**：上述新建立的檔案中，C# 註解部分存在非 UTF-8（疑似 Big5 混淆）的亂碼字元。雖然這完全不影響編譯與執行邏輯，且測試已全數通過，但建議在後續整理程式碼時將檔案編碼統一轉換為標準 UTF-8 以提升程式碼可讀性。
* **基數上限不一致的防禦設計**：
  * **檔案路徑**：
    * `SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs` (定義 `MaximumAuthenticationContactReadRecords = 4096`)
    * `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs` (定義 `MaximumAuthenticationContactRecords = 2`)
  * **說明**：通用回應信封（Response Envelope）定義的上限為 4096，而 Data8 執行器則強制收緊至 2。此設計符合「通用容器寬鬆、具體執行器嚴格」的防禦性原則，能有效在 Data8 邊界阻斷因 CRM 資料異常導致的多筆重複綁定（Ambiguous）並回傳 Fail-Closed 狀態。

---

## 三、 邊界規範驗證 (Review Checklist Verification)

### 1. 敏感資料與例外隔離 (No Secrets/Raw Entities Cross Wire)
* **驗證結果：通過**
* **事實證據**：
  * `AuthenticationContactReadRecord` 與 `AuthenticationContactReadDto` 僅包含 `ContactId`、`AccountLocator`、`DisplayName` 與 `IsActive` 四個公開純值欄位。
  * 經反射與序列化測試（`Wire_DTO_and_result_do_not_expose_a_password_or_secret_field`）證實，沒有任何密碼（`password`）、雜湊（`hash`）、憑證（`credential`）或 legacy 敏感欄位（`new_app_pass`）被投影至 DTO 或 Wire 格式中。
  * 原始 CRM `Entity` 僅在同步呼叫堆疊內部被讀取並立即投影，未跨越 Client 邊界。

### 2. 固定查詢與基數限制 (Fixed QueryExpression & TopCount=2)
* **驗證結果：通過**
* **事實證據**：
  * `Package01Data8ReadOperations.cs` 中的 `CreateAuthenticationContactQuery()` 強制設定 `TopCount = 2`，且 Criteria 僅包含固定的 active 條件（`statecode eq 0`）與 locator 條件。
  * 不接受呼叫端自訂的 FetchXML、Entity 或 Filter 條件，無泛型 CRUD 漏洞。
  * 若 CRM 回傳結果大於 2 筆，會立即拋出 `InvalidOperationException` 淘汰該 WCF Session，確保不會從多筆結果中盲目猜選登入主體。

### 3. 閘門提早返回 (False Gate Returns Early)
* **驗證結果：通過**
* **事實證據**：
  * `DonationDynamicsAccessBootstrap.TryCreateAuthenticationContactReadClient` 在最外層優先判斷 `IsAuthenticationContactReadEnabled(configuration)`。
  * 若閘門為 `false`，則立即返回 `null`，完全不觸發 `BindOptions`、Profile 驗證、Executor 建立或任何 I/O 呼叫。
  * `appsettings.json` 與 `appsettings.Development.json` 中的 `AuthenticationContactReadEnabled` 均已明確設為 `false`。

### 4. 驗證、取消與無重試機制 (Validation, Cancellation & No Retry)
* **驗證結果：通過**
* **事實證據**：
  * `AuthenticationContactReadClient.cs` 對 `profileAlias` 與 `workloadSubjectId` 進行了嚴格的 UTF-8 長度與非空驗證（`NormalizeRequiredText`）。
  * `CancellationToken` 被直接傳遞至下游，且在捕獲 `OperationCanceledException` 時直接向上拋出，無任何背景重試或 fallback 到 legacy SDK 的邏輯。

### 5. 異常狀態閉鎖 (Fail Closed on Zero/Duplicate/Secret/Mismatch)
* **驗證結果：通過**
* **事實證據**：
  * **0 筆結果**：對應 `AuthenticationContactReadStatus.NotFound`，`Contact` 為 `null`。
  * **2 筆結果**：對應 `AuthenticationContactReadStatus.Ambiguous`，`Contact` 為 `null`。
  * **偵測到秘密**：對應 `AuthenticationContactReadStatus.SecretPresent`，`Contact` 為 `null`。
  * **Operation ID 或 ResponseKind 錯配**：對應 `AuthenticationContactReadStatus.ProfileUnavailable`，`Contact` 為 `null`。

### 6. A/B 請求隔離與編碼一致性 (A/B Isolation & Encoding Consistency)
* **驗證結果：通過**
* **事實證據**：
  * `AuthenticationContactReadClient` 為無狀態設計，交錯的 A/B 請求測試（`Retrieve_async_keeps_interleaved_A_and_B_results_immutable_and_request_local`）證實結果完全隔離在各自的呼叫棧（Stack）中。
  * 全鏈路統一採用 `StrictUtf8` (`new UTF8Encoding(false, true)`) 進行嚴格的位元組計數與編碼驗證。

### 7. 矩陣與註冊表一致性 (Matrix/Schema/Registry Agreement)
* **驗證結果：通過**
* **事實證據**：
  * `phase0-organization-call-matrix.json`、`phase0-organization-call-matrix.schema.json` 與 `Package01OperationRegistry.cs` 之間的 Operation ID、參數名稱（`accountLookupValue` / `lineIdLookupValue`）及回應類型（`AuthenticationContactReadRecords`）完全一致。
  * 相關單元測試已將預期能力數量更新為 24 並全數通過。

---

## 四、 結論與建議 (Conclusion & Recommendation)

### 結論：**通過 (PASS)**

當前未提交的變更完全符合 P7.4 認證聯絡人查詢邊界的設計規範與安全約束。建議在修正新檔案的註解亂碼（轉換為標準 UTF-8 編碼）後，即可進行 scope-only commit 與任務封存（archive）。
