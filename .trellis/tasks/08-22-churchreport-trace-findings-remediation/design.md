# 技術設計

## 邊界原則

四項缺陷分屬三個層次，修改必須留在各自的層：

| 缺陷 | 所屬層 | 允許修改的範圍 |
|---|---|---|
| F1 背景競態 | 產品層 | `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/`、`Models/ListSmallGroupWeeklyReport.cs`、`Models/SmallGroupDataList.cs` 及其成員型別 |
| F2 快取鍵 | 產品層 | `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs` |
| F3 文件 | 文件 | `docs/architecture/dataverse-gateway-v1.md` |
| F4 背景觀測 | 工具層 + 產品層 | `ToolUtility/Dataverse/DataverseTrace.cs`（新增 API）、`SmallGroupController.Save.cs`（呼叫端） |

**不可跨越的界線**：`ToolUtility` 是 Host-neutral 共用工具層，不得參考 ASP.NET Core、
`HttpContext`、Session 或任何 Web Hosting 型別。F4 新增的 API 只接受字串與基本型別。

---

## F1：背景上傳與前景請求的狀態隔離

### 問題結構

```
IMemoryCache["{SessionId}_{UserId}_{指紋}_{時戳}_ListManager"]
  └── ListManager
        └── m_ListSmallGroupWeeklyReport : ListSmallGroupWeeklyReport
              └── m_SmallGroupDataList : SmallGroupDataList
                    ├── m_SmallGroupData.Members         : List<Member>   ← 背景 RemoveTransferredMembers 就地改寫
                    ├── m_NewPersonFollowUpData.Members  : List<Member>   ← 背景 RemoveTransferredMembers 就地改寫
                    └── m_AllMemeberData.Members         : List<Member>   ← 背景上傳讀取
```

前景的 42 個並行請求走同一個快取鍵拿到**同一個** `ListManager` 實例，因此讀寫的是同一組 `List<Member>`。

### 選定方案：短臨界區 + 背景獨佔快照

長時間持鎖（14 秒）會阻塞前景，不可接受。改為「短鎖複製、長工無鎖、短鎖發布」：

```
[請求執行緒]
  取鎖 ──► 深拷貝 SmallGroupDataList 的三組 Members ──► 放鎖        (毫秒級)
  啟動 Task.Run(獨佔快照)
  立即回應使用者

[背景執行緒]
  在快照上執行 UploadIntegrateDataAsync            (14 秒，無鎖)
  在快照上執行 RemoveTransferredMembers            (無鎖)
  取鎖 ──► 將清理結果原子發布回共用圖 ──► 放鎖      (毫秒級)
```

### 實作要點

1. **同步原語**：在 `SmallGroupDataList` 新增 `private readonly object _syncRoot = new();` 並公開
   `internal object SyncRoot => _syncRoot;`。使用 `lock`（非 `SemaphoreSlim`），因為臨界區內只有
   記憶體複製、無 `await`。

2. **快照 API**：在 `SmallGroupDataList` 新增
   `public SmallGroupDataList CreateIsolatedSnapshot()`，於 `lock (_syncRoot)` 內建立新的
   `SmallGroupDataList`，其三組 `Members` 為 **新的 `List<Member>` 且每個 `Member` 為新實例**
   （淺拷貝 `List` 不夠——背景會改 `Member` 的欄位）。`Member` 若無複製建構式則新增之。

3. **上傳目標切換**：`SaveIntegrate` 不再把 `InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport`
   直接交給背景。改為建立一個背景專屬的 `ListSmallGroupWeeklyReport`，其
   `m_SmallGroupDataList` 指向快照，其餘純量欄位（`ListEntityId`、`ListEntityName`、
   `m_SelectDate` 等）由值複製而來。新增
   `public ListSmallGroupWeeklyReport CreateBackgroundUploadCopy()` 承載此邏輯。

4. **發布回寫**：背景清理完成後，取 `SyncRoot` 鎖，把快照中被保留下來的成員集合**整份替換**
   共用圖的對應 `Members` 參考（`list = newList`，而非 `Clear()` + `AddRange()`），使前景任何
   時刻看到的都是完整清單，不會看到半清空狀態。

5. **前景保護**：所有直接列舉或改寫這三組 `Members` 的位置都必須改為在 `lock (SyncRoot)` 內
   取得快照後再操作。以 `m_SmallGroupData.Members`、`m_NewPersonFollowUpData.Members`、
   `m_AllMemeberData.Members` 三個字串 grep 全 repo 定位所有呼叫點。

