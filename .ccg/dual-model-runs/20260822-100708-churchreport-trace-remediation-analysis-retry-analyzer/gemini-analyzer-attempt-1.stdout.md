# ChurchReport Trace  remediation — 設計一致性分析報告

本報告針對 `prd.md`、`design.md`、`implement.md` 以及目前原始碼樹進行架構與設計一致性審查，評估實作計畫中的潛在風險，並提供具體的優化建議。

---

## 1. UX Analysis (使用者體驗影響評估)

* **響應性提升**：`SmallGroupController.SaveIntegrate` 採用 Fire-and-Forget 模式，前端在發送請求後能立即收到 `{ status = "1", message = "資料已送出，正在處理中..." }` 的回應，無需在瀏覽器端等待長達 14 秒的 CRM 寫入與成員轉移操作，顯著提升了使用者的操作流暢度。
* **資料一致性風險**：由於背景非同步處理與前台讀取請求並行，若背景執行緒在就地修改快取資料（如移除已轉移成員）時缺乏隔離，使用者在並行瀏覽其他頁面（如 `IntegrateView` 或 `DataApi`）時，可能會看到暫時性缺失或損壞的成員列表，甚至遭遇系統錯誤。
* **系統穩定性**：若背景執行緒因無 Session 狀態導致記憶體洩漏（Memory Leak），將逐漸耗盡伺服器資源，最終導致服務中斷（OOM），嚴重影響所有使用者的體驗。

---

## 2. Design Evaluation (設計系統與模式評估)

* **追蹤模式一致性**：`DataverseTrace` 引入 `BeginBackgroundOperation` API，延續了現有 `BeginRequest` 的 `IDisposable` 模式。這使得背景工作的生命週期管理與主請求保持一致，便於開發人員理解與維護。
* **快取隔離模式**：`InMemoryDataContextSmallGroup` 作為 Scoped 服務，其職責應是管理單次請求/背景工作內的狀態。將 Scoped 變數與全域 `IMemoryCache` 混合使用時，必須有明確的 Fallback 機制，以確保在無 Session 脈絡下（如背景執行緒）仍能維持單一 Scope 內的資料一致性。

---

## 3. Technical Considerations (技術考量與審查發現)

### Critical Findings (關鍵缺陷)

#### F2: `InMemoryDataContextSmallGroup` 無 Session 時的快取洩漏與隔離失效
* **具體路徑**：`SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs` (第 218-233 行)
* **問題分析**：
  在背景執行緒中，`CurrentSession` 為 `null`。目前的 `GetCurrentSessionId()` 會回傳：
  ```csharp
  var tempKey = $"NOSESSION_{Environment.MachineName}_{Thread.CurrentThread.ManagedThreadId}_{DateTime.UtcNow.Ticks}";
  ```
  由於每次讀取屬性（如 `ListManager`、`SmallGroupDataList`）都會重新呼叫此方法，`Ticks` 的變動導致每次產生的 Key 都不同。這會引發兩個嚴重問題：
  1. **無法共享實例**：在同一次背景工作執行過程中，多次讀取 `ListManager` 會得到不同的全新實例，失去快取與狀態共享的作用。
  2. **記憶體洩漏**：每次讀取都會向 `IMemoryCache` 寫入一個永不被重複查詢的快取項，且 `Startup.cs:210` 未設定 `SizeLimit`，這將導致記憶體持續增長直至崩潰。
* **修復建議**：
  實作 `TryGetSessionCacheKey(out string key)`。若 `CurrentSession == null`，回傳 `false` 且 `key = null`。屬性 Getter 偵測到無 Session 時，應直接回退至 Scoped 實例的區域變數（例如 `m_ListManager ??= new ListManager()`），不寫入全域 `IMemoryCache`。

#### F1: `SmallGroupDataList` 背景就地修改與非原子性發布競態風險
* **具體路徑**：
  * `SpeechMessageProducts.ChurchReport\Controllers\SmallGroupController\SmallGroupController.Save.cs` (第 133-155 行)
  * `SpeechMessageProducts.ChurchReport\Models\SmallGroupDataList.cs`
* **問題分析**：
  背景執行緒直接對 `weeklyReportRef.m_SmallGroupDataList.m_SmallGroupData.Members` 執行 `RemoveTransferredMembers`。此列表是直接指向 `IMemoryCache` 中的共享實例。
  1. **並行讀寫衝突**：前台讀取執行緒（如 `SmallGroupController.DataApi.cs`）在無鎖狀態下遍歷該列表時，背景執行緒同時在進行 `Remove` 操作，將直接引發 `InvalidOperationException: Collection was modified`。
  2. **非原子性更新**：若採用 `Clear()` + `AddRange()` 方式更新列表，會造成列表短暫變空的空窗期，並行的讀取請求將讀取到空資料。
* **呼叫點統計**：
  經 Grep 檢索，`m_AllMemeberData.Members` 呼叫點達 25 處以上，`m_SmallGroupData.Members` 與 `m_NewPersonFollowUpData.Members` 亦有十餘處，全 repo 累計呼叫點**已超過 30 處**。若對所有呼叫點加上 `lock`，改動範圍過大且極易遺漏。
