# P7.1 認獻單強型別讀取能力最終審查報告（Claude reviewer, p71-dedication-booking-final-review）

審查範圍：目前工作樹中未提交的變更（`git diff`），限於
`payments.dedication.retrieve.by.contact` 的 registry / Data8 executor / ProductClient typed DTO read、
Phase-0 matrix/schema agreement，以及對應的固定 QueryExpression、fail-closed input、response branch、
A/B mutation isolation、lease/permit disposal 測試。已交叉核對前一次已提交的 P7.1 commit
(`f4d1d334`) 中的相關實作以取得完整脈絡，並實際執行 `dotnet test`（43 項相關測試，含
`OperationRegistryAgreementTests`、`Package01OperationRegistryTests`、`Data8ProfileOperationExecutorTests`、
`OnPremiseData8ConnectorClientFactoryTests`、`Package01DedicationBookingReadClientTests`、
`Package01DedicationBookingReadRegistryTests`）驗證，全數通過。

本次工作樹 diff 的實質變更僅 4 處：
1. `Data8ProfileOperationExecutor.cs`：`TryValidateDedicationBookingRecords` 新增
   `record.DedicationBookingStatusOption != 100000001` 的 fail-closed 檢查。
2. Phase-0 matrix/schema：新增 `Package01DedicationBookingRecords` enum 值、
   `payments.dedication.retrieve.by.contact` 加入 allowlist，並補上
   `responseKind`/page/byte/item 上限與更新後的 `templateHash`。
3. 對應五個測試檔的新增/調整斷言（含新測試案例與既有 count 斷言的 +1）。

---

## 🔴 Critical Findings（關鍵缺陷）
無。

## 🟡 Warning Findings（警告事項）

- **`SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs:1314`** — 認獻單「進行中」狀態碼
  `100000001` 在三處各自硬編碼：`Package01Data8ReadOperations.cs:61`
  （`ActiveDedicationBookingStatus` 常數，用於 QueryExpression 條件與
  `ProjectDedicationBookingRecord` 投影檢查）與本次新增的
  `Data8ProfileOperationExecutor.cs:1314`（executor 層獨立驗證，無共用常數，直接寫字面值 `100000001`）。
  - Why：這符合本專案既有的「executor 對 connector 回應做獨立深度防禦」慣例（同檔案中
    `TryValidateContactImage` 也是同樣哲學），並非新引入的反模式；且目前兩個常數值一致，測試全數通過。
    但兩個類別分屬不同 project（`Connectors.Data8` vs 同一 project 的 executor，實際上都在
    `Connectors.Data8` project 內，只是不同檔案），沒有共用的具名常數，未來若 CRM 端調整
    「進行中」OptionSet 數值，容易只改一處而遺漏另一處，導致所有讀取被靜默 fail-closed（安全但會造成
    功能性 regression，且錯誤只會在 executor 驗證失敗、回傳 `operation.invalid-response` 時才會被發現）。
  - Fix（建議，非必要立即修正）：可考慮讓 executor 參照 `Package01Data8ReadOperations` 的
    `ActiveDedicationBookingStatus`（或抽到共用常數），或至少加上一行註解交叉引用另一處常數位置，
    降低未來漂移風險。不影響本次 P7.1 範圍的合併可行性。

## 🔵 Info Findings（一般資訊）

- **三層防禦一致性**：QueryExpression 條件（`new_dedication_booking_status = 100000001`）→
  connector 投影 `ProjectDedicationBookingRecord`（不符即擲例外）→ executor
  `TryValidateDedicationBookingRecords`（本次新增的第三層）三處都獨立檢查同一狀態值，
  彼此邏輯一致，測試 fixture（`OnPremiseData8ConnectorClientFactoryTests`、
  `Package01DedicationBookingReadClientTests`、`Data8ProfileOperationExecutorTests`）皆使用
  `100000001` 作為 happy path，未發現既有測試因新增檢查而回歸失敗（已由 `dotnet test` 實測確認）。
- **Fail-closed 順序**：已實際追蹤 `Data8ProfileOperationExecutor.ExecuteAsync`
  （`Data8ProfileOperationExecutor.cs:107-181`）程式碼路徑，確認參數驗證
  （`TryCreateConnectorOperation` → `TryCopyValidatedParameters`）發生在
  `_connectorRouter.Resolve(profile)`（第 160 行）與 `pool.AcquireAsync`（第 197 行）之前；
  `Package01DedicationBookingReadRegistryTests.cs` 本次把斷言目標從
  `TryCopyValidatedParameters(` 改成 `if (!TryCreateConnectorOperation(`，這個改動忠實反映實際程式碼順序
  （`TryCreateConnectorOperation` 是外層呼叫，內部才呼叫 `TryCopyValidatedParameters`），非弱化測試。
