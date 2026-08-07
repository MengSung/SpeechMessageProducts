# Dynamics 365 連線管理實作計劃書

> 版本：1.1　｜　原始日期：2026-08-04　｜　P6～P8 路線重校：2026-08-06
> 對應規格：`docs/dynamics-connection-management-spec.md`
> 基準：HEAD `eb87c5fa` · Dynamics 測試 411 通過 / 0 失敗 / 7 略過（另 8 項 SQL live 通過）

---

## 0. 計劃原則

**P0　不推翻已驗證的資產。**
`Abstractions`、`ProductClient`、`Gateway` HTTP 主機、`ControlPlane`（容量准入＋世代管理）、`WorkerSupervisor` 以下 63 檔全部保留。本計劃是**補齊缺口與換掉底層傳輸**，不是第四次重寫。

**P1　每個階段都要能單獨驗收。**
每階段結束時方案必須可建置、測試全綠、且有一個可展示的成果。

**P2　最快讓 F5 能跑。**
`Embedded` 模式排在 Central Gateway 之前。開發體驗是這次調整的直接動機。

**P3　P6／P7 先在 Lenovo Legion 完整驗證，P8 才進雲端。**
P5 已封存，P6.1 離線 gate 已通過；P6.2 Official Worker 真機相容性目前保留為
`evidence-pending` 的未來獨立支線，不阻塞 Data8-first 的 P7。P7.0～P7.5 也在同一
開發主機完成 capability migration、local cutover 與 ToolUtility removal，並同時保留
`Embedded + Data8` 與 `DedicatedGateway + Data8`。P8 才以獨立目標部署單一 ChurchReport
cloud `CentralGateway + Data8`。離線綠燈不可冒充真機或雲端成功。

---

## 1. 前置決策（阻塞項，必須先完成）

| # | 項目 | 執行者 | 產出 | 阻塞什麼 |
|---|---|---|---|---|
| **A1** | 瀏覽器開啟 `…/Organization.svc?wsdl&sdkversion=8` 與 `=9`（對 8.2 伺服器） | 你 | 兩個回應的比對結果 | 決定 §11.1 是否必修 |
| **A2** | 在 8.2 伺服器執行 `Get-CrmOrganization` | 你 | `jesus` 的 OrganizationId | `OrganizationCatalog` 完整性 |
| **A3** | 確認 `Data8` 為 `ConnectorKind` 的預設實作 | 你 | 書面確認 | 全部後續階段 |

**A1 判定**
- 兩者皆回傳正常 WSDL → §11.1 降為選用，`_sdkMajorVersion` 可暫不改
- `=9` 對 8.2 出錯 → §11.1 升為 P2 必修項

**A3 已於 2026-08-04 對話中口頭確認：Data8 為永久保留的合法 Connector。** 請書面追認。

---

## 2. 階段規劃

### P1　契約層對齊（2～3 天）

**目標**：把規格的型別與守門規則落到程式碼，先不動任何傳輸。

| 工作 | 檔案 |
|---|---|
| `DynamicsExecutionMode` → `ConnectionMode`（三值） | `Abstractions/Execution/` |
| 新增 `ConnectorKind`、`CeVersion` 列舉 | `Abstractions/Execution/` |
| `ProductDynamicsOptions` 精簡為三欄位 | `Abstractions/Configuration/` |
| 新增 `OrganizationCatalog` 型別與載入器 | `Abstractions/Configuration/` |
| 新增 `IProfileResolver` / `ResolvedProfile` | `Abstractions/Configuration/` |
| 抽出 `IRequestGuard` 並補齊 G1 保留字檢查 | `ControlPlane/Guard/`（新） |
| 把 `Gateway/Security/*Authorizer` 接到 `IRequestGuard` | `Gateway/Security/` |

**驗收**
- §10.1 契約與守門測試全綠（4 項）
- §10.2 Profile 解析測試全綠（4 項）
- 五個 Organization 的 GUID 進入 `OrganizationCatalog`（`jesus` 待 A2）
- 方案 Release build 0 warning / 0 error
- 既有 411 項測試不退步

---

### P2　Data8 連接器修正（1～2 天）

