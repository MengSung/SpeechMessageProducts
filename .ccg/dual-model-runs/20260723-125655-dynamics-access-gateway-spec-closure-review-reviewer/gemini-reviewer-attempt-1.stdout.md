# Dynamics Access Gateway 架構設計規範審查報告

本報告針對 **Dynamics Access Gateway** 的規劃與設計文件進行架構與安全性審查。審查範圍包含：
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`

---

## 1. 總體評估 (Summary)

本設計規範（SPEC）針對五至十個產品共享 Dynamics 365 組織存取的場景，提出了一個技術上非常嚴密且具可行性的 **Dynamics Access Gateway (網關服務) + 私有無 SDK Web API 程式庫** 的混合架構。

設計成功拒絕了「各產品獨立引用程式庫（Library-only）」與「通用透明代理（Generic transparent proxy）」的替代方案，並給出了具體的架構與安全理由。規範中對於連線池隔離、憑證洩漏防範、版本偵測、並行控制（ReplicaSlotLease）、等冪性帳本（Idempotency Ledger）以及無 SDK 遷移路徑的規劃極為詳盡，完全滿足了前次審查的所有回歸檢查點（Regression Checks）。

由於本專案為**後端 API 網關服務**，不包含前端使用者介面（UI），因此常規的 UI 可存取性（Accessibility, a11y）與瀏覽器相容性指標在此不適用。然而，本審查已將「可存取性」維度轉化為 **API 存取控制與安全性** 進行評估。

### 驗證評分表 (Validation Report)

```
VALIDATION REPORT
=================
User Experience (API Usability): 19/20 - 統一的 REST API 與 pre-registered 操作設計，極大地簡化了客戶端整合難度，但操作註冊表（Operation Registry）的變更流程需在實作中進一步明確。
Visual Consistency (N/A): 20/20 - 無前端 UI，後端 API 命名與 DTO 結構設計具備高度一致性。
Accessibility (API Security): 20/20 - 嚴格的服務間工作負載身份驗證（mTLS/JWT），完全杜絕客戶端越權與任意路由逃逸。
Performance: 20/20 - 基於 SocketsHttpHandler 的連線重用、本地與分散式雙重並行限制、元資料快取，效能指標與測試門檻定義清晰。
Browser Compatibility (N/A): 20/20 - 後端服務間通訊，不涉及瀏覽器相容性問題。

TOTAL SCORE: 99/100

ISSUES FOUND:
- [Warning] 跨 Profile 的 OrganizationAdmissionSettings 一致性驗證機制與 JSON 宣告結構。
- [Info] ReplicaSlotLease 協調器（IReplicaSlotCoordinator）的技術選型建議。
- [Info] CanonicalKeyV1 序列化效能與跨平台位元組順序。

RECOMMENDATION: PASS
```

---

## 2. 安全性與存取控制問題 (Accessibility / Security Issues)

本專案無前端 UI，故無 HTML/ARIA 等 a11y 問題。以下為 API 安全性與存取控制的審查結果：

* **無發現 Critical 級別安全性漏洞。**
* 設計中已完全封鎖呼叫端傳遞自訂 FetchXML、OData 語法、CRM 標頭或物理設定檔的管道，並強制使用服務端定義的 `capabilityOperationId`，安全邊界非常穩固。

---

## 3. 設計與架構問題 (Design Issues)

### 【Warning】跨 Profile 的 OrganizationAdmissionSettings 一致性驗證與 JSON 結構
* **相關檔案/章節**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 6.1 & 7.1)
* **問題描述**：
  設計中要求同一個 `OrganizationAdmissionKey`（即同一個 Dynamics 組織）的所有 Profile 必須宣告完全相同的 `OrganizationAdmissionSettings`（如 `AggregateMaxInFlight`、`MaximumGatewayReplicas`、`QueueCapacity` 等），否則驗證器將拒絕該配置。
  然而，在 Section 6.1 的 JSON 範例中，這些設定是重複寫在每個 Profile 的 `Runtime` 屬性內（例如 `church-ce82-prod` 與 `church-ce91-prod` 各自擁有一套 `Runtime` 設定）。這種設計容易因為手動配置失誤而導致衝突，且驗證器在跨 Profile 檢查時的邏輯會變得較為複雜。
* **修正建議**：
  建議在 JSON 綱要設計中，將 `OrganizationAdmissionSettings` 提升至與 `Profiles` 平級的獨立區塊（例如以 `OrganizationAdmissionKey` 作為鍵值的 Map），或者在實作 Phase 2.1 的驗證器時，加入極為嚴格的跨 Profile 欄位比對邏輯，一旦發現同一個組織 ID 的設定不一致，立即拋出明確的配置錯誤並拒絕載入。

### 【Info】ReplicaSlotLease 協調器（IReplicaSlotCoordinator）的技術選型
* **相關檔案/章節**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` (Preconditions & Phase 3.5)
* **問題描述**：
  實作計畫中要求選擇一個具備原子性條件建立/更新/釋放語義的持久型共享協調器（durable, shared coordinator），並明確指出不能使用處理程序本地記憶體或無協調器授權的時鐘。然而，規範中並未列出建議的技術選型。
