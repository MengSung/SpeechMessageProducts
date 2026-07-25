# Dynamics Access Gateway 架構規格書評審報告

本評審報告針對 `dynamics-access-gateway-spec-final` 規劃產出物進行架構與設計審查。審查範圍包含：
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`

---

## 1. 總體評估 (Summary)

本架構設計非常詳盡且具備高度的工程嚴謹性。設計方案捨棄了在多個產品中複製 SDK 依賴與憑證的傳統做法，改採**集中式 Gateway + 私有無 SDK Web API 程式庫**的混合架構，這對於 5 到 10 個產品的規模而言在技術上是非常合理的決策。

設計中對於**連線池隔離、不可變設定檔世代（Generation）切換、零容忍洩漏閘門、以及明確的版本路由與探針驗證**等關鍵安全與效能需求，皆給出了具體且可執行的技術方案。整體架構設計健全，能有效達成移除 `Dynamics 365 SDK DLL` 依賴的最終目標。

---

## 2. 審查問題回覆 (Review Answers)

### 問題 1：Gateway + 私有無 SDK WebApi 程式庫是否合理？是否具體拒絕了其他替代方案？
* **回覆**：**是，技術上非常合理。**
  * 規格書在 `design.md` 第 2.2 與 2.3 節中，基於本地程式碼現況（如 `ChurchReport` 的單一設定檔 SOAP 連線池限制）進行了對比。
  * **拒絕 Option A (僅 Library)** 的理由：避免在 5-10 個產品中重複管理憑證、連線狀態、Token 快取、重試與版本相容邏輯，降低洩漏與配置漂移風險。
  * **拒絕 Option B (通用透明代理)** 的理由：防止呼叫端傳遞任意 OData 查詢或標頭，避免 CRM 綱要洩漏並收斂攻擊面。
  * 最終選擇 **Option C (受控 Gateway)** 以集中安全邊界與執行期狀態。

### 問題 2：HTTP 處理器、憑證、Token 快取、元資料快取、重試/熔斷、併發狀態及重載生命週期是否由足夠的不可變 Key 隔離？
* **回覆**：**是。**
  * `design.md` 第 7.1 節定義了 `ProfileRuntimeKey`，其包含 `profileId`、`configurationGeneration`（不可變設定世代）、`apiVersion`、`normalized organization origin`、`authMode` 與 `secretVersionFingerprint`（機密版本指紋）。
  * 該 Key 確保了當設定或機密變更時，會產生全新的執行期實例，並透過 Replace-and-Drain 機制安全汰換舊實例，絕不在原地（in-place）修改 active 狀態，從根本上杜絕了狀態交叉污染。

### 問題 3：設計是否留有跨設定檔路由、機密洩漏、呼叫端逃逸、殘留洩漏、陳舊執行期變異或不安全自動重試的漏洞？
* **回覆**：**否，設計已進行嚴格收斂。**
  * **路由與逃逸**：產品呼叫端僅能使用 workload identity 與邏輯別名（alias），由 Gateway 在伺服器端映射至實體設定檔，呼叫端無法傳遞自訂 URL、標頭或未授權的 profile 名稱。
  * **機密洩漏**：設定檔僅包含機密名稱引用，實際值由執行期從機密提供者解析，且禁止記錄於日誌與遙測中。
  * **殘留洩漏**：定義了零容忍釋放閘門（`design.md` 第 7.5 節），要求測試已退休的 runtime 是否徹底釋放 timer、handler 與 stream。
  * **不安全重試**：非等冪寫入必須使用 CRM 替代鍵/Upsert 或分散式等冪帳本，否則禁止自動重試。
  * *註：關於租約到期後的行為有微小缺失，已列於下方 Warning 發現中。*

### 問題 4：CE 8.2/9.1 API 版本與驗證限制描述是否安全？是否避免了 client-secret 或 WS-Trust 盲目假設？
* **回覆**：**是。**
  * `design.md` 第 6.3 節明確區分了 Windows/IWA（用於 AD）與 AdfsOAuth（用於 IFD），並強調不支援 ROPC 與 WS-Trust 回退。
  * 規格明確指出 client-secret/certificate 驗證是 Dataverse-only 特性，不盲目承諾用於 CE on-premises。
  * Windows/IWA 設定檔必須在 Phase 0/1 通過實體環境的託管模式冒煙測試，否則該 profile 保持不可用。

### 問題 5：效能與高可用性聲明是否有界限、可測試且與 Dynamics 服務保護相容？
* **回覆**：**是。**
  * 透過 `AggregateMaxInFlight` 與 `MaximumGatewayReplicas` 計算出每台 replica 的保守本地分配額度（`LocalMaxInFlight`），在分散式限制器失效時能安全回退，避免超載 Dynamics。
  * `design.md` 第 10 節定義了明確的延遲與快取命中率效能指標，並在第 11.2 節規劃了併發、熔斷與負載測試。

### 問題 6：遷移範圍、無 SDK 強制檢查及測試/釋放閘門是否具體？
* **回覆**：**是。**
  * 規格書誠實列出了現有專案（如 `ChurchReport` 的 HintPath 違規與 `ToolUtility` 的套件依賴）的耦合現況。
  * 提供了具體的 `ripgrep` 與 PowerShell 掃描指令作為 CI 強制閘門，確保遷移後無任何 SDK 殘留。

### 問題 7：是否存在矛盾、遺漏的明確決策或危險假設？
* **回覆**：整體設計非常嚴密，僅有少數關於「租約到期後行為」與「Linux 驗證順序」的細節需要微調，已列於下方發現中。

---

## 3. 評審發現 (Findings)

### ⚠️ Warning (警告)

#### 1. ReplicaSlotLease 租約寬限期到期後的具體行為未定義
* **相關檔案/章節**：`design.md` 第 7.2.2 節 (Replica-admission enforcement) 與 `implement.md` Phase 3 第 5 點。
* **原因說明**：規格書提到「existing leased replicas continue only with their fixed conservative local allocation until their emergency lease grace period expires.」（現有的已租約副本僅在緊急租約寬限期到期前，繼續以其固定的保守本地分配運行）。然而，規格並未明確定義當緊急租約寬限期（emergency lease grace period）**到期後**，該副本的具體行為。是應該拒絕所有 Dynamics 請求（fail-closed），還是繼續以保守分配運行但發出高警報？為了防止在協調器長期故障時超載 Dynamics，應明確規定寬限期到期後必須拒絕新的 Dynamics 請求（fail-closed）。
* **建議修正**：在 `design.md` 第 7.2.2 節與 `implement.md` Phase 3 第 5 點中補充說明：「當緊急租約寬限期到期且無法重新取得租約時，該 Gateway 副本必須停止向 Dynamics 發送任何新請求（Fail-Closed），並回傳服務不可用錯誤，直到租約成功更新為止。」

---

### ℹ️ Info (提示)

#### 1. Linux 託管環境下 Kerberos/gMSA 的可行性驗證順序建議提前
* **相關檔案/章節**：`design.md` 第 6.3 節 (Authentication feasibility gates) 與 `implement.md` Preconditions。
* **原因說明**：規格將 Linux 託管下的 Kerberos/keytab/gMSA 測試列為 Windows/IWA 設定檔可用的前提條件（feasibility probe）。由於 Linux 上的 Kerberos 整合在實務上極具挑戰性（涉及 keytab 管理、SPN 註冊、DNS 配置等），建議在 `implement.md` 的 Phase 0 或 Phase 1 早期，將此環境驗證列為硬性阻塞點（Hard Gate），以避免在未驗證 Linux 託管可行性前投入過多 Gateway 開發資源。
* **建議修正**：在 `implement.md` 的 Preconditions 中，明確將「Linux 託管環境下的 Kerberos/gMSA 冒煙測試成功」列為進入 Phase 1 的硬性准入條件，而非僅作為一般前提。

#### 2. 冪等性帳本 (Idempotency Ledger) 的容量限制與清理機制
* **相關檔案/章節**：`design.md` 第 9.3 節 (Metrics and alerts) 與 `implement.md` Phase 2 第 6 點。
* **原因說明**：雖然規格提到等冪性帳本使用固定 TTL，但未明確說明帳本本身的容量上限與清理機制。若發生高併發寫入，帳本可能因累積大量雜湊值而面臨效能瓶頸。
* **建議修正**：在規格中補充說明等冪性帳本的容量限制與自動清理（Eviction/Cleanup）策略，例如設定最大 Key 數量限制，並在達到上限時採用 LRU 淘汰或拒絕寫入。

---

## 4. 評審結論 (Recommendation)

**[PASS WITH WARNINGS / 建議通過但需修正警告項目]**

本架構設計在技術可行性、安全性與效能隔離上均達到了極高的標準。只要針對上述 **Warning** 中關於 `ReplicaSlotLease` 寬限期到期後的 Fail-Closed 行為進行規格補充，即可正式批准此設計並啟動實作階段。
