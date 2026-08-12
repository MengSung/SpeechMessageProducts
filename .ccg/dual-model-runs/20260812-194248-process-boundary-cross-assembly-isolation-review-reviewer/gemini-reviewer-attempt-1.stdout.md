# 跨組件進程隔離審查報告 (Process Boundary Cross-Assembly Isolation Review)

本報告針對 `.trellis/tasks/08-12-process-boundary-cross-assembly-isolation/` 任務所引入的未提交變更進行審查。此變更旨在解決 `WorkerTestHost` 在跨測試組件（Dynamics 與 ChurchReport）執行時的進程衝突問題，透過引入基於檔案鎖（`FileShare.None`）的跨進程租約（Lease）來序列化相關測試。

---

## 需求驗證摘要

1. **跨 Worktree 隔離與路徑分割**：**通過**
   - `BuildLockPathForTesting` 採用 canonical solution root 的 SHA-256 雜湊值（取前 16 字元）作為鎖檔名的一部分。這確保了同一個 worktree 的測試共用同一個鎖，而不同 worktree 之間不會互相爭奪，且鎖路徑中完全不洩露 worktree 的實際路徑。
2. **有界等待與 Fail-Closed 錯誤處理**：**通過**
   - `Acquire` 方法限制了最大等待時間（預設 2 分鐘），且僅在捕獲到 Windows 共享/鎖定衝突（Win32 Error Code 32 或 33）時進行重試。其他 I/O 錯誤（如目錄不存在、權限不足等）會直接向上拋出，不會被誤分類為超時，符合 fail-closed 原則。
3. **單一擁有者與生命週期管理**：**通過**
   - `FileStream` 由 `WorkerTestHostProcessBoundaryLease` 實例獨佔，無 static 變數持有 handle。當測試結束或 testhost 進程異常中止時，作業系統會自動釋放 file handle，避免鎖殘留。
4. **共享源碼編譯與 Collection 套用**：**通過**
   - `WorkerTestHostProcessBoundaryCollection.cs` 以 Link 方式編譯進兩個測試專案，且所有相關的 `WorkerTestHost` 測試類別與 ChurchReport 的 disabled 測試類別均套用了同一個 `[Collection(WorkerTestHostProcessBoundaryCollection.Name)]`，確保序列化執行。
5. **無弱化斷言或資源洩漏**：**通過**
   - 未弱化任何現有斷言，且測試用鎖檔案在測試結束後會被妥善刪除。

---

## 審查發現 (Review Findings)

### 🔴 Critical
* **無**：未發現任何 Critical 級別的安全性或功能性漏洞。

### 🟡 Warning
* **無**：未發現任何 Warning 級別的潛在風險。

### 🔵 Info

#### 1. 檔案編碼與中文註解亂碼
* **檔案路徑**：
  - `TestInfrastructure/WorkerTestHostProcessBoundaryCollection.cs`
  - `SpeechMessage.Dynamics.Tests/WorkerTestHostProcessBoundaryLeaseTests.cs`
  - `ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs`
  - `SpeechMessage.Dynamics.Tests/OfficialWorkerSoakAndPerformanceTests.cs`
  - `SpeechMessage.Dynamics.Tests/OfficialWorkerProfileExecutorTests.cs`
  - `SpeechMessage.Dynamics.Tests/OfficialWorkerControlPlaneAdmissionTests.cs`
  - `SpeechMessage.Dynamics.Tests/DedicatedGatewayProcessBoundaryTests.cs`
* **原由**：上述檔案中的繁體中文註解在讀取時呈現亂碼。這通常是因為檔案儲存為不帶 BOM 的 UTF-8 編碼，在 Windows 環境下被誤讀為 ANSI/Big5。
* **建議**：建議在提交前確保這些檔案使用 **UTF-8 with BOM** 編碼儲存，以維持程式碼的可讀性與維護性。

#### 2. 多餘的 `GC.SuppressFinalize` 呼叫
* **檔案路徑**：`TestInfrastructure/WorkerTestHostProcessBoundaryCollection.cs` (第 55 行)
* **原由**：`WorkerTestHostProcessBoundaryLease` 類別並未定義解構函式（Finalizer），因此在 `Dispose` 方法中呼叫 `GC.SuppressFinalize(this)` 是多餘的。
* **建議**：雖然這不會造成執行期錯誤，但移除它可以使程式碼更為簡潔。

---

## 結論與建議

本變更的設計與實現非常嚴密，完全符合隔離性、安全性與生命週期管理的要求。在解決跨組件測試衝突的同時，也確保了不同 worktree 之間的獨立性與 fail-closed 的錯誤處理機制。

**建議：通過 (PASS)**，僅需在提交前調整檔案編碼以修正中文註解亂碼問題。
