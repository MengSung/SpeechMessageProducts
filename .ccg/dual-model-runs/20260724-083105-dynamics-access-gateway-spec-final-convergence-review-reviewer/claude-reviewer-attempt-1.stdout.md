已完整讀取四份規劃文件（prd.md 307 行、design.md 1371 行、implement.md 603 行、gateway design spec 481 行），並逐一核對 16 個審查問題與 24 項回歸檢查項目。以下為審查報告。

---

## Review: Dynamics Access Gateway 架構 SPEC（第 N 輪 / final-convergence-review）

### Critical 🔴
無。

四份文件中所有零容忍洩漏類別（跨 profile 憑證/token/cookie 洩漏、caller 導向路由、audit/queue/cache 中殘留使用者身分、記憶體/控制代碼洩漏）都在 design.md §7.5「Zero-tolerance release gates」中有明確條列，且在 §11.1/§11.2 與 implement.md Phase 4 有對應的可執行測試項目與明確失敗條件，未發現任何會直接導致安全性/隔離性破口的設計缺陷。

### Warning 🟡

- **design.md §4.1（第 213–244 行）、implement.md Phase 1 步驟 4（第 126–132 行）**
  **問題**：Embedded 模式被要求「一律」在解析任何 secret／runtime／admission slot 之前，先以 signed manifest 或 central registry 驗證 `ProductProfileBinding`/`OrganizationAdmissionCoordinatorRef`；若 registry 不可達、逾時、過期或驗證失敗則 fail closed（NotReady）。但同一節緊接著又承諾「Visual Studio development 可用 `appsettings.Development.json` 選擇 Embedded + fake CRM fixture」，且 prd.md 明確要求「Visual Studio development 必須能夠 observe/test 兩種模式」。
    文件中**沒有**說明：開發環境下的 Embedded fake-server 測試，是否仍必須連上一個（正式或準正式）central registry 才能通過驗證；如果是，開發機離線時無法測試 Embedded 模式，與 prd 的可測試性要求衝突；如果否，則暗示存在一個未定義的「開發用低信任 registry / signing key」，但文件從未定義其信任錨點、金鑰輪替、防降級規則是否與生產環境隔離——這正是文件在別處（design.md §4.1 trust artifacts 清單）要求必須明確化的同一類風險（trust anchor、key rotation、anti-rollback）。
  **建議修正**：在 design.md §4.1 增加一段明確決策，例如：定義一個與生產環境完全隔離的 *Development trust anchor*（獨立簽章金鑰 / registry endpoint，僅可簽發指向已知非生產 `ExpectedOrganizationId` 的 manifest），並明訂：(a) 開發用 manifest 絕不能驗證通過任何生產 alias/組織識別；(b) 若無法連上該開發用 registry，Embedded 開發模式一樣 fail closed（不得回退為信任本地 JSON）。同時在 implement.md Phase 1 或 Phase 4 測試清單中，增加一條「開發環境 Embedded fake-server 在開發 registry 不可達時仍 fail closed」的測試項。

### Info 🟢

- **design.md §7.2.2（第 744–751 行）**：ADR 要求涵蓋 coordinator 的 acquire/renew/release、outage 行為等，但未顯式提及「coordinator store 全新建立時（第一次上線）如何取得初始 `AdmissionEpoch`」的 bootstrap 情境。由於 ADR 本身已被列為 Phase 2 前的強制先決條件，且 ADR 範疇已包含「outage/fail-closed behavior」，此項可安全遞延至 ADR 撰寫階段決定，不需在本 SPEC 層級展開，僅供實作團隊留意。

- **design.md §4.1 / §9.1**：「本地 Gateway process」供 VS 開發使用的段落，未說明開發用 Gateway 實例如何取得非生產 mTLS/JWT workload 身分（例如是否使用獨立的開發 PKI 信任根）。風險遠低於 Embedded 的 registry fail-closed 問題（因為 Gateway 路徑的失敗模式本就是「認證失敗 → 拒絕」，不存在信任降級疑慮），但建議與上述 Warning 一併在後續文件中補一句話說明開發環境 workload 身分來源，以求對稱完整。

---

### 16 項審查問題回覆（逐項確認）

1. **Gateway + 私有 no-SDK WebApi library 是否技術上合理，Library-only 與 transparent-proxy 是否有具體理由被拒絕？** 是。design.md §2.2 選項 A/B/C/D 比較表逐項列出「決定性缺點」（A：五到十份重複的憑證/連線/快取管理；B：caller 可任意表達 CRM schema、洩漏攻擊面），並用本地既有程式碼證據（單例 `ICrmConnectionPool`、SOAP 耦合）佐證。

2. **HTTP handler/HttpClient、Windows 憑證、OAuth token cache、metadata cache、retry/circuit state、queue/並行狀態、reload 生命週期是否由足夠不可變的 profile-generation key 隔離？** 是。design.md §7.1「ProfileRuntimeKey」與 §7.2 逐狀態表格明確規定每類狀態的隔離規則與生命週期。