* **修正建議**：
  建議在 Phase 0 或 Phase 1 的技術評估中，明確列出候選技術（例如 Redis Redlock、Consul Session、ZooKeeper 或基於關係型資料庫的行鎖/應用程式鎖如 SQL Server `sp_getapplock`），並評估其在 Windows/Linux 混合部署環境下的高可用性與延遲表現。

### 【Info】CanonicalKeyV1 序列化效能與跨平台位元組順序
* **相關檔案/章節**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 7.1.1)
* **問題描述**：
  `CanonicalKeyV1` 定義了長度前綴的規範化編碼：`UInt32BigEndian(UTF8(value).length)`。
* **修正建議**：
  在 .NET 實作中，建議使用 `System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian` 來寫入長度前綴，以確保在不同 CPU 架構（Little Endian vs Big Endian）下的位元組順序一致性，並避免不必要的記憶體配置（Allocation）。

---

## 4. 具體改進建議 (Suggestions)

1. **操作註冊表（Operation Registry）的動態更新機制**：
   目前設計中，操作註冊表是靜態定義的。未來若有新的 API 操作需求，是否需要重新部署整個 Gateway？建議在 Phase 2 中預留「操作註冊表與設定檔採用相同 replace-and-drain 機制進行熱重載」的設計，避免因新增一個簡單的查詢範本而需要重啟服務。
2. **PreAuthenticate 測試基準**：
   規範中非常正確地要求預設停用 `PreAuthenticate`。在 Phase 2.4 進行 Windows/IWA 測試時，務必撰寫專門的並行測試案例，模擬多個不同 Windows 身份的請求在極短時間內交替發送，驗證底層 TCP 連線不會發生身份交叉污染（Connection-bound auth leakage）。

---

## 5. 優秀設計點 (Positive Notes)

1. **Fail-Closed 租約設計**：
   ReplicaSlotLease 在協調器失效或 TTL 到期時立即 fail-closed，停止新請求准入且僅排空已租用工作，無任何「緊急寬限期」。這是一個極佳的防禦性設計，能徹底防止因網路分割或協調器故障導致的 Dynamics 服務超載。
2. **雙重並行限制與 LocalMaxInFlight 衍生**：
   `LocalMaxInFlight` 由總預算與最大副本數自動計算得出，而非獨立配置，且在 CI/HPA 與執行期租約雙重強制執行。這消除了因人為配置失誤導致並行度超標的風險。
3. **等冪性帳本（Idempotency Ledger）的 OutcomeUnknown 處理**：
   對於 dispatch 後結果未知的寫入，帳本將其標記為 `OutcomeUnknown` 且禁止自動重試，這在分散式系統中是唯一正確且安全的做法，避免了重複扣款或重複建立資料的風險。
4. **嚴格的 Windows 認證 Tagged Union**：
   區分 `HostIdentity` 與 `SecretReference`，且 `HostIdentity` 內禁止任何密碼欄位，這完美契合了 gMSA 與 Kerberos 的無密碼安全實踐。
