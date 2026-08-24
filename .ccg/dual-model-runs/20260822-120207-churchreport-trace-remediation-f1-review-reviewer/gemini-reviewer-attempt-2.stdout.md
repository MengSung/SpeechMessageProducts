# F1 背景上傳狀態隔離程式審查報告

本報告針對 F1 背景上傳狀態隔離的變更進行審查。審查範圍包含 Controller、Model 及其對應的單元測試。

---

## 1. 整體評估 (Summary)

本次變更成功實作了「唯讀退路」的設計模式。在 Request 期間，系統透過短鎖建立深層副本（`CreateBackgroundUploadCopy`），背景上傳與清理操作僅修改該副本，絕不回寫至前景的 `Session` 或 `IMemoryCache`。前景則透過附加 `requiresRefresh=true` 的 JSON 回應，引導前端重新整理並從 CRM 載入最新狀態。此設計有效避免了長達 14 秒的陳舊快照覆蓋同期 CRUD 的風險，且執行緒隔離與資源釋放機制設計良好。

---

## 2. 審查重點檢查清單

### 1. `Task.Run` 閉包捕獲檢查
*   **結果**：**通過**。
*   **說明**：`Task.Run` 內僅捕獲了 `backgroundCopy`、`selectDate`、`account`、`password`、`loginType`、`weeklyReportData`、`happyWeekIndex`、`happyWeekTopic`、`pauseCheckBox` 與 `scopeFactory`。並未捕獲 `this` (Controller 實例)、`InMemoryContext` 或前景的 `weeklyReportRef`。這確保了背景工作不會意外延長 Request 上下文的生命週期。

### 2. `IServiceScope` 與背景 Trace Scope 所有權與釋放
*   **結果**：**通過**。
*   **說明**：背景工作內部正確使用了 `using var traceScope` 與 `using var scope = scopeFactory.CreateScope()`。所有從 DI 容器取得的服務（如 `IToolUtilityProvider`）其生命週期皆受限於該背景 `scope`，並在工作結束時正確釋放，無跨 Request 資源或 Session 洩漏風險。

### 3. `Member` 深拷貝完整性
*   **結果**：**通過**。
*   **說明**：`Member` 的拷貝建構子完整複製了所有公開可變欄位，且刻意忽略了 `ParentListSmallGroupWeeklyReport` 屬性，確保複製後的 `Member` 實例不會保留對父週報的引用，有效防止了循環引用與記憶體洩漏。

### 4. 背景週報副本隔離性
*   **結果**：**通過**。
*   **說明**：`CreateBackgroundUploadCopy` 僅複製上傳所需的資料，並對 `m_SmallGroupDataList` 呼叫 `CreateIsolatedSnapshot()` 進行深拷貝。此外，`m_UploadIntegrateData` 在建構時即為全新實例，未與前景共用任何可變模型或 CRM Entity 圖。

### 5. 回應相容性、敏感資訊日誌與測試覆蓋
*   **結果**：**通過**。
*   **說明**：
    *   回應正確附加了 `requiresRefresh = true`。
    *   日誌中未輸出任何敏感資訊（如密碼）。
    *   新增的 `SmallGroupDataListSnapshotIsolationTests` 測試涵蓋了深拷貝驗證、背景修改不影響前景的競態測試，以及多副本隔離測試，設計非常嚴謹。

---

## 3. 具體發現與分級 (Findings)

### Critical (嚴重問題)
*   **無**。未發現任何會導致系統崩潰、資料損毀或嚴重安全性漏洞的問題。

### Warning (警告事項)
*   **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
    *   **行號**：154-157
    *   **說明**：在背景清理副本成員的 `try-catch` 區塊中，若發生例外，僅使用 `System.Diagnostics.Debug.WriteLine` 記錄。在 Release 模式下，`Debug.WriteLine` 不會輸出任何內容，這會導致此處的例外被完全吞掉，增加生產環境的排障難度。
    *   **建議**：改用 `ToolUtilityClass.TraceByLevelStatic` 或標準的日誌記錄器（`ILogger`）來記錄此例外。

### Info (提示資訊)
*   **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
    *   **行號**：71-74
    *   **說明**：`password` 變數被閉包捕獲並傳遞至背景工作。雖然這是 legacy 系統的必要設計，但明文密碼在記憶體中傳遞存在安全風險。程式碼中已有 `TODO(credential-lifecycle)` 註記，建議未來重構時引入更安全的憑證管理機制。
*   **檔案**：多個檔案（如 `SmallGroupController.Save.cs`、`SmallGroupDataList.cs` 等）
    *   **說明**：檔案中的中文註解在部分讀取環境下呈現亂碼（可能與 UTF-8 with BOM 或 Big5 編碼轉換有關）。
    *   **建議**：確保所有原始碼檔案統一使用 **UTF-8 without BOM** 編碼，以維持跨平台與工具的一致性。

---

## 4. 驗證報告 (Validation Report)

本評分針對 F1 背景上傳狀態隔離的 bugfix 進行評估：

```
VALIDATION REPORT
=================
User Experience: 19/20 - 採用唯讀退路並附加 requiresRefresh=true，前端重整載入最新資料，UX 流程清晰，避免了資料覆蓋。
Visual Consistency: 20/20 - 此變更為後端與資料模型隔離，不涉及 UI 視覺變更，維持既有 consistency。
Accessibility: 20/20 - 無 UI 變更，不影響 accessibility。
Performance: 19/20 - 透過深拷貝與背景 Task.Run 異步處理，避免了前景 Request 的阻塞，且無 Session 快取無界成長問題。
Browser Compatibility: 20/20 - 回傳標準 JSON 格式，相容於所有瀏覽器。

TOTAL SCORE: 98/100

ISSUES FOUND:
- [Warning] SmallGroupController.Save.cs (Line 154-157): 背景清理副本成員時的例外僅用 Debug.WriteLine 記錄，Release 模式下會被吞掉。
- [Info] SmallGroupController.Save.cs (Line 71-74): 明文密碼被閉包捕獲，已有 TODO 規劃未來重構。

RECOMMENDATION: PASS
```
