# CCG 架構安全分析報告：p74-memberinfo-storlesson-contact-read-boundary-analysis

## 結論
`go-local-design`

---

## 證據（檔案與行為）

1. **Server Authorization 驗證**
   - **檔案**：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs`
   - **行為**：在 `LoadContactStorLessons` 端點中（第 854-856 行），系統在執行任何 Dynamics 查詢或 typed composition 前，已先呼叫 `EnsureCorrectUserData()` 與 `CanViewContact(contactGuid)` 進行物件級授權把關。
   - **狀態風險評估**：`StorLessonQueryService` 於每次 Request 內皆為獨立實例化（第 863 行），其底層的 `IPackage01FeeReadClient` 透過 `DonationDynamicsAccessBootstrap` 取得，不包含 request-specific 的 session 或 credential 狀態，無 Session Leakage 或 InMemoryContext 污染風險。

2. **既有服務與 Client 重用性**
   - **檔案**：`SpeechMessageProducts.ChurchReport\Services\StorLessonQueryService.cs`
   - **行為**：
     - `GetByContactAsync` 接受 `contactName` 為 `null`，並直接呼叫 `IPackage01FeeReadClient.RetrieveStorLessonsByContactAsync`。其 `profileAlias` 來自 deployment-owned options，`workloadSubjectId` 固定為 `"church-report-service"`，無 caller-controlled 參數。
     - 當 `gate=true` 時，若發生異常（如 `OperationCanceledException`），會直接向上拋出，無 typed branch 內部的 fallback/retry 邏輯，符合重用規範。

3. **Parity 與 Cancellation 傳遞**
   - **檔案**：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs`
   - **行為**：`LoadContactStorLessons` 呼叫 `GetByContactAsync` 時已傳遞 `HttpContext.RequestAborted`，且 `catch` 區段排除 `OperationCanceledException`（第 891 行），確保 cancellation 傳遞鏈完整。`MapDtos` 與 `MapEntities` 欄位結構一致，無 parity 缺口。

---

## 必要修正

### 1. 新增獨立 Sub-gate 邏輯
- **檔案**：`SpeechMessageProducts.ChurchReport\Services\DonationDynamicsAccessBootstrap.cs`
- **修正**：新增 `IsPackage01MemberInfoStorLessonsReadEnabled(IConfiguration configuration)` 方法，讀取配置項 `DynamicsAccess:Package01MemberInfoStorLessonsReadEnabled`。此 sub-gate 必須繼承自 base gate `IsPackage01Enabled`（即 base gate 為 false 時，sub-gate 強制為 false）。

### 2. 設定檔預設關閉 (Disabled-by-default)
- **檔案**：`SpeechMessageProducts.ChurchReport\appsettings.json`
- **修正**：在 `DynamicsAccess` 區段中新增 `"Package01MemberInfoStorLessonsReadEnabled": false`。

### 3. Controller 分流邏輯修改
- **檔案**：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs`
- **修正**：修改 `LoadContactStorLessons`，將分流邏輯由 `queryService.IsPackage01Enabled` 改為依據新 sub-gate `DonationDynamicsAccessBootstrap.IsPackage01MemberInfoStorLessonsReadEnabled(configuration)`。

### 4. 測試契約驗證
- **檔案**：`ChurchReport.MemberInfo.Tests\Controllers\StorLessonControllerProductClientContractTests.cs`
- **修正**：新增或修改測試，驗證當 `Package01MemberInfoStorLessonsReadEnabled` 為 `false` 時，不觸發 ProductClient I/O 且走 legacy 路線；當為 `true` 時，走 typed 路線、傳遞 `RequestAborted`、不帶 caller-controlled name，且無 fallback 契約。

---

## 審查發現分類 (Reviewer Findings)

### Critical
- **無**。授權把關（`EnsureCorrectUserData` 與 `CanViewContact`）已在 Controller 端點入口處確實執行，且 sub-gate 預設為 false，無越權或未授權存取風險。

### Warning
- **Sub-gate 實例化隔離**
  - **檔案**：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs`
  - **原由**：在 `MemberInfoController.LoadContactStorLessons` 中實例化 `StorLessonQueryService` 時，必須確保傳入的 `package01FeeReadsEnabled` 參數與新 sub-gate 旗標一致，而非誤用 base gate `Package01FeeReadsEnabled`，否則會導致 sub-gate 失去分流控制作用。

### Info
- **組態設定檔同步**
  - **檔案**：`SpeechMessageProducts.ChurchReport\appsettings.json`
  - **原由**：需確保在 `appsettings.json` 中明確寫入 `"Package01MemberInfoStorLessonsReadEnabled": false`，以落實 disabled-by-default 的安全設計。

---

## 明確禁止事項
1. 禁止在 `gate=true` 的 typed branch 中加入任何對 `ToolUtility` 或 legacy FetchXML 的 fallback 呼叫。
2. 禁止將本機分析結果宣稱為 CE/consumer cutover/P7.5/P8 的准入證據。
3. 禁止修改既有 `IPackage01FeeReadClient` 介面或新增自訂的 executor/registry。
