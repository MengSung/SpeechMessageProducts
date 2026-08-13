以下為針對 **P7.1 dedication-booking typed-read** 架構設計與本機實作規劃的審查報告。本審查聚焦於資料流風險、合約命名與放置、邊界隔離、生命週期管理及相容性。

---

### 核心設計決策與架構邊界
1. **權威矩陣對應**：`ORG-CALL-00041` 轉譯為強型別能力 `payments.dedication.retrieve.by.contact`。
2. **無狀態隔離**：ChurchReport 消費端在此階段保持不變，不啟用 Feature Gate，亦不進行真實 CE 呼叫。所有測試均在本機以 Fake/Mock 執行，避免污染部署邊界。

---

### 審查發現與具體修復建議 (Findings & Remediation)

#### 🔴 Critical Findings (關鍵缺陷)

1. **回應邊界與聯集隔離缺陷 (Response Boundary & Union Isolation Defect)**
   * **具體檔案**：`SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs`
   * **風險說明**：`OperationResponseData` 是一個封閉聯集（Closed Union），用以確保 Dynamics 存取層不會外洩原始的 CRM SDK `Entity` 或 `EntityCollection`。若在新增 `payments.dedication.retrieve.by.contact` 能力時，未在 `OperationResponseKind` 中定義專屬的 `Package01DedicationBookingRecords`，或未在 `OperationResponseData` 中實作嚴格的單一分支驗證（`ValidateSingleSafeBranch`），將會破壞此安全邊界，導致 CRM 詮釋資料外洩。
   * **具體修復**：
     1. 於 `OperationResponseKind` 新增 `Package01DedicationBookingRecords` 列舉值。
     2. 於 `OperationResponseData` 新增 `IReadOnlyList<DedicationBookingRecord>? DedicationBookingRecords` 屬性與建構子參數。
     3. 實作 `IsValidDedicationBookingRecords` 驗證方法，限制最大筆數（例如 4096 筆）、禁止空值或無效的 GUID，並對字串欄位長度進行上限限制。

2. **跨 Profile A/B 洩漏與狀態隔離風險 (Cross-Profile A/B Leakage & State Isolation)**
   * **具體檔案**：`SpeechMessage.Dynamics.ProductClient/FeeReads/Package01DedicationBookingReadClient.cs` (預計新增)
   * **風險說明**：產品端強型別客戶端必須是完全無狀態（Stateless）且 Request-Local 的。若在實作 `IPackage01DedicationBookingReadClient` 時引入任何靜態快取、成員變數快取，或重用連線/租約狀態，將可能導致不同 Profile（例如不同教會組織）之間的認獻單數據交叉洩漏（Cross-Profile Leakage）。
   * **具體修復**：
     1. `Package01DedicationBookingReadClient` 僅能作為 `IDynamicsOperationExecutor` 的包裝器，不得持有任何與請求相關的狀態。
     2. 每次呼叫 `RetrieveDedicationBookingsByContactAsync` 時，必須將 `profileAlias` 與 `workloadSubjectId` 傳遞至底層 Executor，且回傳的 DTO 列表必須是全新反序列化且唯讀的防禦性複本（Defensive Copy）。

---

#### 🟡 Warning Findings (警告事項)

1. **Data8 查詢範圍與參數洩漏風險 (Data8 Query Scope & Parameter Leakage)**
   * **具體檔案**：`SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs` (預計新增/修改)
   * **風險說明**：依據設計規範，`contactName` 僅作為相容性參數，絕不能影響查詢範圍。若在 Data8 Connector 建立 `QueryExpression` 時，將 `contactName` 加入 Filter 條件中，可能會因為名稱不一致或惡意輸入導致查詢範圍被繞過或限縮。
   * **具體修復**：在 Data8 執行器中建構 `QueryExpression` 時，必須僅以 `contactId` (GUID) 作為唯一的過濾條件。`contactName` 參數應被忽略，或僅用於日誌記錄，絕不能進入 Dynamics 查詢條件。

2. **Data8 租約生命週期與 N+1 預防 (Data8 Lease Lifecycle & N+1 Prevention)**
   * **具體檔案**：`SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs` (預計新增/修改)
   * **風險說明**：既有的 `DonationBookingService.FillBookingList` 採用了對每筆認獻單進行 `RetrieveEntity` 的 N+1 查詢模式。若在 Data8 實作中沿用此模式，將會頻繁佔用並耗盡 Data8 連線池租約（Lease），導致嚴重的效能瓶頸與逾時。
   * **具體修復**：必須在 Data8 Connector 中使用單一的 `RetrieveMultiple` 查詢，一次性投影出該聯絡人所有的 `new_dedication_booking` 欄位，並在 Request 結束時立即釋放/歸還 Connector 租約。

3. **意外宣告消費端或 CE 驗證憑證 (Accidental Claim of Consumer or CE Evidence)**
   * **具體檔案**：`ChurchReport.MemberInfo.Tests` 及相關整合測試
   * **風險說明**：本階段（P7.1）僅限於本機端強型別讀取能力的架構與合約實作，ChurchReport 消費端在此階段應保持不變。若在此時修改了 `Package01FeeReadsEnabled` 等 Feature Gate，或在整合測試中直接對真實的 Dynamics CE 進行呼叫，將會錯誤地宣告「消費端已遷移」或「CE 驗證成功」，繞過了 P7.4 的安全授權與回滾機制。
   * **具體修復**：所有簽入的設定檔中，Feature Flag 必須保持為 `false`。本階段的測試應完全依賴 `FakeDynamicsOperationExecutor` 或 Mock 進行單元測試，禁止在此階段執行真實的 CE 呼叫或修改 ChurchReport 的消費端呼叫鏈。

---

#### 🔵 Info Findings (一般資訊)

1. **合約命名與放置規範 (Contract Naming & Placement)**
   * **具體檔案**：`SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs`
   * **風險說明**：為了維持合約命名的一致性，新能力應命名為 `payments.dedication.retrieve.by.contact`，並在 `OperationIds` 中宣告為 `PaymentsDedicationRetrieveByContact` 常數。
   * **具體修復**：將常數放置於 `OperationIds.cs` 的 Package 1 唯讀能力區段，並在 `Package01OperationRegistry.cs` 中將其歸類於 `package-1-fee-reads`。

2. **TDD 紅燈測試要求 (TDD Fail-First Tests Requirement)**
   * **具體檔案**：`SpeechMessage.Dynamics.Tests/Package01OperationRegistryTests.cs`
   * **風險說明**：為了確保合約變更的正確性，必須先撰寫會失敗的測試（Red Tests），驗證新註冊的 Operation ID、Response Kind 以及參數定義符合預期，隨後再進行實作。
   * **具體修復**：在 `Package01OperationRegistryTests.cs` 中新增測試案例，斷言 `payments.dedication.retrieve.by.contact` 已被正確註冊且其屬性（如 `MaximumResultItemCount`、`MaximumPageBytes`）符合 Conservative 限制。
