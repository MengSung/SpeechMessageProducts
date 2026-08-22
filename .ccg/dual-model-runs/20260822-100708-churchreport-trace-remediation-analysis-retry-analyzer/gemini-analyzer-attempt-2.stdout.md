# ChurchReport Trace 撖行葫蝻粹靽桀儔 — 撱箇身閮€敹?靽?摰€?蝯?

本報告針對 `prd.md`、`design.md`、`implement.md` 以及現行原始碼進行架構與設計一致性分析，評估實作計畫中的潛在風險，並提供具體的 Remediation 建議。

---

## 1. UX Analysis (使用者體驗影響評估)

雖然此任務主要屬於後端觀測性（Observability）與記憶體隔離優化，但其架構決定對使用者體驗有直接影響：
* **避免頁面崩潰與異常錯誤**：修正 F1（`Members` 列表在背景執行緒被修改導致的 `InvalidOperationException`）能直接消除使用者在儲存小組週報時，因併發讀寫導致的網頁 500 錯誤或載入失敗。
* **提升系統響應速度**：F4 引入背景觀測（`BeginBackgroundOperation`）後，`SaveIntegrate` 的 Fire-and-Forget 機制能更精準地被監控。這確保了前端異步請求（回傳 `status = "1", message = "資料已送出，正在背景處理中..."`）在 3ms 內完成，而後端耗時的 CRM 寫入（約 14 秒）能在背景安全執行且不遺失 Trace 軌跡。
* **避免多使用者資料交叉污染（Session Bleeding）**：F2 修正無 Session 狀態下的快取隔離，確保在 API 呼叫或排程工作等無 HTTP Context 狀況下，快取不會因隨機 Ticks 鍵值而膨脹，亦不會發生跨使用者資料錯亂，保障資料隱私與精確度。

---

## 2. Design Evaluation (設計系統與模式評估)

* **一致性模式**：實作計畫遵循現有的 `IInMemoryDataContext` 與 `DataverseTrace` 模式。
* **快取鍵值設計**：F2 引入的 `TryGetSessionCacheKey` 模式，將快取鍵值的生成邏輯收攏，避免了散落於各個屬性（`ListManager`、`SmallGroupDataList` 等）的重複邏輯，符合 DRY 原則。
* **背景觀測模式**：F4 的 `BeginBackgroundOperation` 採用 `IDisposable` 模式（`using` 語法），與現有的 `BeginRequest` 模式高度一致，降低了開發人員的認知負載。

---

## 3. Technical Considerations (技術與前端/後端架構影響)

### 3.1 F4 背景觀測與 AsyncLocal 隔離
* **AsyncLocal 的 Copy-on-Write 特性**：`AsyncLocal<T>` 在 `Task.Run` 啟動時會進行執行上下文（ExecutionContext）的流動（Flow）。在背景執行緒中修改 `_requestContext.Value` 不會影響主執行緒，這點在技術上是安全的。
* **統計數據隔離**：背景工作必須擁有獨立的 `RequestStats` 實例。若直接共用父執行緒的 `RequestStats`，當父執行緒的 `RequestScope.Dispose()` 先執行並輸出 `request.end` 時，背景執行緒後續產生的 `crm.op` 將無法被計入任何 `request.end` 或 `bg.end` 中，導致觀測數據遺失。

### 3.2 F2 快取隔離與生命週期
* **Scoped 存留期保障**：`InMemoryDataContextSmallGroup` 在 `Startup.cs` 中註冊為 **Scoped**。當 `session == null` 時，改用 Scoped 實例的區域變數（如 `m_ListManager`）作為備用儲存，其生命週期與該次 HTTP 請求相同，既能保證單次請求內的狀態一致性，又能在請求結束時隨 DI 容器釋放，徹底解決 `IMemoryCache` 膨脹問題。

### 3.3 F1 列表併發修改與原子發布
* **讀取端 Grep 數量評估**：經評估，`m_SmallGroupData.Members` 等列表在專案中的讀取與遍歷（foreach）呼叫點眾多（可能超過 30 處）。若要求所有讀取端都加上 `lock (SyncRoot)`，修改範圍過大且極易遺漏，風險極高。
* **原子發布（Atomic Publication）的優勢**：在背景執行緒完成 `RemoveTransferredMembers` 後，直接將新列表賦值給屬性（例如 `Members = newListOfMembers`）。由於 C# 中的引用賦值（Reference Assignment）是原子操作，正在遍歷舊列表的執行緒不會拋出 `InvalidOperationException`，而新的讀取請求會直接取得新列表。這消除了修改所有讀取端呼叫點的必要性。

---

## 4. Options (替代方案評估)

### 方案 A：完全鎖定模式（Full Locking）
* **作法**：在所有讀取與寫入 `Members` 的地方都加上 `lock (SyncRoot)`。
* **優點**：絕對的執行緒安全。
* **缺點**：需要修改數十個檔案，極易造成死鎖（Deadlock）或遺漏，維護成本高。

