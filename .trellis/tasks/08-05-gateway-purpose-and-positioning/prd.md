# 釐清 Dynamics Gateway 存在意義與定位

> 建立日期：2026-08-05
> 狀態：規劃中（Phase 1 · 需求探索）
> 起因：使用者的心智模型與實際架構不符，需要先取得共識再決定後續工程方向
> 路線重校日期：2026-08-06；目前核准目標為先在 Lenovo Legion 完成 P6／P7，再由 P8 將單一 ChurchReport 部署為雲端 Central Gateway。

---

## 1. 問題陳述

使用者原本的理解是：

> ToolUtility 可以「選擇」走 Data8 或走 Gateway，去存取 D365 8.2 / 9.1。
> 也就是把 Gateway 當成一個**可替換的傳輸選項**。

實際架構不是這樣。這個落差必須先解決，否則後續所有工程決策都建立在錯誤前提上。

---

## 2. 已確認事實（來自程式碼與文件，非推測）

### 2.1 Gateway 不是傳輸選項，是政策邊界

| 證據 | 內容 |
|---|---|
| `SpeechMessage.Dynamics.Gateway/Program.cs:7` | 「Gateway 不接受任意 CRUD / 任意 FetchXML；只接受已註冊 operations」 |
| `ControlPlane/Guard/RequestGuard.cs`（G1） | `fetchXml`／`organizationId`／`connectorKind`／`credential`／`endpoint` 是保留字，命中即 400 |
| `Abstractions/Connectors/IConnectorLease.cs` | 只有 `ExecuteAsync(ConnectorOperation, CancellationToken)`，**沒有** `IOrganizationService Service { get; }` |
| `Abstractions/Connectors/ConnectorOperation.cs` | `Parameters` 與 `Values` 都是 `IReadOnlyDictionary<string, string?>`（扁平 scalar） |

因此 ToolUtility（142 個 `IOrganizationService` 形狀的方法）**在型別層面就接不上 Gateway**。
要接上只有兩種做法，兩者都不是「換個設定」：
1. 把 Gateway 改成泛用 CRM proxy → 等於刪掉 Gateway 的核心安全前提
2. 把 142 個方法改寫成註冊過的 capability operation → 數個月工程

### 2.2 Gateway 目前的實際能力

| 項目 | 數量 |
|---|---|
| Phase 0 盤點的 ChurchReport CRM 呼叫點 | 70（35 讀 / 23 寫 / 4 action / 2 function / 1 metadata / 5 連線基礎設施） |
| `Package01OperationRegistry` 已宣告的 capability | 9 |
| Data8 Connector **已實作**的 | **1**（`runtime.health.whoami`） |

證據：`Connectors.Data8/OnPremiseData8ConnectorClientFactory.cs:191`、`Data8ProfileOperationExecutor.cs:163`
盤點檔：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json`
（migrationStatus：54 `mapped-pending-evidence` / 16 `temporary-legacy`）

### 2.3 舊文件曾把 Central Gateway 綁定多產品（已被 2026-08-06 決策取代）

| 出處 | 內容 |
|---|---|
| 規格書 §0.1 | 「讓**多個產品**在存取多個 Organization 時…」 |
| 規格書 §1 | `CentralGateway` = 「獨立服務，**多產品共用**」 |
| 設計說明書 §3.5 | 「由於現有產品可能從四、五個增加到十個以上…」 |
| 舊版計畫書 P8 | 曾把 Central Gateway 的觸發條件綁定第二個產品；此限制已不再是目前產品路線。 |

設計說明書 §4 明確定義 Central Gateway 集中的**不是連線，是管理責任**：
Workload Authentication、Authorization、Profile Registry、Secret Reference Resolution、
Operation Registry、Retry／Timeout／Backpressure、Audit／Telemetry／Health、
Profile Runtime Generation、Aggregate Organization Admission。

### 2.4 Dedicated 與 Central Gateway 的部署價值不同

| 模式 | 真正解決的問題 | 前提 |
|---|---|---|
| `CentralGateway` | 雲端集中部署與治理：service identity、TLS、secret 不落入產品、統一監控與回滾；未來也可擴充多產品 | 單一 ChurchReport 即可成立 |
| `DedicatedGateway` | 單產品的**進程隔離**：SDK/WCF 不進產品進程、crash 邊界、獨立回收 | 不需要多產品 |

這兩個被「Gateway」一個詞綁在一起，是心智模型混淆的來源之一。

### 2.5 目前設定下的實際狀態

- ChurchReport `appsettings.json`：`ConnectionMode=DedicatedGateway`、`ProfileAlias=crm91`、
  `Endpoint=https://localhost:5101/`、**`Package01FeeReadsEnabled=false`**
