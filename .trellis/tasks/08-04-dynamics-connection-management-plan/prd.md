# Dynamics 連線管理新版規劃

## 目的與使用者價值

以 `docs/dynamics-connection-management-spec.md` 與
`docs/dynamics-connection-management-plan.md` 為唯一權威來源，整理一份可執行、可驗證且不受舊方向影響的 PRD、架構設計與分階段實作計畫。

完成後，產品可透過設定選擇 `Embedded`、`DedicatedGateway` 或 `CentralGateway`，並以 `ProfileAlias` 選定目標 Dynamics 365 組織；Connector 的選擇則由受保護的 Profile 決定。此規劃必須確保沒有跨使用者、跨組織或跨設定檔的狀態外洩，且所有連線、通道、工作程序、許可與非受控資源都有明確且可測試的釋放路徑。

## 已確認事實

- 本 Task 是既有 `07-23-dynamics-connection-compatibility` 的子 Task；舊 Task 的早期 PRD 僅保留歷史，不得覆蓋本 Task。
- `Data8` 是永久合法的 `ConnectorKind`，不是暫時回滾方案；未來可與兩種官方 .NET Framework 4.8 Worker Connector 並存。
- 三種永久 `ConnectionMode` 為 `Embedded`、`DedicatedGateway`、`CentralGateway`；它們與 `ConnectorKind` 是獨立維度。
- `Embedded` 需要支援 Visual Studio 2026 按 F5 的同進程開發與部署；`DedicatedGateway` 可隨單一產品部署並使用 localhost HTTPS；`CentralGateway` 可由多個產品共用。
- 產品端只設定 `ConnectionMode`、`ProfileAlias`，以及非 Embedded 時的 `Gateway.Endpoint`。產品端不得持有 Organization ID、CRM endpoint、ConnectorKind 或憑證。
- Profile 與 Pool 的隔離鍵為 `(ProfileAlias, GenerationId)`；同一實體 Organization 的容量預算依確認的 Organization ID 共用。
- 初始 Catalog 已有 elijah、david、solomon、speechmessage 與 sunnyvalechback；`sunnyvalechback` 是 CE 9.1 + Data8 的第一個啟用 Profile。
- 不再以直接 Web API、IFD 或 D365APP01 管理通道作為此架構的開發或驗證路徑。
- SQL 只是一種可替換的分散式容量協調器；Embedded、DedicatedGateway 與單節點 CentralGateway 不以 SQL 為前置條件。

## 決策紀錄：為什麼是這個方向

本專案於 2026-07-23 至 08-04 之間經歷三次架構轉向。本節記錄各次失效原因，避免第四次重複繞路。

| 版本 | 方向 | 失效原因 |
| --- | --- | --- |
| v1（07-23） | 完全禁用 SDK，純 HTTP／OData v4 | CE on-premises 的 ADFS 服務身分權杖流程可能不存在。微軟文件明載 `Microsoft.PowerPlatform.Dataverse.Client` 對 on-prem「this article is not for you, yet」；`AuthType=AD`／`IFD` 僅適用 on-prem 而非雲端 Dataverse |
| v2（07-29） | 官方優先 ＋ Central／Local Gateway | 需要管理員身分在 ADFS 註冊 OAuth client，該通道無法取得，工作停滯 17.5 小時 |
| v3（08-02） | 官方 NuGet Worker（net48 獨立進程） | 並行度等於進程數；`WorkerCount=1, MaxInFlightPerWorker=1` 實際為序列化執行，與「節省資源」目標相反 |

### v4 採用 Data8 的五個理由

1. 它是目前唯一在 .NET 10 上能連 on-prem IFD 的路徑。官方 `ServiceClient` 走 MSAL／OAuth 是給雲端的；Data8 在官方套件之上補了 WS-Trust 那一段。
2. 它現在就在生產環境運作，已連通 CE 8.2 與 CE 9.1。
3. 它不是外部相依，而是本 repo 內的原始碼：55 檔／6,919 行、MIT 授權。上游消失不影響本專案。
4. 同進程池化的資源成本遠低於多進程：20 條並行在 Data8 是 1 個進程 20 個連線物件；在 Worker 架構是 20 個 net48 進程。
5. 使用者於 2026-08-04 明確表示「不一定非要用官方的方案，現在的 Data8 也無所謂」。

