# 小組回報重複姓名問題（Fix Duplicate Member Names）架構與設計審查報告

## 執行摘要

本報告針對 `SpeechMessageProducts.ChurchReport` 分支中「小組回報重複姓名」問題及其擬採修正設計進行系統性的 ASP.NET Core / .NET 10 架構、效能、併發與記憶體洩漏審查。

**關鍵結論**：擬採設計的總體方向（Session 隔離 Semaphore 入口、Atomic Publish/Build-then-Swap、Detached Read Snapshot、不以姓名 Distinct 掩蓋問題、LINE 登入改為順序執行）正確且符合防禦性架構規範。但 **在同步 CRM I/O 併發門控（Gate）、Scope Key 完整性核對、與 Semaphore/Snapshot 生命週期** 上，仍存在若干需調整之 Critical 與 Warning 風險，必須在實作前釐清與補強。

---

## 1. UX Analysis (使用者體驗影響評估)

### 1.1 使用者歷程與體驗衝擊 (User Journey)
* **資料一致性與信心維護**：既有系統因並行 Request 競爭，導回 UI 呈現重複人員（例如同一個人出現兩列以上），小組長在點擊出席勾選或填寫回報時易產生迷惘，甚至因重複勾選導致 CRM 側被建立重複記錄。修復後，UI 將精確 1:1 對映 CRM 出席記錄。
* **合法同名人員體驗**：教會環境中極易出現同名同姓會員（例如兩位「張偉」）。**堅決不採用姓名 Distinct 遮蔽**，能確保合法同名人員的資料與出席記錄均能獨立呈現與操作，維護使用者維護個人資料的權益。

### 1.2 併發請求回應時間變化 (Performance & Latency Perception)
* **LINE 登入與頁面初始化**：將 `Task.Run` + `Task.WhenAll` 的平行操作移除，改為單執行緒依順序執行後，LINE 登入階段的耗時可能有些微累積（+100ms ~ +300ms）。然而，這避免了先前因並行向 CRM 查詢 PresentRecord 失敗而「誤判無資料並建立重複記錄」的嚴重大瑕疵，使用者不會再面臨資料混亂的後果，此體驗權衡完全合理。

---

## 2. Design Evaluation (設計評估與一致性)

| 設計維度 | 現狀 (As-Is) 瑕疵 | 擬採設計 (To-Be) 評估 | 評估結果與對齊建議 |
| :--- | :--- | :--- | :--- |
| **載入鎖門控 (Gate)** | 直接修改共享 `m_ListSmallGroupWeeklyReport`；過早標示 `LoadFlag=true` | `ListManager` 實例持有 `SemaphoreSlim`，以 Build-then-Swap 模式原子發布 | **對齊良好**。徹底消除 Partial Load 與多執行緒交錯讀寫風險。 |
| **列識別碼 (Row Key)** | DevExtreme DataGrid 依 `PresentRecordId` 作為 Key，但資料源包含重複 key 時 UI 會崩潰或勾選連動 | Exact Duplicate `PresentRecordId` 直接 Fail Closed 拋錯，保留同名不同 Key 人員 | **完全正確**。禁止按 FullName Distinct 掩掩飾問題，展現合規防禦思維。 |
| **讀取隔離 (Read Isolation)** | API (如 `DataApi`, `Crud`) 直接傳遞內部可變 List 給 `DataSourceLoader` | 取得深複製 (Deep Copy) Detached Snapshot 後才交付 `DataSourceLoader` | **對齊良好**。確保 DevExtreme 進行排序/篩選時，不會因背景更新或並行修改導致 `InvalidOperationException`。 |

---

## 3. Technical Considerations (技術與架構審查)

### 🚨 Critical Findings (嚴重風險)

#### 1. Semaphore 門控內執行「同步 CRM Network I/O」引發 ThreadPool Starvation 與 Cancel 洩漏
* **位置**：`ListManager.SetupIntegrateData` 呼叫 `m_DownloadIntegrateData.SetupIntegrateData`
* **原因分析**：D365 CRM API (WebServiceConnector / OrganizationService) 為同步網絡 call (`RetrieveMultiple` / `Execute`)。若在 `SemaphoreSlim.Wait()` / `WaitAsync()` 的門控鎖內執行長達 500ms~2000ms 的同步 CRM 查詢，一旦同 Session 出現多個 AJAX 並列請求（例如 Grid + Chart + FollowUp），後續請求將在 Semaphore 内等待。若使用者重新整理或中途取消 Request，沒有傳遞 `CancellationToken` 的 `SemaphoreSlim.WaitAsync()` 與 CRM I/O 將會 **繼續佔用背景執行緒與 Semaphore**，導致 ThreadPool 飢餓。
* **修復要求**：
  1. Semaphore 必須配合 `CancellationToken` (傳遞 `HttpContext.RequestAborted`)，當 Client 斷線時能迅速退出等待。
  2. 鎖內只進行「門控判定與快照交換」，避免在 Semaphore 中長時間等待遠端 CRM 網絡通訊；若必須在鎖內載入，必須嚴格限制 Timeout（如 `TimeSpan.FromSeconds(10)`）。

