我已完整讀取四份規劃文件(prd.md、design.md、implement.md、docs spec),並逐項核對「先前審查回合的迴歸檢查清單」與七個審查問題。以下為最終驗收審查報告。

## Review: Dynamics Access Gateway 架構 SPEC(最終驗收審查)

### 迴歸檢查清單核對結果(逐項確認)

| 檢查項 | 狀態 | 依據 |
|---|---|---|
| ReplicaSlotLease 協調者/續約失敗時 fail-closed,無寬限期 | ✅ 通過 | design.md §7.2.2(LeaseFailure 段落)、spec 規則 8 |
| 產品呼叫僅限 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` | ⚠️ 見 Warning 1 | design.md §5 line 129、132-135 |
| `AggregateMaxInFlight >= MaximumGatewayReplicas >= 1`、衍生本地併發、生產環境需兩個就緒副本 | ✅ 通過 | design.md §6.1.1、§7.2.1 |
| `OrganizationAdmissionKey` 容量跨新舊世代/別名/藍綠/金絲雀共享 | ✅ 通過 | design.md §7.1、§7.2.2 |
| 冪等帳本:原子有界鍵、固定保留/配額、不存原始內容、派送前失敗、`OutcomeUnknown` 不自動重放 | ✅ 通過 | design.md §9.3、implement.md Phase 2.6 |
| Handler/proxy/header、single-flight 取消、共享佇列排空、回應/中繼資料解析上限、遙測/快取重編、可測試的洩漏/釋放閘門 | ✅ 通過 | design.md §7.2、§7.3、§8.1、§11 |
| `OrganizationAdmissionKey` 為不可變共享租約命名空間、需原子式持久協調者、Windows `HostIdentity`/`SecretReference` 嚴格聯集 | ✅ 通過 | design.md §7.2.2、§6.1 |
| 版本化長度前綴正規複合鍵編碼;安全滾動交接(終止副本先保留租約排空、再原子釋放;暫時性續約 RPC 失敗與租約拒絕/到期區分) | ✅ 通過 | design.md §7.1.1、§7.2.2 |
| CE 8.2/9.1 語言謹守證據邊界,不宣稱未經證實的 SDK 對等或 on-prem client-secret 支援 | ✅ 通過 | design.md §6.3、§8.2、prd.md「Confirmed facts」 |

**結論:9 項迴歸檢查中有 8 項確認通過;第 2 項(受控端點)因用語瑕疵需修正,見下方 Warning 1。**

---

### Critical 🔴

無。核心的 fail-closed 租約邏輯、`OrganizationAdmissionKey` 容量共享、冪等帳本、生命週期釋放閘門等零容忍要求皆有明確、可測試的文字定義,未發現會導致洩漏或無界並發的路徑。

### Warning 🟡

- **design.md:132-135** — 產品端 API 說明寫道:「The API does not accept an outbound URI, profile name, auth header for Dynamics, password, token, **FetchXML text without a policy-approved operation**, or an unbounded batch payload.」
  - **Why**: 此句字面上暗示「只要有 policy-approved operation,呼叫端仍可提供 FetchXML 文字」,這與同一節稍早「it must never expose... a generic query surface for arbitrary CRM URL, headers, OData text, FetchXML, credentials, filters, or profile」以及「callers cannot supply a CRM schema target, CRM action/function identifier, filter grammar, raw OData URL」等敘述互相矛盾。若實作者依字面實作,等於重新開啟了 operation registry 原本要封閉的任意查詢語法逃逸口(資料外洩/查詢注入面),違反 prd.md 的「must not expose an unrestricted transparent proxy or generic query surface」硬性需求。
  - **Fix**: 刪除「without a policy-approved operation」這個但書子句,改為明確聲明:FetchXML(若某能力操作內部使用)永遠是伺服器端固定樣板,呼叫端只能提供具型別的具名參數,不能提供任何 FetchXML 文字、片段或旗標。

- **design.md:274-276 對照 §7.1(line 379-382)與 §7.3(line 505-509)** — 驗證器明文要求同一個 `OrganizationAdmissionKey` 下的所有設定檔(profile)不能有衝突的 `AggregateMaxInFlight`/`MaximumGatewayReplicas`(§7.1),但 `QueueCapacity` 的驗證規則(§6.1.1)卻是「no larger than the deployment's hard **per-profile** queue cap」,語意上是逐設定檔各自驗證,而不是要求同一個 `OrganizationAdmissionKey` 下的所有別名/設定檔共用一致的佇列容量與排空逾時(drain timeout)。但實際上 `OrganizationAdmissionManager` 只擁有「唯一」一個共享組織佇列(§7.1、§7.3)。
  - **Why**: 若兩個別名(例如同一組織的 "membership" 與 "reporting")各自宣告不同的 `QueueCapacity`(如 48 與 200)或不同的排空逾時,規格並未定義這個共享佇列最終應採用哪一個數值,也未如同 `AggregateMaxInFlight`/`MaximumGatewayReplicas` 一樣被驗證器擋下衝突設定。這會讓「佇列容量與排空行為」處於未定義狀態,削弱效能/生命週期章節聲稱的「finite and deployment-bounded」保證的可驗證性。
  - **Fix**: 在 §7.1 的「rejects conflicting aggregate-budget/replica settings for the same key」規則中,明確納入 `QueueCapacity` 與排空逾時等所有由 `OrganizationAdmissionManager` 治理的共享參數,要求同一 `OrganizationAdmissionKey` 下所有設定檔必須宣告相同數值,否則驗證失敗。

### Info 🟢

- **design.md:130-131**(`GET /v1/health/profiles/{alias}`) — 文件說明此端點「operator-only」、「not available to ordinary products」,但未具體說明其授權隔離機制(例如:是否需要 JWT 中的 operator claim、是否位於獨立網段/獨立 listener)。因規劃任務允許將可安全遞延的實作決策留待可行性驗證階段,此點無需在本規劃階段補完,但建議在 Phase 3 實作時明確定義,以確保與「product invocation only via .../operations/...」的迴歸要求不會因授權模型鬆散而被繞過。

- **design.md:734-750(效能章節)** — p99 < 1 ms(profile lookup)、p95 < 5 ms / p99 < 15 ms(Gateway overhead)等目標已適當註明「measured after warm-up」「against the real 8.2/9.1 baseline」「may only be relaxed with a documented real-server constraint」,屬於有界、可測試、且不以犧牲隔離/生命週期保證換取效能的合理目標,回答審查問題 5:通過。

---

### 對七個審查問題的簡答

1. **Gateway + 私有 no-SDK WebApi 函式庫是否技術上合理,且替代方案(純函式庫、透明代理)是否有具體理由被否決?** 是。design.md §2.2 表格以具體證據(單例連線池、SOAP/WCF 耦合、五到十個產品重複持有密鑰的風險)否決選項 A、B,選項 C 的取捨(多一個網路跳點 vs. 集中密鑰/生命週期治理)交代清楚。

2. **HTTP handler/HttpClient、Windows 憑證、OAuth token 快取、中繼資料快取、重試/斷路器狀態、佇列/併發狀態、reload 生命週期是否由足夠的不可變設定檔世代鍵隔離?** 是,§7.2 表格逐項對應 `ProfileRuntimeKey`,唯一的共享狀態(組織層級佇列)另有 `OrganizationAdmissionKey` 明確界定範圍,設計自洽。

3. **是否留有跨設定檔路由、密鑰外洩、呼叫端指定端點/標頭/設定檔逃逸、保留期外洩、執行期就地變異、不安全自動重試的路徑?** 除上述 Warning 1(FetchXML 用語瑕疵)外未發現。其餘皆有明確「reject/fail closed」規則覆蓋。

4. **CE 8.2/9.1 版本與驗證限制描述是否安全,未假設 on-prem client-secret 或 WS-Trust 回退?** 是,§6.3 明確聲明 client-secret/certificate 僅為 Dataverse 專屬能力,IFD 僅接受已證實的非密碼授權流程,無 ROPC 或 WS-Trust 回退。

5. **效能與高可用性宣稱是否有界、可測試、且與 Dynamics 服務保護相容?** 是,見上方 Info 2。

6. **遷移範圍、no-SDK 執行檢查、測試/發布閘門是否足夠具體?** 是,implement.md §12.1/Phase 0-6 具體列出約 200 個 SDK 相關來源檔案、三道 ripgrep 掃描規則、CI 強制閘門時機,不假裝是單純換 DLL。

7. **矛盾/缺漏/危險假設?** 見上方兩項 Warning;其餘未發現需要產品端決策卻本可延後的請求。

---

### 總結

規格在本輪之前已修正了先前審查提出的所有零容忍/迴歸項目(fail-closed 租約、共享容量鍵、冪等帳本、正規鍵編碼等均已到位且可測試)。本輪僅發現兩個 **Warning**:(1) `design.md:132-135` 的 FetchXML 用語與整體「無通用查詢面」原則矛盾,需澄清措辭;(2) `OrganizationAdmissionKey` 共享佇列的 `QueueCapacity`/排空逾時一致性未被驗證器強制,需補上與 `AggregateMaxInFlight`/`MaximumGatewayReplicas` 相同等級的衝突檢查。兩者皆為規格文字層級的修正,不涉及架構重新設計,建議在進入 Phase 1 前修正後即可視為驗收通過。

---
SESSION_ID: 426d8086-979e-4606-8161-492dc328f502
