# Dynamics Access Gateway 架構設計規範最終審查報告

本報告針對 Dynamics Access Gateway 的規劃與設計文件進行架構與安全性審查。由於本任務為後端服務與 API Gateway 的架構設計審查，不涉及前端 UI/UX，因此審查重點將聚焦於系統架構設計一致性、安全性隔離、效能邊界、以及遷移可行性。

---

## 1. 總體評估 (Summary)

本次審查的規劃文件（包含 PRD、詳細設計、實作計畫及架構 SPEC）結構完整且技術論證嚴密。設計方案捨棄了傳統依賴 CRM SDK DLL 或 WCF SOAP 的做法，改採**「獨立 Gateway 服務 + 私有無 SDK Web API 核心庫」**的混合架構，為 5 到 10 個以上產品的 Dynamics 365 (CE 8.2/9.1) 整合提供了極具說服力的技術方案。

先前審查階段要求的所有**回歸檢查點（Regression Checks）已全數落實**，包含：
- `ReplicaSlotLease` 協調器失效時的 **Fail-Closed** 機制（立即停止准入與重試，變更為 NotReady，僅排空既有工作）。
- 嚴格限制產品端呼叫格式為 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}`，杜絕調用端注入 CRM schema 或 OData 語法。
- 靜態派生 `LocalMaxInFlight`，並強制生產環境至少配置 2 個 ready-capable 副本。
- 引入 `OrganizationAdmissionKey` 跨代與跨別名共享併發預算，防止 reload 期間併發加倍。
- 設計了具備原子鍵值、固定配額、不儲存敏感資訊且對 `OutcomeUnknown` 狀態不自動重試的**跨副本等冪帳本**。
- 制定了詳盡的連線池代際隔離、單飛（Single-flight）快取更新、遙測去識別化與弱引用哨兵測試機制。

---

## 2. 審查問題回覆 (Review Questions)

### Q1: 提案的 Gateway + 私有 WebApi 庫是否合理？替代方案是否被具體理由拒絕？
* **是。** 設計文件（`design.md` Section 2）詳細對比了三種方案。拒絕「各產品獨立引用 Library」是因為這會導致憑證分發、連線池、快取與相容性邏輯在多個產品中重複，增加洩漏與配置漂移風險；拒絕「通用透明代理」是因為它會洩漏 CRM schema 控制權並擴大攻擊面。最終選擇的「受控 Gateway」能有效集中安全邊界與運行時狀態。

### Q2: 運行時狀態與生命週期是否由足夠的不可變 Profile 鍵值隔離？
* **是。** 運行時狀態（HttpClient、憑證、快取、重試狀態）均與不可變的 `ProfileRuntimeKey` 綁定。該 Key 包含配置世代與秘密版本指紋，確保配置或憑證變更時會創建全新世代並排空舊世代。組織併發則由非秘密的 `OrganizationAdmissionKey` 隔離，確保新舊代重疊時併發預算不超標。

### Q3: 是否存在跨 Profile 路由、秘密洩漏、調用端逃逸或不安全自動重試的漏洞？
* **否。** 
  * 產品端僅能傳遞邏輯別名與操作 ID，無法指定 CRM 實體、URL 或 Header，杜絕了調用端逃逸。
  * JSON 配置僅包含秘密引用，原始秘密在運行時解析且不記錄於日誌或遙測。
  * 舊世代排空設有嚴格截止時間，並在測試中以弱引用哨兵驗證釋放，防止滯留洩漏。
  * 等冪帳本對不確定結果（OutcomeUnknown）不進行自動重試，僅允許具備 CRM 替代鍵/upsert 的操作進行重試。

### Q4: CE 8.2/9.1 API 版本與認證限制描述是否安全？
* **是。** 設計明確區分了 Windows/IWA（AD）與 AdfsOAuth（IFD）的適用場景與可行性門檻，不假設 on-premise 支援 client-secret，且明確拒絕使用 ROPC 或儲存用戶密碼，亦不退回到舊的 WS-Trust/SOAP。

### Q5: 效能與高可用宣稱是否具備邊界、可測試且相容服務保護？
* **是。** 設計定義了明確的 SLO（如 Gateway 額外延遲 p95 < 5ms），並透過 `LocalMaxInFlight` 靜態分配與 `ReplicaSlotLease` 租約限制總併發。實作計畫中規劃了 fake-server 高併發測試、soak 測試與故障注入測試來驗證這些邊界。

### Q6: 遷移範圍、no-SDK 強制檢查與發布門檻是否具體？
* **是。** 文件誠實列出了現有的 SDK 耦合（如 HintPath 違規與約 200 個源文件），並在 `design.md` Section 12.3 提供了具體的 PowerShell/ripgrep 掃描命令作為 CI 強制門檻。

### Q7: 是否存在矛盾、缺失決策或危險假設？
* 詳見下方 Warning 與 Info 發現。設計中已將高風險點（如 Windows/IWA 託管模式、OAuth 授權流）設為可行性門檻，避免了過早做出生產決策的風險。

---

## 3. 審查發現清單 (Findings)

### 🔴 Critical Issues
* **無。** 設計規範完全符合硬性品質要求，且已落實所有回規檢查點。

### ⚠️ Warning Issues

#### 1. `PreAuthenticate` 禁用對 Windows/IWA 效能的潛在影響
* **文件路徑**：`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md` (Section: Compatibility and configuration) & `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 7.2)
* **合理性分析**：設計中規定 `PreAuthenticate` 預設禁用，除非 Windows/IWA 煙霧測試證明其安全。在 NTLM/Kerberos (IWA) 認證中，如果禁用 `PreAuthenticate`，HttpClient 對同一個連線的每次請求都會先收到 401 Unauthorized 挑戰，然後再發送帶有認證標頭的請求。這會導致每次 API 呼叫產生雙倍的 HTTP 往返時間（RTT），對效能有顯著影響。
* **修正建議**：在設計文件中補充說明：「若環境煙霧測試確認安全，應在 Windows/IWA Profile 中明確啟用 `PreAuthenticate`，以避免雙倍 RTT 效能懲罰。」