**目標**：讓「無 Memory Leakage」這條底線成立。

| 工作 | 檔案 |
|---|---|
| `OnPremiseClient` 實作 `IDisposable`，保存 channel 與 ChannelFactory，Dispose 時 `Close()`／失敗 `Abort()` | `PowerPlatform.Dataverse.Client/OnPremiseClient.cs` |
| （視 A1 結果）`_sdkMajorVersion` 改實例欄位，建構子可選傳入 | 同上 |
| 保留 `Copyright © 2021 Data8 Limited`，於檔頭註明本地修改內容與日期 | 同上 |

**驗收**
- 單元測試：Dispose 後 channel 與 factory 皆為 `Closed` 或 `Aborted`
- 單元測試：Dispose 失敗時不吞例外、不阻斷後續清理
- （視 A1）單元測試：同一進程建立 `sdkVersion:8` 與 `sdkVersion:9` 兩個實例，各自送出正確參數
- P6 後整合閘門：對 `sunnyvalechback` 建立→Dispose 100 次，Handle 數不單調成長

---

### P3　連線池抽出與世代化（3～4 天）

**目標**：把 `CrmConnectionPool` 從 `ToolUtility` 抽出，成為規格 §7 的 `IConnectorPool`。

| 工作 | 位置 |
|---|---|
| 新增 `SpeechMessage.Dynamics.Connectors.Data8` 專案（net10） | 新專案 |
| 移植 `CrmConnectionPool` → `Data8ConnectorPool`，實作 `IConnectorPool` | 新專案 |
| 池鍵改為 `(ProfileAlias, GenerationId)` | 同上 |
| 實作 `IConnectorLease`（含 `MarkFaulted`） | 同上 |
| 接上 `ControlPlane` 既有的 `DynamicsProfileRuntimeManager` 世代機制 | `ControlPlane/Runtime/` |
| 新增 `IConnectorRouter`，只讀 `ResolvedProfile.ConnectorKind` | `ControlPlane/Runtime/` |
| `ToolUtility` 保留現有 legacy 路徑不動（Package01 旗標仍為 false） | `ToolUtility/` |

**驗收**
- §10.3 世代與容量測試全綠（7 項）
- §10.4 借還與洩漏測試全綠（8 項），含 soak 無單調成長
- P6 後整合閘門：`sunnyvalechback` 借出→執行 WhoAmI→歸還，重複 200 次，池大小穩定在 Min～Max 之間
- P6 後整合閘門：故意讓連線失效，確認淘汰路徑不把故障連線放回池

---

### P4　Embedded 模式（2～3 天）　★ 第一個可見成果

**目標**：VS 2026 按 F5，ChurchReport 直接取得真實 D365 資料。

| 工作 | 檔案 |
|---|---|
| 重寫 `AddSpeechMessageDynamicsEmbedded`（目前必拋例外） | `Embedded/DependencyInjection/` |
| 實作 `EmbeddedHostAdapter`：同進程呼叫，仍走完整 Guard→Resolver→Admission→Pool | `Embedded/` |
| 設定映射器：既有 `CrmConnection` 區段 → `DynamicsProfiles` ＋ `OrganizationCatalog` | `Embedded/Configuration/` |
| ChurchReport `appsettings.Development.json` 設為 `ConnectionMode: Embedded` | `SpeechMessageProducts.ChurchReport/` |

**驗收**
- §10.5 三模式等價性測試全綠（3 項）
- `Embedded` 模式下 `Gateway.Endpoint` 不存在也能啟動（規則 1.3）
- P6 後整合閘門：VS 2026 F5 → 登入 ChurchReport → 開啟奉獻收費清單 → 資料由 `Embedded` 路徑取得，與 legacy 路徑筆數一致
- 關閉後無殘留進程、Handle 或 Socket

---

### P5　Dedicated Gateway 對齊（2～3 天）

**目標**：同一套核心以獨立進程 ＋ HTTPS 提供，行為與 Embedded 一致。