3. **是否留有跨 profile 路由、secret 洩漏、caller 提供 endpoint/header/profile 逃逸、保留期洩漏、runtime 狀態被非預期修改、或不安全自動重試的路徑？** 未發現。design.md §7.5 zero-tolerance 條款、§5 API 契約明確禁止 caller 提供 CRM schema/URL/header/profile；write 重試僅允許在具備 CRM alternate-key/upsert 或 ledger 保護時發生。

4. **CE 8.2/9.1 API 版本與驗證限制是否安全描述，未假設 on-prem client-secret 或 WS-Trust fallback？** 是。design.md §6.3、§8.2 明確標示 client-secret/certificate 僅為 Dataverse 能力、非 CE on-prem 承諾；IFD 需先通過 target-specific 非密碼服務流程證明才可用，否則 profile 不可用。

5. **效能與高可用宣稱是否有界、可測試、且與 Dynamics service protection 相容？** 是。design.md §10 明確標示「maximum safe sustained throughput」而非峰值，所有目標值需以真實 8.2/9.1 baseline 驗證，且不得以放寬隔離/生命週期防護為代價。

6. **遷移範圍、no-SDK 執行檢查、測試/發布關卡是否足夠具體？** 是。design.md §12 與 implement.md Phase 0/6 有明確 rg/PowerShell 掃描腳本與 CI gate matrix。

7. **是否有矛盾、缺失的明確決策、或危險假設？** 除上述 Warning（Embedded 開發環境信任錨點缺失）外，未發現其他未被明確聲明之 feasibility gate 掩護的矛盾或危險假設。

8. **Gateway/Embedded host-mode JSON 設計是否保留核心安全性質、允許安全的 VS 開發、禁止動態/使用者驅動選擇、並正確協調跨 host mode 的容量？** 基本是，但如 Warning 所述，VS 開發下 Embedded 模式的「安全測試可行性」與「fail-closed 信任驗證」兩個要求之間存在未言明的銜接缺口。

9. **安全的 warm-up 設計是否能加速冷啟動/登入路徑，同時不保留使用者專屬連線、session、LINE ID 或 token？** 是。design.md §10「Safe warm-up」與 §7.2 warm-up 狀態列明確規定僅限 service-identity、single-flight，且測試涵蓋 login-path 各種情境（Phase 4 步驟 4）。

10. **是否要求遷移前建立 Organization-call coverage matrix，將每個現有呼叫點對應到受限 Web API 能力/暫時 legacy 項目/明確排除範圍？** 是。prd.md 功能需求、design.md §5、implement.md Phase 0 步驟 3 定義了完整欄位（含 v8.2/v9.1 證據、encoding context、audit 分類、owner、removal deadline），且明訂 CI 對缺漏欄位的 migrated source root 直接判定失敗。

11. **已遷移產品的 CI/啟動關卡是否足以防止透過 `ICrmConnectionPool`、`ToolUtilityFactory`、Microsoft.Xrm/CrmSdk/Dataverse 套件或原始 CRM 連線字串繞過？** 是。design.md §12.2 第 8 點、implement.md Phase 0 步驟 2 明確列出禁用清單並要求逐一遷移的 source root 強制執行。

12. **產品 JSON 信任邊界是否明確到「可編輯的 JSON 不能授權」且 Embedded 綁定須簽章或 registry 驗證？** 原則上是，但如 Warning 所述，這個「絕對 fail-closed」規則與開發環境可測試性需求之間的具體銜接方式未明確定義，建議補強。

13. **durable coordinator/ledger/audit ADR、queue fairness 演算法、capacity-owner artifact 是否具體到能保證效能安全且可測試？** 是。design.md §7.2.2（ADR 需涵蓋 store、clock source、fencing-token 語意等）、§7.2.1（deficit/weighted fair dispatch、確定性拒絕順序）皆足夠具體。

14. **跨環境但指向同一實體 Dynamics organization 的 profile 是否被強制納入同一 canonical capacity budget，而非各自環境標籤的獨立配額？** 是。design.md §7.1 明確要求跨環境衝突時啟動失敗，除非有明確合併的 `OrganizationAdmissions` 項目；並提供三個互相區分的 key 類型（`CanonicalOrganizationCapacityKey`／`RuntimeHostSlotLeaseNamespace`／`OrganizationAdmissionKey`）防止標籤誤用。implement.md Phase 2 步驟 1 更明確要求刪除任何可由 `tuple(deploymentEnvironment, expectedOrganizationId)` 直接推導容量的輔助程式碼。

15. **Embedded signed-manifest / central-registry 信任模型（schema、trust anchor、金鑰輪替、TTL、撤銷、防降版、逾時、快取過期、fail-closed）是否具體？** 就生產路徑而言具體（design.md §4.1 trust artifacts 清單完整列出各要素）；但如 Warning 所述，此信任模型未涵蓋開發/測試場景下的對應規則。

