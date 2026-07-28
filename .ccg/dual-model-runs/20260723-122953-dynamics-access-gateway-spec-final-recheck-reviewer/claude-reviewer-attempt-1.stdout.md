# Dynamics Access Gateway 架構規範 — 最終複查報告（Claude 獨立審查）

## 審查範圍確認

已完整讀取四份文件：`prd.md`、`design.md`、`implement.md`、
`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`，並比對先前審查回合（`final-reviewer`、`followup-reviewer`）留下的回歸檢查點。本次僅審查規劃文件本身，未修改任何程式碼。

## 回歸檢查點覆核結果

七項回歸檢查點均已在文件中找到具體、可對應的落實段落，判定為**已解決**：

| 檢查點 | 落實位置 | 結論 |
| --- | --- | --- |
| ReplicaSlotLease fail-closed、無寬限期 | design.md §7.2.2；SPEC 規則 8 | 符合 |
| 僅開放 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` | design.md §5；SPEC「Outcome」段 | 符合 |
| `AggregateMaxInFlight >= MaximumGatewayReplicas >= 1`、衍生本地併發、生產需 2 個就緒副本 | design.md §6.1.1、§7.2.1 | 符合 |
| `OrganizationAdmissionKey` 跨世代/別名/藍綠canary 共用預算 | design.md §7.1、§7.2.2 | 符合（但見下方 Warning） |
| 冪等帳本原子鍵、固定配額、禁存原始內容、`OutcomeUnknown` 不自動重放 | design.md §9.3；implement.md Phase 2.6 | 符合 |
| Handler/proxy/header、single-flight 取消、佇列排空、遙測/快取遮蔽、洩漏門檻可測試 | design.md §7.2、§8.1、§9.3、§11 | 符合 |
| CE 8.2/9.1 用語具實證、認證為可行性門檻、不宣稱 on-prem client-secret | design.md §6.3 | 符合 |

## 發現清單

### 🔴 Critical
無。文件在硬性品質要求與回歸檢查點上未發現可造成秘密外洩、跨 Profile 路由或無界資源成長的釋出阻斷級缺陷。

### ⚠️ Warning

**1. `GatewayCapacityCohortId` 從未定義來源與供應規則，但整個藍綠/canary 併發防重複機制完全依賴它**
- 位置：`design.md:406`（§7.2.2）、`implement.md:201`（Phase 3.5）、
  `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md:168`
- 問題：三處文件都把 `ReplicaSlotLease` 的命名空間定義為
  `GatewayCapacityCohortId + OrganizationAdmissionKey`，並宣稱這能讓「blue/green/canary revisions 競爭同一組織容量，而非各自取得不安全的獨立副本上限」。但**沒有任何段落說明 `GatewayCapacityCohortId` 由誰指派、如何配置、以及如何驗證同一組織的所有部署版本（blue/green/canary）確實共用相同值**。如果部署樣板不慎讓不同顏色版本各自帶入不同的 CohortId（例如把它綁定到 release/revision 名稱而非組織/環境），則 §7.2.2 宣稱的「無法讓 reload 或 rollout 使聚合併發加倍」的安全保證會在無任何測試偵測的情況下失效——這正是此鍵值本應防止的失效模式。
- 修正建議：在 `design.md` §7.2.2 與 `implement.md` Phase 3 補充：`GatewayCapacityCohortId` 的具體衍生規則（例如 `deploymentEnvironment + expectedOrganizationId` 本身，或由部署平台以組織/環境為單位統一注入的常數），並新增一項啟動期驗證/契約測試，斷言同一組織的所有並行修訂版本解析出相同的 CohortId，否則拒絕就緒。

**2. JSON Profile schema 的 `Windows` 認證模式範例始終要求 `UserNameSecretName`/`PasswordSecretName`，與 gMSA 託管模式的設計意圖矛盾**
- 位置：`design.md:163-168`（§6.1 範例 JSON，`church-ce82-prod` 的 `Authentication.Mode: "Windows"`）對照 `design.md:293-303`（§6.3，Windows/IWA 的兩種核准託管模式：Windows 服務身分，或 Linux gMSA/keytab）
- 問題：§6.3 明確把 gMSA/keytab 列為核准的 Windows/IWA 託管方式之一，而 gMSA 的核心安全價值正是**不需要應用層保存/輪替任何密碼**（憑證由 AD 自動管理，對行程透明）。但 §6.1 給出的唯一 JSON Profile 範例，其 `Windows` 認證模式固定包含 `UserNameSecretName`、`PasswordSecretName`、`DomainSecretName` 三個秘密參照欄位，且 §6.1.1 的驗證規則要求「secret 欄位僅能是參照」，並未描述一個「無密碼、僅依賴行程身分」的替代子模式。這使規範沒有交代：選擇 gMSA 託管時，Profile 是否仍必須配置密碼類秘密（因而不必要地保留一個可輪替密碼），或是否存在未描述的免密碼欄位組合。
- 修正建議：在 `design.md` §6.1 的 JSON schema 或 §6.3 補一段，明確區分「顯式網域服務帳號（需要 UserName/Password/Domain 秘密參照）」與「gMSA/keytab（僅需託管平台身分設定，Authentication 區塊不含密碼欄位）」兩種 `Windows` 子模式的欄位形狀與驗證規則。

### ℹ️ Info

**1. `PreAuthenticate` 停用對 Windows/IWA 效能的影響已被連線重用性質稀釋，但文件未說明**
- 位置：`design.md:358`（§7.2 表格）、`docs/.../2026-07-23-dynamics-access-gateway-design.md:164`
- 說明：NTLM/Kerberos 的 401 挑戰是**綁定在 TCP 連線**而非逐請求發生；設計已規劃長連線重用（`PooledConnectionLifetimeMinutes`、`MaxConnectionsPerServer`），因此停用 `PreAuthenticate` 的實際成本被攤提到「每條連線建立時一次」，遠低於字面上「每次呼叫雙倍 RTT」的印象。建議在效能章節（design.md §10 或 §7.2）補充此攤提說明，並要求 Windows/IWA smoke test 具體量測「新連線建立時的挑戰延遲」而非泛稱雙倍 RTT，避免實作階段因誤解而過早啟用 `PreAuthenticate`。

**2. 冪等帳本 HMAC 簽章金鑰的來源與輪替機制未指定**
- 位置：`design.md:605`（§9.3）、`implement.md:161-163`（Phase 2.6）
- 說明：兩處都要求指紋為「versioned HMAC-SHA-256 canonical envelope」，但沒有說明簽章金鑰本身是否比照其他憑證，由核准的秘密提供者以參照方式解析、是否有版本輪替流程。鑑於文件對其他所有憑證都嚴格要求秘密參照與世代化輪替，建議在 §9.3 補一句：「HMAC 簽章金鑰本身是一個秘密提供者管理的參照秘密，遵循與其他 Profile 憑證相同的版本輪替與世代隔離規則」，以避免實作時把它當作靜態設定值寫死。

**3.（沿用前次審查）`ReplicaSlotLease` 協調器技術選型與 `MaximumGatewayReplicas`/HPA 上限同步機制仍待實作階段決定**
- 位置：`implement.md` Phase 3.5；`design.md` §7.2.1
- 說明：這屬於可安全遞延至 Phase 1/2 才決定的基礎設施選型，不影響本規劃文件的架構完整性，僅作記錄以便追蹤。

## 總結

規劃文件已完整落實先前所有回歸檢查點，架構決策（Gateway + 私有無 SDK WebApi Library）具備具體證據與被拒絕替代方案的理由，零容忍洩漏、併發預算、冪等帳本、版本相容性等硬性要求均有可測試的具體落地方式。本輪未發現 Critical（釋出阻斷）等級缺陷。兩項 Warning（`GatewayCapacityCohortId` 供應規則缺失、Windows 認證 schema 與 gMSA 免密碼託管模式的落差）建議在下一次文件修訂中補上，因為它們分別觸及「藍綠/canary 併發防重複」與「憑證洩漏面最小化」兩項零容忍主張的實際可驗證性。

---
SESSION_ID: c1375805-b24b-401a-ba69-d6884477d0b5