#### 2. LINE 登入改為順序執行時，共用 `InMemoryContext` 欄位遺留與跨步驟污染
* **位置**：`SmallGroupController.LineLogin.cs`
* **原因分析**：雖然擬採設計移除了 `Task.Run` + `Task.WhenAll` 的並行執行，解決了多 Task 同時存取同一 `InMemoryContext` 的 Race Condition。然而，順序執行的多個方法若中間遭遇 Exception (如 CRM 連線中斷)，`InMemoryContext` 中的部分屬性已被寫入舊資料或中間狀態，可能導致該 Session 殘留半完成的 Session State。
* **修復要求**：順序執行邏輯必須置於 `try...catch` 區塊，若順序執行中途失敗，必須明確重置或清空當前 Request 產生的臨時狀態（Fail Closed & Cleanup），不可讓污染狀態留存在 Session Cache 中。

---

### ⚠️ Warning Findings (警告風險)

#### 1. Scope Key 完整性比對漏洞 (Stale Cache Key Validation)
* **位置**：`ListManager.cs` / `SetupIntegrateData` 重新檢查 gate
* **原因分析**：擬採設計第 2 點提及「gate 內依完整載入 key 重新檢查」。若 Key 僅包含 `ListEntityId`，而忽略了 `m_SelectDate`（日期）、`m_Account`（帳號）、`LoginType`（角色種類），當使用者在前端切換日期或小組時，Gate 重新檢查可能錯誤命中上一次載入的舊日期 candidate，發布了過期的 `ListSmallGroupWeeklyReport`。
* **修復要求**：完整 Scope Key 必須為 Immutable Struct/Record，包含 `(ListEntityId, SelectDate, Account, LoginType)` 4 個維度。在 Semaphore 門控內部重新檢查時，必須 100% 比對此 4 維 Key，否則必須強制重新向 CRM 載入。

#### 2. `SemaphoreSlim` 實例生命週期與記憶體洩漏風險
* **位置**：`ListManager.cs` 的 `SemaphoreSlim` 欄位
* **原因分析**：`SemaphoreSlim` 內部包含 Kernel Event Handle。當 Session 到期被 `IMemoryCache` 剔除時，若 `ListManager` 樹狀結構沒有實作 `IDisposable` 且未調用 `m_Semaphore.Dispose()`，會造成 Unmanaged Handle 累積。
* **修復要求**：`ListManager` 應實作 `IDisposable` 介面，當其被 Session 快取清理或銷毀時，主動釋放 `SemaphoreSlim`。

#### 3. 失敗後重試與 Sticky Error (失敗狀態留存) 處理
* **位置**：`DownloadIntegrateData` 載入失敗或 `PresentRecordId` Exact Duplicate 檢測失敗
* **原因分析**：若在 Gate 內建立 Candidate 時發生 D365 連線異常或檢測到重複 Key 拋出 Exception，若處置不當，可能會：
  * (A) 將半完成的 candidate 誤發布到共享欄位；
  * (B) 或者是 Semaphore 沒有在 `finally` 區塊中 `Release()`，導致該 `ListManager` 永久死鎖。
* **修復要求**：必須採用嚴格的 `try { ... } finally { _semaphore.Release(); }` Pattern，且只有在 Candidate **完全載入成功且 validation 100% 通過** 後，才執行 `m_ListSmallGroupWeeklyReport = candidate` 的原子賦值。失敗時 Candidate 直接廢棄，原快照保持不變。

#### 4. 深複製 (Deep Copy Snapshot) GC 記憶體分配負擔
* **位置**：`DataApi` / `Crud` 等控制器在呼叫 `DataSourceLoader.Load` 之前
* **原因分析**：若小組成員數量較多，每次 UI 請求（例如 DevExtreme DataGrid 的 Paging/Filter/Sort API）都對整份 `ListSmallGroupWeeklyReport` 進行全物件深複製 (Deep Copy)，將會在垃圾回收器 (GC) Gen 0/Gen 1 產生大量短生命週期物件分配。
* **修復要求**：深複製時應僅複製 UI DataGrid 必需的 `Member` 集合與欄位（或使用 Immutable List / Read-Only Copy），避免將無關的歷史紀錄、過鉅字串進行無謂複製。

---

### ℹ️ Info Findings (資訊與最佳實踐)

#### 1. 徹底杜絕 Distinct-by-Name 掩蓋問題
* **評估**：既有問題原因出在 CRM 建立/讀取邏輯及並行寫入，若在後端以 `.DistinctBy(m => m.FullName)` 掩蓋，會直接遮蔽 CRM 資料重複的根因，且會造成兩名同名同姓的真實會友中有一人「永遠被系統吃掉」。擬採設計 4 採用 Exact Duplicate `PresentRecordId` Fail Closed、同名不同 Key 全部保留，完全符合資料嚴謹度要求。

