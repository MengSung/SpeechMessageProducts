# Dynamics 365 連線管理實作計劃書

> 版本：1.0　｜　日期：2026-08-04
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

**P3　真機驗證集中到 P6 後。**
P4、P5、P6 先完成程式與離線生命週期驗證；使用者完成 Embedded Data8 與 Dedicated Gateway 的部署前測試後，
才在 P6 後以同一受控環境執行一次跨模式外部 CE 整合量測。離線綠燈不可冒充真機成功。

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
- P6 程式與離線測試完成後，才執行一次外部 CE 整合量測：legacy／Embedded／Dedicated 的結果一致性、p50／p95／p99、
  200 次 borrow/use/return、故障淘汰與所有資源回到基線。

---

### P7　消費者遷移（3～5 天）

**目標**：把 ChurchReport 的 Package 1 讀取路徑實際切到新架構。

| 工作 |
|---|
| 依既有 Tier A～D 分級，逐步開啟 `Package01FeeReadsEnabled` |
| 每一級與 legacy 路徑做筆數與金額對帳 |
| 任一級失敗即關閉旗標回退 |

**驗收（每一級）**
- 同一聯絡人、同一日期區間，新舊路徑筆數與總額一致
- p95 延遲不劣於 legacy 路徑
- 連續操作 1 小時，記憶體與 Handle 無單調成長

---

### P8　Central Gateway（可選，視需要啟動）

**目標**：多產品共用。**在只有 ChurchReport 一個產品時不需要執行本階段。**

| 工作 |
|---|
| 部署為共用服務，工作負載身分驗證 |
| 多節點時才導入 `IDistributedCapacityCoordinator`（既有 SQL 實作可直接用） |
| 跨產品公平排程與聚合容量驗證 |

**觸發條件**：第二個產品要接入時。

---

## 3. 時程總覽

| 階段 | 工作天 | 累計 | 產出 |
|---|---|---|---|
| A1～A3 前置 | 0.5 | 0.5 | 決策確認 |
| P1 契約層 | 2～3 | 3.5 | 型別與守門就緒 |
| P2 Data8 修正 | 1～2 | 5.5 | 洩漏底線成立 |
| P3 連線池 | 3～4 | 9.5 | 池化與世代就緒 |
| **P4 Embedded** | 2～3 | **12.5** | **★ F5 可跑，真機取得資料** |
| P5 Dedicated | 2～3 | 15.5 | 三模式等價 |
| P6 Official 接入 | 1～2 | 17.5 | 擴充點就緒 |
| P7 消費者遷移 | 3～5 | 22.5 | 實際切換 |
| P8 Central | 視需要 | — | 多產品時才做 |

**單一開發者連續投入約 4～5 週；若維持先前觀測到的 AI 輔助節奏，可壓縮至 2～3 週。**

**最有價值的里程碑是 P4（第 12.5 天）** —— 在那之前的所有工作都是為了讓那一次 F5 成立。

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
| Central Gateway 先行 | 只有一個產品時無收益 |
| 為 Official Worker 寫平行行為測試 | 規則：不承諾兩種 Connector 行為一致 |

---

## 6. 立即可執行的下一步

1. **你**：完成 A1（瀏覽器兩個 URL）與 A2（8.2 伺服器跑 `Get-CrmOrganization`）
2. **你**：書面追認 A3（Data8 為預設 Connector）
3. **開發**：A1／A2 回來後即可開始 P1

A1 與 A2 都不需要新權限，且合計約 10 分鐘。

---

## 7. P4.1：D365 8.2 Organization Catalog（已登錄）

P4 已將使用者在 D365 8.2 匯出的 27 筆 Organization（含 Enabled／Disabled）與既有 5 筆 D365 9.1 組織，
登錄至 ChurchReport 的 `CrmConnection:OrganizationCatalog`。這不是新的產品 Profile 檔：產品仍只需要設定一個
`DynamicsAccess:ProfileAlias`，例如 `sunnyvalechback`、`jesus` 或 `speechmessage-ce82`。

ID 資料只能完成「選對組織與版本」，不能推導連線端點或 credential。因此目前只有已配置 HTTPS ServiceUri 的
`sunnyvalechback` 可在 P6 後進入真機整合路徑；其他 CE 8.2 alias 一旦被選取，會安全拒絕而不誤連 9.1。等各 8.2 組織
提供經核准的 ServiceUri 後，補入同一 Catalog entry 即可啟用，不需改產品程式或複製 OrganizationId。
