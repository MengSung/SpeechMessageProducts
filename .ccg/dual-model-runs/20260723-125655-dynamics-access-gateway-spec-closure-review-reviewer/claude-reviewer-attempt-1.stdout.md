# 審查報告:Dynamics 365 Access Gateway 架構規格(closure review)

## 審查範圍
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/assessment.md`(輔助佐證)
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`

僅審查規劃文件,未修改任何程式碼或現行 production 行為。

---

## 迴歸檢查(Regression checks)逐項確認

| # | 項目 | 結論 | 依據 |
|---|---|---|---|
| 1 | ReplicaSlotLease coordinator/renewal 失敗 fail-closed,無 emergency grace period | ✅ 通過 | design.md §7.2.2、spec 規則 8 |
| 2 | 產品呼叫僅能用 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}`,不接受 schema/action/profile/URL/header/query grammar | ✅ 通過 | design.md §5、spec 「Every product invocation」 |
| 3 | `AggregateMaxInFlight >= MaximumGatewayReplicas >= 1`、`LocalMaxInFlight` 推導而非可調、production 需 2 個 ready replica | ✅ 通過 | design.md §6.1.1、§7.2.1、spec 規則 9 |
| 4 | `OrganizationAdmissionKey` 容量(含 lease namespace)橫跨新舊 generation、同組織多 alias、blue/green/canary | ✅ 通過 | design.md §7.1、§7.2.2 |
| 5 | 冪等 ledger 具原子有界 key、固定 retention/quota、不存原始 body/token/credential、pre-dispatch 失敗、`OutcomeUnknown` 不自動重播 | ✅ 通過 | design.md §9.3、§10 |
| 6 | handler/proxy/header、single-flight cancellation、共用佇列 drain、response/metadata 解析上限、telemetry/output-cache 遮罩、洩漏/釋放 gate 具體可測 | ✅ 通過 | design.md §7.2、§8.1、§9.3、§11 |
| 7 | `OrganizationAdmissionKey` 定義為跨版本不變的共用 lease namespace、要求原子 durable coordinator、Windows `HostIdentity` vs `SecretReference` 嚴格聯集 | ✅ 通過 | design.md §7.2.2、§6.1 |
| 8 | 版本化長度前綴 canonical tuple 編碼、安全 rolling handoff(drain 後才釋放 slot)、renewal RPC 失敗與 lease 拒絕/TTL 過期區分 | ✅ 通過 | design.md §7.1.1(`CanonicalKeyV1`)、§7.2.2 |
| 9 | 禁止呼叫端提供 FetchXML text/fragment/flag(即使該操作內部用固定樣板)、同一 `OrganizationAdmissionKey` 下設定必須一致無衝突 | ✅ 通過 | design.md §5、§6.1.1 |
| 10 | CE 8.2/9.1 用語 evidence-safe:直接 HTTP/OData 可行、版本明確、on-prem AD/IFD 為 feasibility gate、未宣稱未證實的 SDK parity 或 on-prem client-secret 支援 | ✅ 通過 | design.md §6.3、§8.2、§13 |

以上十項先前回合已標記的缺陷,在本版文件中均已具體落實且彼此一致,未發現迴歸。

---

## Critical 🔴

無。未發現會導致 secret 洩漏、cross-profile 路由、caller 逃逸控制或資源無界洩漏的路徑。

## Warning 🟡

- **`design.md` §9.3(Metrics and alerts,約 line 655-660)** — Audit/telemetry retention 僅描述「透過已驗證的 retention job 刪除/過期」,但**未定義該 retention job 本身失敗時的備援行為**。冪等 ledger 有明確的 per-workload/global quota 作為硬性後盾(超額即拒絕新寫入),但一般 audit/telemetry retention 僅有「fixed configured duration」與「maximum event payload/queue size」,沒有等價的「刪除工作失敗 → 硬性儲存上限 + 告警 + (必要時)節流新寫入」機制。這與 hard requirement 中「zero-tolerance ... retention leak」的要求有落差:若 retention job 靜默失敗,稽核資料可能無界累積,形成資源/資料保留洩漏。
  - **建議修正**:在 §9.3 補一段,明確定義 retention job 失敗時的 fail-safe 行為(例如:超過保留期未刪除即觸發告警、達到硬性儲存上限即拒絕/節流新 audit 寫入),並在 implement.md Phase 4 驗證清單加入「retention job 失敗」的 fault-injection 測項,使其與 ledger 的 quota 機制同等具體可測。

## Info 🟢

- **`design.md` §7.4 vs §7.2.2 的「grace period」用詞重疊** — §7.2.2/spec 規則 8 明確禁止 ReplicaSlotLease 遺失後的「emergency admission grace period」;而 §7.4 允許舊 credential generation 在「approved credential grace period」內繼續有效直到新 generation 驗證完成。兩者語意不同(前者是遺失 lease 後禁止繼續放行新出站請求;後者是 secret 輪替期間允許舊憑證持續服務,並非放寬 admission 上限),邏輯上不衝突,但共用「grace period」一詞容易讓實作者誤讀成兩者互通。
  - **建議修正**:在 §7.4 第一次出現「approved credential grace period」處加一句澄清,例如「this grace period governs credential validity continuity only and is unrelated to, and does not relax, the ReplicaSlotLease no-grace-period rule in §7.2.2」。

- **`OrganizationAdmissionKey = tuple(deploymentEnvironment, expectedOrganizationId)` 刻意不含 API 版本** — 這代表若同一組織 GUID 同時有 v8.2 與 v9.1 profile(例如版本切換過渡期),兩者會共用同一組 admission 預算,且驗證器要求兩者 `OrganizationAdmissionSettings` 必須完全一致。設計邏輯正確(避免同組織併發預算被重複計算),但文件未明確點出「同組織同時存在兩個 API 版本 profile」這個具體情境是否為預期支援案例。屬於可安全遞延的澄清,非阻斷項。
  - **建議**:若此情境確實在預期範圍內(例如版本升級過渡期),可在 §7.1 加一句明確說明;若不支援,同樣可加一句排除,避免實作者自行猜測。

---

## 總結 / 結論

本輪為 closure review,先前多輪(closure/final-acceptance/final-recheck/release-review)已標記的問題在四份文件中皆已具體落實且互相一致:Gateway 是唯一出站邊界、REST 合約無 schema/URL/header 逃逸、`OrganizationAdmissionKey` 與 `ProfileRuntimeKey` 隔離模型完整、ReplicaSlotLease fail-closed 語意明確、冪等 ledger 具原子性與有界 retention、`CanonicalKeyV1` 取代字串串接、CE 8.2/9.1 語言 evidence-safe、遷移範圍誠實反映約 200 個 SDK 耦合來源檔案而非「單純換 DLL」。

未發現 Critical 等級缺陷。存在 1 項 Warning(audit/telemetry retention job 失敗時缺乏明確 fail-safe 備援,與冪等 ledger 的 quota 機制不對稱)建議在下一版補上;另有 2 項 Info 級澄清建議,均可安全遞延不阻斷後續開發啟動。**建議:待 Warning 項目補齊說明後即可視為可進入實作啟動(Phase 0/1)的成熟規格。**

---
SESSION_ID: 3c3b6684-12e1-42d1-ba72-689cfa347654