| 工作 | 檔案 |
|---|---|
| `Gateway` 主機改用 P3 的 `IConnectorRouter` ＋ `Data8ConnectorPool` | `Gateway/Program.cs` |
| 移除對 `WorkerSupervisor` 的硬性相依，改為 Router 的一種註冊 | `Gateway/` |
| `appsettings` 加入 `DynamicsProfiles` ＋ `OrganizationCatalog` 區段 | `Gateway/` |
| VS 多重啟始專案設定（ChurchReport ＋ Gateway） | `.sln` / launchSettings |

**驗收**
- 端點矩陣：`/health` 200、`/ready` 200、匿名 `/v1` 401、錯誤 alias 403、未授權 operation 403
- P6 後整合閘門：同一查詢在 `Embedded` 與 `DedicatedGateway` 兩種模式下結果逐筆一致
- 產品端只需改一個 `ConnectionMode` 字串即可切換

#### P5 開發操作與離線驗證補充（2026-08-05）

P5 的 Dedicated Gateway 是可隨 ChurchReport 一起部署的獨立進程，不是 Central Gateway 的前置條件，也不會取代一般開發時的 Embedded F5。

| 情境 | Visual Studio 2026 設定 | 結果 |
|---|---|---|
| 一般產品開發 | 只啟動 ChurchReport 的 `ChurchReport`（或 IIS Express）profile | `ConnectionMode=Embedded`；不讀取 `Gateway.Endpoint`，沒有 HTTP hop。 |
| Dedicated Gateway 開發／除錯 | Multiple Startup Projects：先啟動 `SpeechMessage.Dynamics.Gateway` 的 `DedicatedGateway` profile，再啟動 ChurchReport 的 `DedicatedGateway` profile | ChurchReport 以 `https://localhost:7244/` 呼叫固定的 `sunnyvalechback` profile；Gateway 使用獨立的 Data8 runtime、pool、admission、client 與 permit。 |
| 回滾 | 改回 ChurchReport 一般 profile，或把 `DynamicsAccess:ConnectionMode` 設為 `Embedded` 後重新啟動產品 | 不修改 profile、pool、Catalog 或外部 CE；不可自動退回 Official Worker、Central Gateway 或其他 connector。 |

Dedicated Gateway 的首次本機操作步驟：

1. 在使用者工作階段設定一次 `CRM_PASSWORD` 環境變數；它只由 Gateway child process 的 startup configuration 讀取，**不得**寫入 `appsettings*.json`、`launchSettings.json`、Git、log 或產品 request。
2. 在方案 Properties 的 **Multiple startup projects** 中，把 `SpeechMessage.Dynamics.Gateway` 排第一並選取 `DedicatedGateway` profile；把 `SpeechMessageProducts.ChurchReport` 排第二並選取同名 profile。
3. 按 F5 後，僅以 `GET https://localhost:7244/health` 與 `GET https://localhost:7244/ready` 檢查本機 host。`/ready` 的 `runtime=configured` 只代表 DI 已完成 Dedicated Data8 runtime 的組態化，**不代表**已連上 CE、已驗證密碼或已建立 Data8 session。
4. P5 不執行 `/v1`、WhoAmI、CE、SQL、IIS、DNS、ADFS、IFD、CRMWeb 或 Web API 真機呼叫。外部 CE 跨模式一致性、效能與 soak 量測仍完全延至 P6 後。

Dedicated mode 只重用 Embedded 的 Data8 runtime 程式碼與 immutable profile/catalog shape；兩個 host 不能共用 runtime、pool、admission、Data8 client、lease、permit、credential、token 或其他可變 Session 狀態。Gateway host stop 時由 `DedicatedData8RuntimeHostedService` 唯一 await runtime 的 drain/dispose，程序邊界測試會確認 listener 與 process 回到基線。

---

### P6　Official Worker 接進 Router（1～2 天）

**目標**：把既有 63 檔 Worker 資產接成 `ConnectorKind` 的第二種實作，作為擴充點保留。

**目前狀態（2026-08-07）**：P6 task 為 `in_progress`；P6.1 Router／Pool／Lease、離線
lifecycle 與正式 quality check 已通過。Lenovo Legion readiness material 已經是 `go`，
但兩個 Official Worker 都在 READY 前結束，沒有 CE operation；因此 live compatibility
記為 `evidence-pending`。本次 P6 只需完成文件／spec／quality／結案 gate，不重跑 startup，
不部署雲端，也不切換 ChurchReport consumer。

