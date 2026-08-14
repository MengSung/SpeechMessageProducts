## 結論
`go-local-design`

---

## 證據（檔案與行為）
1. **Server Authorization 優先性與狀態隔離**：
   - 檔案：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs`
   - 行為：在 `LoadContactStorLessons` 端點中（第 854-856 行），系統在執行任何 Dynamics 查詢或 typed composition 前，已先呼叫 `EnsureCorrectUserData()` 與 `CanViewContact(contactGuid)` 進行物件級授權把關。
   - 狀態隔離：`StorLessonQueryService` 與 `Package01FeeReadClient` 皆為無狀態（stateless）設計，且 DTO 投影與轉換皆在 request-local 範疇內完成，不存在 Session、`InMemoryContext` 或共享 mutable state 的洩漏風險。

2. **既有服務與 Client 重用性**：
   - 檔案：`SpeechMessageProducts.ChurchReport\Services\StorLessonQueryService.cs`
   - 行為：`GetByContactAsync` 接受 `contactName` 為 `null`，並直接呼叫 `IPackage01FeeReadClient.RetrieveStorLessonsByContactAsync`。其 `profileAlias` 來自 deployment-owned options，`workloadSubjectId` 固定為 `"church-report-service"`，無 caller-controlled 參數。
   - 異常處理：該方法無任何 fallback 或 retry 機制，若發生異常（如 `OperationCanceledException`）會直接向上傳播，符合 PRD 安全規範。

3. **現有配置與預設關閉**：
   - 檔案：`appsettings.json`（第 595 行）與 `appsettings.Development.json`（第 10 行）
   - 行為：既有的 `Package01FeeReadsEnabled` 均為 `false`，符合預設關閉（disabled-by-default）的防禦姿態。

---

## 必要修正
1. **新增獨立 Sub-Gate 旗標**：
   - **檔案**：`SpeechMessageProducts.ChurchReport\Services\DonationDynamicsAccessBootstrap.cs`
   - **修正**：新增 `IsPackage01MemberInfoStorLessonsReadEnabled(IConfiguration configuration)` 方法，讀取配置項 `DynamicsAccess:Package01MemberInfoStorLessonsReadEnabled`。此 sub-gate 必須繼承自 base gate `IsPackage01Enabled`（即 base gate 為 false 時，sub-gate 強制為 false）。
   - **檔案**：`appsettings.json` 與 `appsettings.Development.json`
   - **修正**：在 `DynamicsAccess` 區段中新增 `"Package01MemberInfoStorLessonsReadEnabled": false`。

2. **Controller Action 分流調整**：
   - **檔案**：`SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs`
   - **修正**：修改 `LoadContactStorLessons`，將分流邏輯由 `queryService.IsPackage01Enabled` 改為依據新 sub-gate `DonationDynamicsAccessBootstrap.IsPackage01MemberInfoStorLessonsReadEnabled(configuration)`。
     - 當 sub-gate 為 `true` 時：以 `package01FeeReadsEnabled: true` 實例化 `StorLessonQueryService`，且傳入的 `fullName` 必須為 `null`。
     - 當 sub-gate 為 `false` 時：保留 legacy 路線，先從舊服務 Retrieve 取得 `fullname`，再進行查詢。

3. **測試契約更新**：
   - **檔案**：`ChurchReport.MemberInfo.Tests\Controllers\StorLessonControllerProductClientContractTests.cs`
   - **修正**：新增或修改測試，驗證當 `Package01MemberInfoStorLessonsReadEnabled` 為 `false` 時，不觸發 ProductClient I/O 且走 legacy 路線；當為 `true` 時，走 typed 路線、傳遞 `RequestAborted`、不帶 caller-controlled name，且無 fallback 契約。

---

## 明確禁止事項
1. **禁止 Fallback 混合**：在 sub-gate 為 `true` 的 typed 分支中，若 `GetByContactAsync` 拋出任何異常，禁止 catch 後退回呼叫 `ToolUtility` 或 legacy SDK 進行二次查詢。
2. **禁止傳遞前端 Name**：在 typed 分支中，禁止將前端傳入的 `contactName` 傳遞給 `IPackage01FeeReadClient`，必須強制為 `null`。
3. **禁止宣稱本機結果為上線憑證**：本分析僅限於本機唯讀設計評估，不得將此結果宣稱為 CE 實機驗證、流量切換（cutover）或 P7.5/P8 的准入證據。

---

## Reviewer 評估分類

### Critical
無。授權把關（`CanViewContact`）與 DTO 狀態隔離設計良好，無安全性破口。

### Warning
- **Sub-gate 實例化隔離**：在 `MemberInfoController.LoadContactStorLessons` 中實例化 `StorLessonQueryService` 時，必須確保傳入的 `package01FeeReadsEnabled` 參數與新 sub-gate 旗標一致，而非誤用 base gate `Package01FeeReadsEnabled`，否則會導致 sub-gate 失去分流控制作用。

### Info
- **與 EquipmentController 的隔離**：`EquipmentController.LoadEquipmentStorLessons` 雖然重用了相同的 `StorLessonQueryService`，但其不受本 sub-gate 影響，兩者在 Controller 層級已達成良好的 A/B isolation。