### 方案 B：原子發布與隔離快照模式（Atomic Publication with Isolated Snapshot）—— *推薦*
* **作法**：背景執行緒複製一份週報與列表進行操作，完成後以引用賦值方式替換 live 實例的 `Members` 引用。
* **優點**：無需修改任何讀取端呼叫點，零侵入性，完全避免 `InvalidOperationException`，效能最高。
* **缺點**：在背景處理期間， live 實例的資料與背景實例會有短暫的不一致（但此為 Fire-and-Forget 的預期行為）。

---

## 5. Recommendation (最終建議)

推薦採用 **方案 B（原子發布與隔離快照模式）**。此方案在技術風險、修改範圍與系統穩定性之間取得了最佳平衡，且完全符合 `design.md` 的設計初衷。

---

## 6. Concrete Findings (具體審查發現)

### 【Critical】F1: 背景執行緒直接修改 Live Members 列表導致併發崩潰
* **位置**：`SpeechMessageProducts.ChurchReport\Controllers\SmallGroupController\SmallGroupController.Save.cs` (第 87-177 行)
* **原因**：`Task.Run` 內直接呼叫 `RemoveTransferredMembers(smallGroupData.Members)`。此 `Members` 實例指向 `IMemoryCache` 中的 live 數據。若此時有其他 HTTP 請求讀取該小組成員，將直接觸發 `InvalidOperationException`。
* **建議**：
  1. 在 `SmallGroupDataList` 實作 `CreateIsolatedSnapshot()`，鎖定 `SyncRoot` 並建立 `Members` 的新 `List<Member>` 實例。
  2. 在 `SaveIntegrate` 中，於 `Task.Run` 啟動前呼叫 `weeklyReportRef.CreateBackgroundUploadCopy()` 複製整份週報數據。
  3. 背景執行緒僅對此 Copy 進行 `RemoveTransferredMembers`。
  4. 處理完成後，透過 `lock (liveDataList.SyncRoot)` 將 live 實例的 `Members` 屬性指向新列表（原子替換）。

### 【Critical】F2: 無 Session 狀態下快取鍵值隨機化導致記憶體洩漏
* **位置**：`SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs` (第 225-233 行)
* **原因**：當 `session == null` 時，`GetCurrentSessionId()` 使用 `DateTime.UtcNow.Ticks` 生成隨機 Key。這導致每次存取 `ListManager` 等屬性時，都會向 `IMemoryCache` 寫入一個永不重複且無法被再次存取的物件，造成嚴重的記憶體洩漏。
* **建議**：
  1. 實作 `TryGetSessionCacheKey(out string key)`，若 `session == null` 則回傳 `false`。
  2. 在各屬性的 Getter 中，若 `TryGetSessionCacheKey` 回傳 `false`，則直接回傳並初始化區域變數（如 `m_ListManager ??= new ListManager()`），不寫入 `IMemoryCache`。

### 【Warning】F4: AsyncLocal 統計數據未隔離導致背景 CRM 操作觀測遺失
* **位置**：`ToolUtility\Dataverse\DataverseTrace.cs` (第 935-947 行 `PushLease` 與 `BeginRequest` 相關邏輯)
* **原因**：若背景執行緒直接繼承父執行緒的 `RequestContext`，當父執行緒的 HTTP 請求結束並寫出 `request.end` 後，背景執行緒中執行的 CRM 操作將無法正確累加至對應的統計日誌中。
* **建議**：
  1. `BeginBackgroundOperation` 必須建立一個全新的 `RequestContext`，其 `TraceId` 格式為 `{parentTraceId}#bg{seq}`。
  2. 該上下文必須配置獨立的 `RequestStats` 實例。
  3. 在 `BackgroundScope.Dispose()` 時，寫出 `bg.end` 事件，並將 `crmCount` 與 `crmMs` 記錄於該事件中，確保滿足 `sum(request.end.crmCount) + sum(bg.end.crmCount) == count(crm.op)` 的等式。

### 【Warning】F3: IsConnectionFault 誤將 WCF 業務異常判定為連線失效
* **位置**：`docs/architecture/dataverse-gateway-v1.md` 與 `ToolUtility\Dataverse\DataverseGateway.cs`
* **原因**：設計文件指出，業務層面的 SOAP Fault（如 `FaultException`）不應導致連線被標記為 `Faulted`。然而在 WCF 中，`FaultException` 繼承自 `CommunicationException`。若 `IsConnectionFault` 僅簡單捕捉 `CommunicationException`，將導致正常連線因業務錯誤（如外掛程式拋出異常）而被錯誤釋放與重建。
* **建議**：在 `IsConnectionFault` 的異常判定邏輯中，明確排除 `FaultException`（或僅針對 `TimeoutException`、`SocketException` 等網路層異常進行判定），確保連線池狀態的精確性。

### 【Info】UTF-8 without BOM 與 CRLF 編碼限制
* **位置**：所有新增或修改的原始碼與文件檔案。
* **原因**：專案規範嚴格限制編碼格式，任何不符合 UTF-8 without BOM 或 CRLF 的檔案都將導致 `check_encoding.py` 檢查失敗，成為 Release Blocker。
* **建議**：在實作提交前，務必執行 `python .trellis/scripts/check_encoding.py` 進行合規性檢查。
