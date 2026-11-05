根據本次 P1-04 Fee 安全實作與雙目標抽取切片（`integration-p1-core-fee`）的原始碼與測試範圍，以下為獨立的程式碼審查報告。

---

# 整合審查報告 (Integration Review: P1-04 Core Fee)

## 1. 審查摘要 (Summary)
本次變更將 `FeeService` 成功獨立至 `ToolUtility.Core`（支援 `net462` 與 `net10.0` 雙目標），修復了原先 Fee 的兩個空方法，並透過安全的 FetchXML 建構、`RequireId` / `RequireText` 驗證以及發布前的穩固 ID 唯一性檢查（`HashSet<Guid>`），確保所有查詢條件與回傳結果合乎 Dataverse SDK 契約。

整體架構設計嚴格遵守：
- **不 Dispose 借入之 SDK 服務** (`IOrganizationService`)。
- **不跨租戶 Singleton、不自建快取與背景工作**。
- **原始 SDK 例外原樣向上傳遞**，交由最終 Host 邊界處理。
- **XML 安全編碼**（使用 `XElement` / `XAttribute` 代替字串拼接）。

---

## 2. 發現與分類 (Findings)

### 【Critical】無 (None)
*未發現會導致安全性漏洞、記憶體流失或破壞 SDK 契約的嚴重錯誤。*

### 【High】無 (High)
*未發現阻礙編譯、跨目標相容性或違反設計規範的高風險問題。*

### 【Warning】
1. **`FeeService` 內部建構子委派與相容性重載**
   - **來源路徑**：`ToolUtility.Core/FeeOperations/FeeService.cs` (行 45-56)
   - **說明**：過載建構子 `FeeService(object? logger, IOrganizationService organizationService)` 雖然明示不保存、不使用 logger，但在 `FetchXmlQueryService` 中直接 `new FeeService(_organizationService)` 呼叫主要建構子。建議確保未來擴充時不會誤用舊有帶 `logger` 的建構子引入隱含依賴。
   - **修正與測試**：現有測試已覆蓋建構子檢核（如 `Constructor_NullService_RejectsImmediately`），建議維持現狀。

2. **日期範圍查詢格式化語系相依性風險（已處理但需注意）**
   - **來源路徑**：`ToolUtility.Core/FeeOperations/FeeService.cs` (行 126-127)
   - **說明**：`startDate.ToString("yyyy-M-d", CultureInfo.InvariantCulture)` 使用了 `InvariantCulture`，能有效避免因主機 ambient culture 不同而產生的日期格式錯誤。單元測試 `DateRange_ThaiCulture_UsesGregorianQuery` 也驗證了這點。

### 【Info】
1. **並行安全性 (Thread Safety)**
   - `FeeService` 為無狀態設計（Stateless），每次呼叫均建立新的 `XElement` 查詢樹與區域 `HashSet<Guid>`，在 `Parallel.For` 1000 次交錯測試中展現良好的執行緒安全與無狀態隔離特性。

---

## 3. 建議與改進 (Suggestions)
- **持續維持 Golden XML 檢核**：現有的 `GoldenQueryAssertions` 與內嵌資源確保了 FetchXML 在重構前後完全一致，建議後續若新增查詢條件，應繼續沿用此黃金測試模式。
- **保持 SDK 例外透明傳遞**：維持目前「不吞例外、不包裝私密訊息」的設計，確保 Host 端的通知機制能夠準確捕捉原始例外。

---

## 4. 正式優點 (Positive Notes)
- **徹底杜絕 XML 隱含注入**：全面屏棄過往危險的字串串接（例如 `$"'{contactName}'"`），改用 `XElement` 與屬性綁定，徹底確保所有特殊字元安全轉義。
- **嚴格的 ID 驗證機制**：在 `Query` 方法中加入重覆 ID 與空實體檢查（`identities.Add(row.Id)`），防止資料污染或不合法的實體列流向業務邏輯。
- **雙目標與單元測試覆蓋完整**：支援 `net462` / `net10.0` 雙目標建置，且測試涵蓋了異常傳遞、並行執行、特殊字元及極端輸入拒絕（Fail-Fast）。

---

## 5. 驗收建議
- **推薦狀態**：`[PASS]`（在本次唯讀審查與現有測試證據基準下，程式碼符合設計規範與安全性要求）。
