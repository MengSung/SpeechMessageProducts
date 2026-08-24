# F1 背景上傳狀態隔離程式審查報告

本報告針對 F1 背景上傳狀態隔離的變更進行程式碼審查，審查範圍包含 Controller、Model 及其對應的單元測試。

---

# VALIDATION REPORT
=================
User Experience: 20/20 - 背景非同步上傳避免了前端 UI 阻塞，且回傳 `requiresRefresh = true` 確保前端資料一致性，提升使用者體驗。
Visual Consistency: 20/20 - 不適用（後端邏輯），給予滿分。
Accessibility: 20/20 - 不適用（後端邏輯），給予滿分。
Performance: 20/20 - 使用深拷貝與獨立 scope 進行背景上傳，避免了前景 Session 鎖定與資料競爭，顯著提升系統併發效能。
Browser Compatibility: 20/20 - 不適用（後端邏輯），給予滿分。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無 Critical 或 Warning 級別的程式碼缺陷。
- (Info) 檔案中存在部分亂碼註解，建議在後續清理中修復編碼問題。

RECOMMENDATION: PASS

---

## 重點檢查項目回覆

### 1. `Task.Run` 閉包捕獲檢查
* **結果**：**通過**。
* **說明**：在 `SmallGroupController.Save.cs` 中，`Task.Run` 啟動前已將所有需要的資料（包括 `backgroundCopy` 副本、`selectDate`、`account`、`password`、`loginType` 等）複製到局部變數中。在 `Task.Run` 內部僅存取這些局部變數，未捕獲 `this` (Controller)、`InMemoryContext`、`weeklyReportRef` 或任何共享的 Members 集合，確保了執行緒安全。

### 2. `IServiceScope` 與背景 Trace Scope 生命週期
* **結果**：**通過**。
* **說明**：
  * 背景工作使用 `scopeFactory.CreateScope()` 建立獨立的 `IServiceScope`，並在 `using` 區塊結束時自動釋放，避免了跨 request 資源洩漏。
  * 背景 trace scope 使用 `DataverseTrace.Current?.BeginBackgroundOperation` 建立並正確釋放。
  * 所有背景邏輯皆包裹在 `try-catch` 中，異常被妥善捕獲與記錄，不會導致執行緒崩潰或洩漏。

### 3. `Member` 深拷貝與父引用切斷
* **結果**：**通過**。
* **說明**：`Member` 的拷貝建構子 `public Member(Member source)` 完整複製了所有公開可變欄位（如 `PresentRecordId`, `ContactId`, `Sunday`, `SmallGroup` 等）。同時，它刻意忽略了 `ParentListSmallGroupWeeklyReport` 屬性的複製，使其在副本中保持為 `null`，成功切斷了對父週報的引用，避免了循環引用與記憶體洩漏。

### 4. 背景週報副本資料最小化與 Uploader 隔離
* **結果**：**通過**。
* **說明**：`CreateBackgroundUploadCopy()` 僅複製了上傳所需的欄位，未複製 `GroupArray` 與 `m_PersonalReportViewModel` 等無關資料，亦未複製 `m_PresentRecordWithNoSmallGroupEntity` (CRM Entity)。此外，`m_UploadIntegrateData` 在建構子中重新 new 出新實例，確保副本使用獨立的 uploader，不與前景共用。

### 5. 回應相容性、敏感資訊日誌與測試覆蓋
* **結果**：**通過**。
* **說明**：
  * **回應相容性**：回傳的 JSON 包含 `requiresRefresh = true`，符合設計。
  * **敏感資訊日誌**：日誌僅記錄例外類型名稱（如 `ex.GetType().Name`），未洩漏任何敏感資訊（如密碼或帳號）。
  * **測試覆蓋**：`SmallGroupDataListSnapshotIsolationTests.cs` 包含了併發修改與列舉的競態測試，驗證了隔離性。

---

## 詳細審查清單與分級評等

### 1. `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
* **[Info] 檔案編碼與亂碼註解 (第 2-13 行, 第 47-50 行等)**
  * **說明**：檔案中存在大量亂碼註解（例如 `// AI-蝜?銝剜?瑼?...`），這是由於檔案編碼轉換問題導致的。雖然不影響編譯與執行，但建議修復以提升可讀性。
