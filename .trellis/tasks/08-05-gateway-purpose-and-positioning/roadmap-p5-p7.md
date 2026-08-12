# P5～P8 Dynamics Gateway 執行路線

> 2026-08-06 重校。為避免既有引用失效，保留 `roadmap-p5-p7.md` 檔名；本文件內容已擴充至 P8。
> 本文件採納舊規劃中「vertical slice、deterministic coverage、aggregate capacity、可獨立回滾」的優點，修正 P5 已結案、P7 編號漂移與 P8 觸發條件。

## 1. 已鎖定的最終成果

1. P5 Dedicated Gateway 已正式封存，不再重開。
2. P6 在 Lenovo Legion 完成 Official Worker Router 擴充點；Official Worker CE 8.2／9.1 live compatibility 保留為未來 `evidence-pending` 支線。
3. P7.0～P7.5 在 Lenovo Legion 完成 ChurchReport 全部 D365 capability 遷移，P7.5 移除 ChurchReport 對 ToolUtility／CRM SDK 的 production dependency。
4. P8.0～P8.4 將已在本機驗收的單一 ChurchReport 部署到雲端 Central Gateway，完成身分、TLS、監控、回滾與 live validation。
5. 第二、第三產品 onboarding 日後另立獨立 task，不阻塞目前 P6～P8。

## 2. 唯一路線

```mermaid
flowchart LR
    P5["P5 Dedicated Gateway：已封存"] --> P6["P6 Official Worker Router extension：live evidence pending"]
    P6 --> P70["P7.0 Inventory + coverage validator"]
    P70 --> P71["P7.1 Read capabilities"]
    P71 --> P72["P7.2 Write / Action / Function"]
    P72 --> P73["P7.3 Special resources"]
    P73 --> P74["P7.4 Product cutover"]
    P74 --> P75["P7.5 ToolUtility removal"]
    P75 --> P80["P8.0 Cloud readiness"]
    P80 --> P81["P8.1 Identity + TLS"]
    P81 --> P82["P8.2 Central Gateway deployment"]
    P82 --> P83["P8.3 ChurchReport cutover"]
    P83 --> P84["P8.4 Live validation + closure"]
```

前置 gate 仍必須依序通過，但使用者可以用一個整合 `/goal` 預先授權 P6 與 P7 的連續執行。這代表不必每一階段重新下提示詞，不代表完全無人值守：本輪 P6 只需完成既有 P6.1 離線 closure、文件／spec／quality 與封存，Official Worker live compatibility 保留為 `evidence-pending`，不重跑已完成的 profile／Credential Manager handoff。P7.2 先記錄環境級可行性，再於 P7.0 matrix 完成後為每個 required operation family 通過 fixture／cleanup activation gate。它也不代表可略過 Trellis task、測試、quality check、spec update、commit 或 archive。

P6 與 P7.0 位於不同 Trellis parent。執行者必須直接使用 `.trellis/tasks/08-05-official-worker-router-ce-integration` 與 `.trellis/tasks/08-05-gateway-capability-inventory` 兩個明確路徑切換，不得靠任一 parent 的 children traversal 尋找整條路線。

## 3. 目前真實狀態

| 階段 | 狀態 | 已有證據 | 下一個 gate |
|---|---|---|---|
| P5 | 已封存 | Dedicated Gateway 離線 host／lifecycle／quality gate | 無；不得重開 |
| P6.1 | 已通過 | Router／Pool／Lease 與離線 lifecycle／quality evidence | 保留現有結果，不重做 |
| P6.2 | `evidence-pending` | readiness 已 `go`，但兩個 Worker 在 READY 前結束，未執行 CE operation | 保留到未來獨立 Official Worker deployment task |
| P7.0 | `planning` | PRD／design／implement 與 preliminary inventory 已存在 | 等 P6 正式結案後 activation |
| P7.1～P7.5 | 尚未建立／啟動 | 由 P7.0 matrix 決定精確 child 邊界 | P7.0 validator 全綠 |
| P8.0～P8.4 | 尚未建立／啟動 | 本文件只有路線定義 | P7.5 結案與獨立 P8 授權 |