6. **密碼生命週期**：目前 `var password = InMemoryContext.ListManager.m_Password;` 被捕獲進背景
   closure，會在受管堆上明文存活整個上傳期間。維持現行行為（改動屬另案），但在程式碼加註 TODO
   指向既有的機密管理技術債，不要靜默忽略。

### 若圖結構過於糾纏的退路

若步驟 5 的呼叫點超過 30 處而無法在本任務內安全覆蓋，改採**唯讀退路**：背景任務只在快照上執行
上傳與清理，**完全不回寫**共用圖，並在 `SaveIntegrate` 回應中標記需重新整理。這徹底消除寫入側競態，
只留下步驟 1 的短鎖複製處理讀取側。採用退路時必須在 `implement.md` 記錄實際呼叫點數量作為依據。

---

## F2：無 Session 時不得寫入 IMemoryCache

### 現行行為

```csharp
if (session == null)
{
    var tempKey = $"NOSESSION_{Environment.MachineName}_{Thread.CurrentThread.ManagedThreadId}_{DateTime.UtcNow.Ticks}";
    return tempKey;   // 每次呼叫都不同 → 每次呼叫都新增一筆 30 分鐘無法回收的快取項
}
```

### 選定方案：把「有沒有 Session」變成呼叫端可判斷的訊號

1. 新增 `private bool TryGetSessionCacheKey(out string key)`，把現行 `GetCurrentSessionId()` 的
   完整鍵構建邏輯（`sessionId` + `boundUserId` + 短指紋 + 短時戳）移入，`session == null` 時
   回傳 `false` 並輸出 `null`。

2. 六個快取屬性（`ListManager`、`SmallGroupDataList`、`WeeklyReportData`、`NewPersonModel`、
   `PersonalInfomationModel`、`HappyGroupDataManager`）一律改為：

```csharp
if (!TryGetSessionCacheKey(out var key))
{
    // 無 Session（背景執行緒、非 HTTP 上下文、除錯評估）：
    // 回傳實例層級的後備物件，永不寫入行程級 IMemoryCache。
    return m_ListManager ??= new ListManager();
}
// ...既有的 IMemoryCache 路徑不變
```

   後備物件存放在既有的 `m_XXX` 欄位（`InMemoryDataContextSmallGroup` 為 Scoped，其生命週期
   隨 scope 結束而回收），因此不會有跨 request 殘留。

3. 保留 `GetCurrentSessionId()` 作為公開行為的相容包裝：`TryGetSessionCacheKey` 失敗時回傳
   一個**固定字串**（例如 `"NOSESSION"`），不再含 `Ticks`。若無其他呼叫端則直接刪除。

4. `WriteSessionDiagnostic` 在無 Session 分支維持輸出，訊息改為明確指出「已改用實例層級後備物件，
   未寫入行程快取」，讓下次 trace 一眼可辨。

### 為什麼不直接設定 SizeLimit

`Startup.cs:210` 的「不設定 `SizeLimit`」是明確的既有決策，且改設 `SizeLimit` 會要求**每一個**
`Set` 呼叫都提供 `Size`，否則執行期擲出例外——影響面遠大於本任務。本任務只堵住無界成長的來源，
`SizeLimit` 留待另案評估。

---

## F3：架構文件的 Faulted 不變量修正

`docs/architecture/dataverse-gateway-v1.md`「核心不變量」第 4 條，現行：

> 任一 timeout、取消或執行例外都會將 lease 標記 Faulted；Faulted client 只會 Dispose，不得回池供另一位使用者或另一個 request 重用。

改為（語意須與 `DataverseGateway.IsConnectionFault()` 一致）：

> 只有**傳輸層**故障會將 lease 標記 Faulted：WCF 通道例外、`TimeoutException`、`WebException`、
> `SocketException`、`IOException`。這類例外代表請求可能未送達或回應已損毀，通道狀態不可信。
> Faulted client 只會 Dispose，不得回池供另一位使用者或另一個 request 重用。
>
> **商業層 fault 不淘汰連線**：`FaultException`（含 `FaultException<OrganizationServiceFault>`）
> 代表伺服器已完整處理並回傳 SOAP fault——欄位不存在、權限不足、驗證規則失敗、伺服器端 plugin
> 載入失敗都屬此類。通道、token 與安全內容全部健康，淘汰它只會換來一次不必要的重新握手。
>
> **比對順序不可調換**：`FaultException` 在 WCF 型別階層中是 `CommunicationException` 的子類別。
> 若先比對 `CommunicationException`，所有商業 fault 都會被誤判為連線故障，這道判定即等同無效。
>
> **未知例外採「保留連線」**：應用程式自身的錯誤不代表通道損毀，且仍有兩道後備防線——出借前的
> WhoAmI 健康檢查，以及下一次操作必然再擲出的傳輸層例外。

