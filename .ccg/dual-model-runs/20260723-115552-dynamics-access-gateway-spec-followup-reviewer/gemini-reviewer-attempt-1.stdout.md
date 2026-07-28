# Dynamics Access Gateway 架構規格書審查報告

## 1. 總體評估 (Summary)
本規格書針對五至十個產品的 Dynamics 365 整合需求，提出了一個技術上非常嚴密且合理的 **Dynamics Access Gateway (SpeechMessage.Dynamics.sln)** 混合架構設計。該設計成功地將 CRM SDK 依賴、憑證管理、連線池生命週期、版本相容性邏輯以及安全邊界集中於 Gateway 服務中，並透過私有的無 SDK Web API 庫進行 direct-HTTP/OData v4 通訊。

整體設計在**安全性（零容忍洩漏、憑證不落地）**、**隔離性（基於 Generation Key 的 replace-and-drain 機制）**以及**效能控制（併發預算與限制器）**上均有極高的標準與具體的執行步驟，遷移路徑與 CI 掃描閘門也十分明確。

---

## 2. 審查問題回覆 (Review Questions Answers)

1. **Gateway + 私有無 SDK 庫的合理性與替代方案拒絕原因**：
   * **是**。針對 5-10 個產品的規模，此 hybrid 設計非常合理。規格書明確拒絕了「各產品獨立引用庫」（會導致憑證與連線狀態多份拷貝、漂移風險高）與「通用透明代理」（會洩漏 CRM 結構、擴大攻擊面且審計困難），拒絕理由具體且符合架構最佳實踐。
2. **隔離 Key 的充分性**：
   * **是**。設計中定義了 `ProfileRuntimeKey`（包含 profileId、generation、apiVersion、origin、authMode、secretVersionFingerprint），所有 HTTP handler、HttpClient、Token 快取、元數據快取、重試與限制器狀態均以此 Key 進行嚴格隔離，reload 生命週期亦採用 replace-and-drain 機制，確保無原地修改（mutation-in-place）的風險。
3. **逃逸、洩漏、殘留與不安全重試的路徑防範**：
   * **是**。產品端僅能透過 workload identity 映射至 logical alias，無法自訂 endpoint/header/profile，防範了路由逃逸；憑證僅以 reference 方式解析；舊 generation 設有 drain 逾時與強制釋放機制；寫入操作禁止盲目重試，必須有明確的冪等性設計（如 alternate-key 或 ledger）。
4. **CE 8.2/9.1 版本與驗證限制描述的安全度**：
   * **是**。明確區分了 8.2 與 9.1 的 API root，且不進行自動升級。驗證方面明確區分了 Windows/IWA 與 AdfsOAuth，且明文禁止 ROPC 與 WS-Trust SOAP 回退，未過度承諾 Dataverse 的 client-secret 機制在 on-premises 的適用性。
5. **效能與高可用性聲明的邊界與相容性**：
   * **是**。設計了 `AggregateMaxInFlight` 與 `MaximumGatewayReplicas` 來推導本地限制，並在分散式限制器失效時提供保守回退，確保不超出 Dynamics 服務保護限制。定義了明確的效能基準目標（如 Gateway 額外開銷 p99 < 15ms）。
6. **遷移範圍、no-SDK 強制檢查與測試閘門的具體性**：
   * **是**。識別了現有的 HintPath 與 `Microsoft.Xrm` 耦合，提供了具體的 `rg` 掃描指令與 PowerShell 回退機制，並在 `implement.md` 中定義了 Phase-based 的審查閘門。
7. **矛盾、遺漏決策或危險假設**：
   * 發現了關於 **Autoscaling 併發超標**、**Linux 容器部署下的 IWA 驗證可行性** 以及 **Idempotency Ledger 跨實例共享** 的潛在風險，詳見下方 Findings。

---

## 3. 審查發現 (Findings)

