# ChurchReport 完全 Gateway 化與雲端 Central Gateway 設計

> 日期：2026-08-05；2026-08-06 依使用者核准路線重校
> 狀態：方向已核准，規劃文件重校中
> 核准方向：ChurchReport 最終完全移除產品端 ToolUtility／`IOrganizationService` D365 存取；新產品一律採 capability operation 模型。

## 1. 設計目標

建立一個所有 SpeechMessage 產品都能使用的 Dynamics 存取邊界：產品只呼叫強型別 ProductClient；Gateway／Embedded 執行層負責 operation authorization、Profile 解析、Organization admission、Connector lease、D365 呼叫、錯誤清理與結果投影。

本設計保留 P4 Embedded、已封存的 P5 Dedicated Gateway 與 P6 Official Worker Router 擴充點作為平台基礎；P6 Official Worker live compatibility 為 `evidence-pending` 的未來支線。P7 負責以 Data8 完成 ChurchReport 全量 capability 遷移，並保留 `Embedded + Data8` 與 `DedicatedGateway + Data8`；P8 才把已完成本機驗收的單一 ChurchReport 以 `CentralGateway + Data8` 部署到雲端。第二、第三產品 onboarding 是後續獨立範圍，不阻塞 P6～P8。

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

## 9. P8：單一 ChurchReport 雲端 Central Gateway

P8 在 P7.5 完成且本機 evidence 封存後啟動，不等待其他產品。P8 只把已通過 Lenovo Legion 驗收的相同 ProductClient／operation contract 部署至雲端，不在部署階段重新發明 capability。固定拆分如下：

- **P8.0 Cloud deployment readiness**：確認雲端主機、網路、DNS、憑證、service identity、secret provider、CE reachability、備份、部署包與 rollback package 均可用；任何缺口都先 No-Go。
- **P8.1 Host／identity／TLS hardening**：Gateway 僅接受已核准 ChurchReport workload identity，TLS 憑證、私鑰與 CRM credential 由部署環境擁有；產品 request、設定檔與 artifact 不得保存 secret。
- **P8.2 CentralGateway＋Data8 deployment**：以服務管理員建立可重啟、可 drain、可確定停止的 Gateway／Data8 runtime；監測 process、connection、channel、handle、queue、permit 與 generation，不允許跨 profile mutable state。Official Worker 只有在未來另行選用並取得真機證據時才納入雲端 composition。
- **P8.3 ChurchReport cutover**：先 health／ready 與受控 operation smoke，再以明確變更視窗將 ChurchReport endpoint 指向 Central Gateway；不得同時改 operation contract、Profile、ConnectorKind 或 CE version。
- **P8.4 Live validation／monitoring／rollback／closure**：核對功能結果、p50／p95／p99、錯誤率、資源基線、告警與 rollback drill；觀測窗通過後才結案。

未來第二、第三產品仍須遵守 shared／product-namespaced catalog、workload allowlist、版本治理、aggregate capacity 與 noisy-neighbor 規則，但另立 task 規劃與驗收，不是 P8 的完成條件。

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

### 12.1 遷移重疊期間的 aggregate capacity gate

P7.4 的逐 capability cutover 期間，legacy ToolUtility 與 Gateway 可能在不同 process 同時存取同一個
Organization。每個 process 自己的 In-Memory admission／host-slot coordinator 不能證明 aggregate capacity
受到限制；`DedicatedGateway` 未註冊 durable SQL host-slot coordinator 的現況尤其不能被當作跨 process
容量保證。

因此第一個 P7.4 consumer feature gate 開啟前，child task 必須提出並由使用者核准一個明確方案：

1. 讓所有同時 active 的 legacy/Gateway host 共用 durable distributed admission／host-slot authority，並以
   壓力測試證明總併發不超過 Organization budget；或
2. 在 rollout runbook 中強制兩條路徑不重疊（drain 一條後才啟用另一條），並以 deployment/runtime evidence
   證明沒有併發流量。

不可把「暫時一律改用 Embedded」預設為答案；它是部署選擇，仍需以可驗證的 capacity、isolation、rollback
條件另行核准。無論選項為何，request-time connector/profile fallback 與未設計 dual-write 仍一律禁止。

## 13. 驗收條件

1. Capability matrix 對每個正式 ChurchReport D365 use case 都有唯一 owner 與狀態，無未分類 call site。
2. ChurchReport production code 不再引用 ToolUtility 或 CRM SDK type，所有 D365 行為經 ProductClient。
3. 每個 enabled operation 有授權、guard、executor、connector support、錯誤、隔離與 lifecycle tests。
4. CE 8.2／9.1 需要支援的組合皆有真實 Organization evidence；離線綠燈不得取代真機證據。
5. Drain／dispose 後 process、task、timer、registration、permit、lease、connection、channel、handle 與 socket 回到宣告基線。
6. 新產品 architecture test 拒絕 ToolUtility／CRM SDK reference，並只能呼叫授權的 shared／namespaced capabilities。
7. P8 的 ChurchReport 雲端 Central Gateway 有可重現部署包、service identity／TLS／secret 邊界、監控告警、rollback drill 與受控 live evidence。
8. 每個 P7.4 enabled capability 都有已核准的 aggregate-capacity overlap 方案與相應壓力／drain evidence。
9. P7.5 完成後才可啟動 P8；P8 不回頭改變 P7 capability contract，也不以未來產品作為結案前置。

## 14. 不在本 Parent Task 直接實作的內容

- 不在本 parent 一次修改全部業務程式碼。
- 不把任意 CRUD／FetchXML／SDK object 暴露成 Gateway API。
- 不因 ChurchReport 完成遷移便立即刪除仍有 consumer 的 ToolUtility project。
- 不在 P6／P7 本機階段提前部署或切換雲端 Central Gateway；該工作由 P8 獨立承擔。
- 不把第二、第三產品 onboarding 併入 ChurchReport 的 P8 結案條件。
- 不在缺少真機證據時宣稱 CE 8.2／9.1 operation coverage 完成。

## 2026-08-12 重新基準化設計決策

P7 之後的 child 不得再以 parent 早期的 P6/P7.0 next action 為執行依據。`08-12-p7-remaining-work-rebaseline` 以 P7.0 70-row source matrix 為 immutable baseline，將現況分解為 registry、Data8 executor、typed ProductClient、ChurchReport consumer、CE 8.2/9.1、Embedded/Dedicated、rollout/rollback、temporary legacy、P7.3 資源需求與 P7.5 removal blocker 等獨立狀態。

這個 matrix 是 capability child 的排程與 release gate，不是 CE 寫入授權、profile routing authority 或部署設定。任何 local-only reducer、disabled gate、靜態 registry、unit test 或歷史 CE no-go 都不能被升格為 product cutover 或 live evidence。P7.4 必須逐 capability、disabled-by-default 並具 deterministic rollback；P7.5 只能在所有 production row 遷移及 zero-reference scan 通過後移除 ChurchReport dependency；P8 僅可由 P7.5 的 immutable handoff 建立。
