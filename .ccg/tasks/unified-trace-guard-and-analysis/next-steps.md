# 早上修改整理與下一步規劃

> 產出時間：2026-08-20 ｜ 依據：commit 400100ab..a297e8bd、`D:\除錯追蹤` 實際 trace、原始碼

---

## 一、今天早上做了什麼

09:04 的《分析結論與下一步》列了 5 項待辦。對照今天的 commit：

| # | 09:04 的計畫 | 狀態 | Commit |
|---|---|---|---|
| 1 | 例外分類，別讓欄位打錯燒掉連線 | ✅ 完成 | `400100ab` 09:45 |
| 2 | 建線加 trace（`pool.create.*`、`pool.acquire.fail`） | ✅ 完成 | `df92601b` 10:12 |
| 3 | 把建線移出鎖 | ✅ 完成 | `55b53ca3` 10:28 |
| 4 | Gateway 平行安全 | ⚠️ **只做到「看得見」** | `ada276f7` 11:09 |
| 5 | 補分析器規則 | ⚠️ **做了，但不在版控裡** | 未 commit |

另外 `a297e8bd` 11:30 把 `DescribeRequest` 加上 `msg:` 前綴，讓 SDK 訊息名稱不再與 entity logical name 混欄。

**實作面實際發生的事：**

- `DataverseGateway.IsConnectionFault`：改成先判 `FaultException` 再判 `CommunicationException`（順序不可調換，因為前者是後者的子類別）。商業層 fault 不再淘汰連線。
- `BoundedClientPool.EnsureMinimum`：改成「鎖內只做名額保留（`Pending`）→ 鎖外握手 → 鎖內入池」。網路握手已經不在鎖裡了。
- `DataverseTrace`：事件從 10 種擴到 **23 種**，新增 `crm.op`、`pool.snapshot`、`proc.snapshot`、`gateway.concurrent`、`gateway.scope.end`、`pool.lock.wait`、`pool.create.*`、`pool.acquire.fail`、`pool.dispose`。
- `CrmOperationTrace`：新檔，成為兩個 `IOrganizationService` 代理記錄 CRM 操作的唯一路徑，兩邊稽核輸出必然一致。

**測試狀態（本次實測）：**

```
ToolUtility.Dataverse.Tests   57 / 57  通過   （昨天 44）
ToolUtility.Tests             63 / 63  通過
```

---

## 二、盤點時發現的三件事

### 🔴 1. 更新後的分析器不在版控裡

| | 路徑 | 行數 | 大小 | 修改時間 |
|---|---|---:|---:|---|
| Repo 內 | `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1` | 860 | 41,956 B | 08-19 12:36 |
| 實際在用 | `D:\除錯追蹤\PowerShell\Analyze-ChurchReportTraces.ps1` | **1,280** | **74,564 B** | **08-20 11:15** |

產出 11:27 那份報告的是右邊那一支。Repo 裡那支只認得 10 種事件，而 C# 現在發 23 種。

旁證：`D:\除錯追蹤\PowerShell\Analyze-ChurchReportTraces-001版本.ps1` 是 41,956 B —— 與 repo 內現版**位元組數完全相同**，等於留了一份 repo 版的快照在旁邊，可佐證兩者是同一血緣的前後版本。

已比對過：**兩支的函式名稱集合完全相同**，是同一支腳本的原地演進，不是分叉 —— 所以覆蓋回 repo 的風險低。

### 🔴 2. Trace.log 的數字目前不能當量測基準

```
總行數                                    11,732
  GetCurrentSessionId                      6,202   (52.9%)
  GenerateCurrentRequestFingerprint        4,293   (36.6%)
  [Perf] 系列                                 76   ( 0.6%)
```

89.5% 是噪音，而且 `Program.cs:195,220` 設了 `AutoFlush = true` —— **每一行都是請求執行緒上的同步磁碟寫入**。94 秒的量測窗內發生了 10,495 次同步寫入。

這代表：**「應用程式那 2.2 秒」裡面有多少是量測本身造成的，現在無法分離。** 先降噪，phase 數字才有意義。

另外這些噪音行**沒有時間戳**（只有 listener 初始化那幾行有），所以 Trace.log 無法逐行做時間關聯。

### 🔴 3. Trace.log 明文寫 Session GUID

```
[GetCurrentSessionId] 📋 Session ID: a43325c3-2beb-1b14-55ac-2c3b9d6336f0
```