### 為什麼 `Embedded` 不得是繞過治理的捷徑

v2 曾以「開發走直接呼叫、正式走 HTTP，兩條路不同」為由否定 Embedded。該顧慮成立，但正確的解法不是刪掉 Embedded，而是讓它**只省略 HTTP 傳輸、不省略任何治理層**。若 Embedded 繞過 Profile、Request Guard 或 Admission，等同回到產品自持憑證的舊狀態，且開發時測到的行為無法代表正式環境。

### 官方 Worker 資產的處置

既有 63 檔（`WorkerProtocol` 19、`WorkerHost` 19、`WorkerSupervisor` 11、兩個 Worker 8、測試 6）保留為 `ConnectorKind` 的擴充點：不刪除、不進 CI、不寫平行行為測試。若日後出現「只能使用原廠元件」的合規要求，替換 Connector 這一層即可，上層不動。

## 需求

1. 產出清楚界定範圍與可量測驗收條件的 PRD。
2. 產出架構設計，清楚說明三種模式、Profile、Organization Catalog、Connector Router、generation-isolated pool、admission 與資源釋放的責任邊界。
3. 產出分階段實作計畫；第一個實作切片必須可獨立測試、回滾及驗證，且不重寫既有完成的 P1/P2 成果。
4. 任何後續程式都必須避免 Session、Memory、Connection、Worker、Permit、Timer、Task、Handle、credential 與 cross-profile state leakage。
5. 所有新增或實質修改的 `.cs`／`.cshtml` 必須具完整繁體中文註解、UTF-8 without BOM、CRLF 與最終 CRLF。

## 非範圍

- 不重啟或修復 D365APP01 的 CRMWeb／IFD。
- 不以 Web API 取代 Data8 或官方 Worker Connector。
- 不將 SQL、Registry、IIS、DNS 或 ADFS 當成任何 Dynamics 管理或診斷替代通道。
- 不在這個規劃 Task 中直接移除 Data8 或既有官方 Worker 資產。

## 初步驗收標準

- [ ] PRD 僅含產品目標、需求、限制、範圍與可測試的驗收條件，沒有與新版決策衝突的歷史假設。
- [ ] `design.md` 明確表示 Data8 為永久 Connector，並以 `(ProfileAlias, GenerationId)` 隔離 Pool 與可變狀態。
- [ ] `design.md` 明確表示 `ConnectionMode` 與 `ConnectorKind` 可獨立組合，且產品端設定不包含 CRM routing 或 secret material。
- [ ] `implement.md` 將可獨立驗證的交付項拆分成子 Task，列出測試、回滾與實機部署驗證條件。
- [ ] 規劃先由使用者審閱並確認，再啟動任何尚未開始的實作 Task。

## 已決產品決策（2026-08-04 使用者確認）

| # | 決策 | 內容 |
| --- | --- | --- |
| D1 | 實作範圍終點 | **做到 P4：ChurchReport Embedded 於 VS 2026 按 F5 可取得真實 D365 資料。** P5 之後另開任務 |
| D2 | 交付順序 | P1 契約層 → P2 Data8 修正 → P3 世代化 Pool → **P4 Embedded F5（可展示里程碑）** |
| D3 | 效能驗收方式 | **不訂絕對數字。** 先量測 legacy（`ToolUtility` → Data8）路徑的 p50／p95／p99 作為基準，新路徑 p95 不得劣於該基準 |
| D4 | Data8 定位 | 永久合法 Connector，不列入任何移除計劃 |

## 實作驗收標準（P1～P4）

以下條件為本任務實作切片的完成定義。詳細規則編號對應 `docs/dynamics-connection-management-spec.md`。

### 契約與守門（P1）

- [ ] `ConnectionMode` 三值、`ConnectorKind` 三值、`CeVersion` 二值型別存在且被設定驗證使用
- [ ] `ProductDynamicsOptions` 公開表面只有 `ConnectionMode`／`ProfileAlias`／`Gateway` 三個欄位
- [ ] 反射測試證明 `OperationExecutionRequest` 公開表面不含 CRM 型別、`OrganizationId`、`ConnectorKind`、`Credential`、endpoint 或 FetchXML
- [ ] `RequestGuard` 對規格 §3.2 的 G1～G6 六項全部拒絕，且在讀取要求本體前完成
- [ ] `ProfileResolver` 對規格 §4.3 的 R1～R6 六種情形全部 fail closed，含全 0 與全 f 佔位 GUID
- [ ] `OrganizationCatalog` 載入五個 Organization；`jesus` 待 A2 補齊