* **修復建議**：
  必須啟用**唯讀回退 (Read-Only Fallback)** 機制。背景執行緒在啟動時，先對 `SmallGroupDataList` 進行 `CreateIsolatedSnapshot()`（複製 List 容器，若 `Member` 屬性會被修改，則需對 `Member` 進行 `Clone()`）。背景操作僅在此隔離的快照上執行，完成後透過**原子性替換引用**（例如 `m_SmallGroupData.Members = newIsolatedList`）發布更新，前台讀取完全無鎖。

---

### Warning Findings (警告項目)

#### F4: `DataverseTrace` 跨執行緒 `AsyncLocal` 統計污染與 `request.end` 提前結束
* **具體路徑**：
  * `ToolUtility\Dataverse\DataverseTrace.cs`
  * `SpeechMessageProducts.ChurchReport\Middleware\DataverseTraceMiddleware.cs`
* **問題分析**：
  `Task.Run` 會繼承主執行緒的 `ExecutionContext`，使得背景執行緒共享同一個 `RequestContext` 與 `RequestStats`。
  當主執行緒的 HTTP 請求結束時，`DataverseTraceMiddleware` 會處置主 Scope 並寫入 `request.end` 日誌。此時背景執行緒的 CRM 操作（耗時約 14 秒）可能尚未開始或仍在執行，導致 `request.end` 記錄的 `crmCount` 為 0 或不完整。隨後背景執行緒寫入的 `crm.op` 日誌將失去對應的結束事件，破壞了日誌的完整性與可追溯性。
* **修復建議**：
  在背景執行緒啟動時，立即呼叫 `DataverseTrace.Current.BeginBackgroundOperation("SaveIntegrate.Upload")`。此 API 必須執行 Copy-on-Write，建立一個獨立的 `RequestStats`，並生成如 `{parentTraceId}#bg1` 的子 `TraceId` 寫入 `AsyncLocal`，最後在背景工作結束時寫入 `bg.end` 事件，確保統計數據不與主請求混淆。

#### F3: Gateway 連線檢測對 Business Fault 的誤判
* **具體路徑**：`docs/architecture/dataverse-gateway-v1.md` (設計合約)
* **問題分析**：
  合約中指出，若 `IsConnectionFault()` 將所有 `FaultException`（包含業務邏輯阻擋、Plugin 拋出的驗證錯誤等）皆判定為連線失效（Faulted），會導致連線池頻繁釋放並重建正常的 WCF Client。這會造成嚴重的效能懲罰與連線池震盪。
* **修復建議**：
  明確區分 `CommunicationException` / `TimeoutException`（判定為連線失效）與業務型 `FaultException`（如 `FaultException<OrganizationServiceFault>`，判定為連線健康，僅拋出業務異常）。

---

### Info Findings (一般資訊)

#### 編碼與繁體中文文檔規範
* **具體路徑**：所有新增/修改的 `.cs`、`.cshtml`、`.md` 檔案。
* **規範要求**：
  1. 檔案編碼必須為 **UTF-8 without BOM**。
  2. 換行字元必須為 **CRLF**。
  3. 所有新撰寫的架構說明與註解必須使用**繁體中文**。
  4. 執行 `python .trellis/scripts/check_encoding.py` 確保無編碼違規。

---

## 4. Options (替代方案評估)

### 方案 A：全域鎖定與就地修改 (In-place Locking)
* **作法**：在 `SmallGroupDataList` 內部引入全域鎖，並修改全 repo 超過 30 處的呼叫點，在所有讀取與寫入 `Members` 的地方皆加上 `lock (SyncRoot)`。
* **優點**：不佔用額外的記憶體來建立快照。
* **缺點**：改動範圍極大，極易遺漏導致 Thread-safety 破口；且背景執行緒執行 CRM 操作與成員移除時，會長時間佔用鎖，導致前台讀取請求嚴重阻塞。

### 方案 B：唯讀快照與原子發布 (Read-Only Snapshot & Atomic Publication) 【推薦】
* **作法**：背景工作啟動前，呼叫 `CreateIsolatedSnapshot()` 複製成員列表與需要修改的 `Member` 實例。背景工作在隔離的快照上執行 `RemoveTransferredMembers` 與上傳。完成後，在鎖定保護下原子性地替換 `Members` 引用。
* **優點**：前台讀取請求完全無鎖，效能極高；改動僅限於 `SaveIntegrate` 與 `SmallGroupDataList` 內部，風險極低。
* **缺點**：在背景處理期間會短暫消耗雙倍的列表記憶體（僅限於該次請求的成員數量，通常小於 100 筆，記憶體開銷極小）。

---

## 5. Recommendation (最終建議)

強烈推薦採用 **方案 B (唯讀快照與原子發布)**。

**決策支持理由**：
1. **高並發安全**：徹底消除了背景執行緒就地修改共享快取所引發的 `InvalidOperationException` 與資料不完整風險。
2. **改動範圍最小化**：無需修改全 repo 超過 30 處的讀取呼叫點，僅需在 `SmallGroupDataList` 內實作快照複製，並在 `SaveIntegrate` 背景工作中使用該快照，符合高內聚、低耦合的設計原則。
3. **效能優勢**：前台讀取操作無需等待背景 CRM 寫入鎖，維持了系統的高吞吐量。
