已完整讀取四份規劃文件（`prd.md`、`design.md`、`implement.md`、`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`），並比對先前四輪審查（`spec-reviewer` → `followup` → `final` → `final-recheck`）留下的回歸檢查點與已知 Warning，以及本輪 Gemini 產出的初步報告。以下是我（Claude）獨立審查的結果。

---

## Dynamics Access Gateway 架構規範 — Release Review（Claude 獨立審查）

### 審查範圍
僅審查上述四份規劃文件本身，未修改任何程式碼、未審查工作區其他變更。

### 回歸檢查點覆核結果（8 項，本輪新增第 7 項 CohortId 相關措辭）

| 檢查點 | 落實位置 | 結論 |
| --- | --- | --- |
| ReplicaSlotLease fail-closed、無寬限期 | design.md §7.2.2、SPEC 規則 8 | ✅ 符合 |
| 僅開放 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` | design.md §5、SPEC「Outcome」 | ✅ 符合 |
| `AggregateMaxInFlight >= MaximumGatewayReplicas >= 1`、衍生本地併發、production 需 2 個就緒副本 | design.md §6.1.1、§7.2.1 | ✅ 符合 |
| `OrganizationAdmissionKey` 跨世代/別名/藍綠canary 共用預算 | design.md §7.1、§7.2.2 | ✅ 符合 |
| 冪等帳本原子鍵、固定配額、禁存原始內容、`OutcomeUnknown` 不自動重放 | design.md §9.3、implement.md Phase 2.6 | ✅ 符合 |
| Handler/proxy/header、single-flight 取消、佇列排空、遙測/快取遮蔽、洩漏門檻可測試 | design.md §7.2、§8.1、§9.3、§11 | ✅ 符合 |
| **`OrganizationAdmissionKey` 本身即為跨所有 release revision 的不可變租約命名空間**（取代舊版獨立的 `GatewayCapacityCohortId`），並使用嚴格 `HostIdentity` vs `SecretReference` 聯集使 gMSA/Kerberos 免密碼 | design.md §7.2.2（"the lease namespace is exactly `OrganizationAdmissionKey`"）、§6.1（兩組互斥 JSON 範例） | ✅ 符合 — 已確認先前 `final-recheck` 提出的兩個 Warning（`GatewayCapacityCohortId` 供應規則缺失、Windows schema 與 gMSA 免密碼矛盾）**均已修正**：規範已移除獨立 CohortId 概念，直接把 `OrganizationAdmissionKey`（deploymentEnvironment + expectedOrganizationId）定為租約命名空間，且 §6.1 已提供 `HostIdentity`（無密碼欄位）與 `SecretReference`（僅限非人類服務帳號）兩組互斥範例。 |
| CE 8.2/9.1 用語具實證、認證為可行性門檻、不宣稱 on-prem client-secret | design.md §6.3 | ✅ 符合 |

---

### 🔴 Critical
無。本輪未發現會造成秘密外洩、跨 Profile 路由、或無界資源成長的釋出阻斷級缺陷。

### ⚠️ Warning

**1. 複合鍵（Composite Key）皆以欄位直接串接定義，全文件未規定分隔符/跳脫規則**
- 位置：`design.md:348-351`（`ProfileRuntimeKey = profileId + configurationGeneration + apiVersion + normalized organization origin + authMode + secretVersionFingerprint`）、`design.md:363`（`OrganizationAdmissionKey = deploymentEnvironment + expectedOrganizationId`）、`design.md:624-626`（冪等帳本鍵 `authenticatedProduct + logicalProfileId + expectedOrganizationId + capabilityOperationId + idempotencyKey`）。
- 問題：全文檢索 `delimiter`/`separator`/`escap` 均無結果——三組複合鍵都只用 `+` 表示邏輯串接，未定義實際分隔符或跳脫規則。`apiVersion`/`expectedOrganizationId` 是受限枚舉或 GUID，碰撞風險低；但 `logicalProfileId`、`capabilityOperationId`、`authenticatedProduct` 是自由命名字串，理論上 `("appA","1")` 與 `("app","A1")` 直接串接可能產生相同鍵值，導致冪等帳本或 Profile 執行期物件被誤判為同一鍵。這與規範反覆強調的「zero-tolerance cross-profile/session/credential leakage」目標直接相關，因為鍵值碰撞正是造成跨 Profile 狀態互相干擾的具體攻擊面之一。
- 修正建議：在 `design.md` §7.1、§7.2.2、§9.3 明確定義複合鍵的具體序列化格式（例如固定分隔符 `|` 並對欄位值做長度前綴或跳脫），並在 implement.md Phase 2/2.6 加入一項「複合鍵碰撞防護」單元測試需求。

**2. Rolling update／正常關機時的 `ReplicaSlotLease` 釋放時機未定義，可能造成部署期間的可用性風險**
- 位置：`design.md` §7.2.2（第 426-452 行）只定義了「lease 續約失敗/協調器不可用」時的 fail-closed 行為；`implement.md` Phase 4 僅將「replica termination」列為故障注入測試項目，但 design.md 全文未出現任何「graceful shutdown」「SIGTERM」「rolling update」時主動釋放租約的規則。
- 問題：`MaximumGatewayReplicas` 同時受 IaC/HPA 與 `ReplicaSlotLease` 雙重限制，且「超額 process 保持 NotReady」（SPEC 規則 7）。若部署平台採用滾動更新（maxSurge > 0），新版本副本在舊副本仍持有租約期間會停留在 NotReady，直到舊副本主動釋放租約或租約 TTL 到期——但規範沒有規定終止中的副本必須在進入 Drain 前主動釋放其 `ReplicaSlotLease`。若租約 TTL 設得較長，每次正常部署都可能因此延長或卡住，這與「high availability」的硬性品質要求（清單第 3 項）存在落差，且不是可安全遞延到實作階段自行決定的細節，因為它會影響「Coordinator 必須支援 atomic conditional create/renew/release」這個既有設計決策的完整性（release 語意本應包含「正常終止時的主動釋放」）。
- 修正建議：在 `design.md` §7.2.2 補充一條規則：Gateway 進程收到終止信號時，必須在停止接受新請求、進入 Drain 之前，透過協調器的 `release` 操作主動釋放其 `ReplicaSlotLease`；並在 `implement.md` Phase 3/4 把「正常終止主動釋放租約，使滾動更新中的新副本能立即變為 Ready」列為明確驗收條件（而不僅是「replica termination」故障測試）。

### ℹ️ Info

**1. Gemini 本輪報告指出的「字元編碼損壞」（`??`、`�` 亂碼）經 Claude 直接讀檔覆核，判定為誤報**
- 位置：Gemini 報告引用的 `design.md:70-79` 目錄樹、`implement.md` 各 Phase 標題、路徑字串等。
- 說明：以 Read 工具直接讀取上述行號，box-drawing 字元（`├──`、`└──`、`│`）、em dash（`Phase 0 — Baseline`）、en dash（`1–128-character`）與中文路徑 `D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL` 均正確顯示，Grep 對 `\?\?|�` 模式在整個任務資料夾內也查無匹配。研判是 Gemini CLI 自身讀檔管線在處理含 CJK 路徑/檔名時的編碼問題，非規範檔案本身缺陷，不需修正規範內容。

**2. `PreAuthenticate` 停用對 Windows/IWA 效能影響的攤提說明仍未補充（沿用前次審查 Info 項）**
- 位置：`design.md:387`（§7.2 表格）、`docs/.../2026-07-23-dynamics-access-gateway-design.md:164-167`。
- 說明：NTLM/Kerberos 挑戰綁定於 TCP 連線而非逐請求發生，設計已規劃長連線重用，因此停用 `PreAuthenticate` 的實際成本遠低於字面「每次呼叫雙倍 RTT」的印象；建議在效能章節補一句攤提說明，避免實作階段誤解而過早啟用。此為可安全遞延的文字澄清，不影響架構完整性。

**3. `ReplicaSlotLease` 協調器續約失敗的判定粒度（單次 RPC 失敗 vs 租約 TTL 到期）未明確**
- 位置：`design.md:445-452`（§7.2.2）。
- 說明：文字為「lease renewal fails or the coordinator becomes unavailable」即立即 fail-closed，未區分「單次續約請求失敗」與「租約 TTL 視窗內重試後仍失敗（即真正到期）」。若實作把它理解為單次網路抖動就觸發 fail-closed，會造成不必要的服務不穩定。這屬於實作階段的重試/退避機制設計，不牴觸現有零容忍政策的原始意圖，可安全遞延，但建議在 §7.2.2 補一句「fail-closed 觸發條件是租約在其 TTL 視窗內重試耗盡後仍無法續約，而非單次續約 RPC 失敗」以避免實作誤讀。

---

### 總結
本輪未發現 Critical 缺陷。先前 `final-recheck` 提出的兩個 Warning（`GatewayCapacityCohortId` 供應規則缺失、Windows 認證 schema 與 gMSA 免密碼託管的矛盾）**已在目前文件中確認修正**：`OrganizationAdmissionKey` 直接作為租約命名空間，Windows 認證改為 `HostIdentity`/`SecretReference` 嚴格互斥聯集。

本輪新發現兩項 Warning：(1) 三組複合鍵缺乏分隔符/跳脫規則的碰撞風險，(2) 正常終止/滾動更新時缺少「主動釋放租約」規則，可能造成部署期間可用性卡頓。兩者都與規範自身宣稱的「zero-tolerance cross-profile leakage」與「high availability」硬性要求直接相關，建議在下一輪修訂中補上具體文字後即可視為可發布（RECOMMENDATION: 有條件通過，待兩項 Warning 修正）。

---
SESSION_ID: adea78f6-982d-4526-b01c-c4f5dc62ad50