### Data8 修正（P2）

- [ ] `OnPremiseClient` 實作 `IDisposable`；Dispose 後 channel 與 ChannelFactory 皆為 `Closed` 或 `Aborted`
- [ ] Dispose 過程的例外不被吞掉、不阻斷後續清理，多個失敗以 `AggregateException` 彙總
- [ ] 依 A1 結果：若 8.2 不接受 `sdkversion=9`，`_sdkMajorVersion` 已改為實例欄位並由 `CeVersion` 決定
- [ ] 保留 `Copyright © 2021 Data8 Limited`，檔頭註明本地修改內容與日期
- [ ] **真機**：對 `sunnyvalechback` 建立→Dispose 100 次，Handle 數無單調成長

### 連線池與生命週期（P3）

- [ ] 池鍵為 `(ProfileAlias, GenerationId)`
- [ ] 同一 ProfileAlias 同時最多 1 個 Active ＋ 1 個 Draining 世代
- [ ] 舊世代在 Lease 歸零後才 Dispose 其 Pool
- [ ] Admission 佇列有界，滿時立即拒絕 `admission.queue-full`
- [ ] Permit 在例外路徑下仍必定釋放
- [ ] **Permit 於解析 Active Generation 之前取得**（規格規則 7.1）
- [ ] 健康資源歸還原世代 Pool；故障資源永不回池
- [ ] 操作結束後無殘留 Process、Timer、Handle、Task 或 Cancellation Registration
- [ ] Soak：重複借出／歸還，記憶體、Handle、Thread 皆無單調成長
- [ ] 兩個 Organization 於同一進程互不共用狀態
- [ ] **真機**：`sunnyvalechback` 借出→WhoAmI→歸還重複 200 次，池大小穩定在 Min～Max 之間

### Embedded 模式（P4）

- [ ] `AddSpeechMessageDynamicsEmbedded` 可成功註冊（不再無條件拋例外）
- [ ] `Embedded` 模式下 `Gateway.Endpoint` 不存在也能啟動
- [ ] `Embedded` 模式不繞過 `DynamicsOperationContract` 與 `RequestGuard`
- [ ] 沿用既有 `CrmConnection` 設定區段，不需另建 profile 檔、不需 SHA-256 雜湊、不需佈建資料庫
- [ ] **VS 2026 按 F5 → 登入 ChurchReport → 開啟奉獻收費清單 → 資料由 Embedded 路徑取得**（可展示）
- [ ] 與 legacy 路徑相同查詢結果逐筆一致
- [ ] 關閉後無殘留進程、Handle 或 Socket

### 效能（D3）

- [ ] 已量測並記錄 legacy 路徑的 p50／p95／p99 基準
- [ ] Embedded 路徑同一查詢的 p95 不劣於該基準

### 整體

- [ ] 方案 Release build 0 warning／0 error
- [ ] 既有 411 項 Dynamics 測試不退步
- [ ] 遙測掃描通過：無 Credential、Token、Cookie、Session ID、CRM endpoint、Organization GUID
- [ ] `Package01FeeReadsEnabled` 仍為 `false`，legacy 路徑行為未變

## 前置阻塞項

| # | 項目 | 執行者 | 阻塞 | 狀態 |
| --- | --- | --- | --- | --- |
| **A1** | 瀏覽器開啟 8.2 伺服器的 `…/Organization.svc?wsdl&sdkversion=8` 與 `=9`，比對回應 | 使用者 | 決定 `_sdkMajorVersion` 是否必須改為實例欄位 | 待辦 |
| **A2** | 在 8.2 伺服器執行 `Get-CrmOrganization`，取得 `jesus` 的 OrganizationId | 使用者 | `OrganizationCatalog` 完整性 | 待辦 |

兩項合計約 10 分鐘，皆不需新權限。A1 未完成前，P2 的 `_sdkMajorVersion` 項目維持未定。

