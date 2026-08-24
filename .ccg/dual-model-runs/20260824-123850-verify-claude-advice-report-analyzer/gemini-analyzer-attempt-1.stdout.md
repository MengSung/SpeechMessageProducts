# 技術審查報告：Claude 的 SaveIntegrate 發布建議獨立查證

本報告針對工作樹 `1.0.0.6.DesignNewArchitector.Worktree` 中關於 `SaveIntegrate` 背景上傳與快照隔離設計的相關主張進行獨立技術審查。

---

## 一、 主張裁定與本機證據

### 1. 主張：C3 可能靜默產生新舊欄位混合快照並上傳 CRM
*   **裁定**：**正確**
*   **分析與反證**：
    *   在前景 Request 中，`SaveIntegrate` 呼叫了 `weeklyReportRef?.CreateBackgroundUploadCopy()`，進而呼叫 `m_SmallGroupDataList.CreateIsolatedSnapshot()`。
    *   `CreateIsolatedSnapshot()` 雖在 `lock (_syncRoot)` 保護下執行，但其內部對成員的複製是透過 `new Member(member)` 逐一讀取欄位值。
    *   與此同時，前景寫入端（如 `UpdateSmallGroupPresentRecord`）平行啟動 `Task.Run` 呼叫 `UpdateMember(key, values)`。
    *   `UpdateMember` 內部**完全沒有任何鎖保護**，直接使用 `JsonConvert.PopulateObject(values, aUpdatedMember, settings)` 原地改寫既有的 `Member` 實例欄位。
    *   由於寫入端未與 `SyncRoot` 同步，當 `PopulateObject` 正在逐個寫入欄位時，`new Member(member)` 同時在另一個執行緒逐個讀取欄位，將導致讀取到「部分已更新、部分未更新」的混合狀態，此損壞的快照隨後會被背景任務上傳至 CRM。
*   **驗證證據**：
    *   `SpeechMessageProducts.ChurchReport\Models\SmallGroupDataList.cs` -> `CreateIsolatedSnapshot()`
    *   `SpeechMessageProducts.ChurchReport\Models\SmallGroupData.cs` -> `UpdateMember()`
    *   `SpeechMessageProducts.ChurchReport\Controllers\SmallGroupController\SmallGroupController.Crud.cs` -> `UpdateSmallGroupPresentRecord()`
    *   `SpeechMessageProducts.ChurchReport\Models\Member.cs` -> `Member(Member source)`

### 2. 主張：cache 逾期後會得到空白 ListManager，且通常不會透過 EnsureCorrectUserData 自動 CRM 重載
*   **裁定**：**正確**
*   **分析與反證**：
    *   在 `InMemoryDataContextSmallGroup.cs` 中，`ListManager` 屬性在快取未命中時，會執行 `m_ListManager = new ListManager();` 並寫回快取。此時得到的 `ListManager` 帳密欄位皆為預設空值。
    *   在 `BaseChurchController.cs` 的 `EnsureCorrectUserData()` 中，重載判斷條件為：
        ```csharp
        if (!string.IsNullOrEmpty(sessionPassword) &&
            !string.IsNullOrEmpty(listManagerPassword) &&
            sessionPassword != listManagerPassword)
        ```
    *   當快取逾期重建後，`listManagerPassword` 為空字串。這導致 `!string.IsNullOrEmpty(listManagerPassword)` 條件為 `false`，重載邏輯被直接跳過。用戶將持續看到空白的資料，直到手動重新登入。
*   **驗證證據**：
    *   `SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs` -> `ListManager` 屬性 getter
    *   `SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs` -> `EnsureCorrectUserData()`

### 3. 主張：C4 在 UploadData 之前失敗時，完整例外原因可能不會出現在 CHURCH_REPORT_TRACE.TXT
*   **裁定**：**正確**
*   **分析與反證**：
    *   在 `SaveIntegrate` 的背景 `Task.Run` 中，外層 `catch (Exception ex)` 僅記錄了 `ex.GetType().Name`，並呼叫靜態方法 `ToolUtilityClass.TraceByLevelStatic()`。
    *   `TraceByLevelStatic` 寫入的是 `System.Diagnostics.Trace`，而非 `FileToolUtilityTracer` 所管理的 `CHURCH_REPORT_TRACE.TXT`。
    *   若在 `UploadIntegrateDataAsync` 呼叫前（例如 DI Scope 建立失敗或服務解析失敗）拋出例外，完整的 `Message` 與 `StackTrace` 將會遺失，無法在 `CHURCH_REPORT_TRACE.TXT` 中進行診斷。
*   **驗證證據**：
    *   `SpeechMessageProducts.ChurchReport\Controllers\SmallGroupController\SmallGroupController.Save.cs` -> `SaveIntegrate` 的背景 `Task.Run` 異常處理區塊。

