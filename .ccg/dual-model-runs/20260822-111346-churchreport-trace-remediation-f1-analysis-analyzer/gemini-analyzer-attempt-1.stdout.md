# ChurchReport F1 背景上傳狀態隔離：雙模型設計分析報告

本報告針對 `churchreport-trace-remediation-f1-analysis` 任務進行深度分析。在不修改任何檔案的前提下，評估目前 `SaveIntegrate` 背景任務與前景請求共用可變狀態的風險，並提出最小安全設計方案、完整列舉集合使用點、規劃測試優先順序，以及識別範圍外項目與潛在風險。

---

## 1. UX Analysis (使用者影響評估)

- **使用者體驗影響**：
  - **隨機崩潰與報錯**：目前 `SaveIntegrate` 採用 Fire-and-Forget 模式。當背景執行緒在執行 `RemoveTransferredMembers` 清理已轉移成員時，若使用者同時在前端重新整理頁面或切換小組，前景執行緒會列舉（Enumerate）同一個 `Members` 集合，這將直接引發 `InvalidOperationException`（Collection was modified; enumeration operation may not execute），導致前端畫面顯示錯誤或崩潰。
  - **資料顯示不一致**：背景清理與前景讀取並行時，使用者可能會在極短時間內看到成員列表處於「半清理」狀態，或者在清理完成後，前端快取未同步更新，導致使用者看到過時的資料，產生操作困惑。
- **使用者旅程影響**：
  - 使用者執行「儲存並整合（SaveIntegrate）」後，系統立即回應「資料已送出，正在背景處理中...」（響應時間約 3ms），隨後使用者繼續瀏覽其他頁面。若背景同步在 14 秒的執行過程中因競態條件崩潰，使用者將無法得知同步失敗，導致資料遺失。
- **行動端與桌面端體驗**：
  - 行動端網路延遲較高，使用者重複點擊或並行發送請求的機率較高，這會加劇背景競態條件的發生頻率。

---

## 2. Design Evaluation (設計系統與一致性評估)

- **快取模式一致性**：
  - 專案在 F2 中已修復了 `NOSESSION` 快取鍵無界成長的問題，確保了 Session 快取的生命週期與範圍。F1 的設計必須延續此隔離原則，確保背景任務所操作的資料完全侷限於該 Session 的快照中，避免跨使用者或跨請求的資料污染。
- **唯讀回退 (Read-Only Fallback) 模式**：
  - 由於前景讀取點極多（遍佈多個 Controller 與 View），若對所有讀取點加上 `lock`，將破壞現有的無鎖高效能讀取模式。因此，設計上應採用「背景使用隔離快照，完成後原子性替換引用」的模式，這與現代前端的 Immutable 狀態管理（如 Redux/Vuex）概念一致，能保證讀取端的高效能與安全性。

---

## 3. Technical Considerations (技術與架構考量)

### 3.1 目前資料流與共享可變狀態風險
在 `SmallGroupController.Save.cs` 中：
1. **前景捕獲**：`SaveIntegrate` 在主執行緒中捕獲了 `weeklyReportRef`（指向 Session 快取中的 `ListSmallGroupWeeklyReport` 實例）以及 `allMemberData`。
2. **背景執行**：`Task.Run` 啟動背景執行緒，直接呼叫 `weeklyReportRef.UploadIntegrateDataAsync`，並在執行完畢後呼叫 `RemoveTransferredMembers(smallGroupData.Members)` 與 `RemoveTransferredMembers(newPersonData.Members)`。
3. **共享可變狀態風險**：
   - `smallGroupData.Members` 與 `newPersonData.Members` 是直接儲存在 `IMemoryCache` 中的 `List<Member>` 實例。
   - 背景執行緒在無鎖保護下，直接對這些 List 進行 `RemoveAt` 操作。
   - 前景執行緒（如 `SmallGroupController.DataApi.cs` 或 `NewPersonController.cs`）隨時可能並行讀取、遍歷或修改這些 List，引發嚴重的執行期異常與資料競爭。

### 3.2 最小安全設計方案
為確保背景上傳狀態隔離，提出以下最小安全設計：

```
[前景請求執行緒]
  │
  ├── 1. 獲取 SyncRoot 鎖
  ├── 2. 呼叫 CreateBackgroundUploadCopy() 建立快照
  │      ├── 複製 ListSmallGroupWeeklyReport 結構
  │      └── 呼叫 CreateIsolatedSnapshot() 複製 Members 容器與 Clone() Member 實例
  ├── 3. 釋放 SyncRoot 鎖
  │
  ├── 4. 啟動 Task.Run(背景執行緒) ───> [背景執行緒 (使用隔離快照)]
  │                                       │
  └── 5. 立即回傳 JSON 回應 (3ms)         ├── 1. 執行 UploadIntegrateDataAsync
                                          ├── 2. 執行 RemoveTransferredMembers (僅修改快照)
                                          │
                                          └── 3. 獲取 SyncRoot 鎖，將清理後的 Members 
                                                 以「原子性替換引用」回寫至共用快取
```

