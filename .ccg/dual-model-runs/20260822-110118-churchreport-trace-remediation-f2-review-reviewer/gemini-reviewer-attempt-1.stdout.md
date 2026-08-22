# UI/後端架構審查報告：ChurchReport Trace Remediation F2 Review

本報告針對工作樹中 F2 變更範圍進行審查，範圍包括：
1. `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
2. `ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs`

---

## VALIDATION REPORT
=================
* **User Experience (開發者與 API 使用體驗)**: 20/20 - 重構後的 `TryGetSessionCacheKey` 採用 .NET 標準的 `out` 模式，且在無 Session 時安全回傳 Scoped 實例，避免了除錯或背景執行緒存取時引發 `NullReferenceException`，開發者體驗良好。
* **Visual Consistency (程式碼與設計一致性)**: 20/20 - 程式碼排版、命名風格與既有專案高度一致，且嚴格遵守 Scoped 生命週期與資源釋放規範。
* **Accessibility (安全性與隔離性 - 後端安全邊界)**: 20/20 - 完美實現了跨使用者隔離，無 Session 時完全避開程序級快取，防止了 Session Bleeding 與跨使用者資料外洩。
* **Performance (效能與記憶體保留)**: 20/20 - 解決了 `NOSESSION_*` 快取鍵因 Ticks 導致的無界成長問題，重複存取 1,000 次快取項目數為 0，顯著提升記憶體效能。
* **Browser Compatibility (相容性 - 既有呼叫端相容性)**: 20/20 - 保留了 `GetCurrentSessionId()` 並回傳固定 `"NOSESSION"`，確保未修改的 7 個 legacy getter 能夠完全相容運作，無中斷變更。

**TOTAL SCORE: 100/100**

**ISSUES FOUND:**
* 無（僅有關於 Nullable Reference Types 的 Info 級建議）

**RECOMMENDATION: PASS**

---

## 1. Summary (整體評估)
本次 F2 變更非常完整且嚴謹，完美解決了在無 HTTP Session 狀態下（如背景工作、除錯評估）因 `NOSESSION_` 快取鍵包含 `Ticks` 而導致 `IMemoryCache` 項目無界成長的記憶體洩漏問題。變更不僅符合所有需求契約，更在測試中實現了確定性的資源清理，防止了測試替身引入的 CRM/背景資源洩漏。

---

## 2. Accessibility & Security Issues (安全性與隔離性審查)
* **跨使用者隔離 (Cross-User Isolation)**：
  * **核對結果**：在無 Session 時，`TryGetSessionCacheKey` 會回傳 `false`。六個授權 getter（`ListManager`、`SmallGroupDataList`、`WeeklyReportData`、`NewPersonModel`、`PersonalInfomationModel`、`HappyGroupDataManager`）會直接回傳 Scoped context 唯一的後備欄位（例如 `m_ListManager ??= new ListManager()`），完全避開了程序級的 `IMemoryCache`。
  * **安全邊界**：由於後備欄位生命週期受限於 Scoped data context，當 Scope 結束時即會被回收，絕不會跨 Request、使用者或 Tenant 留存，確保了極高的安全性。

---

## 3. Design & Code Quality Issues (設計與程式碼品質審查)

### Info: Nullable Reference Types 潛在編譯警告
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
* **行號**：第 221 行
* **程式碼**：
  ```csharp
  private bool TryGetSessionCacheKey(out string key)
  ```
* **理由**：若專案未來啟用 C# Nullable Reference Types (`#nullable enable`)，由於在 `session == null` 時會執行 `key = null;`，這會因為 `key` 被宣告為 non-nullable `string` 而產生編譯警告。
* **建議**：可將宣告改為 `out string? key`，以提高對 Nullable 語法的相容性。

### Info: 唯讀沙盒環境下的中文註解顯示說明
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs` 及 `.trellis/scripts/check_encoding.py`
* **理由**：在唯讀沙盒環境中讀取時，中文註解顯示為亂碼（例如 `AI-蝜?銝剜?瑼?閮餉圾`）。經由 Byte 還原分析：
  * `蝜?` 轉回 UTF-8 bytes 為 `\xe8\xa8\xad\xe8\xa8\x88`（**設計**）
  * `銝剜?` 轉回 UTF-8 bytes 為 `\xe4\xb8\xad\xe6\x96\x87`（**中文**）
  * `閮餉圾` 轉回 UTF-8 bytes 為 `\xe8\xa8\xbb\xe8\xa7\xa3`（**註解**）
  證實檔案在硬碟上確實是以正確的 **UTF-8 無 BOM** 格式儲存，亂碼僅為讀取工具在 Windows 環境下預設以 CP950 (Big5) 解碼所致，檔案本身無缺陷，符合契約要求。

---

## 4. Suggestions (改進建議)
* 目前變更已非常完善，無額外修改建議。

---

## 5. Positive Notes (值得肯定之處)
1. **相容性設計良好**：保留了 `GetCurrentSessionId()` 並將無 Session 時的傳回值固定為 `"NOSESSION"`，既解決了 Ticks 導致的快取洩漏，又完美相容了其他 7 個尚未重構的 legacy getter，避免了擴大修改範圍帶來的風險。
2. **測試設計嚴謹**：測試案例 `ListManager_without_HttpContext_does_not_add_process_cache_entries_after_repeated_access` 確實模擬了無 HttpContext 的情境，重複存取 1,000 次並驗證快取項目數為 0，能精準抓住原始 Bug。
3. **資源清理徹底**：測試在 `finally` 區塊中對 `ToolUtilityFactory` 進行了 `ResetInstance` 與 static 欄位清理（`_configuration`、`_ambientService`、`_tracer`），並對 `Tracer` 與 `Provider` 進行了 `Dispose`，完全避免了單元測試執行過程中的記憶體與資源洩漏。