- 因此 Gateway 目前不在關鍵路徑上，100% 業務流量走 `WebServiceConnector → ToolUtility → Data8 → D365`
- `DedicatedGateway` 模式**不註冊任何 host slot 協調器**（`Program.cs:177` 條件為 `!Testing && !isDedicatedGateway`）
- `InMemoryRuntimeHostSlotCoordinator` 自述 `IsDurable=false`、「只能保證同一個進程內」
- **推論**：ToolUtility（產品進程）＋ Dedicated Gateway（另一進程）同時打同一個 Organization 時，
  沒有任何機制限制總併發 —— 正是設計說明書 §12.3 警告的情況

### 2.6 Data8 已知缺陷的實際狀態（與規格書不同步）

| 項目 | 規格書說 | 程式碼實際 |
|---|---|---|
| §11.2 `OnPremiseClient` 未實作 `IDisposable` | 必修項、底線不成立 | **已修**（`OnPremiseClient.cs:36` 實作 `IDisposable`，Dispose 對 channel/factory 做 Close/Abort 並彙總失敗） |
| §11.1 `_sdkMajorVersion` 是 static | 視 A1 結果決定是否必修 | **未修**（`OnPremiseClient.cs:77`，fallback = 9，用於 `?wsdl&sdkversion=`），A1 尚無書面結果 |
| §7.1 `IConnectorLease` 含 `IOrganizationService Service` | 規格如此 | **不存在**，實際是扁平 scalar `ExecuteAsync` |

規格書與程式碼已分岔，需一併對齊。

---

## 3. 本任務要產出的決策

1. Gateway 在「只有 ChurchReport 一個產品」的現況下，以何種本機與雲端部署角色保留
2. ToolUtility 的治理路徑：是否改為向 `Data8ConnectorPool` 借 lease（Embedded），或維持現狀
3. P6／P7 本機完成與 P8 ChurchReport 雲端 Central Gateway 部署的明確交界
4. 上述決策對規格書／計畫書／架構圖的修訂範圍

---

## 3.5 共識紀錄：兩種存取模型的本質差異（2026-08-05 討論）

使用者提問「是不是我跟 Gateway 借一個連接器，然後拿來存取 D365？」→ **否**。
這是心智模型的核心分歧點，記錄如下。

| | 模型 A：借連接器（ToolUtility 現況） | 模型 B：送請求要資料（Gateway） |
|---|---|---|
| 產品拿到什麼 | `IOrganizationService`（一個可任意操作的物件） | `FeeRecordDto[]`（已投影的資料） |
| 誰組查詢 | 產品自己（`QueryByAttribute`／FetchXML） | 伺服器端已註冊的 template |
| 產品看得到 CRM schema 嗎 | 看得到（`contact`、`new_lineid`、`statecode`） | 看不到 |
| 能做的事 | 任何 CRM 操作 | 只有已註冊的 operation |
| 真實程式碼 | `ContactService.cs:143` `_organizationService.RetrieveMultiple(query)` | `Package01FeeReadClient.cs:49` 只送 operationId + 具名參數 |