### ℹ️ Info Issues

#### 1. `ReplicaSlotLease` 協調器技術選型未定
* **文件路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` (Phase 3.5)
* **合理性分析**：設計中引入了 `ReplicaSlotLease` 機制來限制副本數並實現 fail-closed，但未明確指定協調器（如 Redis、Consul、Kubernetes Lease API 或資料庫鎖）的技術選型。
* **建議**：這屬於可延遲的決策，但建議在實作計畫中註明，在 Phase 1 或 Phase 2 啟動時，需先評估並選定符合基礎設施架構的租約協調器。

#### 2. HPA 與靜態 `LocalMaxInFlight` 的運維折衷
* **文件路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 7.2.1)
* **合理性分析**：`LocalMaxInFlight` 是由 `AggregateMaxInFlight / MaximumGatewayReplicas` 靜態派生。如果 Kubernetes HPA 動態增加副本數超過 `MaximumGatewayReplicas`，多餘的副本將無法取得租約而保持 NotReady。這是一個為了保護 Dynamics 服務而做出的安全折衷，但會限制 Gateway 的彈性伸縮能力。
* **建議**：在運維與部署手冊中特別註明此限制，確保 HPA 的 MaxReplicas 設定與 Profile 中的 `MaximumGatewayReplicas` 保持同步，避免因自動伸縮導致新副本無法提供服務。

---

## 4. 值得肯定的地方 (Positive Notes)

1. **極為嚴格的資源與記憶體洩漏防護**：設計中不僅要求排空與處置，還具體規劃了在測試中使用「處置計數器」與「弱引用哨兵（weak-reference sentinels）」來驗證垃圾回收，這在 .NET 專案中是非常優秀且少見的嚴格實踐。
2. **防禦性 API 設計**：完全封鎖了調用端傳遞自訂 OData 查詢或 CRM schema 的能力，將 Gateway 定位為「受控能力提供者」而非「透明代理」，極大地降低了安全風險與系統耦合度。
3. **務實的遷移規劃**：實作計畫沒有採取「一次性替換」的危險做法，而是規劃了 Phase 5 的「絞殺者模式（Strangler migration）」，允許逐個操作、逐個產品進行漸進式遷移與驗證。