* **[Info] 執行緒安全閉包 (第 88 行)**
  * **說明**：`Task.Run` 確實沒有捕獲 `this` (Controller)、`InMemoryContext`、`weeklyReportRef` 或共享的 Members，而是使用局部變數與深拷貝副本 `backgroundCopy`，符合執行緒安全與隔離設計。
* **[Info] 獨立 Scope 與 Trace (第 96, 100 行)**
  * **說明**：`using var traceScope = DataverseTrace.Current?.BeginBackgroundOperation("SaveIntegrate.Upload");` 與 `using var scope = scopeFactory.CreateScope();` 正確建立了背景 trace scope 與獨立的 DI scope，避免了跨 request 資源洩漏。
* **[Info] 異常處理與靜態日誌 (第 166 行)**
  * **說明**：在 `catch` 區塊中，由於 `scope` 已經被釋放，程式碼正確地呼叫了靜態方法 `ToolUtilityClass.TraceByLevelStatic` 來記錄日誌，避免了使用已釋放資源的問題。

### 2. `SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs`
* **[Info] 檔案編碼與亂碼註解 (第 2-13 行等)**
  * **說明**：檔案中存在亂碼註解，建議修復。
* **[Info] 副本建立邏輯 (第 100 行)**
  * **說明**：`CreateBackgroundUploadCopy()` 實作中，僅拷貝了上傳所需的欄位，且 `m_SmallGroupDataList` 呼叫了 `CreateIsolatedSnapshot()` 進行深拷貝。未拷貝 `m_PresentRecordWithNoSmallGroupEntity` (CRM Entity) 與 `GroupArray`，避免了不必要的資料攜帶與 CRM Entity 圖共用。

### 3. `SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs`
* **[Info] 檔案編碼與亂碼註解 (第 2-13 行等)**
  * **說明**：檔案中存在亂碼註解，建議修復。
* **[Info] 執行緒安全快照 (第 83 行)**
  * **說明**：`CreateIsolatedSnapshot()` 實作中，使用了 `lock (_syncRoot)` 確保執行緒安全，並呼叫 `CloneSmallGroupData` 進行深拷貝。
* **[Info] 集合深拷貝 (第 126 行)**
  * **說明**：`CloneSmallGroupData` 中，對 `Members` 集合進行了深拷貝：`source.Members.Select(member => member == null ? null : new Member(member)).ToList()`，確保背景修改不會影響前景。

### 4. `SpeechMessageProducts.ChurchReport/Models/Member.cs`
* **[Info] 檔案編碼與亂碼註解 (第 2-13 行等)**
  * **說明**：檔案中存在亂碼註解，建議修復。
* **[Info] 拷貝建構子與父引用切斷 (第 37, 91 行)**
  * **說明**：拷貝建構子 `public Member(Member source)` 完整拷貝了所有公開可變欄位，且**沒有**拷貝 `ParentListSmallGroupWeeklyReport` 屬性，使其在副本中保持為 `null`，成功切斷了對父週報的引用，避免了循環引用與記憶體洩漏。

### 5. `ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs`
* **[Info] 檔案編碼與亂碼註解 (第 2-8 行等)**
  * **說明**：檔案中存在亂碼註解，建議修復。
* **[Info] 測試覆蓋率與競態驗證 (第 24 行)**
  * **說明**：測試設計完善，涵蓋了深拷貝驗證 (`CreateBackgroundUploadCopy_DeepCopiesAllMemberCollectionsAndRequiredUploadMetadata`)、背景修改隔離性與併發列舉測試 (`BackgroundMutationOfSnapshot_DoesNotBreakConcurrentEnumerationOfOriginalMembers`)，以及多快照不互相污染測試 (`CreatingTwoSnapshots_DoesNotCrossContaminateSources`)。

---

## 結論與建議

本次變更完整且正確地實作了 **F1 背景上傳狀態隔離** 的設計要求。透過深拷貝機制、獨立的 DI Scope、執行緒安全的快照鎖定，以及切斷父週報引用，成功消除了背景上傳與前景 CRUD 之間的資料競爭與 Session 鎖定問題。

**建議：PASS（通過）**，僅需在後續維護中將亂碼註解修復為正確的繁體中文即可。