### ⚠️ Warning: Linux 容器部署環境與 Windows/IWA 驗證的相容性可行性閘門缺失
* **相關檔案/章節**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` - Preconditions & Phase 0
* **具體問題**：
  規格書提到對於 CE on-premises AD 部署將使用 `Windows` 驗證模式（IWA），並指出需要「Validate hosting OS and network/IWA support」。然而，現代微服務通常部署於 Linux 容器（如 Kubernetes/Docker）。在 Linux 環境下執行 Windows 整合驗證（IWA）需要複雜的 Kerberos keytab 或 gMSA 配置，這在實務上常成為重大的技術阻礙。規格書未將「Gateway 部署之目標 OS 平台與 Dynamics IWA 驗證的相容性」明確列為 Phase 0 的硬性可行性評估閘門（Feasibility Gate）。
* **規格修正建議**：
  在 `implement.md` 的 **Preconditions** 或 **Phase 0** 中，新增一項明確的評估要求：
  > "確認 Gateway 的部署目標平台（如 Linux 容器）與目標 Dynamics 8.2 所需的 Windows/IWA 驗證機制（如 Kerberos keytab 或 gMSA）之相容性與可行性。若目標平台無法原生支援該驗證，必須在 Phase 1 開始前調整部署架構或驗證方案。"

---

### ⚠️ Warning: 副本自動擴充（Autoscaling）與 AggregateMaxInFlight 併發預算的潛在衝突
* **相關檔案/章節**：`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md` - Non-negotiable runtime rules (Rule 7) & `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` - Section 7.2.1
* **具體問題**：
  規格書指出：「The deployment must not autoscale beyond MaximumGatewayReplicas without recalculating and publishing a new validated profile generation.」並以此計算 `LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumGatewayReplicas)`。
  然而，在雲端或 Kubernetes 環境中，Autoscaling 通常由基礎設施（如 HPA）根據 CPU/Memory 自動觸發。如果因為突發流量導致 Replica 數量自動擴展超過 `MaximumGatewayReplicas`，且此時分散式限制器剛好失效，則各 Replica 的本地限制加總（`LocalMaxInFlight * 實際 Replica 數`）將會超出 Dynamics 的 `AggregateMaxInFlight` 服務保護預算，進而導致 Dynamics 拒絕服務。
* **規格修正建議**：
  在 `design.md` 第 7.2.1 節中補充 Gateway 節點的自我保護機制：
  > "若 Gateway 實例啟動或運行時檢測到當前叢集內 active replicas 數量超過 `MaximumGatewayReplicas`，各實例應自動動態調降其 `LocalMaxInFlight`（例如調整為 `floor(AggregateMaxInFlight / 實際 replicas 數)`），或拋出 Critical 級別的監控警報並限制自身流量，以確保總併發絕對不超出 Dynamics 服務保護限制。"

---

### ℹ️ Info: 冪等性帳本（Idempotency Ledger）的跨實例共享機制未明確
* **相關檔案/章節**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` - Section 10 (Performance strategy)
* **具體問題**：
  規格書提到對於非冪等寫入操作，將使用「bounded product+profile+operation+idempotency-key ledger」來儲存請求指紋與結果。由於 Gateway 規劃為至少雙副本（two-replica）部署，若此 Ledger 僅實作為 process-local（記憶體內快取），當同一個重試請求被負載均衡器路由到不同的 Gateway 實例時，將無法發揮冪等保護作用。
* **規格修正建議**：
  在 `design.md` 第 10 節中補充說明：
  > "明確指出 Idempotency Ledger 必須採用跨實例共享的持久化或分散式快取機制（例如 Redis 或共享資料庫），若採用 process-local 實作，則必須配合黏性會話（Sticky Sessions）路由，並說明其在高可用切換時的冪等失效風險。"

---

## 4. 值得肯定的地方 (Positive Notes)
* **Replace-and-Drain 機制設計完善**：採用不可變的 `ProfileRuntimeKey` 與 replace-and-drain 重新載入流程，徹底杜絕了運行時動態修改憑證或連線配置所引發的狀態不一致與連線洩漏問題。
* **嚴格的無 SDK 邊界**：產品端完全不引用 `SpeechMessage.Dynamics.WebApi` 庫，僅透過 OpenAPI REST 進行通訊，這使得產品端能徹底擺脫 `Microsoft.Xrm` 等舊 SDK 類型耦合，遷移邊界非常乾淨。
* **防範 OData 代理逃逸**：採用 Operation Registry 限制查詢形狀，而非提供通用的 OData 轉發代理，這在安全性與審計合規性上是非常優秀的決策。
