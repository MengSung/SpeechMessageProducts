依照提示所提供的完整原始碼快照、基準差異與測試證據，以下針對 **integration-p1-04-entity-di** 進行獨立的程式碼審查與架構評估。

---

# 程式碼審查報告 (Code Review Report)
**審查目標**：P1-04 Fee 安全實作與雙目標抽取切片（包含 `FeeService`、`EntityQueryService`、`ServiceCollectionExtensions` 及相關合約與測試）。
**審查範圍**：僅針對提示中明列的檔案與程式碼進行唯讀審查。

---

## 總結評估 (Executive Summary)
本階段的實作展現了極高的工程水準：
1. **安全性與防護**：全面使用 XML 安全編碼、拒絕空白參數、防範 XML 注入，且在處理識別碼時嚴格進行 GUID 格式驗證。
2. **資料完整性與封裝**：引入 `ValidatePage` 機制，確保所有由 SDK 返回的實體集合皆具備非空、不重複且符合邏輯名稱的穩定識別碼（Stable ID），並禁止靜默去重或漏報重複。
3. **DI 與生命週期管理**：正確實作 scoped 服務生命週期，不捕獲 request-scoped 服務於 singleton、不外洩連線生命週期、確保借入的 SDK 服務不被隨意 `Dispose`。
4. **雙目標相容性**：成功在 `net462` / `net10.0` (Core) 與 `net48` / `net10.0` (Tests) 雙目標下維持嚴格編譯，並保留了對應的 characterization 測試與黃金檔比對。

整體架構設計穩健，未發現嚴重缺陷（Critical / High）。以下列出幾項微調建議（Warning / Info）。

---

## 審查發現 (Findings)

### 1. 資訊與防禦性提醒 (Info)
* **檔案**：`ToolUtility.Core/FeeOperations/FeeService.cs` 與 `ToolUtility.Core/EntityOperations/EntityQueryService.cs`
* **說明**：
  在 `ValidatePage` 方法中，當檢測到重複的 `Guid.Id` 或無效的實體時，會直接拋出 `InvalidOperationException("Query page contains invalid or duplicate database identities.")`。
  * **評估**：此設計符合規格（拒絕發布不安全或衝突的整頁資料），且未將敏感的識別碼或本文回顯到例外訊息中，符合資安規範。

### 2. 設計一致性提醒 (Info)
* **檔案**：`ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs`
* **說明**：
  舊的 `AddToolUtility()` 無參數多載標註了 `[Obsolete("請改用 AddToolUtility(configuration, product) 明確指定產品；此相容多載預定於階段 6 移除。")]`。
  * **評估**：這為系統未來的階段性重構提供了清晰的過渡路徑，同時確保現有 `ChurchReport` 呼叫端在階段 6 之前不會中斷。

---

## 驗證報告對照 (Scoring & Validation)

依據提示要求的評分格式與現有證據：

```
VALIDATION REPORT
=================
User Experience: 20/20 - 支援雙目標、完整的相容性 API 與明確的過期警告。
Visual Consistency: N/A (後端/SDK核心模組無UI)
Accessibility: N/A (後端/SDK核心模組)
Performance: 19/20 - 無不必要的快取、無背景佇列污染，每次操作維持獨立 Scope 與局部變數配置。
Browser Compatibility: 20/20 - .NET Framework 4.6.2 / .NET 10 雙目標編譯與嚴格模式驗證通過。

TOTAL SCORE: 59/60 (相等於 98.3/100)

ISSUES FOUND:
- 無 Critical 或 Warning 級別問題。
- 現有程式碼在安全編碼、ID 完整性發布與 DI 隔離上表現優異。

RECOMMENDATION: PASS
```

---

## 建議與改進 (Suggestions)
1. **持續維護黃金檔 (Golden Files)**：在後續擴充其他 Query 方法時，應繼續維持現有的 `GoldenQueryAssertions` 與 XML 結構比對模式，確保對外發布的 FetchXML 查詢條件不被意外改動。
2. **依賴注入驗證**：現有的 `ProductRegistrationTests` 透過白箱反射方式驗證 `BoundedClientPool` 的內部 key，既達到了測試覆蓋目的又保護了公開 API 的簡潔性，建議後續若有類似底層狀態驗證，可沿用此模式。

## 正面評價 (Positive Notes)
* **嚴格的 ID 驗證**：所有回傳集合在發布前均經過 `HashSet<Guid>` 的重複性與合法性檢查，防止資料污染。
* **正確的記憶體與連線管理**：明確釐清了「借入 SDK 不 Dispose」的原則，並在 `EntityQueryService` 中實作了安全執行緒的 `Dispose` 與 `GetService` 檢查。
* **文化與時區隔離**：在 `FeeService` 的日期範圍查詢中，透過區域設定隔離與明確的 `InvariantCulture` 輸出，確保了跨語系/跨曆法環境（如泰國曆）下的查詢一致性。
