VALIDATION REPORT
=================
User Experience: 18/20 - 網關 API 設計清晰，但滾動更新時的租約死鎖風險可能影響部署期間的可用性。
Visual Consistency: 19/20 - 規範結構完整，但多處檔案存在字元編碼損壞與亂碼問題。
Accessibility: 20/20 - 後端架構規範，無障礙性指標不適用（N/A），文檔語意結構良好。
Performance: 20/20 - 連線池隔離、並行限制與快取設計非常優異，效能指標與測試門檻定義明確。
Browser Compatibility: 20/20 - 後端 API 規範，不適用瀏覽器相容性，採用標準 HTTP/OData v4 協議。

TOTAL SCORE: 97/100

ISSUES FOUND:
- [Warning] 滾動更新死鎖風險：共享的 `ReplicaSlotLease` 槽位在滾動更新時可能導致新實例無法 Ready。
- [Warning] 複合鍵衝突風險：複合鍵直接拼接字串而無分隔符，存在潛在的鍵值衝突風險。
- [Warning] 租約續約失敗敏感度過高：單次續約請求失敗若立即觸發 Fail-Closed，會因網路抖動導致服務不穩定。
- [Info] 檔案字元編碼損壞：多個 Markdown 檔案中存在 `??`、`?ot`、`???€` 等亂碼字元。

RECOMMENDATION: PASS

================================================================================

# Dynamics Access Gateway 架構規範審查報告

## 1. Summary (整體評估)
本審查針對 Dynamics Access Gateway 架構設計規範（SPEC）進行評估。整體設計非常嚴謹，完整考慮了多產品共享連線、無 SDK 依賴、連線池隔離、並行限制、租約管理、等冪性帳本以及 CE 8.2/9.1 相容性等關鍵架構要求。設計方案技術可行性高，安全邊界清晰，能有效防止憑證洩漏與資源洩漏。

---

## 2. Accessibility Issues (無障礙性問題)
* **評估結果**：無。本規範為後端 API 與網關架構設計，不涉及前端 UI 介面，因此無障礙性（a11y）指標不適用（N/A）。

---

## 3. Design Issues (設計與架構問題)

### Finding 1: 滾動更新死鎖風險 (Rolling Update Deadlock Risk) - **Warning**
* **相關檔案/章節**：`design.md` Section 7.2.2 & `implement.md` Phase 3
* **問題描述**：規範中指出 `ReplicaSlotLease` 的命名空間為 `OrganizationAdmissionKey`，且由所有藍/綠/金絲雀版本共享，總槽數受限於 `MaximumGatewayReplicas`。在進行滾動更新（Rolling Update）時，新版本的 Replica 啟動並嘗試獲取租約槽以達到 `Ready` 狀態，但此時舊版本的 Replica 仍佔用著所有槽位。這會導致新版本無法通過健康檢查，進而導致部署死鎖。
* **具體建議**：明確規定當 Gateway 實例收到停機信號（如 `SIGTERM`）時，必須**立即釋放**其 `ReplicaSlotLease` 租約槽並停止接收新請求，然後才進入 Drain（排空）階段。這樣可以讓滾動更新中的新實例立即獲取釋放的槽位並變更為 `Ready`，在維持總並行上限的同時避免部署死鎖。

### Finding 2: 複合鍵衝突風險 (Composite Key Collision Risk) - **Warning**
* **相關檔案/章節**：`design.md` Section 7.1 & Section 9.3
* **問題描述**：`ProfileRuntimeKey`、`OrganizationAdmissionKey` 以及等冪性帳本鍵（Idempotency Ledger Key）皆由多個字串欄位直接拼接而成（例如 `authenticatedProduct + logicalProfileId + ...`）。若拼接時沒有使用明確的分隔符，可能會導致鍵值衝突（例如產品名為 `prod` 且 ID 為 `1`，與產品名為 `pro` 且 ID 為 `d1` 拼接後的結果相同）。
* **具體建議**：在規範中明確定義複合鍵的標準分隔符（例如 `:` 或 `|`）以及轉義規則，以防止鍵值衝突攻擊或非預期的鍵值重疊。

### Finding 3: 租約續約失敗的敏感度過高 (Lease Renewal Failure Sensitivity) - **Warning**
* **相關檔案/章節**：`design.md` Section 7.2.2 & `implement.md` Phase 4
* **問題描述**：規範指出若 `ReplicaSlotLease` 協調或續約失敗，實例必須立即停止新的 CRM 准入與重試。如果這被實現為「單次續約請求失敗即觸發」，則在面臨短暫的網路抖動時，會導致整個網關頻繁自我宣告為 `NotReady` 並停止服務，造成系統不穩定。
* **具體建議**：澄清「租約續約失敗」是指**租約過期（Lease Expiry）**（即租約 TTL 屆滿且在多次重試後仍無法完成續約），允許實例在租約 TTL 窗口內對短暫失敗的續約請求進行重試，避免因單次網路抖動導致服務中斷。

### Finding 4: 檔案字元編碼損壞 (Garbled Characters & Formatting) - **Info**
* **相關檔案/章節**：`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`, `design.md`, `implement.md`
* **問題描述**：多個 Markdown 檔案中存在編碼或字元損壞問題：
  * `2026-07-23-dynamics-access-gateway-design.md`：
    * Line 1: `??Architecture SPEC`
    * Line 185: `behavior?ot`
    * Line 220 & 225: `reference?ever`
    * Line 265: `D:\?唾?蝘??Ｗ?\蝟編絞撟喳\Dynamics 365 SDK DLL`
  * `design.md`：
    * Lines 70-79：目錄樹結構出現 `???€`、`???€`、`??` 等亂碼。
    * Line 636: `1??28-character`
  * `implement.md`：
    * 多處標題出現 `??`（如 `Phase 0 ??Baseline`）。
    * Line 170: `1??28-character`
* **具體建議**：修正這些損壞的字元，替換為標準的 ASCII/Unicode 字元（例如目錄樹使用 `├──`、`└──`、`│`，路徑使用 `D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL`，長度限制使用 `1-128`）。

---

## 4. Suggestions (改進建議)
1. **AdfsOAuth 驗證架構細化**：建議在 `design.md` 中進一步細化 `AdfsOAuth` 模式下所允許與禁止的欄位結構（如明確禁止 `Password` 欄位），以確保開發人員在實現配置驗證器時有統一的標準。
2. **等冪性帳本清理機制**：等冪性帳本的 TTL 應與業務重試窗口及對帳窗口對齊，建議在實施前由業務團隊確認具體的 TTL 數值（例如 24 小時），並在規範中加入對過期 `Pending` 狀態的自動清理/轉換邏輯說明。

---

## 5. Positive Notes (優秀設計點)
1. **嚴格的無 SDK 邊界**：規範明確禁止了任何對 `Dynamics 365 SDK DLL` 的依賴，並設計了 repository-wide 的 CI 掃描機制（包含 PowerShell 備用方案），確保不會有遺漏的 SDK 耦合。
2. **Fail-Closed 租約設計**：在租約協調器失效時立即停止新請求准入並標記為 `NotReady`，寧可短暫中斷服務也不允許超額並行壓垮 Dynamics，這是非常健全的架構決策。
3. **徹底的生命週期管理**：在配置重載時採用 Replace-and-Drain 機制，並在測試中引入弱引用哨兵（Weak-Reference Sentinels）來驗證舊世代對象的釋放，從根本上杜絕了記憶體與連線洩漏。