JSONL 那邊費了很大力氣做假名化（`u_xxxxxxxx`），legacy 這邊直接把原始 session GUID 寫進去。這就是報告裡「legacy 檔案有敏感模式命中」的來源 —— 是真的命中，不是誤判。

---

## 三、對「下一步建議」的修正

你從另一個 session 帶回來的三點建議，第 1 點的成本判斷需要更新：

> 建議下一步加一層 phase 級 trace，把應用程式那 2.2 秒再分段。

**phase trace 已經存在，而且已經在寫資料了。**

`PerfPhase.Measure()` 在 4 個 controller 有約 50 個埋點，Trace.log 裡現在就有 18 筆 `[Perf-Phase]`。ProcessLogin 那 3,774ms 的分解已經在檔案裡：

```
/Home/ProcessLogin                        total = 3,774ms
  Login.ValidateUserCredentials                  1,946ms   ← 52%
  Login.SetupSystemData                          1,437ms   ← 38%
    ├─ SetupListManager                            691ms
    ├─ SetDonationPaymentModel                     298ms
    ├─ SetupLessonList                             258ms
    └─ SetupAppointmentList                        164ms
  Login.RetrieveUserData                           205ms
```

**缺的不是埋點，是分析器沒解析這些行。** 兩支分析器都只 `Contains('[Perf-N+1]')` 數個數，`[Perf-Phase]` 整個被丟掉。

工作量因此從「C# 埋點專案」降為「分析器加一個 parser」。

**但同時要注意**：`[Perf]` 行的 `crm{n=0,ms=0}` 全部是 0 —— `TimedOrganizationService` 這個裝飾器只掛在 legacy 的 `tu.m_Crm2011OrganizationService` 上，**沒有掛到新的 Gateway 路徑**。所以 `[Perf-Gap]` 說的 `gap=3710ms (未歸因)` 是假的 gap，它只是沒把 CRM 時間扣掉。真正可信的 CRM 時間在 JSONL 的 `crm.op`。

兩套量測目前是**互不相通**的：

```
新路徑  CrmOperationTrace ──→ crm.op          ──→ dataverse-trace.jsonl   ✅ 時間正確
舊路徑  TimedOrgService   ──→ RequestProfiler ──→ Trace.log [Perf]        ❌ crm 恆為 0
                              PerfPhase       ──→ Trace.log [Perf-Phase]  ✅ 有資料、沒人讀
```

---

## 四、工作項目

### B0 — 把分析器收回版控 〔小〕〔阻塞其他項目〕

**為什麼排第一**：74KB / 1,280 行的成果目前只存在於 `D:\` 單一副本，沒有任何歷史。這是唯一一個「什麼都不做就可能歸零」的風險。而且後面每一項都要改分析器，改的必須是同一支。

**設計**

- 方向是「out-of-repo → in-repo」單向覆蓋。已驗證函式集合相同，屬原地演進。
- 覆蓋前先備份 repo 內現版（給 diff 用），確認沒有只存在於舊版的邏輯。
- 補一支 fixture 回歸測試，讓「分析器又漂移了」這件事會被測試抓到，而不是靠人記得。

**實作**

1. `cp D:\除錯追蹤\PowerShell\Analyze-ChurchReportTraces.ps1` → `SpeechMessageProducts.ChurchReport/Tools/`
2. diff 舊版，確認無邏輯遺失
3. 用既有 fixture（`valid` / `invalid` / `missing`）跑三種情境，比對 exit code 0/2/0
4. Windows PowerShell 5.1 + PowerShell 7 各跑一次
5. commit

**驗收**：`git status` 乾淨；三種 fixture exit code 與 `review.md` 記錄一致。

---

### B1 — 降 Trace.log 的噪音與量測污染 〔中〕〔B2 的前置〕

**為什麼排在 phase 分析之前**：phase 數字現在被 10,495 次同步磁碟寫入污染。先量再修會得到錯的歸因結論，然後照著錯的結論去優化。

**設計**

三個決策：

1. **`GetCurrentSessionId` / `GenerateCurrentRequestFingerprint` 的逐步 trace 要不要留**
   - 推薦：降級到獨立的 verbose 開關，預設關閉。這些是當初除錯 session 問題時加的，問題已經解決，但 trace 留著。
   - 不建議直接刪：真的再出問題時會想要它。

2. **`AutoFlush = true` 要不要關**
   - 推薦：改為 `false` + 明確的 flush 點（請求結束、程序停止、未處理例外）。
   - 風險：程序被強制砍掉時會丟失尾端幾行。`FileToolUtilityTracer` 已經是 `AutoFlush = false`，可沿用同樣的處理方式。

3. **要不要給噪音行加時間戳**
   - 若第 1 點採「預設關閉」，此點可省。開著的時候才需要時間戳。

**實作**

- `SpeechMessageProducts.ChurchReport/Program.cs:195,220` — AutoFlush 與 flush 點
- `GetCurrentSessionId` / `GenerateCurrentRequestFingerprint` 的呼叫端 — 加開關
- 加一個測試：驗證 verbose 關閉時這兩個方法不產生 trace 行

**驗收**：同一組操作重跑，Trace.log 行數從 11,732 降到約 1,300 以內，`[Perf]` 佔比從 0.6% 升到 5% 以上。**然後重跑 ProcessLogin，看 1,946ms 這個數字變成多少** —— 差額就是量測本身的成本。

---

### B2 — 分析器解析 `[Perf-Phase]`，做 phase 歸因 〔中〕

**設計**

關鍵設計問題是**父子 phase 的時間重複計算**：

```
Login.SetupSystemData                1,437ms   ← 這裡面已經包含下面四個
  ├─ SetupListManager                  691ms
  ├─ SetDonationPaymentModel           298ms
  ├─ SetupLessonList                   258ms
  └─ SetupAppointmentList              164ms
                              子項合計 1,411ms