16. **實作計畫的 CI gate matrix 是否具體到足以涵蓋 no-SDK 執行、產品 JSON 驗證、隔離性、容量、CE smoke、soak/效能？** 是。implement.md 文末 CI gate matrix 表格含 7 個階段對應的指令、失敗條件與產出物，涵蓋所有要求類別。

---

### 回歸檢查（24 項）確認結果

逐一核對後，**全部 24 項回歸檢查項目均已在目前版本中落實**，未發現退化：

| 類別 | 對應章節 | 狀態 |
|---|---|---|
| RuntimeHostSlotLease/AdmissionEpoch、expiry fence、quarantine | design.md §7.2.2 | ✅ |
| 受限 API 形狀（僅 alias + capabilityOperationId） | prd.md、design.md §5、spec §「Every product invocation」 | ✅ |
| AggregateMaxInFlight/MaximumRuntimeHosts 驗證與衍生 LocalMaxInFlight | design.md §6.1.1、§7.2.1 | ✅ |
| OrganizationAdmissionKey 跨世代/alias/blue-green-canary 共享 | design.md §7.1、§7.2.2 | ✅ |
| Ledger 原子化、有界保留、不儲存原始內容、禁止自動重放 OutcomeUnknown | design.md §9.3 | ✅ |
| Handler/proxy/header、single-flight 取消、queue drain、telemetry redaction、disposal 可測試 | design.md §7.2、§11 | ✅ |
| OrganizationAdmissionKey 不可變 lease namespace、Windows HostIdentity/SecretReference 嚴格聯合型別 | design.md §6.1、§7.1 | ✅ |
| CanonicalKeyV1 編碼、base URI 驗證、duplicate-aware parsing、安全 rolling handoff | design.md §7.1.1、§7.2.2、§7.3 | ✅ |
| 禁止 caller 提供 FetchXML、單一 conflict-free 的 manager-owned 設定 | design.md §5、§6.1.1 | ✅ |
| 單一 canonical-organization-keyed OrganizationAdmissions map、audit 有界 fail-safe | design.md §6.1 範例、§9.3 | ✅ |
| LocalQueueCapacity/MaxDispatchEnvelopeBytes manager-owned、worst-case 界限 | design.md §7.2.1 | ✅ |
| CE 8.2/9.1 用語 evidence-safe（不誤稱 route 驗證等於 release 證明） | design.md §6.2、§8.2 | ✅ |
| 綁定 operation revision hash、限制 active+draining 世代數、warm-up 不留存使用者資料 | design.md §7.2、§10 | ✅ |
| Coverage matrix、OData 預設 header/Prefer allowlist、legacy bypass gate、ADR 前置、fair queueing、capacity artifact | prd.md、design.md §5/§8.1/§7.2.2、implement.md Phase 0 | ✅ |
| 三種 key 類型明確區分防止環境標籤重複計算 | design.md §7.1 | ✅ |
| Embedded manifest/registry fail-closed 語意完整（含 anti-rollback、金鑰輪替） | design.md §4.1 | ✅（生產路徑完整；開發路徑見 Warning） |
| CI gate matrix 具體（指令、失敗條件、artifact） | implement.md 文末表格 | ✅ |
| 禁止由 `tuple(deploymentEnvironment, expectedOrganizationId)` 直接建構容量/queue/lease | design.md §7.1、implement.md Phase 2 步驟 1 | ✅ |
| FetchXML/OData builder 宣告 encoding context 並使用對應 encoder | design.md §5、§8.1、implement.md Phase 2 步驟 7 | ✅ |
| Coverage matrix 作為 CI completeness gate、rollout 階段名稱與執行計畫 phase 對應無歧義 | implement.md Phase 0 步驟 3、design.md §12.2 | ✅ |

---

### Summary

文件整體品質非常高，明顯已經過多輪收斂式審查——本輪要求核對的 24 項回歸檢查與 16 個審查問題絕大部分均已在四份文件中被具體、可測試地落實，未發現 Critical 等級問題。

僅發現 **1 項 Warning**：Embedded 模式「一律 fail-closed 依賴 signed manifest / central registry」的強制規則，與「Visual Studio 開發環境必須能測試 Embedded 模式」的驗收條件之間，缺少明確銜接規則（是否需要獨立的開發用信任錨點/registry，以及其與生產信任根的隔離方式）。建議在 design.md §4.1 及 implement.md 對應段落補上明確決策後即可視為完備。

其餘為 2 項 Info 級別的次要備註（coordinator bootstrap 情境、本地 Gateway 開發身分來源），皆可安全遞延至實作/ADR 階段處理，不影響本 SPEC 的整體可行性判斷。

**建議：可在補上 Warning 項目的明確決策後進入下一階段（Phase 0 執行）。**

---
SESSION_ID: b78663e8-c46f-4652-86b3-1ba98deccd13
