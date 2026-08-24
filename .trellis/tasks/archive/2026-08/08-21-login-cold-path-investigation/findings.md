# 登入冷路徑調查 findings

## 調查範圍與證據

本文件只做唯讀調查，未修改程式碼、組態或追蹤檔，未啟動應用程式，未收集新 trace，未執行 reviewer 或效能改動。

主要契約：

- `.trellis/tasks/08-21-login-cold-path-investigation/prd.md`
- `D:\除錯追蹤\Trace.log.sessionverbose-false-auth-20260821-090207`
- `D:\除錯追蹤\dataverse-trace.jsonl.sessionverbose-false-auth-20260821-090207`

所有判定均標示為「已由證據確認」、「推論」或「無法判定」。

## Q1：request.begin 到第一次 CRM 呼叫之間的 1,538ms 是什麼？

### 判定：無法判定

### 已由證據確認的部分

Trace 中可觀察到下列順序（時間戳照原始 trace）：

```text
00:52:52.460 request.begin
00:52:52.712 pool.acquire.wait waitedMs=0
00:52:52.712 pool.create.begin ensureMin
00:52:53.995 pool.create.end c-1 ms=1224
00:52:53.995 pool.create.begin ensureMin
00:52:54.260 pool.create.end c-2 ms=132
00:52:55.798 pool.health c-1 result=true
00:52:55.798 pool.acquire.hit c-1
00:52:55.798 gateway.execute.enter
00:52:55.798 crm.op contact RetrieveMultiple ms=129
```

因此，依現有事件時間戳，1,538ms 位於第二個 `pool.create.end`（00:52:54.260）與 `pool.health`／`pool.acquire.hit`（00:52:55.798）之間。此位置已由 trace 確認，但不能據此把全部時間歸屬給單一操作。

新建 `PooledClient` 的 `LastValidatedUtc` 初始為 `DateTime.MinValue`，第一次出借必須通過健康檢查（`ToolUtility/Dataverse/PooledClient.cs:41-49`；`ToolUtility/Dataverse/BoundedClientPool.cs:199-223`）。健康檢查會呼叫 `service.Execute(new WhoAmIRequest())`（`ToolUtility/Dataverse/DataverseConnectionManager.cs:104-116`）。

### 無法判定的原因

`pool.health` 目前只有健康檢查完成後的結果事件，沒有 begin 事件或 elapsed 欄位（`ToolUtility/Dataverse/DataverseTrace.cs:763-769`）。此外，JSONL 的 `ts` 是背景 writer 真正寫檔時才呼叫 `DateTime.UtcNow`，不是事件 enqueue 或實際操作發生的時間（`ToolUtility/Dataverse/DataverseTrace.cs:1040-1062`、`1083-1095`、`1116-1125`、`1183-1190`）。

所以目前不能誠實地判定 1,538ms 是 WhoAmI、認證／metadata／channel 建立、背景排程延遲，或它們的組合；任何更精確的歸因都會是猜測。

### 要查明所需的觀測點（僅列觀測需求，未提出效能改動）

1. 在 `EnsureMinimum` 入口與出口記錄 monotonic elapsed、`reserved` 及實際建立數量。
2. 在 `_healthCheck` 呼叫前後記錄 `clientId`、monotonic elapsed 與成功／失敗結果。
3. 在 `OnPremiseClient` 建構及第一次 `WhoAmI` 內部分別記錄認證、metadata、channel 建立等子階段耗時。
4. 在 trace event enqueue 時保存 monotonic timestamp；writer 寫檔時的 wall-clock `ts` 不足以作精密事件排序與耗時歸因。

## Q2：為什麼登入會同步走 `ensureMin` 建線？

### 判定：已確認

`BoundedClientPool.Acquire` 在取得 semaphore 後無條件呼叫 `EnsureMinimum`（`ToolUtility/Dataverse/BoundedClientPool.cs:158-196`）。`phase = "ensureMin"` 只是錯誤事件的階段描述（同檔案 `:192-197`），不是額外的觸發條件。

補足條件是：

```text
reserved = MinSize - alive - Pending
```

只有 `reserved > 0` 才保留 pending 名額並建立 client（`ToolUtility/Dataverse/BoundedClientPool.cs:448-486`）。本次 Development 組態的 `MinSize=2`（`SpeechMessageProducts.ChurchReport/appsettings.Development.json:6-13`），因此冷路徑出現兩次 `ensureMin` 建線事件。