```

直接加總會得到 2,848ms（超過請求總時間）。必須算 **self time = 自身 ms − 直屬子項 ms 合計**，此例 `SetupSystemData` 的 self time 是 26ms —— 也就是它自己幾乎不花時間，時間全在子項。

- phase 名稱以 `.` 分層，父子關係可由名稱前綴推出，不需要改 C# 加 parentId。
- 排序用 self time，不是總時間 —— 總時間排序永遠是最外層的 phase 贏，沒有資訊量。
- 聚合要有上限（沿用現有的 `MaxPairEntries` 模式），phase 名稱種類爆掉時標 WARN 而不是吃光記憶體。

**實作**

- `Analyze-ApplicationTrace`：新增 `[Perf-Phase]` 的 regex 與 `PhaseStats` 聚合
- 新增報告區段：每端點的 phase 樹，含 self time、佔請求百分比
- 名稱與路徑都要走既有的 `Convert-ToSafeLabel`（phase 名稱是程式碼常數不含使用者資料，但路徑可能有）

**驗收**：報告能對 `/Home/ProcessLogin` 輸出上面那棵樹，且 self time 合計 ≤ 請求總時間。

---

### B3 — 打通 phase 與 `crm.op` 的關聯 〔中〕

**為什麼需要**：B2 做完會知道 `ValidateUserCredentials` 花了 1,946ms，但**不知道其中多少是 CRM、多少是本機運算**。這是「該優化查詢還是該優化程式碼」的分水嶺，沒有它 B2 的結論只到一半。

**設計**

三個選項：

| 方案 | 做法 | 評估 |
|---|---|---|
| (a) `[Perf-Phase]` 行加 `traceId` | 兩檔用 traceId join | **推薦**。改動最小，JSONL 那邊已有 traceId |
| (b) phase 事件也寫進 JSONL | 統一到單一檔案 | 較乾淨，但要讓 ChurchReport 依賴 DataverseTrace，跨越了現有的分層 |
| (c) 時間戳區間近似對齊 | 不改 C# | 不可行 —— Trace.log 噪音行沒有時間戳（見 B1） |

推薦 (a)：`RequestProfiler.BuildPhaseLines` 多輸出一個 `traceId=` 欄位，值取自 `DataverseTrace` 目前的 request context。

**注意**：這樣只能算出「phase 期間內發生的 CRM 時間」，在有平行分支時會有歸屬歧義。先接受這個誤差，並在報告裡標明。

**實作**

- `RequestProfiler.BuildPhaseLines` / `BuildSummaryLine` 加 traceId
- 分析器：以 traceId 把 `crm.op` 掛到 phase 上
- 順便修 `[Perf]` 的 `crm{n=0}`：要嘛把 `TimedOrganizationService` 掛到 gateway 路徑，要嘛直接讓 `RequestProfiler` 從 `crm.op` 取數，別維護兩套

**驗收**：報告能對 `ValidateUserCredentials` 輸出 `1,946ms = CRM xxxms + 本機 xxxms`。

---

### B4 — N+1 門檻參數化並下修到 3 〔小〕

對應你的建議 2。現在 C# 端 `PerfThresholds.NPlusOneCrmCount` 與分析器端的門檻是**兩個各自寫死的常數**，先讓它們一致且可調，再下修。

**實作**

- `PerfThresholds` 改為可組態
- 分析器新增 `-NPlusOneThreshold` 參數，預設值與 C# 端對齊
- 用同一份 trace 分別跑門檻 5 與 3，比對多抓到哪些

**驗收**：門檻 3 跑出來的清單是門檻 5 的超集，且新增項目能人工確認是真的迴圈查詢。

---

### B5 — Gateway 平行競態：從「看得見」到「修掉」 〔大〕〔需要設計文件〕

這是**唯一剩下的正確性缺陷**，程式碼註解已經明說了：

> 為什麼只觀測不修正：修正需要把 `_depth` 與 `_lease` 改為 AsyncLocal……那是語意層級的變更，會改變租約數量與生命週期，必須單獨評估。

**設計（需要獨立文件，不要直接動手）**

`DataverseGateway` 是 Scoped，`_lease` / `_depth` 是無同步保護的一般欄位，而產品碼有十餘處 `Task.Run` / `Task.WhenAll` 共用同一實例。兩種後果：一條 lease 永不歸還（池子永久少一格），或連線在別人還在用的時候被還回池子（跨 request 共用 —— 整套架構要防的就是這個）。

三個方向：

| 方向 | 語意 | 代價 |
|---|---|---|
| `AsyncLocal<>` 化 | 每條平行分支自己一條 lease，巢狀共用 | 租約數上升，池子容量規劃要跟著改 |
| 鎖序列化 | 平行分支排隊共用一條 | 把呼叫端的平行化效果消掉 |
| 平行即擲例外 | 不支援平行，明確報錯 | 要先確認那十餘處 `Task.Run` 是否真的會撞到同一 Gateway |

**先做的事不是選方案，是取得數據**：`gateway.concurrent` 事件已經上線但實測 trace 中是 0 次。先在有平行負載的情境下跑一次，確認這條路徑到底會不會被踩到 —— 如果實際上永遠不平行，第三個方向（fail fast）就是成本最低的正解。

**驗收**：先有一份設計文件，說明選了哪個方向與為什麼；實作之後要有一個 `Task.WhenAll` 開 10 條執行緒打同一 Gateway 的測試，斷言 acquire 次數 == return 次數。

---

### B6 — 長時穩定性測試 〔中〕〔要等 B1〕

對應你的建議 3。現在只有 4 筆 `proc.snapshot`（間隔 30 秒，約 90 秒窗口），全是暖機期資料。

**設計**

- 分析器現在是**首尾差**（handle 560 → 847）。暖機期一定會漲，首尾差在暖機資料上必然誤判。要改成**排除前 N 筆之後的線性斜率**。
- 判準：handle 的穩定期斜率應該 ≈ 0。單調上升才是洩漏。
- 跑之前先做 B1 —— 否則 10 分鐘會產生約 70 萬行噪音，而且 AutoFlush 的同步寫入本身就會影響 handle 與記憶體。

**驗收**：≥ 20 筆快照；報告輸出穩定期斜率而非首尾差。

---

### B7 — 兩個小修 〔小〕

1. `BoundedClientPool.CreateClientCore` 的 XML 註解還寫著「本方法目前由 `EnsureMinimum` 在子池鎖內呼叫」—— `55b53ca3` 已經把它移出鎖了，註解過時且會誤導。
2. Trace.log 明文 session GUID（見第二節第 3 點）。B1 若採「預設關閉 verbose」可順帶解決；若決定保留這些行，就要做遮罩。

---

## 五、建議順序

```
B0  收回版控          ← 先做，其他項目都要改這支
 │
B1  降噪 + 關 AutoFlush   ← 讓量測可信
 │
 ├── B2  phase 歸因
 │    └── B3  phase × crm.op 關聯
 │
 ├── B4  N+1 門檻下修到 3
 │
 └── B6  長時穩定性測試

B5  Gateway 平行競態   ← 獨立線，先取得 gateway.concurrent 數據再決定方案
B7  兩個小修           ← 隨時可做
```

**一句話**：B0 是防遺失，B1 是讓數字可信，B2/B3 才是你真正想要的那個答案。跳過 B1 直接做 B2，會得到一份被量測開銷污染的歸因報告。
