# P1-04 剩餘純 SDK 抽取的最小安全邊界與風險分析報告

本報告針對 P1-04 任務剩餘純 SDK 抽取範疇（包含 Attribute 轉換器、Collection、Contact 快取留 host、EntityOptimizedQuery / EntityAttributeHandler、List / MarketingList、Query 服務、CrmAsyncExtensions 及其必要純介面）進行架構與安全邊界分析。

---

## 1. UX 分析 (User Impact Assessment)
- **非同步包裝與同步 SDK 封裝**：
  - **使用者影響**：將同步 SDK 操作以 `Task.FromResult` 或標準非同步方法安全包裝，並確保預先取消（pre-cancel）時不觸發無謂的 I/O。這保障了前端或 Webhook 呼叫在面臨高併發時不會造成執行緒枯竭 (Thread Starvation)。
  - **邊界保護**：原 host 的 2～3 個 Golden 測試必須保持綠燈。針對安全紅（Safety Red）→ 修復 → 搬前綠後 git mv 的防護節奏，確保端到端的使用者體驗與回傳訊息不因重構而改變。
- **訊息與日期保留**：
  - 週報日期邏輯與 `Execute` 回應訊息必須與原 host 行為完全一致，維護前端操作人員與管理員的期望值。

---

## 2. 設計評估 (Design System Evaluation)
- **介面與相容性設計**：
  - **明確 `pageNumber` Overload**：在分頁 API 擴充時，設計明確的 `pageNumber` 重載方法而非破壞舊介面。
  - **舊 Cookie 入口處理**：對於舊的 Cookie 入口，採官方 XML cookie page 推算，**禁止使用 DTD**、落實有界讀取（Bounded Reading），且**不保留 Cookie 狀態**以防狀態污染。
- **API 行為差異隔離**：
  - **過濾條件差異**：明確保留既有的 filter 差異（例如 Dictionary IDs 無 `statecode`，而 Collection IDs 帶有 `statecode`）。
  - **分頁與批次限制**：單頁 API 保留 paging metadata；完整批次必須具備完整有界分頁及全候選驗證 ID，超預算時必須明確拒絕（Reject on Over-budget），防止無限迴圈或 CRM 資源耗盡。

---

## 3. 技術考量 (Technical Considerations)
- **生命週期與資源釋放 (Lifecycle & Disposal)**：
  - **SDK 逾時/Drain/Dispose**：由原 host 完整擁有並管理 SDK 的逾時、drain 與 Dispose。
  - **Core Service Dispose 契約**：Core service 的 `Dispose` 僅停止新入口並清除內部參照，**絕對不能 dispose 借入的 SDK**（因為 SDK 生命週期由 host 控管）。
- **日誌與例外處理 (Logging & Exception Handling)**：
  - **Logger 反射與生命週期**：借入 `IOrganizationService`；object logger 反射僅限於錯誤診斷，**logger 本身不保存**。
  - **例外不吞 (No Exception Swallowing)**：Exception 絕對不吞（Do not swallow exceptions），原封不動交由 host 共用通知層處理。

---

## 4. 潛在真實風險與相容性 (Critical / Warning / Info Findings)

### **[Critical] 1. EntityOptimizedQuery 與 EntityAttributeHandler 的型別對齊與快取污染風險**
- **位置**：`EntityOptimizedQuery`, `EntityAttributeHandler`, Contact Cache (host 層)。
- **分析**：若快取邏輯留在 host 但 SDK 內部進行實體屬性快取，跨執行緒的 `Entity` 屬性字典修改可能引發 Data Race。
- **建議**：確保傳入的 Entity 複本或唯讀投影（Projection），避免快取實體物件被下游業務邏輯直接修改。

### **[Warning] 2. Cookie 推算與 XML 外部實體 (XXE) 安全隱患**
- **位置**：舊 cookie 入口推算邏輯。
- **分析**：需求已明確禁止 DTD 並要求有界讀取。若未來擴充時誤用預設的 `XmlDocument` 或 `XDocument` 剖析未關閉 DTD，可能引入 XML 外部實體注入風險。
- **建議**：強制設定 `XmlReaderSettings` 將 `DtdProcessing = DtdProcessing.Prohibit`。

### **[Warning] 3. 批次有界分頁超預算拒絕的例外處理**
- **位置**：Query 服務與完整批次全候選驗證。
- **分析**：當完整批次超預算被拒絕時，若回傳的例外型別不符合 host 既有的攔截合約，可能導致 HTTP 500 或是未捕捉例外。
- **建議**：定義明確的 `BatchQueryBudgetExceededException` 供 host 辨識與捕捉。

### **[Info] 4. Dictionary IDs 與 Collection IDs 的 Filter 差異維護**
- **位置**：Dictionary 服務 vs Collection 服務。
- **分析**：Dictionary ids 無 `statecode` 而 Collection ids 有 `statecode` 是歷史行為。後續若因程式碼重構被「統一化」，可能導致部分歷史查詢條件失效。
- **建議**：透過專門的單元測試明確鎖定此差異，禁止無意識的合併。

---

## 5. 最少驗收矩陣 (Minimum Acceptance Matrix)

| 驗收項目 | 驗收標準 (Acceptance Criteria) | 驗證方式 |
| :--- | :--- | :--- |
| **1. Host Golden 測試** | 原 host 的 2~3 個 golden 測試在重構前後維持全綠 | `dotnet test` 執行對應測試專案 |
| **2. 安全紅綠循環** | 依照 安全紅 → 修復 → 搬前綠後 git mv 流程執行 | Git 狀態與 CI 測試結果 |
| **3. 介面與行為相容** | 新增明確 `pageNumber` overload，舊 cookie 入口無 DTD、有界讀取且不存 cookie | 單元測試與靜態原始碼掃描 |
| **4. 資源生命週期** | Core service Dispose 僅清參照不 dispose SDK；Logger 不保存；Exception 不吞 | 程式碼審查與 Dispose 測試 |
| **5. 批次查詢預算控制** | 完整批次需有界分頁及全候選驗證 ID，超預算拒絕 | 負向測試驗證預算超支例外 |

---

## 6. 建議 (Recommendation)
**優先採用「介面隔離與明確 Overload 擴充」的保守策略**。在不破壞舊有呼叫端前提下，將純 SDK 邏輯（如 Attribute 轉換器、Query 服務、Collection、Activity 等）抽離至獨立邊界，嚴格遵守「不吞例外、不釋放借入 SDK、不保留 Cookie」的三大原則，並以完整的紅綠測試守衛歷史行為（如 filter 差異與週報日期）。
