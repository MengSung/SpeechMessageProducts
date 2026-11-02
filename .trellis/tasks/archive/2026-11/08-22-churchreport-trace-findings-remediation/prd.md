# ChurchReport Trace 實測缺陷修復

## 背景

2026-08-21 13:48–13:53 對 ChurchReport 執行了一次真實操作重現，產生三份 Trace：

| 檔案 | 內容 | 時間範圍 |
|---|---|---|
| `D:\除錯追蹤\dataverse-trace.jsonl` | ToolUtility Dataverse Trace，4,966 筆結構化事件 | 13:48:01 – 13:53:02 |
| `D:\除錯追蹤\Trace.log` | 應用層與 `[Perf]` 觀測，1,924 行 | 13:48:00 – 13:52:18 |
| `D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT` | 舊版 ToolUtility Big5 錯誤 Trace，297 行 | 13:51:41 – 13:52:21 |

逐事件重算的結論（完整分析見 `D:\除錯追蹤\ChurchReport-Trace-Report.md`）：

- **連線／租約層無洩漏**：625 次 acquire 對應 625 次 return，leaseId 無重複，每條實體連線最大同時租借數為 1，`callerIdAtReturn` 625/625 為空，`gateway.scope.end` 的 `leaseStillHeld` 恆為 false。
- **連線池穩定**：啟動 6 秒內建立 4 條，12 秒時 idle cleanup 淘汰 2 條回到 `MinSize=2`，其後 5 分鐘 `created=4`、`discarded=2`、`alive=2` 完全不變。
- **無記憶體洩漏證據**：三個零請求區間中 Managed 記憶體變化為 −6 / +0 / +0 MB，Handles 分別為 −32 / −14 / −26。成長與負載同步、閒置即停止。

本任務只處理實測暴露出來的四項缺陷，不重構已驗證正確的連線池核心。

## 問題陳述

### F1（P1・正確性）SaveIntegrate 背景任務與前景請求共用可變狀態，無同步保護

`SmallGroupController.Save.cs` 的 `SaveIntegrate` 採 fire-and-forget。它在啟動 `Task.Run` 前捕獲的
`weeklyReportRef` / `allMemberData` **不是值，是指向 Session 快取物件圖的參考**：

```csharp
var weeklyReportRef = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
var allMemberData   = weeklyReportRef?.m_SmallGroupDataList?.m_AllMemeberData;
```

背景任務隨後對這個集合執行 `RemoveTransferredMembers` 就地改寫。程式自身註解已承認缺少同步機制。

**實測證據**：`traceId=0HNNV8V1JEM69:00000035` 的背景視窗（05:52:07.191–05:52:21.382 UTC）長度 14.2 秒，
期間有 **42 個來自同一使用者的並行請求**在飛行中，且這些請求（`UpdateSmallGroupPresentRecord`、
`AssignSmallGroupGet`、`GetMultiGroupChartDataList`）都會從同一個 Session 快取鍵取出 `ListManager`。
競爭視窗是實際存在的，本次未撞出可見錯誤屬僥倖。

風險為集合列舉時被改寫（`InvalidOperationException`）、成員清單讀到半完成狀態、以及使用者看到不一致的畫面。

### F2（P1・記憶體）`NOSESSION_` 快取鍵每次呼叫都產生新鍵，30 分鐘內無法回收

`InMemoryDataContextSmallGroup.cs` 的 `GetCurrentSessionId()` 在 `CurrentSession == null` 時回傳：

```csharp
var tempKey = $"NOSESSION_{Environment.MachineName}_{Thread.CurrentThread.ManagedThreadId}_{DateTime.UtcNow.Ticks}";
```

`Ticks` 讓每次呼叫都得到全新且**永遠無法再命中**的鍵。`ListManager`、`SmallGroupDataList`、
`WeeklyReportData`、`NewPersonModel`、`PersonalInfomationModel`、`HappyGroupDataManager` 等屬性的
getter 每次存取都會呼叫此方法，且會 `_memoryCache.Set(...)` 一個絕對過期 30 分鐘的物件圖。

`Startup.cs:210` 明確不設定 `SizeLimit`，因此無筆數上限，只受 30 分鐘過期與 GC 記憶體壓力驅逐約束。

**本次執行未觸發**（`Trace.log` 中 `NOSESSION` 出現 0 次），屬潛在缺陷。但 fire-and-forget 背景路徑
本來就在非 HTTP 上下文執行，此路徑一旦接觸這些屬性即會觸發。

### F3（P2・文件）Gateway 架構文件的 Faulted 不變量與程式不一致

`docs/architecture/dataverse-gateway-v1.md` 的核心不變量第 4 條寫著「任一 timeout、取消或**執行例外**
都會將 lease 標記 Faulted」。

實測有 7 次 `crm.op ok=false`，對應 7 條 lease **全部以 `state=healthy` 歸還**，`faulted` 計數恆為 0。

查證後確認**程式是對的、文件是舊的**：`DataverseGateway.IsConnectionFault()` 刻意把 `FaultException`
（伺服器已完整回覆 SOAP fault、通道健康）判為不淘汰，只有傳輸層例外才淘汰，並在 XML 註解完整說明
了「`FaultException` 是 `CommunicationException` 子類別、比對順序不可調換」這個陷阱。