- **檔案邊界與修改範圍**：
  - `Member.cs`：
    - 實作 `Clone()` 方法，對 `Member` 進行深拷貝（複製 `AssignedGroup`、`FollowUpNextStep` 等所有可變欄位）。
  - `SmallGroupDataList.cs`：
    - 新增 `private readonly object _syncRoot = new();` 與 `internal object SyncRoot => _syncRoot;`。
    - 新增 `CreateIsolatedSnapshot()` 方法，在 `lock (_syncRoot)` 保護下，建立新的 `List<Member>`，並對其中的每個 `Member` 呼叫 `Clone()`。
  - `ListSmallGroupWeeklyReport.cs`：
    - 新增 `CreateBackgroundUploadCopy()` 方法，建立一個新的 `ListSmallGroupWeeklyReport` 實例，並將其 `m_SmallGroupDataList` 設為 `m_SmallGroupDataList.CreateIsolatedSnapshot()` 的結果，其餘唯讀屬性直接複製。
  - `SmallGroupController.Save.cs`：
    - 在 `Task.Run` 啟動前，呼叫 `weeklyReportRef.CreateBackgroundUploadCopy()` 取得 `backgroundCopy`。
    - 背景執行緒完全使用 `backgroundCopy` 進行上傳與清理。
    - 清理完成後，在 `lock (weeklyReportRef.m_SmallGroupDataList.SyncRoot)` 保護下，將清理後的 `Members` 列表**原子性替換**回共用快取（例如 `sharedData.Members = newIsolatedList;`），絕不使用 `Clear() + AddRange()`。
- **取消／例外／Dispose 處理**：
  - 背景任務使用 `CancellationToken.None`，確保 HTTP 請求取消時背景上傳不中斷。
  - 背景任務的 `Task.Run` 內部必須有完整的 `try-catch`，並使用 `DataverseTrace.Current?.BeginBackgroundOperation("SaveIntegrate.Upload")` 包裹，確保異常被記錄且資源（如 `IServiceScope`）在 `finally` 中被正確 `Dispose`。

### 3.3 全 Repo Members 集合使用點列舉與缺口
經 Grep 檢索，三組 `Members` 集合在全 repo 中的主要使用點如下：

#### 1. `m_SmallGroupData.Members` (共 6 處主要使用點)
- **背景清理與寫入**：
  - `SmallGroupController.Save.cs` (第 150, 267 行)：背景清理呼叫點。
  - `SmallGroupDataList.cs` (第 164, 206 行)：新增成員。
- **前景讀取與維護**：
  - `SmallGroupController.DataApi.cs` (第 124 行)：讀取成員列表以供 API 回傳。
  - `SmallGroupDataList.cs` (第 174 行)：取得成員數量。

#### 2. `m_NewPersonFollowUpData.Members` (共 11 處主要使用點)
- **背景清理與寫入**：
  - `SmallGroupController.Save.cs` (第 157, 273 行)：背景清理呼叫點。
  - `SmallGroupDataList.cs` (第 212 行)：新增成員。
- **前景讀取與維護**：
  - `NewPersonController.cs` (第 119 行)：讀取成員列表。
  - `NewPersonController.cs` (第 156, 212, 239 行)：新增、更新與刪除成員。
  - `DownloadIntegrateData.Setup.cs` (第 137, 143, 294 行)：排序、清理與初始化。

#### 3. `m_AllMemeberData.Members` (共 28 處主要使用點)
- **前景讀取與維護**：
  - `PersonalController.cs` (第 179, 406, 408, 455 行)：讀取成員列表。
  - `PersonalController.cs` (第 701, 722, 742, 771, 795, 830 行)：成員 CRUD 操作。
  - `NewPersonController.cs` (第 221 行)：更新成員。
  - `EquipmentController.cs` (第 231, 251, 335 行)：讀取成員列表。
  - `SmallGroupDataList.cs` (第 60, 167, 215 行)：初始化與新增成員。
  - `DownloadIntegrateData.Setup.cs` (第 135, 141, 273, 315 行)：排序、清理與遍歷。
  - `DownloadIntegrateData.Members.cs` (第 69, 301, 351, 470, 654 行)：初始化與新增成員。
  - `ListSmallGroupWeeklyReport.cs` (第 158, 161, 164-171, 378, 380-385 行)：讀取與更新成員屬性。
  - `ListManager.cs` (第 582 行)：遍歷成員。

> **Grep 缺口說明**：
> 1. **動態/間接參考**：若程式碼中將 `m_SmallGroupData` 傳遞給其他方法，並在該方法內部以 `data.Members` 形式存取，靜態 Grep 將無法直接匹配。
> 2. **歷史備份檔案**：`HomeController-南崁長老教會.md` 等 Markdown 檔案中包含大量歷史備份代碼，這些代碼雖然不參與編譯，但反映了過去的業務邏輯，已排除在編譯範圍外。

---

## 4. Options (替代方案與權衡)

