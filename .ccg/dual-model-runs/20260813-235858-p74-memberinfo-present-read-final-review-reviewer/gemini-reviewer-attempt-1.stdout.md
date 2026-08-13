# UI/API 遷移審查報告：p74-memberinfo-present-read-final-review

本審查針對 `memberinfo.present.retrieve.by.contact` / `ORG-CALL-00026` 的獨立門控、僅限本地的遷移進行靜態代碼審查。

---

## 1. 總體評估 (Summary)
本次變更為 `ORG-CALL-00026` 實現了完全獨立且安全的本地遷移路徑。代碼嚴格遵守了 DTO-only、伺服器端授權驗證、防禦性拷貝、單頁有界查詢以及 fail-closed 等安全屬性。門控預設關閉，且 rollback 機制安全，不會對現有 legacy 流量或資源造成干擾。

---

## 2. 審查發現分類 (Findings)

### 🔴 Critical (嚴重問題)
*無*。未發現任何違反安全屬性、越權或資源洩漏的嚴重問題。

### 🟡 Warning (警告事項)
*無*。代碼結構與測試覆蓋率均符合高標準。

### 🔵 Info (提示資訊)
#### 1. 原始碼檔案註解編碼問題
* **具體檔案**：
  * `SpeechMessage.Dynamics.Connectors.Data8/Package02Data8PresentRecordReadOperations.cs`
  * `SpeechMessage.Dynamics.ProductClient/MemberInfo/IMemberInfoPresentRecordReadClient.cs`
  * `SpeechMessage.Dynamics.ProductClient/MemberInfo/MemberInfoPresentRecordReadClient.cs`
  * `SpeechMessageProducts.ChurchReport/Services/MemberInfo/Package02MemberInfoPresentRecordReadService.cs`
  * 以及相關測試檔案。
* **證據與說明**：上述檔案中的中文註解在讀取時呈現亂碼（例如 `瑼?嚗peechMessage.Dynamics.Connectors.Data8/...`），這通常是因為檔案儲存時使用了非 UTF-8（如 Big5）編碼，或在 Git 傳輸過程中編碼未統一。
* **建議**：雖然這不影響程式編譯與執行，但為了維護性，建議將所有新建立的原始碼檔案統一轉換為 **UTF-8 with BOM** 編碼，以確保在所有開發環境與編輯器中中文註解皆能正常顯示。

#### 2. 深拷貝效能開銷
* **具體檔案**：`SpeechMessageProducts.ChurchReport/Services/MemberInfo/Package02MemberInfoPresentRecordReadService.cs` L175-186
* **證據與說明**：
  ```csharp
  public IReadOnlyList<MemberInfoPresentRecordReadDto> GetRows()
      => new ReadOnlyCollection<MemberInfoPresentRecordReadDto>(
          _rows.Select(row => new MemberInfoPresentRecordReadDto { ... }).ToList());
  ```
  每次調用 `GetRows()` 都會透過 LINQ `Select` 建立全新的 DTO 實例列表。
* **建議**：此設計為了絕對的防禦性拷貝安全（防止呼叫端修改內部狀態）是完全正確且符合安全要求的。由於出席紀錄單頁上限為 128 筆，此效能開銷極小，在此僅作提示，無需修改。

---

## 3. 關鍵安全屬性核對 (Security & Correctness Verification)

| 安全屬性 / 要求 | 核對結果 | 具體程式碼證據 |
| :--- | :---: | :--- |
| **門控預設關閉 (Base & Sub-gates remain false)** | **符合** | `appsettings.json` L605 與 `appsettings.Development.json` L19 中的 `"Package02MemberInfoPresentReadEnabled": false` 均為 `false`。 |
| **False 門控保留 Legacy 路由** | **符合** | `MemberInfoController.cs` L630-633：若門控關閉，直接導向 `LoadContactPresentRecordsLegacy`。 |
| **True 路徑在 Dispatch 前完成伺服器授權** | **符合** | `MemberInfoController.cs` L722-726：在建立 Client 之前，先執行 `EnsureCorrectUserData()` 與 `CanViewContact(contactGuid)`。 |
| **無瀏覽器擁有的 Profile/Workload 權限** | **符合** | `MemberInfoController.cs` L728-733：`ProfileAlias` 來自伺服器端設定，`WorkloadSubjectId` 為 Service 內部常數，瀏覽器僅提供 `contactId` 作為 locator。 |
| **True 路徑為 DTO-only (無 SDK/ToolUtility 依賴)** | **符合** | `LoadContactPresentRecordsTypedAsync`、`Package02MemberInfoPresentRecordReadService` 與 `MemberInfoPresentRecordReadClient` 均不依賴 `IOrganizationService` 或 `ToolUtility`，且無 retry/fallback。 |
| **Data8 查詢固定、限單頁且 Fail-Closed** | **符合** | `Package02Data8PresentRecordReadOperations.cs` L72-76 限制 `ceVersion == "9.1"`；L89-92 檢查 `page.MoreRecords` 拋出異常；L107-112 驗證重複 ID 與 byte 上限。 |
| **防禦性 Request-Local 快照** | **符合** | `Package02MemberInfoPresentRecordReadResult` 建構子與 `GetRows()` 均執行了完整的深拷貝，杜絕跨用戶或跨 Profile 的資料洩漏。 |
| **FullName 保持 Legacy 語意且無二次 Retrieve** | **符合** | `Package02Data8PresentRecordReadOperations.cs` L201-210 使用 `Inner Join` 關聯 `contact` 表並投影 `fullname`，避免了第二次 CRM 查詢。 |

---

## 4. 審查結論 (Recommendation)

### **PASS**

本變更在架構設計、安全防護與測試覆蓋率上均表現優異，完全符合 `ORG-CALL-00026` 的遷移規範。建議在合併前將檔案編碼統一轉換為 UTF-8 以解決註解亂碼問題。
