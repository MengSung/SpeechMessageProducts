# P7.4 ChurchReport ProductClient 逐能力切換設計

## 設計目的

P7.4 是產品 consumer 的遷移層，而不是重新設計 Data8 executor、把 ToolUtility 直接包裝成
HTTP API，或一次替換全部 ChurchReport D365 存取。它只在既有 typed ProductClient capability
已經定義、且對應的資料形狀可以以 DTO/projection 表達時，將單一 consumer capability 改接到
ProductClient。每一批保持 deployment-owned disabled gate，並能由單一 gate 回到既有 legacy path。

## 邊界與資料流

```text
Controller / Service / WebServiceConnector
    -> deployment-owned capability gate (預設 false)
       -> false: 既有 legacy path（未被本批修改）
       -> true : typed ProductClient
                    -> server-derived ProductDynamicsOptions
                    -> existing process-host executor generation
                    -> Gateway/Data8 capability operation
                    -> immutable DTO / request-local projection
    -> response model
```

1. gate、ConnectionMode、ProfileAlias、endpoint、connector 與 workload subject 都是 deployment
   configuration 或 server-owned composition root 的資料；HTTP、LINE、controller 或 service 呼叫端
   不得提供或覆蓋它們。
2. typed path 的 mutable model 保持 request-local。不得將 DTO、response model、client、exception、
   principal 或取消 registration 存入 static/shared cache。
3. executor generation、HTTP handler、Data8 pool、permit 與 connector 的 owner 維持既有 DI process
   host；consumer 只使用 stateless ProductClient facade，絕不自行 Dispose 或另建 ServiceProvider。
4. 取消、timeout 或 fault 必須傳遞到 typed operation；response model 僅在完整成功 projection 後才
   更新，避免 partial model 污染。任何 transport uncertainty 依既有 lease fault/dispose 規則處理。

## capability 分批與依賴

| 批次 | 能力 / rows | 本 task 可做的本機工作 | 不能宣稱或執行的工作 |
| --- | --- | --- | --- |
| A | `fee.dedication.retrieve.by.contact.date.range` / `ORG-CALL-00006` | 完成 fee typed DTO 到畫面 model 的 consumer contract、flag=false short-circuit、flag=true cancellation/fault/isolation tests，並移除該 typed 分支中不必要的 SDK 資料依賴。 | feature enablement、Dedicated traffic、contact identity read、P7.5 removal。 |
| B | `lessons.stor.retrieve.by.contact` / `00061`、`...by.disciplelesson` / `00062` | 將只需要 lesson view 的 callers 改用 `StorLessonProjection`；課程開始時間與階段名稱必須由同一 bounded DTO/wire projection 提供，且 typed path 需全程 async。 | 以 `RetrieveEntity` 回補 entity、`GetAwaiter().GetResult()` 同步等待，或將仍需要 entity 的 caller 標示 migrated。 |
| C | `fee.dedication.retrieve.by.contact` / `00005`、`fees.retrieve.by.dedication.period` / `00064`、`fees.editor.load.by.disciplelesson` / `00066` | 僅在既有 caller 的 DTO/response contract 完整盤點後，建立獨立 sub-batch；必要時保留在 P7.4 task 但不開 gate。 | 以模型猜測取代 CE parity、與 Package02 write 混合。 |
| D | Package02 write/action/function、list、attendance、owner assign、未實作能力 | 記錄為 owning P7.1/P7.2 evidence family prerequisite。 | 透過 P7.4 直接 dispatch、dual-write 或帶入 CE mutation。 |

P7.4 的完成不是「把 flag 設為 true」。每個 row 的 consumer 只有在 typed path 不再需要
ToolUtility/SDK bridge、契約和 lifecycle tests 綠燈、required CE/host evidence 已完成、以及 deployment
capacity gate 已通過後，才可由 deployment owner 另行啟用。

## Batch B 的封閉課程顯示投影

Batch B 的顯示資料流固定為：Data8 `new_stor_lessons` 受限 query → `lesson` inner link 的
`new_class_start_date`、`new_now_stage_name` → `Package01StorLessonRecord` →
`StorLessonRecordDto` → request-local `StorLessonProjection` → controller view model。所有邊界
只允許 Guid、nullable UTC `DateTimeOffset`、nullable bool、decimal 與 bounded string；不得讓
CRM `Entity`、`AliasedValue`、`EntityReference` 或 formatted dictionary 離開 connector。

connector 以精確 aliased UTC DateTime/string reader 驗證 `lesson` 欄位型別並納入同一 page/cumulative
byte budget。`lessons.stor.retrieve.by.disciplelesson` 也必須加入既定 lesson link，否則不能聲稱兩個
operation 的 UI 欄位 parity 相同。ProductClient 只逐欄複製 wire record；ChurchReport 只在 request-local
集合投影，絕不再補送 `RetrieveEntity`。

現有同步 `StorLessonQueryService` public API 不可被 Package01 typed branch 繼續使用，因為它會以
`GetAwaiter().GetResult()` 阻塞 request thread 並使 cancellation 無法流至 client。Batch B 應新增以
`CancellationToken` 為界的 async API；只有兩個 projection-only controller action 可改接此 API 並傳遞
`HttpContext.RequestAborted`。仍需要 `EntityCollection` 的 `DownloadEquipment`、`FeeDownUpLoader`、
`EquipmentStatusCalculator`，以及涉及寫入的 `FindStorLessonId` consumer 均維持 temporary-legacy，不能
被本批 bridge 或統計為 migrated。

## feature gate 與 rollback

每個 capability 使用獨立 deployment-owned gate；既有 `Package01FeeReadsEnabled` 只能涵蓋已明確
盤點的 Package01 read capability，不得被擴張來開啟 write、metadata、image、list 或 profile 操作。
P7.4 如需更細粒度 gate，新增 key 必須預設 false、在 DI/host resolution 之前讀取、且測試證明 false
不建立 process host、ProductClient、HTTP handler、pool、token 或 outbound request。

rollback owner 是 deployment owner：將特定 capability gate 設回 false，等待該 capability in-flight
requests 使用其既有 cancellation/deadline 完成或安全釋放。rollback 不得在 request 途中切換 connector、
profile、CE version 或 protocol，也不得 retry ambiguous operation。read shadow comparison 若採用，必須
有共用 bounded deadline、結果只記錄去識別化固定分類、不得改變 authoritative response，且不保留 task、
timer、buffer、lease 或 registration。

## 實機 enablement gate

P7.4 本機實作與實際開啟 gate 是兩個明確階段。實際 gate enablement 的先決條件是：

1. legacy 和 Gateway 要共用 durable distributed admission/host-slot authority，能量測並保證同一
   Organization aggregate capacity；或
2. runtime/deployment owner 有可演練的 drain-first non-overlap runbook，確保 legacy 已完全停止接收
   該 Organization 流量後才可能開啟 Gateway path。

缺少上述任一證據時，結果是「P7.4 enablement no-go」，而非「本機程式失敗」。不得啟用 flag、
不得送 CE request，並需把去識別化 blocker 寫入 task `check.jsonl` 與 check 記錄。

## P7.5 / P8 邊界

P7.5 需要所有 ChurchReport production temporary-legacy row 清除、zero-reference scan、完整 test/
parity/soak/drain/rollback evidence，故本 task 的單一 disabled read path 不足以啟動 P7.5。P8 只可在
P7.5 commit/archive 產生 immutable handoff 後建立；本 task 不得建立 P8 task、雲端資源或流量。