**關鍵推論**：「借連接器」只在**同一個進程內**成立（Embedded 模式向 `Data8ConnectorPool` 借 lease）。
跨進程的 Gateway 無法交出連接器 —— 連線物件不能序列化過 HTTP。
因此 **ToolUtility 只可能走 Embedded，不可能走 Gateway**，這是物理限制，不是設計偏好。

### 3.6 對「將來產品」的重要澄清

ChurchReport 的 70 個呼叫點是**十餘年累積的歷史包袱**，不是新產品的入門門檻。
Gateway 的 operation registry 是**按需成長**的：新產品需要幾個 operation 就註冊幾個。
這點會影響「第二個產品是否該走 Gateway」的評估基準。

---

## 4. 已收斂決策與非阻塞技術證據

- [x] 第二、第三產品不阻塞目前路線；P8 先由單一 ChurchReport 的雲端 Central Gateway 部署觸發，其他產品日後另立獨立 onboarding task。
- [x] 「產品進程零 ToolUtility／CRM SDK／`IOrganizationService`」是 P7.5 與新產品的硬性驗收條件。
- [x] 進程隔離是 `DedicatedGateway` 的部署價值；`Embedded`、`DedicatedGateway`、`CentralGateway` 仍共用相同 operation contract，不要求所有環境一律使用獨立進程。
- [x] CE 8.2／9.1 由 immutable Profile 與 Connector support matrix 路由。若選擇同進程 Data8，必須另有雙版本並存與資源基線證據；這是技術驗證，不再是產品方向的開放問題。
- [x] Lenovo Legion 是 P6 與 P7 的本機開發、整合與驗收主機；正式雲端 host/service identity/TLS/monitoring/rollback 屬 P8，不倒灌至 P6 或 P7。

### 4.1 2026-08-05 後續對話：使用者傾向產品走 Gateway

使用者以「ChurchReport 走 Gateway」為目標繼續探索，並詢問是否必須把
ChurchReport 目前透過 ToolUtility 使用的所有能力逐一註冊到 Operation Registry。

此方向後續已由使用者核准為最終架構；實作仍必須保留下列差異：

- 搬移「ToolUtility 技術方法」不是目標，例如不應把通用 `RetrieveMultiple`、
  `Update(Entity)` 或任意 FetchXML 原封不動暴露成 Gateway operation。
- 應盤點 ChurchReport 的「業務 use case」，由一個 capability operation 在 Gateway
  邊界內組合必要的查詢、驗證、更新與交易補償，再回傳產品安全的 DTO。
- Gateway 不會依 ToolUtility 呼叫自動產生 operation；每個 operation 都需要明確的
  contract、authorization／guard、executor、DTO、registry 登錄與測試證據。
- ChurchReport 最終必須完全移除所有 legacy ToolUtility CRM 存取；這是 P7.5 的完成定義，
  不再是開放問題。

### 4.2 Microsoft 官方模型與本專案模型的界線

使用者追問目前 Gateway 設計是否符合 Dynamics 365 Customer Engagement on-premises
8.2／9.1 的官方程式模型，以及 `IOrganizationService` 是否已經改變。

已核對的官方事實：

- Microsoft Learn 的 CE on-premises 9.1 文件仍明確稱 `IOrganizationService` 為存取
  Organization data 與 metadata 的主要 web service；它沒有被 Operation Registry 取代。
- 最新 Dataverse SDK 文件仍保留 `IOrganizationService` 的 Create、Retrieve、
  RetrieveMultiple、Update、Delete、Associate／Disassociate 與 Execute 契約，並建議
  client application 透過 `Microsoft.PowerPlatform.Dataverse.Client.ServiceClient` 取得其實作。
- CE on-premises 9.1 的 XRM Tooling 文件仍支援以 `CrmServiceClient` 連線，並列出
  AD、IFD 與符合前置條件的 OAuth 等 authentication shape。
- Microsoft 並沒有規定產品必須實作本專案的 Operation Registry。Registry、
  ProductClient 與 Gateway 是本專案加在官方 Organization Service 之上的應用架構邊界；
  是否符合官方支援，仍取決於 Gateway 內部 connector 是否使用目標版本支援的 SDK／
  Organization Service 契約、authentication 與真實伺服器驗證。

