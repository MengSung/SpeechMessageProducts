# ChurchReport 完全 Gateway 化與多產品 Operation 治理設計

> 日期：2026-08-05  
> 狀態：待使用者書面審閱  
> 核准方向：ChurchReport 最終完全移除產品端 ToolUtility／`IOrganizationService` D365 存取；新產品一律採 capability operation 模型。

## 1. 設計目標

建立一個所有 SpeechMessage 產品都能使用的 Dynamics 存取邊界：產品只呼叫強型別 ProductClient；Gateway／Embedded 執行層負責 operation authorization、Profile 解析、Organization admission、Connector lease、D365 呼叫、錯誤清理與結果投影。

本設計保留 P4 Embedded、P5 Dedicated Gateway、P6 Official Worker 作為已建平台基礎，擴充 P7 為 ChurchReport 全量 capability 遷移，並擴充 P8 為多產品 operation governance 與 Central Gateway onboarding。

## 2. 已確認的現況缺口

- Phase 0 有 70 個 normalized CRM call-site rows；它們不是 70 個 ToolUtility 方法，也不是 70 個 operations。
- `Package01OperationRegistry` 宣告 9 個 operations，但 Data8 executor 目前只真正執行 `runtime.health.whoami`。
- ChurchReport 的 `Package01FeeReadsEnabled=false`，主要業務仍走 `WebServiceConnector → ToolUtility → Data8 → D365`。
- 現有 P7 只涵蓋 Package 1 fee-read consumer；現有 P8 只涵蓋 Central Gateway deployment／capacity，均不足以達成 ToolUtility removal。

## 3. 長期產品邊界

| 層級 | 可以知道 | 不得知道或持有 |
|---|---|---|
| ChurchReport／新產品 | ProductClient 方法、產品 DTO、`ConnectionMode`、`ProfileAlias`、必要時的 Gateway endpoint | CRM endpoint、credential、token、`ConnectorKind`、SDK `Entity`、`QueryBase`、`OrganizationRequest`、`IOrganizationService`、connector lease |
| ProductClient | Capability Operation ID、版本化 request／response contract、取消與錯誤模型 | Connector 實作、credential、任意 FetchXML、可變 CRM session |
| Gateway／Embedded ControlPlane | Workload identity、operation allowlist、Profile、Organization admission、generation、timeout／backpressure | 使用者 Session、Cookie、LINE 身分、產品內部 mutable state |
| Connector／Worker | 目標 CE 版本所需 SDK／SOAP／WS-Trust 細節與有界連線資源 | 跨 Profile／Organization／request 共用的可變 session 或 credential |

`Embedded`、`DedicatedGateway`、`CentralGateway` 只決定治理核心位於哪個進程；三者對產品暴露相同 ProductClient／operation contract。即使選 Embedded，新產品仍不得借用 `IOrganizationService`。

## 4. Operation Catalog 分層

### 4.1 平台共同 capability

跨產品語意完全一致且不洩漏任一產品 schema 的基礎能力，例如有界 runtime health 或經核准的 metadata projection。

### 4.2 共用領域 capability

只有在輸入、輸出、授權、資料範圍與錯誤語意都相同時才共用。僅因多個產品都操作 `contact`，不足以證明它們可共用同一 operation。

### 4.3 產品專屬 capability

使用穩定 namespace，例如 `churchreport.*`、`construction.*`、`ticketing.*`、`association.*`。Workload authorization 必須禁止產品僅靠猜中 Operation ID 便呼叫其他產品能力。

既有 `Package01OperationRegistry` 保留為一個 operation module；後續不得把所有產品持續堆入同一 static registry。P7 的 catalog child task 要提供可組合 module 與單一權威查詢介面，Gateway authorizer、ProductClient 驗證與 support matrix 共用該介面。

## 5. 一個 Capability 的完整契約

每個 operation 必須同時具備：

1. 穩定且版本化的 Operation ID。
2. 有界 request contract：欄位 allowlist、型別、長度、集合上限、必填與正規化。
3. 有界 response DTO：不得回傳 SDK object、credential、endpoint、原始例外或 lifecycle metadata。
4. Workload／Profile／Operation authorization。
5. Gateway 與 Embedded 共用的 executor 行為。
6. 各 `ConnectorKind × CeVersion` 的 `supported`／`unsupported`／`evidence-pending` 狀態。
7. timeout、cancellation、idempotency、concurrency 與 retry contract。
8. 單元、契約、隔離、資源生命週期、結果對帳、效能及真機證據。

Registry 登錄只表示「契約存在且可被授權」；只有 executor、connector support 與整合證據都通過後，才可表示「可上線」。

## 6. 資料流與資源所有權

```text
Product Service
  → typed ProductClient method
  → IDynamicsOperationExecutor
  → HTTPS Gateway 或 Embedded adapter
  → RequestGuard / workload authorization
  → ProfileResolver / active generation
  → Organization admission permit
  → ConnectorPool.AcquireAsync
  → ConnectorLease.ExecuteAsync
  → Data8 或 Official Worker → D365
  → typed projection
  → finally dispose lease and release permit
```

排隊期間不得持有 Connector、lease、WCF channel、worker request slot 或 credential-derived mutable state。每次 request 的 permit 與 lease 只屬於該次 operation；成功、失敗、取消與逾時都必須走同一個有界清理路徑。

## 7. ChurchReport 遷移單位

