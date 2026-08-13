以下為針對 P7.4 奉獻能力對應與隔離稽核的架構與安全性分析報告：

---

### Finding 1: ORG-CALL-00059 去重判定與安全遷移路徑
* **分類**: Info
* **分析與判定**:
  * `ORG-CALL-00059` (`RetrieveDedicationBookingByFetchXml`) 與 `ORG-CALL-00041` 在業務邏輯上皆為「依據聯絡人（Contact）讀取作用中奉獻預約（Active Dedication Booking）」。因此，**可以安全地將其去重並統一至 `ORG-CALL-00041` 的能力家族**。
* **必要證據**:
  * 系統中已實作 `DonationBookingReadService`，其 `RetrieveAsync` 方法封裝了 `IPackage01DedicationBookingReadClient.RetrieveDedicationBookingsByContactAsync`，並返回唯讀且不可變的 `DonationBookingReadResult`。
  * `DonationBookingReadBoundaryContractTests` 已驗證 `OperationIds.PaymentsDedicationRetrieveByContact` 已被納入 `RequestGuard` 的允許清單（allowlist）中，且支援 `ConnectionMode.Embedded` 與 `Gateway` 路由。
* **不應升級的 Evidence (限制條件)**:
  * 舊有的 `RetrieveDedicationBookingByFetchXml` 依賴 `fullname` (string) 與 `contactId` (string) 進行 FetchXml 模糊或 ad-hoc 查詢，並伴隨 N+1 次的 `RetrieveEntity` 呼叫。在去重遷移時，**絕不能**保留以 `fullname` 字串比對作為授權依據的邏輯（避免同名同姓越權漏洞），必須強制升級為以伺服器端驗證過的 `Guid contactId` 作為唯一輸入。
  * 遷移時應維持 `IsPackage01DedicationBookingReadEnabled` 的 feature gate 隔離，不應在未經部署驗證前直接啟用或繞過此 gate。

---

### Finding 2: ORG-CALL-00060 邊界漏洞與 DTO 遷移不安全因素
* **分類**: Critical
* **分析與判定**:
  * `ORG-CALL-00060` 目前的實作存在嚴重的安全邊界漏洞，若僅進行 DTO-only 遷移（僅修改資料傳輸物件而不重構控制流與授權）是極不安全的，必須採取 **Fail-Closed** 策略。
* **具體邊界漏洞**:
  1. **IDOR (不安全直接對象引用) 漏洞**: `DonationDedicationFeeFormService.GetFeesByContactIdAsync` 接受來自客戶端傳入的 `contactId` (string) 參數，並直接呼叫 `_utility.RetrieveEntity("contact", id)` 讀取 CRM 資料。此處完全沒有校驗該 `contactId` 是否屬於當前登入/授權的 Session 擁有者，攻擊者可透過篡改 `contactId` 讀取任意聯絡人的奉獻與費用資料。
  2. **共享可變狀態 (Shared Mutable State) 污染**: 費用讀取會寫入並修改 `DonationPaymentFormModel`（該 Model 存放在 Session 範圍的 `DonationPaymentManager` 實例中）。在多工或併發請求下，容易因狀態競爭（Race Condition）導致 Session 數據交叉污染（Session Bleeding）。
  3. **缺乏伺服器端衍生授權範圍**: 目前缺乏在進入 CRM I/O 前建立的、不可變的伺服器端授權憑證（Authenticated Principal / Server-derived Immutable Scope）。
* **最小恢復前置條件 (Minimum Recovery Pre-conditions)**:
  1. **參數來源限縮**: 廢除從 HTTP 請求參數（Query/Route/Body）直接接收 `contactId` 的 API 設計。聯絡人 ID 必須嚴格從伺服器端 Session (`Session[WebLoginContactId]`) 或經由 LINE Webhook 驗證的加密 Principal 中衍生取得。
  2. **狀態去耦與唯讀化**: 將費用查詢改為呼叫 `DonationFeeQueryService.RetrieveFeeAuditByContactAsync`，直接返回不可變的 `DonationFeeAuditReadResult` DTO，禁止直接修改共享的 `DonationPaymentFormModel` 實例屬性。
  3. **引入 Request-Local 授權攔截**: 在執行 CRM 查詢前，必須經由 `RequestGuard` 或自訂的授權過濾器，比對請求上下文中的聯絡人 ID 與當前 Session 綁定的聯絡人 ID 是否一致，實施 Fail-Closed 機制。

---

### Finding 3: 稽核範疇與任務限制確認
* **分類**: Info
* **分析**:
  * 本次分析僅產出 source-only architecture/security review 報告。
  * 確認未修改任何程式碼、Matrix 註記、Runtime 行為，亦未啟用任何 Feature Gate、CE、流量控制、P7.5 或 P8 相關功能。