因此本專案的改變不是「Microsoft 不再使用 `IOrganizationService`」，而是把它從
ChurchReport 的產品程式邊界移到 Gateway 內部的 connector／worker 邊界。

### 4.3 使用者指定的分析方式

使用者明確要求本任務不需要雙模型 review 或 analysis。後續規劃只使用主代理的
程式碼檢查、既有 task/history 與 Microsoft 官方文件；不再啟動 Gemini／Claude。

### 4.4 已決定：ChurchReport 最終完全 Gateway 化

使用者選擇完成定義 A：採分階段遷移，但最終 ChurchReport 不再直接透過
ToolUtility／`IOrganizationService` 存取 D365；所有正式業務流量改由強型別
ProductClient 呼叫受控 Gateway capability operation。

此決策代表：

- ToolUtility 可在遷移期間作為對帳與回滾來源，但不是永久雙軌架構。
- 遷移單位是可獨立驗證的業務 capability，不是 ToolUtility 方法的一對一複製。
- 每個 capability 必須有 ProductClient contract、Registry definition、authorization／guard、
  connector executor、typed DTO、取消／逾時／錯誤契約、隔離／生命週期測試與真機證據。
- 完成後 ChurchReport 不得引用或傳遞 SDK `Entity`、`QueryBase`、
  `OrganizationRequest`、`IOrganizationService`、credential、endpoint 或 connector kind。

### 4.5 完全 Gateway 化的驗收方向

- [ ] 建立 ChurchReport 全部正式 D365 use case 與 capability operation 的可追溯矩陣。
- [ ] 每個仍在使用的 use case 都有 ProductClient 強型別方法與 Gateway executor。
- [ ] 需要支援的 CE 8.2／9.1 ConnectorKind 皆有明確 support／unsupported 狀態；不得以 Registry 登錄冒充已實作。
- [ ] 新舊路徑完成資料結果、錯誤語意、p50／p95／p99、配置與資源基線比對。
- [ ] 取消、逾時、故障連線、profile generation 切換與 host shutdown 後，lease／permit／WCF channel／worker／HTTP response 均回到宣告基線。
- [ ] 切換後掃描 ChurchReport，不再有產品端直接 CRM SDK／ToolUtility D365 呼叫。
- [ ] 回滾與逐 capability feature gate 經驗證；最終驗收後才移除 legacy route。

### 4.6 新的範圍決策

本 task 仍只負責定位與完整遷移設計。現有 worktree 同時存在尚未提交的 ChurchReport
錯誤復原／CRM 生命週期高風險變更；不得把全站 Gateway 遷移混入同一個不可分割
change set。需要由 parent task 管理全域矩陣，再以可獨立建置、測試、對帳、回滾的
vertical-slice child tasks 逐批交付。

### 4.7 多產品長期方向：新產品不再依賴 ToolUtility

使用者進一步確認建設公司、票款通、協會等未來產品是否應直接採 Gateway operation
模型，並依產品需求新增／修改 operation。建議與目前共識如下：

- ChurchReport 的最終目標是移除 ToolUtility 作為產品端 D365 runtime dependency；
  遷移期間可保留 legacy path 作結果對帳與可控回滾，完成後移除 DI、設定、專案參考與直接呼叫。
- 「移除 ToolUtility」不等於立刻刪除 repository project。只有當所有既有 consumer 都已
  遷移且回滾保留期結束，才以獨立退役任務刪除或封存 ToolUtility。
- 新產品預設不得再引入 ToolUtility／CRM SDK。它們只依賴強型別 ProductClient 與
  capability contract；部署可選 Embedded、DedicatedGateway 或 CentralGateway，但產品
  程式的 API 形狀保持一致。
- operation 不是依產品逐份複製。若業務語意、授權與 DTO 契約相同，應重用共同 capability；
  若語意或資料邊界不同，才建立 namespaced product operation。