文件不改，下一次拿架構圖對 trace 稽核的人會把正確行為誤判成違規。

### F4（P2・觀測性）背景工作的 CRM 耗時完全不在 `request.end` 的統計內

`request.end` 在 HTTP 管線結束時寫出，而 fire-and-forget 的工作在那之後才開始，因此：

| traceId | request.end 記錄 | 實際背景工作 |
|---|---|---|
| `0HNNV8V1JEM66:00000025` | `durationMs=5, crmCount=0, leaseCount=0` | 62 次 CRM／3,958 ms |
| `0HNNV8V1JEM69:00000035` | `durationMs=0, crmCount=0, leaseCount=0` | 172 次 CRM／14,138 ms |

全域影響：

- **625 次 CRM 操作中有 234 次（37.4%）不被任何 request 歸因**
- **39,305 ms CRM 總耗時中有 18,096 ms（46%）落在 `request.end` 統計之外**

這些事件仍掛在已結束 request 的 traceId 上（`DataverseTrace.Current` 這個 `AsyncLocal` 被
`Task.Run` 的 ExecutionContext 帶進背景執行緒），所以分析器看到的是「一個 0ms 的請求後來又做了 172 次 CRM」。

後果是任何基於 `request.end` 的容量規劃、慢請求排名與 CRM 歸因，對 fire-and-forget 端點都是結構性盲區。
自動報告把 `/SmallGroup/SaveIntegrate` 列為「2 次、平均 3ms」，實際上它是全場最重的操作。

## 目標

1. 消除 F1 的資料競爭，讓背景上傳不與前景請求共用可變集合。
2. 消除 F2 的無界快取成長路徑。
3. 讓 F3 的架構文件與程式行為一致。
4. 讓 F4 的背景工作有獨立、可分析的觀測邊界。

## 非目標

- 不重構 `BoundedClientPool` / `DataverseGateway` / `ClientLease` / `PooledClient` 的核心生命週期，這些已由本次 trace 實證通過。
- 不處理 `appsettings.json` 明文密碼、`ToolUtilityClass` legacy credential fallback、`ICrmConnectionPool` 相容介面移除——屬既有技術債，不在本次範圍。
- 不修 D365 伺服器端 `WeeklyReportPlugIn.dll` 缺檔問題——那是伺服器部署事項，不在程式碼範圍。
- 不修 4 個檔案的原始碼編碼損毀（`BaseChurchController.cs` 等）——另案處理。
- 不改 `IsConnectionFault()` 的分類邏輯，只改文件。
- 不重寫 `SaveIntegrate` 為佇列式背景服務——那是更大的架構變更，本次只消除競態與盲區。

## 驗收標準

### F1

- `SaveIntegrate` 背景任務不再持有任何指向 Session 快取物件圖的參考；捕獲的資料為背景任務獨佔。
- 背景清理（`RemoveTransferredMembers`）不再就地改寫前景請求可見的集合。
- 既有回應契約不變：仍立即回傳 `{ status = "1", message = "資料已送出，正在背景上傳中..." }`。
- 新增單元測試證明：背景任務執行期間對前景可見集合的列舉不會擲出 `InvalidOperationException`。

### F2

- `GetCurrentSessionId()` 在無 Session 時不再產生每次唯一的快取鍵。
- 無 Session 路徑不得寫入 `IMemoryCache`，或寫入的鍵在同一流程內可重複命中。
- 新增單元測試證明：在無 `HttpContext` 的情況下重複存取 `ListManager` 一千次，`IMemoryCache` 的項目數不隨次數成長。

### F3

- `docs/architecture/dataverse-gateway-v1.md` 的核心不變量第 4 條改為描述「僅傳輸層故障淘汰、商業層 fault 保留連線」，並說明理由與比對順序限制。
- 架構圖元件對照表中 ⑦ 的「已驗證的保護」欄位同步更新。
- 文件內新增本次 trace 的實證數據（7 次 business fault、0 次 faulted 歸還）作為佐證。

### F4

- `DataverseTrace` 提供背景工作專用的 scope API，使背景 CRM 操作有自己的開始／結束事件與獨立統計。
- 背景事件可與觸發它的 request 關聯（保留來源 traceId），但**不再污染該 request 的 `request.end` 統計**。
- `SaveIntegrate` 的背景任務使用此 API。
- 重跑一次同樣的操作後，`Σ request.end.crmCount + Σ 背景結束事件.crmCount == count(crm.op)` 成立。
- 分析器可據此把背景工作獨立列出，不再顯示為「0ms 的請求」。

### 全域

- `ToolUtility.Dataverse.Tests` 與 `ToolUtility.Tests` 既有測試全數維持通過（基準：37/37、63/63）。
- `ChurchReport.Tests` 與 `ChurchReport.MemberInfo.Tests` 全數維持通過。
- 方案可建置，無新增警告。

## 證據來源

- `D:\除錯追蹤\dataverse-trace.jsonl`（4,966 筆事件，本任務所有量化數字的來源）
- `D:\除錯追蹤\Trace.log`
- `D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT`
- `D:\除錯追蹤\ChurchReport-Trace-Report.md`（修正版分析報告）
- `docs/architecture/dataverse-architecture-code-conformance-v1.md`（本任務 F1／F4 對應該文件已列出的「A 上線前必須處理」項目）
