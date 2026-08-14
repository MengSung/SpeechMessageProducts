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

### 2026-08-13 P7.4 admission boundary 完成後的設計狀態

P7.4 已有一個 task-owned local control-plane：它只停止並計量已註冊的 Package01 legacy fee ingress，
不保存 request/session/CRM Entity/profile/endpoint/credential，也以 exactly-once lease release 與 bounded
drain fail closed。這降低 host shutdown 與新受控 legacy work 的重疊風險，但不提供 Organization-level
capacity proof。同步 legacy SDK call 不能中止、未註冊 ingress 不可觀察、以及 per-host state 不可持久化
協調，仍是 feature enablement 的硬性 no-go。

所有 launch/appsettings gate 因此保持 false。後續 P7.4 設計可獨立遷移具 typed DTO 與 request-local
contract 的 consumer，但每一 capability 仍需 own CE/parity/authorization/isolation/performance/cleanup/
rollback evidence；P7.5/P8 的 predecessor 不因本機 admission child 完成而改變。

### 2026-08-13 P7.2 recurring dedication payment-return 寫入邊界

`ORG-CALL-00064` 已有 typed fee-period read，但它是 recurring payment-return 金融寫入的 dedup
precondition，不是可獨立切換的 read consumer。新 child 將 legacy chain 分解為 dedup read、contact card
update、fee create、owner assignment、booking completion 與 notification 六個不可混合的 family。local-only
plan 不接受 Entity、Owner、raw card/token、profile、endpoint 或 credential，且不保存 request state。

未來每個 family 都要有 server-owned authorization、固定 request DTO、single writer、new nonce／ledger／
task-owned fresh fixture、preflight、single dispatch、exact read-back/reconcile 與 reverse-known-key cleanup。
未具備這些證據前，Data8 executor 與 product consumer 都是 false，歷史 Slice C cycle 不能成為新 family
authority。這可讓本機設計與 P7.4 獨立 readonly work 繼續，但不會將 read-new/write-legacy 混合成未受治理雙路徑。

### 2026-08-13 P7.5 前置 evidence boundary

P7.5 需要兩個獨立的真實 evidence：immutable gap matrix 不再有 temporary-legacy／consumer／
special-resource／mixed／legacy-sdk blocker，且 ChurchReport production source/project/settings metadata
通過 conservative zero-reference scanner。兩者不能互相推導，因此 P7.5 prerequisite child 以固定 schema
分開報告。scanner 只讀 allowlisted production `.cs`、唯一 `.csproj`、settings key name 和 matrix；
其輸出只含 bounded category/count/family。comment/string stripping 不完整、encoding/path 不安全、raw string
不支援或 report 被竄改時一律 fail closed。它不保有 runtime client、profile、credential、request/session
或 scanner cache，不能變成產品行為或 deploy state。

### 2026-08-13 P7.5 prerequisite report 設計結論

前置 evidence child 已證明 scanner 對現行 production source 可完整且保守地完成 lexical scan；C# line-start
preprocessor directive 與 JSONC comments 是合法 metadata，不應造成 false invalid，但 raw string、未封閉
literal/comment、invalid UTF-8/path/reparse、invalid JSONC escape 仍須 fail closed。settings parser 只 decode
object key，所有 value 僅做嚴格語法 skip，不 materialize、log、hash 或輸出。

這份 report 的 `no-go` 由 matrix、production source、project dependencies、settings keys 四類獨立 evidence
共同決定；任一類清除都不抵消其他缺口。它提供可重複排程資料，不替代 server authorization、Data8 executor、
ProductClient、consumer migration、CE/host parity、capacity admission 或 deployment evidence。

### 2026-08-13 fixed list catalog capability 分片

`ORG-CALL-00014` 的零參數、固定 app-named list catalog 適合作為 P7.1 的資料層 capability，而非 P7.4
consumer cutover。Data8 connector 應以 server-owned `QueryExpression` 固定 entity、ColumnSet、status/purpose/
app-named filters、排序與 finite paging；CRM Entity 只存在於當前 connector lease stack，完成後只回傳封閉
wire record。ProductClient 只能驗證 operation/discriminator 後複製為 request-local DTO；不得保留 list、
profile、workload、Entity、page、paging cookie、cache 或 transport state。