建線由呼叫 `Acquire` 的執行緒同步完成；`DataverseGateway.Execute` 最外層直接呼叫 `_manager.Acquire()`（`ToolUtility/Dataverse/DataverseGateway.cs:97-104`）。應用程式啟動時沒有 Dataverse 預熱：DI 只註冊 singleton／scoped 服務（`ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs:64-91`），`Startup` 只呼叫 `services.AddToolUtility()`（`SpeechMessageProducts.ChurchReport/Startup.cs:419-422`），`Program` 建立並執行 host，未 resolve manager 或呼叫 `Acquire`（`SpeechMessageProducts.ChurchReport/Program.cs:94-140`）。已註冊的 hosted services 是 Session／identity audit，不是 Dataverse warmup（`Startup.cs:327-331`、`553-557`）。

本次 trace 已確認登入 request 是觀察到的首次觸發者；但僅從程式碼不能斷言所有部署中的第一個 HTTP request 必然觸發，因背景或其他非 HTTP 呼叫也可能先使用 `IDataverseGateway`／manager。

另：`BoundedClientPool.cs:513-514` 的註解聲稱建線在子池鎖內，但現行實作是在鎖外建立（`451-466` 先保留 `Pending`，`469-486` 再提交），且測試 `ToolUtility.Dataverse.Tests/BoundedClientPoolTests.cs:297-354` 明確保護「建線在鎖外」的現況。這是附帶發現，未修改。

## Q3：能否用背景暖機消除登入冷路徑？風險是什麼？

### 判定：推論

目前沒有實作或新實跑，因此只能就既有生命週期契約推論可行性與風險，不能宣稱已驗證。

若未來考慮背景暖機，以下既有不變量必須維持：

- 未通過 `WhoAmI` 的 client 不得交給 request（`ToolUtility/Dataverse/PooledClient.cs:47-49`；`BoundedClientPool.cs:204-223`）。
- `Pending` 保留與補滿競爭控制不得被破壞，否則並行暖機／請求可能重複建立 client（`BoundedClientPool.cs:451-458`）。
- shutdown 與建線交錯時，client 必須由 pool 釋放，不能留下 orphan（`BoundedClientPool.cs:469-496`）。
- pool key、服務帳號及隔離邊界必須由 DI／可信組態產生，不得採用 caller 提供值（`ToolUtility/Dataverse/DataverseConnectionManager.cs:29-75`）。
- lease 歸還前要清除 `CallerId`，避免跨 request 身分狀態外洩（`ToolUtility/Dataverse/PooledClient.cs:105-129`、`184-198`）。

暖機失敗的安全語意應是「不阻擋 host 啟動，且絕不發出失敗或未驗證 client」。若第一個 request 早於暖機完成，必須等待共享 readiness 結果，或遵循既有具上限的 `Acquire` 建立／失敗路徑；不可讀取半完成狀態。timeout、取消及 host stopping 也必須讓背景工作收斂並使 `Pending` 回到零。

既有測試對首次 `Acquire` 會建線有隱含前提，若未來真的改變觸發時機，至少需重新檢視：

- `ToolUtility.Dataverse.Tests/BoundedClientPoolTests.cs:297-354`
- `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs:365-455`、`:460-500`
- `ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs:120-172`

本次調查不實作暖機，也不提出效能改動。

## Q4：`SetupSystemData` 的 1,988ms 組成與可辨識的重複工作

### 判定：已確認（組成）；部分重複性判定為推論／已由程式碼確認

Trace 的父 phase 與子 phase：

| Phase | 時間 | 判定 |
|---|---:|---|
| `Login.SetupSystemData` | 1,988ms | 已由 trace 確認 |
| `SetupListManager` | 609ms | 已由 trace 確認 |
| `SetDonationPaymentModel` | 1,024ms | 已由 trace 確認 |
| `SetupLessonList` | 279ms | 已由 trace 確認 |
| 三個可見子 phase 之外 | 76ms | 算術差額；已由數字確認，但內部組成未拆出 |

父 phase 與子 phase 是巢狀關係，不能把四列直接相加後再宣稱等於登入總耗時。

### `list RetrieveMultiple` 六次

