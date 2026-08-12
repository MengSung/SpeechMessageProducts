# P7.4 Fee Editor Read Boundary Design

## 範圍與選擇

本 child 不替換既有 `FeeManagementController.Fee`／`Present`／`GetFeeData` 的 editable Grid。那些
action 會填入 session-cached `FeeList.FeeDataList`，並可流向 `UpdateFeeData` 與 `SaveBatch`；將 typed
DTO 填回該模型會把 read boundary 與寫入狀態耦合，破壞 P7.4 的 rollback、isolation 與 P7.2 writer
boundary。

採取的最小可交付設計是在同一 controller 新增一個從未被既有 UI 呼叫的 JSON-only route。它沒有 Razor
view、沒有 DevExtreme editable datasource、沒有 legacy branch。其唯一 rollback 是 deployment 將新的
`Package01FeeEditorReadEnabled` 設回 false；此時 endpoint 在任何 data work 前固定拒絕。

## Gate、授權與資料流

```text
HTTP browser lesson locator
  -> Package01FeeReadsEnabled && Package01FeeEditorReadEnabled ?
       false -> fixed denied JSON (zero parse / FeeList / client / I/O)
       true  -> CurrentLogin session snapshot
              -> FeeList.EnsureLoginScope (only clears mismatched cached data; no I/O)
              -> IsLessonListLoadedFor(current account/password)
              -> request-local distinct parsed IDs copied from server LessonList
              -> parse browser locator after snapshot authorization
              -> target exists exactly once in allowlist
              -> FeeEditorReadService
              -> IPackage01FeeReadClient.RetrieveFeeEditorRowsByDiscipleLessonAsync
              -> verify every DTO DiscipleLessonId == target
              -> fresh immutable FeeEditorReadResult
              -> JSON scalar projection
```

`CurrentLogin` 只讀登入時已寫入的 session keys。`EnsureLoginScope` 在 account/password mismatch 時清除
舊 `FeeList` data，且不會自行查 CRM；接著的 `IsLessonListLoadedFor` 仍須為 true，否則 endpoint 拒絕。
因此 request 不會因為 endpoint 被呼叫而建立 lesson snapshot。用 `LessonList` 複製 target allowlist 前，
每個 `DiscipleLessonsId` 必須能 parse 成唯一 `Guid`；null、invalid 或 duplicate 表示 snapshot 不可作為
權威授權資料而 fail closed。此 validation 是有限清單上的固定成本工作，不是 CRM scan。

為讓 false gate 的零工作性質可直接證明，feature gate 在 controller 的第一個 executable branch 判斷。
controller 的 constructor 只保存 `IConfiguration`；它不會建立 ProductClient、process host、HTTP handler、
Data8 pool、timer 或 ToolUtility。服務與 ProductClient 只在已通過 gate、authorization 與 locator check 後組成。

## Service 與 response contract

`FeeEditorReadService` 是 request-local、無狀態的 coordinator。它接受已注入的 typed client 以及
server-bound `ProductDynamicsOptions`，只以固定 workload subject `church-report-service` 呼叫精確
`RetrieveFeeEditorRowsByDiscipleLessonAsync` operation。service 不接收 browser profile、endpoint、connector、
owner、credential 或 name；它不持有 `HttpContext`、Session、cache、static collection 或任何 disposable。

`FeeEditorReadRow` 只複製 `StorLessonRecordDto` 的已允許 scalar：stor lesson、contact 與 disciple lesson
identifier、建立／付款日期、完成狀態、聯絡人顯示資料、課程名稱、開課日、階段及費用。它是不可變型別。
`FeeEditorReadResult` 建構時將 rows 複製到私有 list，再以 read-only wrapper 發佈；不能回轉為 array 或
可寫 `List`。上游 response 先全部 materialize、逐列驗證 lesson target，再一次建構 result；故 null、
mismatch 或 exception 都不會產生 partial response。

`OperationCanceledException` 不由 controller 的 general catch 處理，保留 ASP.NET Core 的取消語意。
其他 service exceptions 只得到固定拒絕／失敗訊息，不含 ID、profile、endpoint、raw upstream response 或
例外本文。由於本 child 沒有建立 connection、lease、timer、stream、background task 或 cache，它不需要
額外 dispose；ProductClient/executor 的 lease、transport 與 cancellation owner 保持既有 DI process host。

## Compatibility、rollback 與不變量

| 項目 | 不變量 |
| --- | --- |
| Existing editor | `Fee`、`Present`、`GetFeeData`、`UpdateFeeData`、`SaveBatch` 不修改。 |
| Legacy data | 新 route 不呼叫 `EnsureLessonListLoaded`、`SetupLessonList`、`SetupPresentFeeList`、`RetrieveEntity` 或 ToolUtility。 |
| Feature disabled | 新 gate 或 base Package01 gate 任一 false 即固定拒絕；不 parse、不 composition、不 I/O。 |
| Rollback | deployment owner 將 editor gate 設回 false；沒有資料寫入、cache 或 fixture 因此無 cleanup。 |
| CE/cutover | 完成本機 path 不開 gate、不發 CE、不中斷 legacy、不構成 Dedicated/traffic/P7.5/P8 evidence。 |

## TDD 與驗證設計

1. 先建立 pure `FeeEditorLessonAccessResolver` 測試：current login mismatch、未載入、empty/invalid/duplicate
   server snapshot、snapshot 外 target 都拒絕，valid distinct snapshot 才授權。此 helper 不看 browser string、
   不查 CRM、不存 state。
2. 再建立 `FeeEditorReadService` failing tests：exact client operation、固定 workload/profile、cancellation
   forwarding、mismatch/null rejection、immutable defensive copy、A/B interleaved rows 不共享 reference。
3. 加入 controller source contract test：dual gate 在 first branch、authorization 在 parse 前、沒有 legacy
   loader/ToolUtility/FeeList data mutation、cancellation catch order 正確。
4. 實作最小 helper/model/service/controller，逐一觀察 RED 後 GREEN。
5. 補 ProductClient exact mapping regression（如果既有 coverage 未鎖定 00066），接著執行 focused suites、
   child boundary full test/build、encoding/CRLF、scope/diff 和最多 45 秒 CCG review。

## 外部 gate

P7.4 enablement 仍是 no-go：legacy ToolUtility 尚未加入與 Gateway 相同的 durable admission authority，且
full legacy ingress coverage 與 deployment-owned non-overlap evidence 尚未實機證明。因此 test 僅用 fake
client；checked-in gates 一律 false。此 child 只消除一個安全可驗證的 consumer contract 缺口，不能啟動
P7.5 ToolUtility removal 或 P8 Central Gateway。