`ORG-CALL-00065` 已保持獨立並完成 local-only contract：它的 fixed template 額外排除測試名稱並投影領袖 lookup，且現行 consumer
使用沒有完整 isolation boundary 的 shared `EntityCollection` cache。兩者只能共用由 registry 施加的
bounded query family 設計，不可共用 operation ID、response branch、cache key 或 consumer rollout。這可避免
00014 的 completed local evidence 被誤當作 00065 的實作、consumer、CE 或切流證據。00065 的 Data8 lease stack
只能將 lookup 映射為 nullable GUID；`EntityReference.Name`、Entity、formatted values、cookie、profile 與
transport state 都不得跨出 connector。ProductClient 必須為每個 request 防禦性複製並發布不可變 DTO collection，
所以 A/B interleaving 不會經由 list backing storage、cache、retry 或 background state 泄漏。這一 design 仍是
local-only，沒有授權 CE mutation、feature enablement、traffic、P7.5 或 P8。
將系統目錄資料錯誤宣稱為 universally-safe shared cache，並保留每個 consumer 的 authorization、locale、
performance、rollback 與 CE evidence 責任。

### 2026-08-14 evidence hierarchy 與下一 family 選擇

P7/P8 parent 一律按下列 hierarchy 判讀：registry／Data8 executor／typed ProductClient 只代表某 operation
contract 已存在；disabled local boundary 只代表 gate=false 下的本機 DTO、authorization、A/B isolation、
cancellation 與 lifecycle contract；matrix 的 consumer 欄才描述 legacy production consumer 是否實際被 own
child 接管；CE evidence 僅限該 operation family、CE version 與證據 cycle；Embedded、Dedicated、Central host
與 traffic cutover 分別需要額外的 deployment evidence。任何較低層證據不得推導成較高層完成。

下一 child 的 source audit 必須先拒絕將 typed DTO rehydrate 成 `Entity`／`EntityCollection`，或寫入
Session mutable graph／legacy write path。browser/request locator 只能在 server-owned authorization 後使用，
且不可選 profile、connector、credential、owner 或 organization。disabled gate 必須在 I/O、client composition
與 session mutation 前 short-circuit，並有 deployment rollback owner。write/action/function family 則必須另有
idempotency、exact read-back/reconcile、fresh fixture、deterministic cleanup、timeout/no-replay 與 rollback
design；不得藉 P7.4 read consumer path 繞過 P7.2 governance。

這些規則也限制 P7.2 的新 payment control plane：`CeDispatchAllowed=false` 與
`ProductConsumerAllowed=false` 的 local state 可供未來 independent family 重用其治理概念，但不能改變
歷史 Slice C non-replay，也不能當成 CE、consumer、P7.5 或 P8 authorization。

### 2026-08-14 MemberInfo smallgroup tree authorization boundary

00031／00032 不可由既有 `MemberInfoController` 的 Church／Shepherd tree path 直接抽取成 Gateway
capability。Church branch 的 fixed descriptor query 雖可在未來形成受控 template，但 Shepherd branch 的 list
assignment 來自 Session／`InMemoryContext`，並可能用保存 credential 啟動 shared `ListManager` loader；兩者
不具同一個在 I/O 前建立的 immutable request-local scope。

### 2026-08-14 MemberInfo relation-goal boundary

00033 使用同一個 MemberInfo access chain，故即使它目前的 `connection` projection 是固定欄位，也不能在
authorization boundary 未成立時由既有 `allowedIds` 建立 Gateway capability。它另有 response-boundary 缺口：
共用 `RetrieveAllEntities` 在 `MoreRecords` 時沒有 capability-specific page/row/text/byte budget，且 relation
helper 將所有查詢例外吞為空 display text。未來須讓 request-local immutable scope 在選擇 profile、取得 lease 或
CRM I/O 前建立，並以封閉 error union 區分 empty、fault 與 partial；不能用 Church-only 或 legacy fallback 修補。