同時更新「架構圖元件對照表」第 ⑦ 列的「已驗證的保護」欄位，並在文末新增一段實證：

> **2026-08-21 實測佐證**：一次真實重現產生 625 次 CRM 操作，其中 7 次以 `ok=false` 結束
> （2 次為伺服器端 `WeeklyReportPlugIn.dll` 載入失敗、5 次為 `new_start_tracking_date` 欄位
> 探測）。7 次全為 `FaultException`，對應的 7 條 lease 全部以 `state=healthy` 歸還，`faulted`
> 計數恆為 0，且後續操作繼續使用同一條連線並全部成功——證實商業層 fault 不會污染連線。

---

## F4：背景工作的獨立觀測邊界

### 現行機制與失效點

`DataverseTraceMiddleware` 在 request 邊界呼叫 `DataverseTrace.BeginRequest(...)`，建立
`RequestContext`（含 `TraceId`、`User`、`RequestStats`）寫入 `AsyncLocal<RequestContext>`。
`RequestScope.Dispose()` 在 HTTP 管線結束時讀出 `Stats` 並寫出 `request.end`。

`Task.Run` 捕獲 ExecutionContext，因此背景流程繼承同一個 `RequestContext` **參考**。後果：

1. 背景的 `crm.op` 掛著已結束 request 的 traceId。
2. 背景累加的 `RequestStats` 沒有任何人再讀——`request.end` 早在背景開工前就寫出去了。

### 選定方案：新增背景 scope API

在 `DataverseTrace` 新增（純字串介面，不觸碰 Host 型別）：

```csharp
/// <summary>
/// 為背景工作建立獨立的觀測範圍。背景工作繼承自 request 的 AsyncLocal 統計不再被沿用，
/// 改由本範圍自己累計並於結束時寫出，使 request.end 與背景結束事件的總和等於實際 CRM 次數。
/// </summary>
/// <param name="operationName">背景作業名稱，例如 "SaveIntegrate.Upload"；不得含使用者資料。</param>
public IDisposable BeginBackgroundOperation(string operationName)
```

行為：

| 步驟 | 動作 |
|---|---|
| 進入 | 讀取繼承而來的 `RequestContext`，取出 `TraceId` 作為 `parentTraceId`、`User` 沿用 |
| | 產生子 traceId：`{parentTraceId}#bg{Interlocked.Increment(ref _bgSeq)}` |
| | 建立**全新** `RequestStats`，寫入 `_requestContext.Value`（copy-on-write，不影響父流程） |
| | 寫出 `bg.begin`：`{ ev, ts, traceId, parentTraceId, op, user }` |
| 離開 | 寫出 `bg.end`：欄位與 `request.end` 完全一致，另加 `parentTraceId` 與 `op` |
| | 還原前一個 `RequestContext` |

`Enabled == false` 或無繼承 context 時回傳 `NoopScope.Instance`，與 `BeginRequest` 一致。

### 呼叫端

`SmallGroupController.Save.cs` 的 `Task.Run` lambda **最外層**（在 `_scopeFactory.CreateScope()`
之前）加上：

```csharp
using var traceScope = DataverseTrace.Current?.BeginBackgroundOperation("SaveIntegrate.Upload")
                       ?? (IDisposable)NullDisposable.Instance;
```

`DataverseTrace.Current` 為靜態 `AsyncLocal`，在背景流程中仍可取得（`RequestScope.Dispose`
還原的是請求流程自己的副本，不影響已分支出去的背景流程）。

### 不變量檢查

修改後重跑同一組操作，以下等式必須成立：

```
Σ request.end.crmCount + Σ bg.end.crmCount == count(crm.op)
```

分析器應新增此檢查；不成立即代表仍有孤兒背景工作未套用此 API。

### 為什麼不直接延後 request.end

延後 `request.end` 直到背景完成，會讓「使用者感知延遲」這個最重要的指標被 14 秒的背景工作污染，
使慢請求排名失去意義。request 與背景必須是兩個獨立的觀測單位。

---

## 相依與順序

- F3 純文件，與其他三項無相依，可先做。
- F4 需先於 F1 完成：F1 改動 `SaveIntegrate` 的 `Task.Run` 主體，F4 也改同一段，先做 F4 可避免衝突解決。
- F2 獨立，可平行。
- F1 影響面最大，最後做。

## 回滾

四項各自獨立成 commit。F1 若在整合測試暴露非預期行為，單獨 revert F1 的 commit 即可，
不影響 F2/F3/F4。