## 4. P6：Lenovo Legion Official Worker Router 擴充點

### P6.2A Historical deployment readiness

- 使用預計執行 Gateway／Worker 的 Windows identity。
- 由 deployment owner 建立 CE 8.2 與 CE 9.1 profile input；endpoint、Organization、ConnectorKind 與 credential reference 必須互相一致。
- Credential 只存在 Windows Credential Manager／核准 secret provider，不寫入 source、命令列、JSON artifact、log 或 Trellis 文件。
- readiness probe 必須由 `profile-input-required` 收斂為 `go`；任何 identity、ACL、manifest、runtime、package-lock 或 secret 解析缺口都維持 No-Go。

### P6.2B Historical read-only live matrix

- 以與正式系統隔離的 CE 9.1 `sunnyvalechback` 作未來 Data8／Official Worker allowlisted health／connection control；CE 8.2 使用另行核准的 read-only profile。Official Worker identity/connection evidence 不再是 P6 或 Data8 P7 的必要矩陣；若未來選用 Official Worker，另立 task 再恢復。
- CE 8.2 與 CE 9.1 各自保存 sanitized result、ConnectorKind、operation ID、p50／p95／p99 與 drain 後 process／handle／pipe／permit baseline。
- 任何不相容、錯誤 routing、credential/session/profile leakage 或資源無法回到基線都是 release blocker；不得自動改用另一 Connector、Profile、CE version 或 transport。
- P6.1 的離線 quality、文件與 spec 判斷全綠後依 Trellis Phase 3 執行 task-owned commit 與 archive，才解除 P7.0 前置條件；P6.2 保留為 `evidence-pending`。

P6 是本機 connector／CE gate；它不修改 ChurchReport consumer、feature flag 或 ToolUtility dependency，也不部署雲端。
即使 `sunnyvalechback` 可安全建立 test member，P6 仍不執行業務 write/action/function；P6 的價值是證明版本隔離的 Official Worker、Router／Pool／Lease、identity 與 deterministic cleanup，業務寫入屬 P7.2。

## 5. P7.0：Capability inventory 與 deterministic coverage

- 以 Phase 0 的 70 個 normalized call-site rows 為權威來源，不把 70 rows 當成 70 operations。
- 依業務 use case 收斂為 platform、真正共用 domain 或 `churchreport.*` typed capabilities。
- 分開記錄 Registry declared、Executor implemented、Consumer enabled、Real CE evidence；禁止單一「完成」欄位。
- 建立完全離線、固定排序、固定 exit code 的 validator，阻擋未分類 row、缺 owner／DTO、未知 connector／CE、generic CRUD／FetchXML、無 rollback owner 與 lifecycle 邊界。
- 建立 ToolUtility／CRM SDK reference baseline；P7.0 只報告現有數量，P7.5 才要求 production zero reference。

P7.0 結案輸出是 P7.1～P7.3 的精確 capability child 清單、ownership、依賴與 evidence matrix。它不夾帶 operation implementation。

## 6. P7.1：Read capabilities

先以 Package01 fee／stor 完成第一個完整 vertical slice，再依 matrix 處理其餘 read family。每個 slice 固定包含：

1. fail-first contract／authorization／support-matrix tests；
2. bounded request／response DTO 與 stable Operation ID；
3. Registry、Data8／Official Worker executor support 與 ProductClient；
4. cancellation、timeout、paging、error sanitization、permit／lease cleanup；
5. legacy parity 或 bounded shadow comparison；
6. capability feature gate、rollout owner、rollback owner；
7. CE 8.2／9.1 evidence 與效能／資源基線。

Read shadow failure 不得改變使用者 response；shadow task 必須共用 bounded deadline 並確定清理。

## 7. P7.2：Write／Action／Function capabilities

