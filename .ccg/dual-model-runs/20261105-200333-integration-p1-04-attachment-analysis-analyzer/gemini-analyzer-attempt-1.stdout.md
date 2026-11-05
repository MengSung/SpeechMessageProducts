# Gemini Design Analyst 整合分析報告
**任務代號：** `integration-p1-04-attachment-analysis`  
**分析範圍：** `AttachmentService.cs`, `IAttachmentService.cs`, `AttachmentServiceTests.cs`, 以及已實現核心的 `EntityQueryService.cs`  
**輸出語言：** 繁體中文  
**分析原則：** 唯讀、零寫檔、依循雙模型評估框架（Critical / Warning / Info）

---

## 1. UX Analysis (使用者體驗與行為影響評估)
- **錯誤訊息固定化與安全性：**
  - 當前 `AttachmentService` 捕捉例外時會回顯 `DateTime.Now` 以及動態 `ex.Message`（內含實體 ID 與名稱）。這可能導致敏感的資料庫欄位或內部識別碼外洩至日誌或前端顯示。
  - **建議：** 改為固定英文錯誤訊息（如 `InvalidOperationException` 保持一致的標準前綴），並透過 InnerException 保留原始除錯脈絡，符合產品 CJK=0（核心錯誤訊息不帶在地化或動態業務隱私）的安全要求。
- **邊界傳輸與資源防護：**
  - 附件上傳涉及 `byte[]` 轉 Base64，當檔案過大時會造成記憶體倍增與峰值壓力。
  - 建議引入不可變的傳輸限制設定（例如 `AttachmentTransferLimits`：預設單檔 5MiB、單頁編碼本文 32Mi 字元、最多 5000 列），在進行 Base64 編碼與 I/O 呼叫前先行驗證，防止記憶體遭無界耗盡（DoS 潛在風險）。

---

## 2. Design Evaluation (設計系統與架構一致性)
- **依賴注入與建構式設計：**
  - 現有 `AttachmentService` 的 ctor 接收 `object logger` 與 `IOrganizationService organizationService`，但這兩個欄位在實際方法中並未使用，方法改以 `ref IOrganizationService crmService` 傳入。
  - **設計對齊：** 應參考 `EntityQueryService` 的設計模式——保留唯讀的 null 驗證以維護相容性，但不保存未使用的 logger 或 SDK 欄位，確保物件生命週期乾淨、無隱含快取或執行緒污染。
- **SDK 邊界與 `ref` 參數保護：**
  - `ref IOrganizationService crmService` 在方法中被使用，必須確保呼叫端的 SDK 參照不會在方法內部被偷偷替換或呼叫 `Dispose`（SDK 的生命週期由 Host 控管，服務僅借入使用）。

---

## 3. Technical Considerations (前端與後端架構考量)
- **查詢與資料驗證 (Download / Upload)：**
  - **Download：** 應利用 `EntityQueryService`（或內部區域個體）來重用 annotation 的整頁穩定 ID 驗證（確保 `objectid` 等於 `entityId.ToString()` 且不包含重複或不合法的實體）。
  - **Upload：** 必須嚴格檢核空位元組（empty/null bytes）、空實體 ID、未支援的 entityName，並拒絕無效的 `annotation.objectid` 錯置關聯。
- **測試涵蓋率提升：**
  - 目前舊測試僅斷言 `Assert.True(true)` 或空集合，未落實真實的 SDK 邊界黃金案例（Golden Path）與例外斷言。
  - 必須補足至少 2-3 個成功案例（如：全查詢下載、空 bytes 上傳、特殊字元原樣上傳）以及安全性/防護測試（如：ArgumentException、Timeout 轉 InnerException、null/empty ID、容量超出拒絕等）。

---

## 4. Findings & Classification (問題發現與分類)

| 級別 | 類別 | 描述與檔案路徑 | 具體建議 |
| :--- | :--- | :--- | :--- |
| **Critical** | 安全與錯誤處理 | `AttachmentService.cs` 的 `catch` 區塊動態組合 `DateTime.Now` 與 `ex.Message`（含實體 ID/名稱）。 | 修正為固定英文錯誤訊息，確保 CJK=0 且不洩漏動態業務識別碼，並完整保留 `InnerException`。 |
| **Critical** | 資源安全與效能 | `UploadAttachment` 未限制 `byte[]` 大小，直接 `Convert.ToBase64String` 可能引發記憶體用盡與資源峰值。 | 引入 `AttachmentTransferLimits` 限制（如單檔 5MiB），在 I/O 前先行攔截超限請求。 |
| **Warning** | 架構一致性 | `AttachmentService` 的 ctor 接收 `logger` 與 `organizationService` 但實際未保存使用，欄位懸空。 | 參考 `EntityQueryService`，移除未使用的欄位保存，僅保留 null 驗證與 `ref` 區域呼叫。 |
| **Warning** | 測試品質 | `AttachmentServiceTests.cs` 包含 `Assert.True(true)` 的虛假斷言。 | 保留舊測試的同時，補上真正的 SDK 邊界黃金案例與異常處理斷言。 |
| **Info** | 設計規範 | `EntityQueryService` 已驗證透過 `ColumnSet(true)` 與 `ValidatePage` 確保資料完整性。 | 於 `AttachmentService` 的下載邏輯中，可透過局部實例化或重用查詢服務確保 `objectid` 綁定正確。 |

---

## 5. Recommendations (建議與紅綠重構清單)

1. **紅綠重構步驟 (Red-Green Refactoring Checklist)：**
   - **Phase Red：** 撰寫涵蓋錯誤訊息固定化、容量限制、`ref IOrganizationService` 保持不變以及參數驗證的單元測試（預期失敗）。
   - **Phase Green：** 修改 `AttachmentService.cs`：
     - 移除未使用的 `_logger` 與 `_organizationService` 欄位儲存（僅保留建構式 null 檢查）。
     - 將 `catch` 內的動態字串改為固定英文錯誤訊息並保留 `ex` 作為 `InnerException`。
     - 在 `UploadAttachment` 中加入對 `documentbody` 的長度限制檢查（依循安全傳輸上限）。
     - 在 `DownloadAttachment` 與 `UploadAttachment` 中強化對 `entityId`、`entityName` 及 SDK 回傳資料的驗證。
   - **Refactor / Git MV：** 確保 namespace 與 public signature 完全相容，不破壞既有呼叫端（如 `ToolUtilityClass` 轉接）。

2. **偏好方案總結：**
   遵循 `EntityQueryService` 的極簡無狀態、零快取、不接管 SDK `Dispose` 權限的設計哲學，確保 `AttachmentService` 在 .NET Framework 4.6.2 與 .NET 10 雙環境下的行為一致、安全且具備高強健性。
