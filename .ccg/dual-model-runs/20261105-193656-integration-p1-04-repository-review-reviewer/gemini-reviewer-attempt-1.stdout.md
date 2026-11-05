# Gemini UI Reviewer / Frontend & Component Quality Review

> **Reviewer**: Senior UI Reviewer (Gemini Dual-Model Workflow)  
> **Target**: P1-04 Repository slice & related Core data operations (`ToolUtility.Core` & `ToolUtility.Tests`)  
> **Language**: 繁體中文  
> **Sandbox Status**: READ-ONLY / No File System Write Permission  

---

## 1. Summary (整體評估)

本次 P1-04 Repository 抽取與安全強化切片（包含 `EntityRepository`、`EntityQueryService`、`CoreTargetFrameworkTests` 以及相關 Characterization / Safety 測試）在架構設計、安全防禦（fail-closed）、型別安全（.NET Framework 4.6.2 / .NET 10 雙目標宿主與 Nullability）以及資源生命週期管理上表現非常嚴謹。

主要優點：
1. **嚴格的生命週期與範圍隔離**：Repository 與查詢轉接器僅「借入」SDK 授權範圍（scope），不自行建立連線、快取或背景佇列，並透過 `using` 確保 Query 服務的資源釋放，不增加既有呼叫端的 Dispose 負擔。
2. **防禦性程式設計（Fail-Closed）**：新增了對空名稱、無效欄位、重複識別（duplicate IDs）、跨表回應以及 FetchXML DTD 注入的防禦，在資料進入或離開 SDK 邊界時進行嚴格驗證。
3. **異常契約相容性**：精確區分了 SDK 自身的 `ArgumentException` 與轉接器包裝，保留原始的 `InnerException` 與固定外層訊息，避免破壞呼叫端既有的異常處理與通知邊界。
4. **雙目標與 strict 編譯**：針對 .NET Framework 4.6.2 與 .NET 10 採用條件編譯（如 `ArgumentNullException.ThrowIfNull` vs 顯式檢查），維持零警告、零錯誤的 strict 規範。

---

## 2. Accessibility Issues (可訪問性 / 無障礙問題)
*(不適用：本切片為後端資料存取與 Repository 核心邏輯層，無前端 UI 介面或 DOM 結構互動)*

---

## 3. Design & Architecture Issues (設計與架構問題)

| 項次 | 類別 | 檔案路徑 | 說明 | 嚴重性 |
| :---: | :--- | :--- | :--- | :--- |
| 1 | 架構設計 | `ToolUtility.Core/EntityOperations/EntityRepository.cs` | 類別建構子中建立無狀態 marker 傳遞給 `EntityCrudService` 與 `OwnerManagementService`。雖然符合當前重構過渡期的簽章相容，但實務上 marker 略顯形式化。 | **Info** |
| 2 | API 設計 | `ToolUtility.Core/EntityOperations/EntityQueryService.cs` | `RetrieveEntity` 三參數 overload 選擇不加入介面 `IEntityQueryService` 以避免破壞外部實作者，這是一個務實的防禦性相容決策，但長遠來看若介面需要統一，後續版本可考慮加入具預設實作的介面成員或獨立擴充方法。 | **Info** |

---

## 4. Suggestions & Improvements (改進建議)

1. **診斷與 Trace 記錄**：
   - 程式碼已移除原先可能洩漏個資或業務資料的 `Trace.WriteLine` 內容，改由安全測試驗證不外洩 `RecordId` 或原始例外訊息。建議未來若需保留可觀測性（Observability），應採用結構化記錄（Structured Logging）並對敏感識別碼進行遮罩（Masking）。
2. **單元測試涵蓋範圍**：
   - 現有的 `RepositorySafetyTests` 與 `RepositoryCallerRouteTests` 涵蓋了 1000 次並行隔離、故障重試及 12 案消費點路由，測試品質極高。建議在後續 Phase 5 完整方案驗收時，持續維持這套嚴格的斷言機制。

---

## 5. Positive Notes (表現優異之處)

- **嚴格遵守唯讀與授權 scope**：明確規範 Repository 不保存 Entity、Query 或回應，避免了跨使用者／租戶的狀態汙染。
- **執行緒與記憶體安全**：透過弱參照（WeakReference）與並行壓力測試驗證操作完成後物件可被正確回收，且沒有殘留全域引用。
- **繁體中文與檔案編碼註解**：程式碼與技術報告均遵循 UTF-8 與繁體中文註解規範，脈絡清晰，合規性良好。

---

## 6. Reviewer Conclusion (審查結論)

- **審查結論**：**[PASS]** (本切片符合 P1-04 既定規格與安全紅綠標準，無關鍵性缺陷)