### 方案 A：前景與背景讀寫皆加鎖 (Full Locking)
- **作法**：在所有讀取與寫入 `Members` 的地方都加上 `lock (SyncRoot)`。
- **優點**：概念簡單，能保證絕對的執行緒安全。
- **缺點**：修改範圍極大（全 repo 超過 40 處呼叫點），極易遺漏；且會導致前景讀取效能下降，增加死鎖（Deadlock）風險。

### 方案 B：背景使用隔離快照 + 原子性替換 (Snapshot & Atomic Write-back)
- **作法**：背景任務啟動前複製快照，背景僅操作快照，完成後以 `lock` 保護並進行原子性替換。
- **優點**：前景讀取端完全無鎖，效能極高；改動範圍侷限於 `SaveIntegrate` 與 `SmallGroupDataList` 內部，風險極低。
- **缺點**：在背景執行期間，前景若有新寫入，回寫時需小心合併，否則可能覆蓋前景的新資料。

### 方案 C：唯讀快照 + 前端重新整理 (Snapshot & Refresh Signal)
- **作法**：背景使用快照上傳與清理，**完全不回寫**共用快取，並在 `SaveIntegrate` 回應中標記需重新整理，強制前端重新發送請求從 CRM 載入最新資料。
- **優點**：徹底消除寫入側競態，背景與前景完全解耦。
- **缺點**：前端需要配合修改，且會增加一次 CRM 載入的開銷。

---

## 5. Recommendation (推薦方案與理由)

**推薦採用【方案 B：背景使用隔離快照 + 原子性替換】**。

### 理由：
1. **影響範圍最小化**：無需修改全 repo 超過 40 處的讀取呼叫點，僅需在 `SmallGroupDataList` 內實作快照複製，並在 `SaveIntegrate` 背景工作中使用該快照，符合高內聚、低耦合的設計原則。
2. **效能保留**：前景讀取請求完全無鎖，維持系統的高吞吐量與極速響應。
3. **安全性高**：透過 `SyncRoot` 鎖保護快照建立與回寫過程，配合 `Member.Clone()` 深拷貝，徹底消除 `InvalidOperationException` 與資料競爭。

---

## 6. 測試優先順序與回歸測試規劃

### 優先順序 1：並行讀寫競態測試 (Concurrent Read/Write Race Test)
- **測試目標**：驗證背景清理時，前景並行讀取不會崩潰。
- **測試方法**：啟動背景 `SaveIntegrate` 模擬長達 2 秒的清理過程，同時啟動 10 個執行緒並行讀取 `m_SmallGroupData.Members`。
- **預期結果**：無任何 `InvalidOperationException` 拋出，且前景讀取到的資料完整。

### 優先順序 2：Session 隔離與洩漏測試 (Session Isolation & Leakage Test)
- **測試目標**：驗證不同使用者的背景任務不會互相干擾。
- **測試方法**：模擬兩個不同的 SessionId，同時發送 `SaveIntegrate` 請求。
- **預期結果**：User A 的成員清理不會影響 User B 的成員列表。

### 優先順序 3：生命週期與資源釋放測試 (Lifecycle & Resource Release Test)
- **測試目標**：驗證背景任務結束後，資源被正確釋放。
- **測試方法**：監控背景任務執行前後的 `IServiceScope` 與 `DataverseTrace` 實例數量。
- **預期結果**：任務結束後，所有 Scoped 服務與 Trace 範圍皆被正確 Dispose，無記憶體與連線洩漏。

---

## 7. 範圍外項目與 Critical/Warning 風險提示

### 範圍外項目 (不可修改)
- **背景服務架構**：不可將 `SaveIntegrate` 重構為 Hangfire 或 ASP.NET Core `BackgroundService` 等佇列式架構。
- **已提交之 F2/F3/F4 代碼**：不可回退或修改 `3bf57fce` (F2) 及 F4 的 `BeginBackgroundOperation` 實作。

### Critical 風險
- **原子性替換 vs Clear()+AddRange()**：
  - **風險分類：Critical**
  - **說明**：若在回寫共用快取時使用 `Clear()` 接著 `AddRange()`，在多執行緒環境下，前景讀取執行緒極有可能在 `Clear()` 之後、`AddRange()` 之前讀取到空集合，導致 UI 顯示空白。必須使用**原子性替換引用**（如 `m_SmallGroupData.Members = newIsolatedList;`）來發布更新。
- **Member 屬性修改的深拷貝**：
  - **風險分類：Critical**
  - **說明**：如果僅複製 `List<Member>` 容器，而未對 `Member` 實例進行 `Clone()`，背景任務修改 `Member` 的屬性（如 `AssignedGroup`）仍會直接影響前景的 `Member` 實例，導致資料競爭。因此，`Member` 必須實作 `Clone()` 進行深拷貝。

### Warning 風險
- **記憶體增長**：背景 `Task.Run` 捕獲了 `weeklyReportRef`，如果該物件持有大對象或未釋放的資源，可能會延長其生命週期，導致記憶體增長。
- **憑證安全**：在 `SaveIntegrate` 中捕獲了 `password`，雖然是為了背景上傳，但應避免在日誌或 Trace 中洩漏此敏感資訊。