- 每次新增或修改 operation 必須同步維護 Registry、版本化 contract、ProductClient、
  workload authorization、connector support matrix、executor、測試與 rollout evidence。
- Gateway／Connector 不應依賴整個 ToolUtility facade。可將經證明為純粹且共用的內部
  邏輯抽至適當底層模組，但不得把 legacy mutable service／session ownership 搬入 Gateway。

### 4.8 多產品 Operation 分層原則

預計把 operation catalog 分成：

1. 平台共同 capability：跨產品語意一致且可安全共用的健康、metadata 或明確受限資料能力。
2. 共用領域 capability：例如真正共享同一契約的 contact identity／payment reference 能力。
3. 產品專屬 capability：例如 `churchreport.*`、`construction.*`、`ticketing.*`、`association.*`。

產品專屬 client 可以提供 ToolUtility 類似的 C# 使用便利性，但不得暴露 SDK `Entity`、
`QueryBase`、任意 FetchXML 或 connector lease。

### 4.9 現有 P4／P5／P6 計畫缺口與 P8 重校（2026-08-06）

使用者詢問既有 P4、P5、P6 是否已包含「ChurchReport 完全 Gateway 化、移除
ToolUtility，以及未來多產品按需擴充 operation」的規劃。對照
`docs/dynamics-connection-management-plan.md` 後，結論是：**只包含基礎，沒有包含完整遷移。**

- P4 只建立 Embedded execution path、設定映射與一個奉獻收費清單的可見驗收；沒有
  全部 ChurchReport capability inventory／executor／ProductClient 遷移。
- P5 只使相同核心可由 Dedicated Gateway 獨立進程與 HTTPS 提供，並驗證 host、
  health／ready、router／pool 與隔離；計畫甚至明定 P5 不執行 `/v1` 或真實 CE 呼叫。
- P6 只把 Official 8.2／9.1 Worker 接進 Router，解決 ConnectorKind／版本相容與
  process lifecycle；它不是業務 operation 實作階段。
- P7 目前只遷移 Package 1 fee-read consumer，並不涵蓋 ChurchReport 全部讀、寫、
  action、function、metadata、attachment、background work，也沒有 ToolUtility 移除驗收。
- 舊版 P8 把 Central Gateway deployment 綁定第二產品；目前已改為先將單一 ChurchReport
  部署至雲端 Central Gateway，完成 workload/service identity、TLS、secret ownership、監控、
  rollback 與 live evidence。多產品 governance 另列未來獨立範圍。

因此既有 P4～P8 不足以交付使用者剛核准的完整目標。後續 design 必須保留 P4～P6
作平台基礎，並重寫／擴充 P7 之後的工作分解，加入全量 capability mapping、分批讀寫
遷移、ChurchReport ToolUtility removal gate，以及 P8 單一產品雲端 Central Gateway 部署。

---

## 5. 驗收標準

本規劃 task 完成需同時滿足：

- [ ] `prd.md` 明確記錄 ChurchReport 完全 Gateway 化、新產品禁止 ToolUtility／CRM SDK 與 ToolUtility 分階段退役決策。
- [ ] `design.md` 定義 ProductClient／Gateway／Connector 邊界、operation catalog 分層、資料流、錯誤、資源所有權、效能、rollout 與 CE 8.2／9.1 evidence gate。
- [ ] `implement.md` 將全量遷移拆成具 owner、依賴、驗證與回滾的 parent／child 交付樹；parent 不直接承擔巨型跨域 implementation。
- [ ] `docs/dynamics-connection-management-plan.md` 保留 P4～P6 基礎，並將 P7／P8 修訂為完整 ChurchReport cutover／ToolUtility removal 與單一產品雲端 Central Gateway 部署。
- [ ] P6／P7 單一授權總計畫允許代理在每一個 gate 全綠後自動銜接下一個 Trellis child；P8 仍需獨立目標與授權。
- [ ] 書面規格不存在未解 placeholder、互相矛盾的完成定義或把 Registry declaration 冒充 executable／production evidence 的敘述。
- [ ] 使用者完成書面審閱並核准後，才建立第一個 `gateway-capability-inventory` child；本 parent 不直接進入程式實作。