#### 2. 自動化測試缺口 (Test Coverage Gaps)
* **現有測試缺口**：
  1. **並行請求撞門測試 (Single-flight Concurrency Test)**：缺乏 10 個 Task 同時呼叫 `SetupIntegrateData` 時，驗證 CRM 實際底層 connector 僅被呼叫 1 次且最終資料一致的測試。
  2. **同名同姓 vs 重複 Key 測試**：缺乏測試資料中存在「同名同姓但不同 Key（應通過）」與「完全相同 Key（應 Fail Closed 拋錯）」的單元測試。
  3. **Scope Key 變更測試**：切換日期 `m_SelectDate` 後存取，驗證 Gate 重新檢查是否能識別 Key 已不相同並重新載入。

---

## 4. Options (替代方案與權衡)

| 方案選項 | 優點 | 缺點 / 權衡 | 建議與結論 |
| :--- | :--- | :--- | :--- |
| **Option A: 擬採設計 (Session Lock + Build-Then-Swap + Fail Closed)** | 衝擊最小，完全相容現有 Session Cache 架構，不改變 Controller 呼叫方式。 | 仍需仔細維護 `ListManager` 內的 Semaphore 生命週期與 Scope Key 比對。 | **首選方案**。符合防禦性重構原則。 |
| **Option B: 引入 Lazy Cache / AsyncKeyedLock 全域單例** | 完全免除 `ListManager` 內手動管理 Semaphore 的複雜度。 | 需要變更 `InMemoryDataContext` 或引入額外的 NuGet 封裝，侵入性較高。 | **暫不採用**。專案目前約束儘量降低架構變動。 |
| **Option C: 前端/後端按 FullName 進行 Distinct 遮蔽** | 程式碼修改量最少。 | **嚴禁採用**！嚴重毀損資料正確性，無法區分真實同名人員，掩蓋 CRM 產生重複記錄的 BUG。 | **拒絕採用**。 |

---

## 5. Recommendation (綜合建議與決策報告)

### 建議架構與實作規範 (Architectural Blueprint)

1. **Semaphore 門控實作範本**：
   `ListManager` 必須採用安全的 **Build-then-Swap** 與 **Immutable Scope Key** 機制：
   ```csharp
   private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);

   public async Task<ListSmallGroupWeeklyReport> GetOrLoadIntegrateDataAsync(
       string listId, DateTime selectDate, string account, string loginType, CancellationToken ct)
   {
       var targetKey = new LoadScopeKey(listId, selectDate, account, loginType);
       
       // 1. Fast path: Read without lock if snapshot is valid and key matches
       var currentReport = this.m_ListSmallGroupWeeklyReport;
       if (currentReport != null && currentReport.LoadFlag && currentReport.ScopeKey == targetKey)
       {
           return currentReport;
       }

       // 2. Slow path: Acquire lock with CancellationToken
       await _loadLock.WaitAsync(ct);
       try
       {
           // Double check inside gate
           currentReport = this.m_ListSmallGroupWeeklyReport;
           if (currentReport != null && currentReport.LoadFlag && currentReport.ScopeKey == targetKey)
           {
               return currentReport;
           }

           // Build in local candidate
           var loader = new DownloadIntegrateData();
           var candidate = new ListSmallGroupWeeklyReport { ScopeKey = targetKey };
           
           loader.SetupIntegrateData(account, password, loginType, selectDate, listId, ..., candidate);

           // 3. Validation: Exact duplicate PresentRecordId fail closed
           ValidateRowKeysUnique(candidate.m_SmallGroupDataList.m_AllMemeberData);

           candidate.LoadFlag = true;
           
           // 4. Atomic publish
           this.m_ListSmallGroupWeeklyReport = candidate;
           return candidate;
       }
       finally
       {
           _loadLock.Release();
       }
   }
   ```

2. **Row Key 驗證規則 (Fail Closed Guarantee)**：
   * 檢查所有 `Member.PresentRecordId`：
     * 若同一 Candidate 內出現 **非空且完全相同的 PresentRecordId > 1 筆**，立即拋出 `InvalidOperationException("檢測到 CRM 重複之 PresentRecordIdKey: ...")`，絕不發布該 Candidate！
     * 若為 **不同 PresentRecordId 但 FullName 相同**，認定為合法同名人員，**全數完整保留**。

3. **Controller Snapshot 交付**：
   * 所有給予 DevExtreme `DataSourceLoader.Load` 的資料源，必須呼叫 `snapshot.CloneMembers()` 取得切斷引用 (Detached) 的獨立 List 複本，確保 UI 排序與篩選不影響 Session Cache 實例。

4. **測試補強計畫**：
   * 於 `ChurchReport.MemberInfo.Tests` 新增：
     1. `Concurrent_Requests_To_ListManager_Should_Trigger_Single_Flight_Load`
     2. `Identical_FullName_With_Different_PresentRecordId_Should_Be_Preserved`
     3. `Exact_Duplicate_PresentRecordId_Should_Fail_Closed`
     4. `ScopeKey_Mismatch_On_Date_Change_Should_Rebuild_Candidate`