`sunnyvalechback` 已確認是與正式系統分離的 CE 9.1 公司研發 Organization。P7.2 可依
matrix 將它作為 test-owned fixture environment；P6 不執行業務 write/action/function。
若未來明確選用 Official Worker，才另立 task 取得其 read-only identity／connection evidence；
CE 8.2／9.1 的 Official Worker live evidence 不是 Data8 P7 的必要條件。

| 工作 | 檔案 |
|---|---|
| `OfficialWorkerPool` 實作 `IConnectorPool`／`IConnectorLease` | `WorkerSupervisor/` |
| 註冊為 `ConnectorKind.OfficialCrm82Worker` / `OfficialCrm91Worker` | `ControlPlane/` |
| §5.2 相容性矩陣在 Profile 載入時強制 | `ControlPlane/Runtime/` |
| **不進 CI 的平行行為測試**；只保留既有的 Worker 單元測試 | — |

**驗收**
- 相容性矩陣測試全綠（Official82 × Ce91 等不合法組合被拒）
- 以 `ConnectorKind: OfficialCrm91Worker` 建立一個測試用 ProfileAlias，能啟動並回應
- 預設 Profile 仍為 `Data8`，未啟用 Official 時不啟動任何 net48 進程
- P6 程式與離線測試完成後，保存 Official Worker live compatibility=`evidence-pending`；
  P7 先以 Data8 驗證 `Embedded + Data8` 與 `DedicatedGateway + Data8` 的 capability 結果、
  p50／p95／p99、故障淘汰與所有資源回到基線。Official Worker 的外部 CE 整合量測另立 task。

---

### P7　ChurchReport 完全 Gateway 化（多個可獨立驗證批次）

**目標**：將 ChurchReport 的全部正式 D365 業務能力改由強型別 ProductClient 呼叫受控 operation，完成 CE 8.2／9.1 證據、逐 capability rollout 與 ToolUtility removal gate。P7 不是把 ToolUtility 方法一對一搬成遠端 CRUD，而是以業務 use case 組合粗粒度 capabilities。

#### P7.0　Capability inventory 與 coverage gate

- 將 Phase 0 的 70 個 normalized call-site rows 對應到業務 use case、Operation ID、ProductClient owner、consumer、ConnectorKind／CeVersion support 與 rollout 狀態。
- 建立 deterministic coverage validator；Registry 登錄、executor 實作、consumer enablement 與真機 evidence 分欄追蹤，禁止用單一「已完成」混淆不同層級。
- 未分類 call site、無 owner 的 temporary legacy 或未經授權的 generic CRUD／FetchXML 都阻塞 P7.5。

#### P7.1　Read capabilities

- 先完成既有 Package 1 fee／stor 六個 operations 的 Data8 executor、typed projection、ProductClient 與 Tier A～D rollout。
- 再依 capability matrix 拆分 MemberInfo、Contact／List、Activity／report、metadata 等 children；每個 child 必須能獨立測試、對帳與回滾。
- Read 可做有界 shadow comparison，但 shadow failure 不得污染 authoritative response，且取消／逾時後不得保留 task、timer、registration、permit、lease 或 response buffer。

#### P7.2　Write／Action／Function capabilities

- 依 transaction／idempotency／authorization 邊界拆分 Create、Update、Associate／Disassociate、Action 與 Function。
- 每次只允許一條 authoritative writer；禁止未設計的 dual-write。每個 operation 明確定義重複送達、optimistic concurrency、部分完成、timeout-after-commit 與 reconciliation。
- CE 9.1 live evidence 使用隔離的 `sunnyvalechback` 與唯一 test member／test-owned records。此確認只代表環境級可行性，不是任意寫入授權；每個 matrix-required operation family 都必須在 activation 前定義 allowed mutations、fixture owner、cleanup/reconciliation 與 ambiguous-timeout policy。CE 8.2 只有 matrix 標示 required 的 capability 需要相應 write fixture；其他組合明確 unsupported 並 fail closed。

#### P7.3　特殊資源能力