遷移以「可由使用者辨識並可獨立驗證的業務 use case」為單位，不以 ToolUtility 方法或 SDK 呼叫數為單位。一個 capability 可在 Gateway 內組合多次 Retrieve／Create／Update／Execute，避免產品端產生細碎 HTTP 往返與部分成功狀態。

P7 分為：

- **P7.0 Capability inventory**：70 rows → 業務 use case → operation → ProductClient → connector support → consumer 的可追溯矩陣。
- **P7.1 Read slices**：Package01 fee／stor，之後依矩陣處理 MemberInfo、Contact、List、Activity、報表與 metadata。
- **P7.2 Write／Action／Function slices**：明確 idempotency、optimistic concurrency、單一路徑切換與補償；禁止未設計的 dual-write。
- **P7.3 Special resources**：Attachment、分頁／大型結果、background work、metadata cache 與 stream ownership。
- **P7.4 Product cutover**：Controller／Service／WebServiceConnector 改依賴 ProductClient，逐 capability feature gate 與讀取 shadow comparison。
- **P7.5 ToolUtility removal gate**：ChurchReport 移除專案參考、DI／Factory、legacy credential／endpoint、SDK type 與直接 CRM 呼叫。

## 8. ToolUtility 的退役含義

P7.5 完成只代表 ChurchReport 不再依賴 ToolUtility。ToolUtility project 在其他既有 consumer 完成遷移且 rollback observation window 結束前可以留在 repository；最終刪除必須是獨立、可回滾的退役 child task。

Gateway／Connector 不得引用整個 ToolUtility facade。可重用邏輯只能在確認沒有 request／profile mutable state、SDK object retention 或隱含連線 ownership 後，抽到較低層的明確 owner。

## 9. 多產品與 P8

P8 在第二個產品接入時啟動，除了 Central Gateway deployment，還必須包含：

- ProductClient package／namespace ownership。
- Shared 與 product-namespaced operation review gate。
- Workload → allowed profile／operation policy。
- Operation version、deprecation、usage telemetry 與 removal gate。
- 新產品不得引用 ToolUtility／CRM SDK 的 architecture test。
- 多產品公平排程、aggregate Organization capacity 與 noisy-neighbor tests。

新產品新增能力時，先搜尋既有 catalog；只有契約語意或授權邊界不同時才新增 product operation。破壞性 contract 變更建立新版本，舊版本在所有 consumer 遷移且無流量後才移除。

## 10. 錯誤、重試與寫入安全

- Gateway 對外只回傳穩定且去敏的錯誤碼；不得回傳 raw CRM／WCF／SDK exception text。
- Authentication／authorization 必須先於 body 解析與 outbound work，避免未授權 caller 探測契約。
- 只有明確標記 retry-safe 的 read 或具 idempotency key 的 write 才可重試。
- Write operation 必須定義重複送達、部分完成、version conflict 與 timeout-after-commit 的結果語意。
- Cancellation 必須由 ProductClient 傳遞至 Gateway、admission、lease 與 connector；取消後不得留存 request map、timer 或 registration。

## 11. 效能策略

- Operation 採業務級粗粒度，避免把每個 SDK Create／Retrieve／Update 包成一次 HTTP。
- Request／response 都有集合與 payload 上限；大型結果使用 continuation／page contract，不以無界陣列回傳。
- ProductClient 重用由 `IHttpClientFactory` 管理的 handler；不得每次 request 建立新的 `HttpClient` 或可變全域 header。
- 以 p50／p95／p99、allocation、working set、handle、socket、WCF channel、pool size 與 queue depth 比較 legacy／Embedded／Dedicated。
- 效能優化不得繞過 authorization、admission、generation isolation 或 deterministic cleanup。

## 12. Rollout 與回滾

- Read capability 可先 shadow-read，但比較結果不得影響使用者 response；shadow 工作需共享 bounded timeout 並確定清理。
- Write capability 不做無協議 dual-write；每次只允許一條 authoritative write path，切換須有 idempotency／reconciliation 設計。
- Feature gate 以 capability 為單位，不以整站單一開關綁定。
- 任一 slice 發生資料差異、隔離、資源、錯誤語意或效能退步，即只回滾該 capability。
- ToolUtility reference 只在全部 capability 通過 observation window 後移除。

## 13. 驗收條件

1. Capability matrix 對每個正式 ChurchReport D365 use case 都有唯一 owner 與狀態，無未分類 call site。
2. ChurchReport production code 不再引用 ToolUtility 或 CRM SDK type，所有 D365 行為經 ProductClient。
3. 每個 enabled operation 有授權、guard、executor、connector support、錯誤、隔離與 lifecycle tests。
4. CE 8.2／9.1 需要支援的組合皆有真實 Organization evidence；離線綠燈不得取代真機證據。
5. Drain／dispose 後 process、task、timer、registration、permit、lease、connection、channel、handle 與 socket 回到宣告基線。
6. 新產品 architecture test 拒絕 ToolUtility／CRM SDK reference，並只能呼叫授權的 shared／namespaced capabilities。
7. P7／P8 文件、support matrix、runbook 與監控告警與實際程式一致。

## 14. 不在本 Parent Task 直接實作的內容

- 不在本 parent 一次修改全部業務程式碼。
- 不把任意 CRUD／FetchXML／SDK object 暴露成 Gateway API。
- 不因 ChurchReport 完成遷移便立即刪除仍有 consumer 的 ToolUtility project。
- 不把 Central Gateway 設為單產品 ChurchReport 的必要條件。
- 不在缺少真機證據時宣稱 CE 8.2／9.1 operation coverage 完成。