- **跨使用者/Profile 隔離**：`Package01DedicationBookingReadClient` 為 stateless singleton，
  每次呼叫建立 request-local `Dictionary`/DTO，回傳前以 `ReadOnlyCollection` 包裝防禦性複製結果
  （`Package01DedicationBookingReadClient.cs:107-118`），且 `OperationResponseData.ForPackage01DedicationBookingRecords`
  在建構 envelope 時對輸入集合呼叫 `ToArray()` 具現化，避免呼叫端集合被後續變動影響已發佈結果。
- **Raw CRM Entity 不外洩**：`ProjectDedicationBookingRecord`（`Package01Data8ReadOperations.cs:679-704`）
  僅在同步投影期間持有 `Entity`/`OptionSetValue`/`Money`，回傳純量 record；executor 與 ProductClient
  皆只操作封閉 DTO，未發現任何路徑把 SDK `Entity`/`EntityCollection` 往上層傳遞。
- **Query 可控性**：`CreateDedicationBookingByContactQuery`
  （`Package01Data8ReadOperations.cs:357-382`）完全寫死 entity/ColumnSet/條件/排序/分頁，
  caller 提供的 `contactName` 只作相容顯示用途，不進入查詢；已由
  `OnPremiseData8ConnectorClientFactoryTests.cs` 新測試以故障注入的方式逐欄斷言查詢內容
  （entity name、11 個 allowlisted 欄位、3 個條件、2 個排序欄、`PageInfo.Count = 128`），與
  `MaximumRowsPerPage` 常數一致。
- **Matrix drift**：`OperationRegistryAgreementTests`（count 21→22）與
  `Package01OperationRegistryTests`（同樣 count 21→22）已同步更新，`phase0-organization-call-matrix.json`
  的 `responseKind`/`maximumPageCount`/`maximumPageBytes`/`maximumCumulativeResponseBytes`/
  `maximumResultItemCount` 與 registry/`GetDefinition` 對齊（`4` 頁、`65536`/`262144` bytes、
  `4096` 筆），未見不一致。`templateHash` 由於 registry 定義本身變動（新增
  `responseKind`/上限欄位）而重新計算，屬預期行為。
- **Lease/Permit 釋放**：新增測試（`Data8ProfileOperationExecutorTests` 的
  `Execute_async_projects_registered_dedication_booking_read_through_a_lease_with_a_dedicated_branch`
  與 fail-closed 測試）明確斷言 `admission.AcquireCount`/`ReleaseCount`/`factory.DisposedCount`，
  且透過 `await using pool.DrainAsync()` 驗證 owned service 於 request 結束後準確釋放一次；
  `OnPremiseData8ConnectorClientFactoryTests` 新測試同樣斷言 `service.DisposeCount.Should().Be(1)`。
- **文件/編碼規範**：新增 XML doc comment 均為繁體中文，內容聚焦於資料隔離/資源釋放/fail-closed
  的「為什麼」而非重述程式碼字面行為，符合本專案既有風格。

---

## 審查結論

工作樹中的變更（新增 `DedicationBookingStatusOption` 的 executor 端 fail-closed 檢查、Phase-0
matrix/schema 同步更新、以及對應的固定查詢/fail-closed/隔離/釋放測試）與已提交的 P7.1
（`f4d1d334`）主體實作一致、無矛盾，`dotnet test` 實測 43 項相關測試全數通過。

- 無 Critical 缺陷。
- 1 項 Warning（狀態碼 `100000001` 於 connector 與 executor 兩處硬編碼、無共用具名常數），
  屬可延後處理的可維護性風險，不阻擋本次合併。
- 其餘為確認性 Info 項目，涵蓋隔離性、資源釋放、query 可控性、matrix 一致性與文件規範，
  均已對照原始碼與測試逐一驗證，非僅依賴描述性註解。

本階段僅完成 registry/executor/ProductClient 的底層強型別讀取能力；本機測試結果**不得**視為
CE、consumer cutover、P7.5 或 P8 的部署 evidence（ChurchReport 消費端與 Feature Gate 狀態不在本次
diff 範圍內，維持既有 legacy/關閉狀態，需另行確認）。