- Attachment、large paging、background／scheduler、metadata cache 與其他 stream／process／subscription owner 使用獨立 contract 與 lifecycle gate。
- Payload、page、queue、concurrency 與 retention 全部有硬上限；cleanup／drain／dispose failure 是 release blocker。

#### P7.4　Product cutover

- Controller、Service 與 WebServiceConnector 逐 capability 改依賴 ProductClient；產品不再傳遞 SDK `Entity`、`QueryBase`、`OrganizationRequest`、`IOrganizationService`、credential、endpoint 或 connector kind。
- 每個 capability 以獨立 feature gate 切換；任一資料差異、錯誤語意、隔離、效能或資源退步只回滾該 capability。

#### P7.5　ToolUtility removal gate

- Capability matrix 不得再有 ChurchReport production temporary-legacy rows。
- 移除 ChurchReport 對 ToolUtility／CRM SDK 的 project reference、DI／Factory、legacy credential／endpoint 與直接呼叫。
- 完整 Release build、Dynamics／ChurchReport tests、zero-reference scan、Data8 在必要
  `Embedded`／`DedicatedGateway` 組合的真機結果、p50／p95／p99、soak 與資源基線全部
  通過後，才關閉 rollback window。未選用的 Official Worker 不得被當成 P7 blocker。
- P7.5 只移除 ChurchReport dependency；ToolUtility project 若仍有其他 consumer，保留至獨立退役任務。

---

### P8　單一 ChurchReport 雲端 Central Gateway（P7.5 後獨立啟動）

**目標**：將已在 Lenovo Legion 完成 P7.5 驗收的單一 ChurchReport 部署至雲端機房，透過 Central Gateway 正確存取 D365，並完成安全、監控、rollback drill 與 live validation。P8 不等待第二產品，也不在部署階段重新設計 P7 capability。

| 子階段 | 工作 | 驗收重點 |
|---|---|---|
| P8.0 | Cloud deployment readiness | host、network、DNS、TLS、service identity、secret provider、CE reachability、部署／rollback package 齊全；缺一即 No-Go |
| P8.1 | Host／service identity／TLS hardening | 最小權限 workload allowlist；未授權 caller 在 body parsing／Profile resolution／outbound work 前被拒絕；secret 不落入產品或 artifact |
| P8.2 | Central Gateway＋Data8 deployment baseline | 以 Data8 為第一個 ChurchReport composition；可重現 install/start/restart/drain/stop；process、connection、channel、handle、permit、queue 與 generation 有單一 owner／baseline。若未來明確選用 Official Worker，另立 task 取得 evidence 後才納入。 |
| P8.3 | ChurchReport cutover | 先受控 smoke，再只切 endpoint／deployment-owned routing；不混入 contract、Profile、ConnectorKind 或 CE version 變更 |
| P8.4 | Live validation／monitoring／rollback／closure | 功能、p50／p95／p99、錯誤率、資源、告警與實際 rollback drill 全綠，觀測窗通過才結案 |

第二、第三產品的 catalog governance、workload policy、公平排程與 noisy-neighbor 容量驗證屬未來獨立 onboarding task，不是 P8 完成條件。

---

## 3. 時程總覽

| 階段 | 工作天 | 累計 | 產出 |
|---|---|---|---|
| A1～A3 前置 | 0.5 | 0.5 | 決策確認 |
| P1 契約層 | 2～3 | 3.5 | 型別與守門就緒 |
| P2 Data8 修正 | 1～2 | 5.5 | 洩漏底線成立 |
| P3 連線池 | 3～4 | 9.5 | 池化與世代就緒 |
| **P4 Embedded** | 2～3 | **12.5** | **★ F5 可跑，受控核心就緒** |
| P5 Dedicated | 2～3 | 15.5 | 獨立進程與 HTTPS 對齊 |
| P6 Official 接入 | 1～2 | 17.5 | Connector 擴充點就緒 |
| P7.0 Capability inventory | 獨立批次 | — | 權威 coverage matrix 與 validator |
| P7.1～P7.3 Capability slices | 依矩陣拆分 | — | 全部讀、寫與特殊資源 operations |
| P7.4～P7.5 Cutover／Removal | 依驗證批次 | — | ChurchReport 完全 Gateway 化並移除 ToolUtility dependency |
| P8.0～P8.4 ChurchReport cloud Central | P7.5 後獨立批次 | — | 單一 ChurchReport 雲端部署、cutover、monitoring、rollback 與 live evidence |