- 依 transaction、authorization、idempotency 與 rollback owner 拆 child，不依 CRM entity 或 ToolUtility 方法拆分。
- 每次只有一條 authoritative writer；禁止沒有協議的 dual-write。
- 明確定義 duplicate delivery、optimistic concurrency、partial completion、timeout-after-commit 與 reconciliation。
- Live write evidence 只可在明確核准的非正式環境／測試資料範圍執行；若缺少安全的 fixture 或 cleanup path，該 slice 必須停在 No-Go，不能用 mock 冒充真機完成。
- CE 9.1 使用已確認與正式系統隔離的 `sunnyvalechback` 與唯一 test member；這只代表環境級可行性，不是任意寫入授權。每個 required operation family 仍需在 activation 前定義 allowed mutations、fixture owner、可辨識且可清理的 test-owned records、cleanup/reconciliation 與 ambiguous-timeout policy。CE 8.2 只有 capability matrix 標為 required 時才需要 write evidence，否則明確標示 unsupported 並在 dispatch 前 fail closed。

## 8. P7.3：Special-resource capabilities

- Attachment／stream：大小、類型、buffer、timeout 與 dispose 均有硬上限。
- Paging／large result：continuation token、page size、retention 與 cancellation 有界，不保留跨 user/session state。
- Background／scheduler：queue、retry、idempotency、shutdown drain、subscription／timer／task owner 可驗證。
- Metadata cache：Profile／Organization 隔離、容量／TTL 上限與 eviction/dispose 路徑明確。

任何 stream、buffer、timer、registration、task、process、handle 或 cache retained reference 無法回到宣告基線，該 child 不得結案。

## 9. P7.4：ChurchReport ProductClient cutover

- Controller、Service 與 WebServiceConnector 逐 capability 切至 ProductClient，不做全站一次切換。
- 第一個 feature gate 開啟前，必須選定並證明其中一種 aggregate-capacity 方案：共用 durable admission authority，或先 drain legacy 再啟用 Gateway 的 non-overlap runbook。
- 任一資料差異、錯誤語意、授權、隔離、效能或資源退步只回滾該 capability。
- 不在 request-time 改 ConnectorKind、Profile、CE version 或 transport；回滾由 deployment-owned gate 決定。

## 10. P7.5：ChurchReport ToolUtility removal

只有下列條件全部通過才可移除 dependency：

- capability matrix 無未分類或 production temporary-legacy row；
- 所有 enabled consumer 有對應 CE、parity、錯誤、效能與 lifecycle evidence；
- ChurchReport production zero-reference scan 不再找到 ToolUtility、CRM SDK、`IOrganizationService`、`Entity`、`QueryBase` 或 `OrganizationRequest`；
- project reference、DI／Factory、legacy endpoint／credential settings 與直接呼叫已移除；
- Release build、完整 Dynamics／ChurchReport tests、soak、drain 與 rollback drill 全綠；
- observation window 通過，且 rollback package 可重現。

P7.5 只代表 ChurchReport 不再依賴 ToolUtility；若 repository 仍有其他 consumer，ToolUtility project 留待獨立退役 task。

## 11. P8：單一 ChurchReport 雲端 Central Gateway

### P8.0 Cloud deployment readiness

確認 cloud host、網路、DNS、TLS certificate、service identity、secret provider、CE reachability、備份、部署包、容量基線與 rollback package。缺一即 No-Go。

### P8.1 Host／service identity／TLS

建立最小權限 ChurchReport workload identity、Gateway／Worker service identity、TLS trust 與 secret ACL。驗證未授權 workload 在 body parsing、Profile resolution 與 outbound work 前被拒絕。

### P8.2 Central Gateway＋Data8 deployment baseline

以 `CentralGateway + Data8` 的可重現部署包安裝服務，驗證 startup、health、ready、restart、drain、forced termination、log／metric sanitization 與 connection／channel／permit／queue／generation baseline。若未來選擇 Official Worker，另立 task 取得其獨立 evidence；不得把未驗證 Worker 混入本次 Central Gateway baseline。

### P8.3 ChurchReport cutover

在變更視窗先做受控 smoke，再只變更 ChurchReport 的 Central Gateway endpoint／deployment-owned routing。不得同時改 capability contract、Profile、ConnectorKind 或 CE version。

### P8.4 Live validation、monitoring、rollback、closure