---

## 6. 不在範圍內

- 實作任何程式碼變更（本任務只到規劃）
- 移除 Data8 或 Worker 資產（已決議永久保留為擴充點）

## 2026-08-12 重新基準化補充（現行權威）

本 parent 的早期文字仍描述 P6／P7.0 尚待完成；該描述已被下列封存證據取代，僅保留作為歷史規劃脈絡：

- P3 Data8 generation-owned pool、P4 Embedded、P5 Dedicated Gateway、P6 Router／Pool／Lease 已完成；Official Worker live compatibility 維持 `evidence-pending`，但不阻擋 Data8-first P7。
- P7.0 已封存 70-row capability inventory 與 coverage validator；P7.1 已完成六項 Package01 typed Data8 read，取得 CE 9.1 唯讀證據，ChurchReport feature gate 仍為 disabled。
- P7.2 已封存本機候選版。Slice C 最後的 fresh CE cycle 是 `write-not-committed` no-go，且 exact cleanup 已完成；D–H 是 local-only contract，不能被宣稱為 executor、consumer 或 CE evidence。

現行唯一可執行的後續工作為 child `08-12-p7-remaining-work-rebaseline`：它必須以現行程式碼、設定與上述唯讀封存 evidence 產生新的 authoritative 70-row gap matrix。只有 matrix 指出且完成驗收的能力才可建立後續 P7.1／P7.2／P7.3／P7.4／P7.5 child；P8 一律等待 P7.5 immutable handoff。不得重試已封存的 CE cycle、復用 nonce／ledger／fixture／descriptor，或把 disabled feature gate、local-only plan、registry declaration 當成上線證據。

### 2026-08-12 P7.3 啟動補充（現行權威）

`08-12-p7-remaining-work-rebaseline` 已封存並產生 authoritative matrix；因此上段「唯一可執行」
的敘述已完成其歷史作用。現行 active child 是
`08-12-churchreport-special-resource-migrations`（P7.3），僅處理 ORG-CALL-00028、00029、00034、
00040、00063 的本機 bounded resource contract。它不開啟 ChurchReport consumer、feature gate 或 CE
寫入，也不構成 CE evidence、ToolUtility removal 或 P7.4/P7.5/P8 的完成證明。

### 2026-08-13 P7.4 admission boundary 補充（現行權威）

P7.3 已封存。P7.4 active parent `08-12-churchreport-productclient-cutover` 的 child
`08-13-p74-legacy-gateway-admission` 已完成並驗證 repository-side fail-closed legacy intake/drain
controller、deployment runbook、固定分類 validator、full solution tests 與 Release build。這個 child 明確
保留三個 external enablement blockers：同步 ToolUtility SDK I/O 不可 cancellation fence、全 legacy ingress
coverage 未獲證明、per-host memory controller 不能取代 canonical durable coordinator。

因此所有 checked-in `Package01FeeReadsEnabled` 值（appsettings、Development 及 DedicatedGateway
launch profile）均為 false；沒有 CE mutation、feature/traffic enablement、P7.5 或 P8 操作。後續 P7.4
可繼續依 matrix 完成獨立的 local-only consumer migration，但不得把本 child 的 local evidence 升格為
deployment/cutover evidence。P7.5 仍等待 zero-reference/parity/soak/drain/rollback 全綠；P8 仍等待
P7.5 immutable handoff 與獨立 deployment authorization。

### 2026-08-13 P7.2 定期定額付款回傳寫入邊界補充（現行權威）