先前「P7 只需 3～5 天、全計畫 4～5 週」的估算只涵蓋 Package 1 fee-read，已被 ChurchReport 完全 Gateway 化的新範圍取代。P7 總量必須由 P7.0 capability matrix 依獨立 rollback owner 與真機 evidence 數量估算，不以 70 個 call-site rows 或 ToolUtility 方法數直接換算工期。

P4 仍是第一個可見平台里程碑；P7.5 是 ChurchReport 本機產品遷移完成里程碑；P8.4 是本階段單一 ChurchReport 雲端 Central Gateway 完成里程碑。

---

## 4. 風險與緩解

| 風險 | 機率 | 影響 | 緩解 |
|---|---|---|---|
| A1 顯示 8.2 不接受 `sdkversion=9` | 中 | 低 | §11.1 改實例欄位，約 5 行 |
| Data8 修 `IDisposable` 後行為改變 | 低 | 中 | P2 先做 100 次建立／Dispose 真機驗證再往下 |
| 同進程雙版本仍有未知衝突 | 低 | 高 | P3 驗收含「兩 Organization 同進程」測試；若失敗退回 Official Worker（P6 已備妥） |
| 抽出 `CrmConnectionPool` 影響 legacy 路徑 | 中 | 高 | `ToolUtility` 保留原始碼不動；新池是複製後改造，不是就地重構 |
| 又一次方向變更 | — | 高 | 本計劃已鎖定 Data8＋三模式；`ConnectorKind` 作為擴充點吸收未來變化，不再改架構 |

---

## 5. 不做什麼（明確排除）

| 項目 | 理由 |
|---|---|
| 移除 Data8 | 已決定永久保留 |
| 刪除 63 檔 Worker 資產 | 保留為 `ConnectorKind` 擴充點 |
| 強制使用 SQL | 規則 6.9：非必要條件 |
| 直接 Web API／OData 傳輸 | 已於 2026-08-02 退役 |
| 在 P7.5 前提前部署 Central Gateway | 雲端只接收已完成本機驗收的 contract／deployment package；避免把 migration 與 deployment 風險疊在一起 |
| 為 Official Worker 寫平行行為測試 | 規則：不承諾兩種 Connector 行為一致 |

---

## 6. 立即可執行的下一步

1. 完成 P6.1 文件、spec、quality 與結案 gate，保留 Official Worker live compatibility=`evidence-pending`；不重跑 P6.2 startup。
2. P6 封存後，依 `docs/superpowers/plans/2026-08-06-p6-p7-integrated-execution.md` 自動銜接 P7.0～P7.5，以 Data8 驗證 `Embedded + Data8` 與 `DedicatedGateway + Data8`；P8 保持未啟動。
3. 若未來要補 Official Worker，另立獨立 deployment task；第一個 ChurchReport cloud deployment 仍固定為 `CentralGateway + Data8`。

---

## 7. P4.1：D365 8.2 Organization Catalog（已登錄）

P4 已將使用者在 D365 8.2 匯出的 27 筆 Organization（含 Enabled／Disabled）與既有 5 筆 D365 9.1 組織，
登錄至 ChurchReport 的 `CrmConnection:OrganizationCatalog`。這不是新的產品 Profile 檔：產品仍只需要設定一個
`DynamicsAccess:ProfileAlias`，例如 `sunnyvalechback`、`jesus` 或 `speechmessage-ce82`。

ID 資料只能完成「選對組織與版本」，不能推導連線端點或 credential。因此目前只有已配置 HTTPS ServiceUri 的
`sunnyvalechback` 可在 P6 後進入真機整合路徑；其他 CE 8.2 alias 一旦被選取，會安全拒絕而不誤連 9.1。等各 8.2 組織
提供經核准的 ServiceUri 後，補入同一 Catalog entry 即可啟用，不需改產品程式或複製 OrganizationId。
