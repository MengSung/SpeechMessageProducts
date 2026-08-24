# 第二次獨立審查：SaveIntegrate 發布判定報告

本審查報告針對 `SaveIntegrate` 相關程式碼進行唯讀審查，評估其資料一致性、快取機制、可觀測性及發布安全性。

---

## 一、 五點獨立查證判定與證據

### 1. 靜默混合欄位快照風險
*   **判定**：**正確**
*   **證據**：
    *   `SmallGroupDataList.CreateIsolatedSnapshot()` 在執行時會取得 `_syncRoot` 鎖，並呼叫 `CloneSmallGroupData` 對 `Members` 進行深拷貝（透過 `new Member(member)` 複製每個成員實例）。
    *   然而，`SmallGroupData.UpdateMember()` 在原地修改成員欄位時，其內部的 `lock (m_MemberDataLocker)` 鎖已被註解停用，且該方法完全沒有使用 `SmallGroupDataList` 的 `_syncRoot` 鎖。
    *   當前景執行緒呼叫 `UpdateMember` 並透過 `JsonConvert.PopulateObject` 逐個寫入欄位時，若背景執行緒同時執行 `CreateIsolatedSnapshot()`，將會產生競態條件（Race Condition），導致複製出來的 `Member` 實例包含部分新、部分舊的欄位值，產生**靜默混合欄位快照（Torn Read）**。

### 2. 快取失效時的 CRM 重載失效
*   **判定**：**正確**
*   **證據**：
    *   在 `InMemoryDataContextSmallGroup.cs` 中，當 `ListManager` 快取未命中（Cache Miss）時，會執行 `new ListManager()` 並寫入快取，此時新物件的 `m_Password` 為空。
    *   在 `BaseChurchController.EnsureCorrectUserData()` 中，Step 1 會檢查 `sessionPassword` 與 `listManagerPassword`。若 `sessionPassword` 非空（使用者已登入）但新快取的 `listManagerPassword` 為空時，程式會繼續往下執行。
    *   然而，後續的 Step 3 與 Step 4 皆要求 `!string.IsNullOrEmpty(listManagerPassword)` 才會進行密碼比對或呼叫 `SetupListManager`。Step 5 則只在 `sessionPassword` 為空時觸發。
    *   因此，當快取失效重建為空白 `ListManager` 後，`EnsureCorrectUserData()` 將直接結束，**不會**觸發 `SetupListManager` 來重載 CRM 資料，導致系統處於未載入資料的空白狀態。

### 3. 背景上傳失敗的可觀測性缺失
*   **判定**：**正確**
*   **證據**：
    *   在 `SmallGroupController.Save.cs` 的 `SaveIntegrate` 背景 Task 中，outer catch 區塊僅記錄了例外型別 `ex.GetType().Name`，完全遺失了錯誤訊息（Message）與堆疊追蹤（StackTrace）。
    *   此外，該區塊呼叫的 `ToolUtilityClass.TraceByLevelStatic` 內部僅使用 `System.Diagnostics.Trace.WriteLine` 輸出。
    *   根據 `FileToolUtilityTracer.cs` 的實作，只有 `FileToolUtilityTracer.Write()` 才會將日誌寫入 `CHURCH_REPORT_TRACE.TXT`。因此，背景上傳失敗（pre-upload fault）的錯誤不會被寫入該追蹤檔，確實缺乏完整的可觀測性。

### 4. `bg.end` 寫出僅代表 Scope 結束
*   **判定**：**正確**
*   **證據**：
    *   `DataverseTrace.BackgroundScope` 實作了 `IDisposable`。其 `Dispose()` 方法是在 `using` 區塊結束時被自動呼叫，並向佇列寫入 `EventKind.BackgroundEnd`（即 `bg.end`）。
    *   不論背景的 CRM 寫入操作是成功完成還是拋出例外，只要離開 `using` 範疇，`Dispose()` 就會執行。因此，`bg.end` 僅代表該背景工作範疇的結束，不代表 CRM 寫入成功。

### 5. 發布判定標準
*   **判定**：**正確**
*   **證據**：
    *   **正常發布**：不應通過。正常發布必須符合嚴格的品質與資料一致性標準。既然已證實存在靜默資料損壞風險（問題 1）與快取失效導致資料無法載入風險（問題 2），必須在發布前予以修復。
    *   **緊急 Hotfix**：若新版本解決了更具破壞性的阻斷性問題（例如 Session 串連/洩漏等高危安全漏洞），且此一致性風險在舊版本中已同樣存在，則可允許作為緊急 Hotfix 先行發布，但必須同步建立監控機制，並於隨後立即安排修復。

---

## 二、 限制與消費查證

### 1. 安全記錄方向建議
為避免敏感資訊（如成員個資、帳密、詳細堆疊）洩漏，建議採用以下安全記錄方向：
*   **Outcome（執行結果）**：僅記錄 `Success`、`Failed` 或 `Aborted`。
*   **Error Class（錯誤分類）**：將例外歸類為粗粒度的代碼，例如 `CrmNetworkTimeout`、`CrmValidationError`、`SerializationError`，而不記錄具體的 `ex.Message`。
*   **Operation ID / Trace ID**：記錄關聯的 `TraceId`。若需排查詳細錯誤，應由管理員持該 ID 至受保護的內部安全日誌系統中查詢，避免將敏感堆疊直接寫入一般追蹤檔。

### 2. `requiresRefresh=true` 消費查證
*   **查證結果**：**未被實際消費**。
*   在 `IntegrateView.cshtml` 中，不論是 `$.ajax` 的 `success` 回呼還是全域的 `onSuccess(data)` 函式，都僅讀取了 `response.message` 與 `response.status` 來顯示 Toast 通知。
*   網格的重新整理是透過 `grid.refresh()` 寫死在 `setTimeout` 中觸發，頁面並未對 `requiresRefresh` 屬性進行任何判斷或處理。

---

## 三、 程式碼審查發現分類

### Critical (嚴重缺陷)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Models\SmallGroupData.cs`
    *   **原因**：`UpdateMember` 中的 `m_MemberDataLocker` 鎖被註解，且未與 `SmallGroupDataList.CreateIsolatedSnapshot()` 使用相同的 `_syncRoot` 鎖。這會在多執行緒併發時導致成員資料的**靜默混合欄位快照（Torn Read）**，有資料損壞風險。
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs`
    *   **原因**：`EnsureCorrectUserData()` 在 `ListManager` 快取失效重建（密碼為空）且 Session 密碼存在時，無法正確觸發 `SetupListManager`，導致 CRM 資料無法重載，使用者介面將呈現空白。

### Warning (警告)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\SmallGroupController\SmallGroupController.Save.cs`
    *   **原因**：背景 Task 的 outer catch 僅記錄例外型別名稱，且未寫入 `CHURCH_REPORT_TRACE.TXT`，導致 pre-upload 階段的錯誤缺乏足夠的可觀測性。
*   **檔案路徑**：`ToolUtility\Dataverse\DataverseTrace.cs`
    *   **原因**：`BackgroundScope.Dispose()` 寫出的 `bg.end` 無法反映 CRM 操作的真實成功與否，可能誤導日誌分析人員。

### Info (提示)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Views\Home\IntegrateView.cshtml`
    *   **原因**：後端回傳的 `requiresRefresh=true` 屬性在前端視圖中未被任何 JavaScript 消費，屬於冗餘欄位。
