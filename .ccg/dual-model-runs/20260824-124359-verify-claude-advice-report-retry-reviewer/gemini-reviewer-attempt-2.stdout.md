# 獨立審查報告：SaveIntegrate 發布判定與併發一致性分析

本審查報告針對 `SaveIntegrate` 背景上傳機制、快照隔離、日誌觀測性及發布判定進行唯讀審查。

---

## 核心問題查證與判定

### 1. 靜默混合欄位快照（Torn Read）風險
*   **判定結果**：**正確**
*   **證據與分析**：
    *   在 `SmallGroupDataList.cs` 中，`CreateIsolatedSnapshot()` 確實使用了 `lock (_syncRoot)` 來保護並複製 `Members` 集合與成員實例。
    *   然而，在 `SmallGroupData.cs` 的 `UpdateMember()` 中，原有的 `lock (m_MemberDataLocker)` 已被註解停用，且該方法**完全沒有**使用與 `CreateIsolatedSnapshot()` 相同的 `_syncRoot` 鎖。
    *   `UpdateMember()` 內部使用 `JsonConvert.PopulateObject(values, aUpdatedMember, settings)` 直接原地（In-place）修改 `Member` 實例的欄位。
    *   當背景執行緒正在進行快照複製（讀取 `Member` 屬性），而前景執行緒同時收到更新請求並執行 `PopulateObject` 時，將會產生資料競爭（Data Race），導致背景執行緒讀取到僅被部分更新的成員欄位，從而產生**靜默混合欄位快照（Torn Read）**。

### 2. Cache Miss 與 CRM 重載判定
*   **判定結果**：**錯誤**（即：**不會**重載 CRM）
*   **證據與分析**：
    *   當 `InMemoryDataContextSmallGroup.ListManager` 發生快取失效時，會執行 `new ListManager()`，此時新物件的 `m_Password` 為 `null` 或空字串。
    *   在 `BaseChurchController.cs` 的 `EnsureCorrectUserData()` 中，觸發 `SetupListManager`（重載 CRM）的關鍵判斷式為：
        ```csharp
        if (!string.IsNullOrEmpty(sessionPassword) &&
            !string.IsNullOrEmpty(listManagerPassword) &&
            sessionPassword != listManagerPassword)
        ```
    *   由於新物件的 `listManagerPassword` 為空，`!string.IsNullOrEmpty(listManagerPassword)` 評估為 `false`。
    *   因此，`EnsureCorrectUserData()` 在新物件密碼為空時，**不會**進入該分支重載 CRM，而是直接跳過。

### 3. 背景上傳 Fault 的可觀測性缺失
*   **判定結果**：**正確**
*   **證據與分析**：
    *   在 `SmallGroupController.Save.cs` 的 `SaveIntegrate` 背景任務中，外層的 `catch (Exception ex)` 僅記錄了例外型別名稱：
        ```csharp
        ToolUtilityClass.TraceByLevelStatic(1, 1, $"SaveIntegrate 丟失/異常: {ex.GetType().Name}");
        ```
    *   此處完全忽略了 `ex.Message` 與 `ex.StackTrace`。
    *   此外，`TraceByLevelStatic` 並非寫入主追蹤檔 `CHURCH_REPORT_TRACE.TXT` 的專用 Writer，且缺乏上下文關聯。若上傳前發生錯誤（如網路中斷、欄位驗證失敗），開發人員將只能在日誌中看到模糊的 `System.NullReferenceException` 或 `HttpRequestException` 型別名稱，**缺乏完整可觀測性**。

### 4. DataverseTrace.BackgroundScope 的語意
*   **判定結果**：**正確**
*   **證據與分析**：
    *   `DataverseTrace.BackgroundScope.Dispose()` 寫出的 `bg.end` 事件，僅代表該 `using` 區塊（Scope）執行完畢並釋放。
    *   該 Dispose 邏輯中並無任何機制去檢查區塊內部的 CRM 寫入是否成功。即使區塊內拋出未捕獲的異常，`Dispose()` 仍會被執行並寫入 `bg.end`。因此，`bg.end` **僅代表 Scope 結束，不代表 CRM 寫入成功**。

### 5. 發布判定（正常發布 vs 緊急 Hotfix）
*   **判定結果**：**正確**
*   **發布建議**：
    *   **正常發布（Normal Release）：【NEEDS_IMPROVEMENT / 阻擋發布】**
        *   雖然新版引入了快照隔離，安全性較舊版有所提升，但由於**第 1 點所述的靜默資料一致性風險（Torn Read）依然存在**，這可能導致寫入 CRM 的資料損壞。在正常發布流程中，不應允許帶有已知資料一致性風險的版本上線。
    *   **緊急 Hotfix（Emergency Hotfix）：【PASS / 條件性通過】**
        *   若生產環境目前正遭遇嚴重的阻斷性故障（如嚴重的記憶體洩漏或 Session 崩潰），且此版本已解決了該阻斷性問題，則可作為緊急 Hotfix 臨時上線。但**必須同時建立高優先級的追蹤任務**，於下一個迭代中立即修復 `UpdateMember` 的鎖定機制。

---

## 程式碼審查發現分類

### CRITICAL (嚴重缺陷)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Models\SmallGroupData.cs`
    *   **原因**：`UpdateMember` 中的 `m_MemberDataLocker` 被註解停用，且未與 `SmallGroupDataList.CreateIsolatedSnapshot()` 使用相同的同步鎖（`_syncRoot`）。這會在多執行緒併發時導致成員資料的 Torn Read，有資料損壞風險。
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\SmallGroupController\SmallGroupController.Save.cs`
    *   **原因**：背景任務的 `catch` 區塊僅記錄 `ex.GetType().Name`，導致所有非預期錯誤（如資料庫連線失敗、欄位格式錯誤）的詳細原因被永久吞沒，嚴重阻礙生產環境的故障排查。

### WARNING (警告)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs`
    *   **原因**：`EnsureCorrectUserData()` 在 `ListManager` 密碼為空時不會主動重載 CRM，可能導致後續操作因缺少憑證而失敗，此處的狀態機防禦邏輯不夠嚴密。

### INFO (資訊)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Views\Home\IntegrateView.cshtml` (及相關前端 JS)
    *   **原因**：經查證，`SaveIntegrate` 回傳的 `requiresRefresh=true` 屬性**並未被前端實際消費**。前端 AJAX 回呼通常僅檢查 `status == "1"`，此欄位目前為冗餘欄位。

---

## 改善建議

### 1. 安全日誌記錄方向（不記錄敏感資訊與堆疊）
為符合安全合規性，請勿記錄 `ex.ToString()`、Stack Trace 或任何帳密/成員個資。建議改用以下結構化欄位記錄：
*   **Operation ID**：使用 `traceScope.TraceId` 或 `Guid.NewGuid()` 作為關聯識別碼，以便與伺服器端詳細日誌進行關聯。
*   **Error Class**：分類記錄錯誤類型（例如：`CrmNetworkException`、`JsonSerializationException`、`NullReferenceException`）。
*   **Outcome**：明確記錄操作結果狀態（例如：`PreUpload_Failed`、`CrmWrite_Failed`）。

### 2. 併發鎖定修正
*   應將 `SmallGroupDataList.SyncRoot` 公開，並在 `SmallGroupData.UpdateMember()` 寫入臨界區時，同樣取得該 `SyncRoot` 鎖，以確保「建立快照」與「原地更新成員」互斥。
