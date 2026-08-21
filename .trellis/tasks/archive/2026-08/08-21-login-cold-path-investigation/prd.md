# PRD：登入 3.3 秒盲區與 ensureMin 同步路徑調查

## 性質

**這是純調查任務，不修改任何程式碼。** 產出是一份分析文件，供後續 B-2 實作任務決策使用。

## 背景

前一任務（`archive/2026-08/08-20-perf-crm-attribution-and-switch-decouple`）已修好
`[Perf]` 的 CRM 歸因。用修好的工具重讀 2026-08-21 09:02 的實跑，發現登入的時間分佈
與原先假設完全不同。

### 已確認的事實（來自 trace，不是推測）

證據檔案（唯讀，不得修改）：
- `D:\除錯追蹤\Trace.log.sessionverbose-false-auth-20260821-090207`
- `D:\除錯追蹤\dataverse-trace.jsonl.sessionverbose-false-auth-20260821-090207`

登入請求 `traceId = 0HNNV3PMLLIEV:00000002` 的事件時序：

```
00:52:52.460  request.begin
00:52:52.712  pool.acquire.wait   waitedMs=0
00:52:52.712  pool.create.begin   reason=ensureMin
00:52:53.995  pool.create.end     ms=1224  ok=true
00:52:53.995  pool.create.begin   reason=ensureMin
00:52:54.260  pool.create.end     ms=132   ok=true
              ← 1,538ms 之間三個 trace 檔皆無任何事件
00:52:55.798  pool.health
00:52:55.798  pool.acquire.hit
00:52:55.798  gateway.execute.enter
00:52:55.798  crm.op RetrieveMultiple contact ms=129 ok=true
```

`[Perf-Phase]` 分解：

```
Login.ValidateUserCredentials                  3,062ms
Login.SetupSystemData                          1,988ms
  ├ SetDonationPaymentModel                    1,024ms
  ├ SetupListManager                             609ms
  └ SetupLessonList                              279ms
Login.RetrieveUserData                           292ms
```

request 總時間 5,840ms，action 5,776ms。

### 關鍵推論

從 `request.begin` 到第一次 `gateway.execute.enter` 共 **3,338ms，期間零次 CRM 呼叫**。
`[Perf]` 標示的 `slowest=contact.RetrieveMultiple:3059ms` 是外層計時假象：
該查詢的實際 CRM 時間只有 129ms，3,059ms 其中絕大部分是連線建立與取得租約。

因此優化該查詢本身最多只能省 129ms。真正的成本在連線建立路徑上。

## 必須回答的問題

### Q1：那 1,538ms 是什麼？（最高優先）

`00:52:54.260`（最後一個 `pool.create.end`）到 `00:52:55.798`（第一個 `pool.acquire.hit`）
之間，三個 trace 檔完全沒有事件。

必須查明這段時間程式在做什麼。可能方向（不限於此，也不得預設答案）：
- Dataverse SDK 的 metadata 下載或 WCF channel 首次建立
- 認證握手（`ClaimsBasedAuthClient`）
- `pool.health` 檢查本身的成本
- 應用程式自身的非 CRM 運算
- 現有儀表在此路徑上沒有觀測點（若是，指出應在何處加）

**若無法從現有 trace 與程式碼推斷，明確寫「無法從現有證據判定」，並列出需要哪些額外觀測點。
不得猜測後當成結論陳述。**

### Q2：`ensureMin` 的完整同步路徑

- `BoundedClientPool.cs:193` 的 `phase = "ensureMin"` 由誰、在什麼條件下觸發
- `EnsureMinimum`（`BoundedClientPool.cs:448`）是否持有子池鎖（`:513` 的註解提到「在子池鎖內呼叫」），
  以及該鎖對並行請求的影響
- 這條路徑是否**必然**發生在第一個 HTTP 請求的執行緒上，還是有其他觸發時機
- 應用程式啟動到第一個請求之間，有沒有既有的暖機機制

### Q3：改動的可行性與風險

不需要提出完整設計，但要回答：
- 把 `ensureMin` 移到背景暖機，有哪些不變量會被破壞
  （例：暖機未完成時第一個請求如何等待？暖機失敗是否會擋住應用程式啟動？）
- 第一個請求若在暖機完成前抵達，正確行為應該是什麼
- `BoundedClientPool` 現有測試中，哪些會因為時機改變而失效

### Q4：`SetupSystemData` 的 1,988ms 組成

登入的 30 次 CRM 呼叫中觀察到：
- `list` `RetrieveMultiple` × 6（42, 46, 26, 27, 27, 28 ms）
- `list` `Retrieve` × 4（26, 31, 33, 42 ms）
- `listmember` `RetrieveMultiple` × 4（35, 27, 30, 35 ms）

指出這些呼叫分別由哪段程式發出、是否為迴圈內逐筆查詢、能否合併為批次查詢。
給出「可合併」與「不可合併」的分類與理由，不需要寫實作。

## 交付物

`.trellis/tasks/08-21-login-cold-path-investigation/findings.md`

內容須包含：
1. Q1～Q4 各自的結論，每個結論標註是「已由證據確認」或「推論」或「無法判定」
2. 每個結論附上依據：檔案:行號，或 trace 事件與時間戳
3. 一張「登入 5,840ms 成本歸屬表」，把時間分配到具體成因，
   無法歸屬的部分明確列為「未歸屬」並寫出毫秒數
4. B-2 的建議標的排序，附各自的預估效益（毫秒）與風險

## 非目標（明確排除）

- **不修改任何 `.cs`、`.json`、`.ps1` 檔案**
- 不啟動應用程式、不收集新的 trace
- 不執行 `Start-CcgDualModelRun.ps1` 或任何 reviewer
- 不修改 `D:\除錯追蹤` 下任何檔案（唯讀分析）
- 不提出或實作任何效能改動
- 不處理 Session leakage（六判準已全部為 0，無事可做）
- 不處理 Memory leakage（09:02 實跑的 17 筆 proc.snapshot 顯示
  handles 825→780 遞減、managedMb 平穩於 31、threads 51→40 遞減，無洩漏跡象）

## 完成定義

`findings.md` 存在且 Q1～Q4 皆有結論（「無法判定」是可接受的結論，猜測不是）。
不得有任何檔案被修改，`git status --short` 除 `findings.md` 外不應出現其他變動。