取得功能、p50／p95／p99、錯誤率、queue、permit、lease、connection、worker recycle、working set、handle 與 alert evidence；實際演練 rollback。觀測窗通過後才 commit/archive P8。

## 12. 單一 P6／P7 Goal 的自動續跑規則

整合 Goal 從既有 P6.1 closure checkpoint 開始：確認 scoped Git/text baseline、P6.1 離線 quality 與 Official Worker `evidence-pending` 記錄；不重跑 P6.2。同步只記錄 P7.2 的 CE 9.1 環境級 test-member 可行性。P7.0 matrix 產生後，再在 P7.2 activation gate 確認每個 required family 的資料 owner、允許操作與 cleanup/reconciliation。

P6 closure gate 全綠後，整合 Goal 可一次授權：

- 從目前 P6.1 closure checkpoint 續作，不重做已綠的 P6.1，也不重跑 P6.2；
- P6 gate 全綠後執行 spec update、task-owned commit、archive；
- 啟動既有 P7.0，完成後依 matrix 建立並啟動 P7.1～P7.5 children；
- 每個 child 都先規劃、再實作、再 Trellis check，通過才 commit/archive 並自動進入下一個；
- 允許 Lenovo 本機的 feature-gated ChurchReport cutover 與明確核准測試環境的 CE evidence；
- 禁止啟動 P8、部署雲端、push、建立 PR 或操作第二／第三產品。

同一 gate 最多三次自我修復 cycle；同一 root cause 連續兩次即停止。Credential/profile/authorization 缺口不得盲目重試，直接轉 operator handoff。

只有下列情況可以暫停並要求使用者：缺少無法由 repository 推導的 profile／secret／非正式 CE fixture、需要不可逆資料操作、遇到產品語意歧義、或同一阻塞條件依 Goal 規則已重試仍無法安全前進。一般測試失敗、編譯錯誤與可修復的文件／程式缺口由代理自行診斷、修正、重跑與續作。

## 2026-08-12 路線圖狀態校正

| 階段 | 現行狀態 | 可否重做／啟動 |
| --- | --- | --- |
| P3–P6 | 已完成；P6 Official Worker live compatibility=`evidence-pending` | 不重做；不阻擋 Data8-first P7。 |
| P7.0 | 已封存 70-row inventory／validator | 唯讀 baseline。 |
| P7.1 | 六個 Package01 typed Data8 read 與 CE 9.1 read-only evidence 已完成；consumer disabled | 唯讀 evidence；未代表所有 read 完成。 |
| P7.2 | 本機 RC 已封存；Slice C CE no-go 已 cleanup；D–H local-only | 舊 cycle 永不重試；不得當作 CE/cutover evidence。 |
| P7 remaining rebaseline | 已完成品質閘門，待 scope-only commit/archive | authoritative 70-row gap matrix 是後續唯一排程基準。 |
| P7.3–P7.5 | P7.3 應於 rebaseline archive 後建立；P7.4/P7.5 尚未建立 | 依 matrix 與前置 child gate 依序建立。 |
| P8.0–P8.4 | 尚未建立 | 僅在 P7.5 immutable handoff 後建立。 |

後續品質策略不變：一般變更執行 targeted tests；每一 child 邊界與 P7/P8 最終交付執行完整 solution tests、Release build、encoding／CRLF、scope、isolation、lifecycle 與 rollback gate。Gemini／Claude 每次等待上限 45 秒，逾時或 quota/session 限制即記錄降級並改採本機驗證，不得反覆等待。

## 13. 目前下一步

先完成 `08-12-p7-remaining-work-rebaseline` 與
`08-12-process-boundary-cross-assembly-isolation` 的 scope-only commit/archive。之後建立
`churchreport-special-resource-migrations`，以 matrix 的 5 個 special-resource row 為範圍，先完成
attachment/image/stream、paging/result、metadata cache 的 bounded owner、lifetime、cancellation、drain、
dispose 與 A/B isolation evidence。P7.4 僅能在個別 capability 的 CE/parity/rollback gate 都通過後建立；
P7.5 與 P8 均不得提前啟動。