可接受的後續資料流必須先由另一個 child 建立：authenticated principal → server-derived MemberInfo scope →
server-selected Church 或 Shepherd capability → request-local bounded list allowlist → fixed descriptor/membership
template → Data8 projection → immutable DTO。scope 不得讀寫 Session、InMemoryContext、legacy ListManager、
credential、shared authorization cache 或 browser locator；invalid、duplicate、stale 或 ambiguous scope 必須 fail
closed。只有該前置 boundary 通過後，descriptor 與 membership 才可各自有 Data8/ProductClient、A/B isolation、
cancellation/lease cleanup、disabled gate、CE 與 rollback evidence。

### 2026-08-14 dedication capability identity boundary

`ORG-CALL-00059`／`00041` 的關係是「product service 與其 legacy transport helper」，不是兩個可各自
遷移的 Gateway capability。當同一 fixed active-booking filter、同一 contact locator 與同一 consumer scalar
contract 皆可由 typed operation 覆蓋時，只保留 `payments.dedication.retrieve.by.contact` 作為唯一新資料層
operation。這避免兩個看似相近、實則可能在 filter、排序、paging、projection 或 rollback 上漂移的 duplicate
registry。任何 legacy consumer 的真實 cutover 仍必須獨立證明 authorization、gate、capacity、CE、host、traffic
與 rollback。

`ORG-CALL-00060` 不能從既有 payment form／manager／`Entity` path 直接抽成 typed client。安全資料流必須先是
authenticated principal → immutable server-derived policy scope → 已授權的 target contact locator → fixed bounded
DTO contact projection → request-local form projection。前置 scope 不得讀寫或引用 Session、InMemoryContext、
ListManager、cached Entity、form、profile、connector、credential 或 browser／Line locator。若 role 的 target scope
不能由 server policy 先決定，必須 fail closed，而非將直接 CRM `RetrieveEntity` 或既有 fee-read branch 當替代。

## 2026-08-14 current-state rebaseline evidence hierarchy

本次 70-row current matrix 的 source hash 與封存 matrix 不同；差異只證明 ORG-CALL-00026 與
ORG-CALL-00057 新增的 registry／Data8／ProductClient local implementation，並未改變 consumer、CE、host、
traffic、temporary-legacy 或 P7.5 state。封存 P7.5 report 固定讀取舊 hash matrix，因此只保留其歷史
source/project/settings no-go evidence；它不能用來升格兩個新 local row，也不能宣稱 current P7.5 scan 完成。

下一個 direct P7.4 local-only consumer 候選經 source audit 為零。看似 read 的 ORG-CALL-00063 在 browser POST
值進入 `InMemoryContext` 後使用 stored FetchXML／EntityCollection，並相鄰出席、週報與通知寫入；它沒有
weekly-specific gate=false zero-work，因此不符合 immutable request-local authorization、DTO-only、無 write-adjacency
的資格。此 no-go 只停止 direct consumer cutover；後續必須先建立可獨立驗收的 server-derived authorization
boundary，才能重新評估依賴該 boundary 的 capability。

## 2026-08-14 runtime.health.whoami 本機邊界

`runtime.health.whoami` 不是 ChurchReport browser／Session consumer cutover，而是 deployment runtime
health capability。其 request shape 固定為零 operation parameter、零 idempotency key；ProductClient 僅可把
已驗證的 deployment-owned profile alias 與 workload subject scalar 交給既有 executor。client 不持有
connector、lease、`HttpContext`、principal、cookie、credential、CRM SDK 型別、cache、timer、subscription 或
background task；executor 維持 transport/lease 的唯一 owner 與 deterministic cleanup 責任。

response 必須同時符合固定 operation ID、CE 9.1、WhoAmI response branch 與三個非空 GUID。任何 operation、
version、branch 或 identity scalar 不一致均回傳 sanitized fail-closed result，不產生 fallback、重試或跨 profile
狀態。A/B interleaving 與 cancellation tests 必須證明 request state 不被 retained。這個 child 僅建立本機
data-plane contract，並不改變 P7.4 consumer、P7.5 removal 或 P8 deployment gate。