`DownloadListManager.FindListCollection` 在 `DownloadListManager.cs:483,488,494,499,504,508` 依六種角色關聯逐一查詢。查詢的 entity 是 `list`，實際 `RetrieveMultiple` 路徑在 `PresentRecordQueryService.cs:287-300`。對應 trace CRM 內層耗時為：

```text
42ms、46ms、26ms、27ms、27ms、28ms（合計 196ms）
```

六次只差 relation attribute，因此「存在查詢合併的研究空間」是推論；在未確認 CRM schema、角色結果、排序與去重語意前，不能判定可以安全合成單一查詢，也未提出或實作該改動。

### `list Retrieve` 與 `listmember RetrieveMultiple` 各四次

`DownloadListManager.cs:233-258` 迴圈建立每個 `WeeklyReportRecord`；第一次成員總數取用在 `:269-304`，同一名單的圖表總數又在 `:306-311` 再取一次。`GetSmallGroupMemberNumber` 每次先 `Retrieve("list", ..., "type")`，再查 listmember（`DownloadListManager.cs:344-377`；listmember 實作在 `ListService.cs:168-183`）。

trace 值為：

```text
list Retrieve：26ms、31ms、33ms、42ms（合計 132ms）
listmember RetrieveMultiple：35ms、27ms、30ms、35ms（合計 127ms）
```

這四組重複計數由程式碼直接確認；同一 `ListEntity` 的成員數先後被兩個用途讀取，合計原始 CRM 內層時間 259ms。能否在不改變輸出與例外語意下共用結果屬後續設計問題，本次不提出效能改動。

### 其他 `SetupSystemData` 成本

Donation model 會讀取兩個 task、信用卡、認獻資料與 OptionSet（`DonationPaymentModelAssembler.cs:56-73`、`:109-122`、`:124-153`、`:161-194`）。課程載入的細節未逐一完成事件映射，因此不能把其內部 CRM 事件錯歸到上述 list/listmember 族群。

## 登入 5,840ms 成本對照

| 成本 | 毫秒 | 判定 |
|---|---:|---|
| request middleware 等（total-action） | 64 | 已由 trace 確認 |
| `Login.ValidateUserCredentials` | 3,062 | 已由 trace 確認 |
| 其中 `ensureMin` client 建立（1,224 + 132） | 1,356 | 已由 trace 確認 |
| `pool.create.end` 至 `pool.health` 的區間 | 1,538 | 區間已確認；內部歸因無法判定 |
| `Login.RetrieveUserData` | 292 | 已由 trace 確認 |
| `Login.SetupSystemData` | 1,988 | 已由 trace 確認 |
| `SetupSystemData` 三個可見子 phase 差額 | 76 | 算術差額；內部組成未判定 |
| action 中未列 phase 的其餘時間 | 434 | 算術差額；不代表獨立、不重疊區段 |

`[Perf] slowest=contact.RetrieveMultiple:3059ms` 不代表 CRM 查詢本身耗時 3,059ms；同一呼叫的 CRM 內層 trace 為 129ms，外層值包含 acquire／冷建線路徑。這是既有 trace 事實，本報告不提出查詢優化。

## 附帶發現（未處理）

1. `ToolUtility/Dataverse/BoundedClientPool.cs:513-514` 的建線鎖定註解與現行實作及測試不一致；未修改。
2. JSONL `ts` 在 writer 寫檔時產生，不能當作精密的事件發生時間；未修改 trace writer。
3. 調查開始前工作樹已有 dirty state，包含 `.ccg/tasks/unified-trace-guard-and-analysis/.turns.json`、已刪除的 08-20 task 檔案，以及未追蹤的 08-21 task／archive 目錄；未還原、未提交、未修改。

## 未能完成事項與原因

- Q1 的 1,538ms 無法進一步拆成 WhoAmI、認證、metadata、channel 或 writer／排程成本：現有 trace 沒有健康檢查 begin／elapsed，且 JSONL 時戳是 writer 時間。需要新增觀測點並重新實跑，但本任務禁止修改程式與收集新 trace。
- Q3 沒有實作或驗證背景暖機：本任務是純調查且明令不得提出或實作效能改動。
- 課程載入及 `SetupSystemData` 其他子路徑未逐一映射：現有證據不足，且本任務禁止另開調查。
