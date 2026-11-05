根據提供的專案快照與程式碼變更清單，針對 **P1-04 屬性工具 / SDK extension / Constants 增量唯讀 Code Review**，以下為獨立審查報告。本審查嚴格遵循唯讀沙箱限制與指定的審查標準，未直接對檔案系統進行任何寫入或執行。

---

# 整合切片 P1-04 UI / 程式碼審查報告
**審查目標**：`ToolUtility.Core` 中的屬性工具 (`EntityAttributeUtilityService`)、SDK 擴充 (`OrganizationServiceExtensions`)、查詢服務 (`EntityQueryService`) 與常數目錄 (`CrmEntityColumns`) 及其對應測試。

---

## 一、 摘要 (Summary)
本切片成功將 `EntityAttributeUtilityService`、`OrganizationServiceExtensions` 與 `CrmEntityColumns` 遷移至 `ToolUtility.Core`，並在雙目標架構 (`net462` / `net10.0` 產品與 `net48` / `net10.0` 測試宿主) 下維持完整的測試覆蓋率（net48 達 99.36%，net10.0 達 100%，合計通過 395/314 測試案例）。
整體架構設計落實了「Fail-Closed（失敗即關閉）」、不跨請求殘留狀態、弱引用回收以及不包裝 SDK 真實例外（除既有公開相容的 `InvalidOperationException` 外層固定訊息與保留 `InnerException` 外）等安全與合規要求。程式碼品質極高，未發現 Critical 級別漏洞。

---

## 二、 發現與分類 (Findings)

### 1. Critical 級別
- **無 (None)**：未發現任何會導致安全性漏洞、記憶體流失、執行緒不安全或契約違規的嚴重缺陷。

### 2. Warning 級別
- **[Warning-01] `EntityAttributeUtilityService` 建構子對 logger 的相容保留與零使用**
  - **位置**：`ToolUtility.Core/AttributeOperations/EntityAttributeUtilityService.cs` (第 31-38 行)
  - **說明**：為維持向後相容，保留了接收 `object logger` 的建構子，並對 `null` 執行 `ArgumentNullException` 檢查，但內部不保存該物件。
  - **建議**：此設計符合既有相容契約，但應確保所有新呼叫端優先使用無參數建構子 (`EntityAttributeUtilityService()`)，避免傳遞無意義的 stub 物件。

- **[Warning-02] `OrganizationServiceExtensions.ExecuteRetrieveMultiple` 內的 `using var queries` 生命週期**
  - **位置**：`ToolUtility.Core/Extensions/OrganizationServiceExtensions.cs` (第 34 行)
  - **說明**：擴充方法中每次呼叫都會 `new EntityQueryService(organizationService: service)` 並包在 `using` 區塊中進行 Dispose。
  - **建議**：雖然設計上 `EntityQueryService.Dispose()` 只清除借入參照而不關閉 SDK 連線（符合預期），但高頻率的短暫實例化在高併發情境下會增加 GC 壓力。考量此為過渡期相容擴充，目前實作可接受，但長期建議呼叫端直接使用 `EntityQueryService` 執行個體。

### 3. Info 級別
- **[Info-01] `CrmEntityColumns` 的陣列 Clone 機制確保執行緒安全**
  - **位置**：`ToolUtility.Core/Constants/CrmEntityColumns.cs`
  - **說明**：各實體分類的 Property 均透過 `(string[])BasicColumns.Clone()` 回傳新陣列複本，完美防止多執行緒／多呼叫端共享陣列導致的資料汙染（已透過 `ColumnContractTests` 驗證）。

- **[Info-02] 嚴格 Nullable 標註與反射驗證**
  - **位置**：`ToolUtility.Tests/CoreTargetFrameworkTests.cs`
  - **說明**：透過 `NullabilityInfoContext` 動態反射驗證介面合約，確保除了 `attributeName` 允許 `Nullable` 外，其餘字串參數皆嚴格遵循 `NotNull` 契約，防範 oblivious 狀態漂移。

---

## 三、 建議與改善事項 (Suggestions)
1. **維持現有測試與 Golden 檔案鎖定**：`columns-all.xml` 成功鎖定 21 組欄位順序，建議日後若有 CRM 欄位擴充，必須確實同步更新黃金檔與特性測試。
2. **防範日後重構誤改相容契約**：在文件 (`wp-reports/P1-04-attribute-extension.md`) 中已明確記錄 `GetAttributeValue` / `RemoveAttribute` / `SetEntityAttributeToNull` 的行為差異與例外封裝原因，維護團隊後續切勿因「簡化例外」而移除既有的 `InvalidOperationException` 轉發機制。

---

## 四、 正面評價 (Positive Notes)
- **記憶體與生命週期管理**：透過弱引用 (`WeakReference`) 與 1,000 次並行 A/B 測試嚴格證明服務不殘留 Entity、logger 或 SDK 參考，實例化後資源回收正常。
- **安全性與防禦性編程**：FetchXML 解析與查詢驗證落實 DTD Prohibit、XmlResolver 為 null、根實體檢查及整頁穩定 ID 驗證，有效防止注入與惡意回應。
- **雙目標相容性**：同時支援 `net462` 與 `net10.0`，並在測試端確實透過反射證明組件載入正確的產品目標，無假陽性。

---

## 五、 最終結論
- **審查狀態**：`PASS` (通過)
- **建議動作**：可繼續推進後續的 P1 整合任務。
