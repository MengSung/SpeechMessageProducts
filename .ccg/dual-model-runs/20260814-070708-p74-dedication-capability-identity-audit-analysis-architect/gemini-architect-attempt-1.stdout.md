# P7.4 奉獻能力對應與隔離稽核：架構分析報告

本報告為 **source-only architecture/security review**，僅針對既有程式碼與架構設計進行分析與判定，**不修改任何檔案**，亦不建議啟用任何 feature gate、CE、流量切換、P7.5 或 P8 行為。

---

## 審查判定與發現分級

### [Info] ORG-CALL-00059 與 ORG-CALL-00041 之去重判定
*   **判定結果**：**可安全去重**。`ORG-CALL-00059` 應去重並整合至 `ORG-CALL-00041`。
*   **架構與安全證據**：
    1.  **查詢語意一致性**：`ORG-CALL-00059`（`FetchXmlQueryService.cs`）使用 FetchXML 依 `contactId` 查詢 active 狀態（`new_dedication_booking_status = 100000001` 且 `statecode = 0`）的認獻單。這與 `ORG-CALL-00041` 在 `Package01Data8ReadOperations.cs` 中定義的 `CreateDedicationBookingByContactQuery` 條件完全相同。
    2.  **消除 N+1 查詢漏洞**：舊有 `DonationBookingService.FillBookingList` 在呼叫 `00059` 取得 collection 後，會對每筆 booking 執行 `_utility.RetrieveEntity("new_dedication_booking", bookingEntity.Id)` 進行二次查詢。而 `ORG-CALL-00041` 已透過 `Package01DedicationBookingReadClient` 投影了所有必要欄位並封裝為唯讀的 `DedicationBookingRecordDto`，可直接取代舊有的 ad-hoc 查詢與逐筆 Retrieve 迴圈。
    3.  **合規測試覆蓋**：`Package01DedicationBookingReadRegistryTests.cs` 已鎖定 `ORG-CALL-00041` 的封閉讀取契約（Operation ID: `payments.dedication.retrieve.by.contact`，Template ID: `payments.dedication.by.contact.v1`）。
*   **禁止升級之限制（Evidence Boundary）**：
    *   `ORG-CALL-00041` 目前僅完成本機 DTO-only boundary，其 feature gate 預設為 `false`，且在 `appsettings.Development.json` 中明確禁止開發 profile 自行開啟。
    *   此本機驗證證據**不得**升級為 consumer cutover、CE 啟用、流量切換、P7.5 或 P8 證據。

---

### [Critical] ORG-CALL-00060 之邊界漏洞與安全風險
*   **判定結果**：**Fail Closed（禁止直接進行 DTO-only 遷移）**。
*   **具體 Boundary 漏洞**：
    1.  **未授權的輸入來源（Unauthenticated Input）**：`DonationDedicationFeeFormService.GetFeesByContactIdAsync` 接受外部傳入的 `string contactId`，並直接呼叫 `_utility.RetrieveEntity("contact", id)`。此路徑在進入服務前，**完全沒有**經過任何伺服器端衍生且不可變的授權範圍（server-derived immutable authorization scope）驗證，存在越權讀取任意聯絡人資料的風險。
    2.  **狀態修改副作用（State Mutation Side-effect）**：該服務在讀取 fee 資料的同時，會呼叫 `FillFromContactAsync` 直接修改傳入的 `DonationPaymentFormModel`（包含 `FullName`、`Mobile`、`DedicationNumber`、`Ntbt` 等個人識別資訊）。此 mutable model 屬於 Session 狀態，讀取操作與狀態寫入高度耦合。
    3.  **缺乏隔離邊界**：在 Session、manager state、cache 或 CRM I/O 之前，沒有建立 authenticated-principal 驗證，直接暴露了 `ToolUtility` 的 CRM 實體讀取。
*   **最小恢復前置條件**：
    *   必須先建立一個伺服器端衍生的不可變授權範圍（例如 `auth.contact` capability），在 API 入口處驗證當前 Session 的 authenticated-principal 是否與傳入的 `contactId` 一致。
    *   必須將讀取（Read）與狀態修改（Mutation）職責分離，禁止在讀取 fee 資料的同時直接修改 Session 內部的 `DonationPaymentFormModel` 狀態。
    *   必須使用專用的 `DonationFeeAuditAccessResolver` 或類似的唯讀安全邊界，來取代 ad-hoc 的 `RetrieveEntity("contact", id)`。

---

### [Info] 任務範疇與限制確認
*   本分析僅產出 source-only task record。
*   未對 matrix、runtime、CE、gate、traffic、P7.5 或 P8 進行任何修改或啟用建議。