已建立 child `08-13-p72-dedication-payment-return-write-boundary`，僅處理 recurring dedication
payment-return 的本機寫入邊界。現有 `RecurringDonationPaymentProcessor.HandlePaymentReturn` 混合 booking／
fee period 讀取、contact card 更新、fee create、fee owner assignment、booking completion 與 notification，
不可將 `fees.retrieve.by.dedication.period` typed read 直接接入 legacy 寫入鏈。

child 僅可交付 immutable、DTO-only、zero-I/O 的 decision／plan／TDD 與未來 governed fixture family 設計。
所有 CE executor 與 consumer 維持 disabled；timeout、ambiguous、partial、read-back mismatch 或 cleanup
uncertainty 必須 no-go/no-replay。歷史 Slice C nonce、ledger、fixture 與 descriptor 不可重試或復用。此工作
不阻擋其他獨立 P7.4 read-only migration，但 P7.5/P8 gate 不會提前解除。

### 2026-08-13 P7.5 前置證據閘門補充（現行權威）

P7.4 已完成數個 disabled-by-default local consumer boundary，但權威 matrix 仍有 70 個
`temporary-legacy` row，且 ChurchReport production source 仍有 ToolUtility／CRM SDK 依賴。因此建立
child `08-13-p75-prerequisite-evidence-zero-reference-gate`，交付離線、deterministic、去識別化的
matrix/source scanner、zero-reference enforcement gate 與 capability-family backlog。它只驗證目前
不具備 P7.5 removal 條件，不能改 matrix、產品程式、setting、feature gate、CE 或 P8。

真正 P7.5 removal child 只有在 report 顯示沒有 production temporary-legacy、ToolUtility／CRM SDK
reference、pending CE/host evidence，且 parity、soak、drain、rollback gate 都綠時才可建立。P8 仍只在
P7.5 commit/archive 產生 immutable handoff 後，並具雲端 host、identity、TLS、secret、network 與 deployment
authorization 時才可開始。

### 2026-08-13 P7.5 prerequisite evidence 完成結果（現行權威）

child `08-13-p75-prerequisite-evidence-zero-reference-gate` 已完成並產出 deterministic、去識別化的
離線報告。它的結論是正確的 `no-go`，不是工具失敗：70 個 matrix row 仍是
`temporary-legacy`，67 個 consumer 未遷移，所有 70 row 的 CE/host evidence 尚待完成，且 production
source、project dependency、settings key 各仍有 legacy reference。report 的所有靜態條件即使未來清除，
也只能稱為 `prerequisite-ready`，不能稱為 P7.5 removal、CE、切流或 P8 ready。

因此下一步是根據 capability-family backlog 建立下一個 independently verifiable local P7 child；所有
checked-in feature flag 維持 false，歷史 Slice C 不重試，ToolUtility removal 與 P8 仍等待所有真實
parity、soak、drain、rollback、commit/archive immutable handoff 先決條件。

### 2026-08-13 ORG-CALL-00014 app-named list catalog read（現行權威）

已建立 child `08-13-p71-appnamed-list-catalog-typed-read`，只處理權威 matrix 的
`ORG-CALL-00014`／`list.catalog.retrieve.app.named`。此 operation 是無 caller parameter 的固定 list
catalog read：legacy `ListService.RetrieveLists()` 對 `list` 固定投影 `listname`、`createdfromcode`、
`lastusedon`、`purpose`、`listid`，並固定 status、purpose 與 app-named filter。child 必須建立有界、
DTO-only、server-owned registry/Data8/ProductClient capability 與本機 isolation/lifecycle evidence；不接
ChurchReport consumer、不開 feature gate、不執行 CE、切流、P7.5 或 P8。

`ORG-CALL-00065` 雖同屬 list catalog family，卻有不同 operation ID、template、filter 和 consumer。
其 ChurchReport consumer 目前持有共享 `EntityCollection` memory cache，尚未證明 request-local DTO 和完整
isolation boundary；它維持 temporary-legacy，不能為了重用 registry 或加速交付而與 `00014` 合併、
rehydrate Entity、共享 cache 或自動 cutover。