### 4. 主張：`bg.end` 不是上傳成功證明
*   **裁定**：**正確**
*   **分析與反證**：
    *   `DataverseTrace.BackgroundScope` 實作了 `IDisposable`。在 `SaveIntegrate` 中以 `using var traceScope` 宣告。
    *   不論背景任務是成功完成還是中途拋出例外，只要離開 `using` 範疇，`Dispose()` 就會被無條件觸發並寫入 `bg.end`。
    *   `bg.end` 欄位僅代表執行緒生命週期的結束，不包含任何代表業務成功或失敗的 outcome 狀態。
*   **驗證證據**：
    *   `SpeechMessageProducts.ChurchReport\Controllers\SmallGroupController\SmallGroupController.Save.cs` -> `using var traceScope`
    *   `ToolUtility\Dataverse\DataverseTrace.cs`

### 5. 主張：不應因功能已合併而重寫 feature branch 歷史
*   **裁定**：**正確**
*   **分析與反證**：
    *   合併提交 `ebd2af507` 已經存在於工作樹中。重寫已合併分支的 Git 歷史會破壞團隊協作與稽核追蹤。後續修正應採用常規的修正提交（Fix Commits）。

---

## 二、 發布門檻評估

### 「新版嚴格優於目前生產版」是否可單獨作為發布通過的理由？
*   **結論**：**否，不可單獨作為通過理由。**
*   **判定依據**：
    1.  **漸進式風險改善**：新版引入的快照隔離（`CreateBackgroundUploadCopy`）確實解決了舊版背景任務直接共用前景可變圖形所導致的嚴重並發覆蓋問題。
    2.  **專案規範發布門檻**：然而，正常發布門檻要求系統必須具備執行緒安全性與資料一致性。新版在 `UpdateMember` 寫入端缺乏同步鎖的情況下，存在**靜默產生損壞快照並寫入 CRM** 的高風險缺陷（如裁定 1 所示），且快取逾期會導致資料空白（如裁定 2 所示）。
    3.  **結論**：不能因為新版「風險比舊版低」，就允許帶有已知並發損壞風險的程式碼通過正常發布門檻。必須修正上述 Blocker 後方可發布。

---

## 三、 項目清單 (Critical / Warning / Info)

### 1. Critical (Release Blockers)
*   **`Member` 讀寫競態修正**：
    *   *位置*：`SpeechMessageProducts.ChurchReport\Models\SmallGroupData.cs` 的 `UpdateMember` 方法。
    *   *原因*：必須引入與 `CreateIsolatedSnapshot` 互斥的同步機制（例如在 `SmallGroupDataList.SyncRoot` 鎖保護下進行 `UpdateMember` 的寫入），防止快照複製時讀取到被 `JsonConvert.PopulateObject` 修改到一半的混合資料。
*   **快取逾期空白狀態修正**：
    *   *位置*：`SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs` 的 `EnsureCorrectUserData` 方法。
    *   *原因*：當 `listManagerPassword` 為空但 Session 仍有帳密時，應允許重新初始化 `SetupListManager`，避免快取逾期後系統陷入無法自動重載的空白狀態。

### 2. Warning (Emergency Hotfix 條件)
*   **Hotfix 容許條件**：
    *   僅在生產環境因舊版並發衝突導致嚴重的 Session 洩漏（Session Bleeding）或 CRM 資料大面積損壞，且無法即時完成完整鎖重構時，方可作為緊急 Hotfix 上線，但上線後必須對 CRM 寫入進行嚴密監控。

### 3. Info (上線後最高優先改善項目)
*   **完整例外日誌記錄**：
    *   *位置*：`SmallGroupController.Save.cs` 的 `SaveIntegrate` 背景 `catch` 區塊。
    *   *建議*：將 `ex.GetType().Name` 改為記錄完整的 `ex.ToString()`，並確保其寫入 `CHURCH_REPORT_TRACE.TXT`。
*   **前端響應 `requiresRefresh`**：
    *   *位置*：`IntegrateView.cshtml`
    *   *建議*：前端應讀取並響應 JSON 回傳的 `requiresRefresh` 標記，而非使用寫死的延遲 `grid.refresh()`。
*   **DataverseTrace 狀態擴充**：
    *   *建議*：為 `BackgroundScope` 增加業務結果標記，使 `bg.end` 能區分成功與失敗。

---

## 四、 本機證據與假設糾錯

1.  **快照測試的局限性**：
    *   現有測試 `CreateBackgroundUploadCopy_DeepCopiesAllMemberCollectionsAndRequiredUploadMetadata` 僅驗證了單執行緒下的深拷貝正確性，**未能覆蓋並發讀寫情境**。這容易誤導開發人員認為快照機制已完全執行緒安全。
2.  **`SyncRoot` 的孤立性**：
    *   本機搜尋證實 `SyncRoot` 僅在 `CreateIsolatedSnapshot` 內部被鎖定，寫入端完全沒有遵守此同步協定，此設計缺陷是導致讀寫競態的根本原因。
