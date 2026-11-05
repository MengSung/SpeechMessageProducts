# Gemini UI / Code Reviewer 審查報告

> **審查目標**：Integration P1-04 Attachment 切片（`AttachmentService`, `IAttachmentService`, `AttachmentTransferLimits`, 相關測試及 Golden XML）
> **角色與限制**：唯讀 sandbox、繁體中文輸出、依據 CCG reviewer 規範分類為 Critical / High / Warning / Info。

---

## 一、 摘要 (Summary)

本次 P1-04 附件（Attachment）切片重構將原本的 ToolUtility 附件操作遷移至 `ToolUtility.Core`（支援 `.NET Framework 4.6.2` 與 `.NET Core 10.0` 雙目標），並引入了嚴格的有界資源控制 (`AttachmentTransferLimits`)、Base64 線性掃描驗證（不配置額外解碼副本）、借入式 `ref IOrganizationService` 生命週期隔離以及防禦性例外處理。

經詳細審查程式碼、單元測試、特徵化測試 (`AttachmentCharacterizationTests`, `AttachmentSafetyTests`, `AttachmentCallerRouteTests`) 與 Golden XML 檔案：
- **相容性與契約**：完整保留原命名空間、介面名稱、`ref` 參數語義、舊建構式相容與 7 欄附件格式。
- **安全與強固性**：在 `SDK` 呼叫前加入多層次的邊界檢查（如容量限制、Base64 padding/長度合法性、實體 ID 與 Parent 關聯驗證），並採用 Fail Closed 原則。
- **測試覆蓋**：附帶完整的黃金檔（Golden XML）與安全性負向測試（Negative Tests），無直接硬編碼與記憶體洩漏風險（WeakReference 驗證通過）。

整體程式碼品質極高，設計架構嚴謹，符合 .NET 嚴格模式（`#nullable enable`, strict 0 警告錯誤）。

---

## 二、 發現與分類 (Findings & Classifications)

### Critical / High
- **無（None）**：未發現會導致執行期嚴重崩潰、記憶體洩漏、安全性漏洞或破壞既有外部合約（API Contract）的缺陷。

### Warning (警告)
1. **`DownloadAttachment` 中的 `using var reader = new EntityQueryService(organizationService: crmService);` 資源處置行為**
   - **位置**：`ToolUtility.Core/AttachmentOperations/AttachmentService.cs` (Line 72)
   - **情境與說明**：程式碼中使用 `using var reader = new EntityQueryService(...)`。雖然註解已明確指出該 `using` 僅用於清除查詢轉接器借用參照、不處置外部 `crmService` SDK，但在某些靜態分析工具或團隊開發者的直覺中，`using` 容易被誤認為會呼叫傳入服務的 `.Dispose()`。
   - **建議**：維持現狀的註解說明即可，或考慮將 `EntityQueryService` 的具體實作生命週期交由呼叫端或工廠控制，避免在方法內頻繁 `new` 物件（儘管 `EntityQueryService` 本身為輕量轉接器）。

2. **Base64 空白與字元掃描的效能與異常訊息粒度**
   - **位置**：`ToolUtility.Core/AttachmentOperations/AttachmentService.cs` (`ValidateBody`)
   - **情境與說明**：`ValidateBody` 對 Base64 字串進行逐字元線性掃描。當遇到不合法字元或 padding 錯誤時，統一拋出 `InvalidOperationException("The attachment Base64 data is invalid.")`。
   - **建議**：雖然錯誤訊息統一有助於防範資訊外洩（符合資安規範），但在內部日誌或偵錯時，若能有更細緻的例外原因（保留在 InnerException 或 debug 記錄中）將有助於遠端診斷。目前設計為了符合「固定英文包裝不含 ID、名稱、時間、原訊息」之安全約束，故此設計可被接受。

### Info (資訊)
1. **雙目標編譯與條件編譯巨集 (`#if NETFRAMEWORK`)**
   - **位置**：多個檔案中的 `ArgumentNullException.ThrowIfNull` 與 `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`。
   - **說明**：為相容 `.NET Framework 4.6.2` 與 `.NET Core 10.0` 做了精確的平台分支處理，並通過了 `CoreTargetFrameworkTests` 的組件目標反射驗證，確保輸出正確無誤。

2. **記憶體與執行緒安全設計**
   - **位置**：`AttachmentSafetyTests.ConcurrentRefScopes_NoCrossTalkAndDrain`
   - **說明**：1000 次並行 A/B 測試驗證了無狀態服務在多執行緒交錯呼叫下不會發生資料串擾（Cross-talk），且 `active = 0` 與 `WeakReference` 測試證明了未使用的建構式參數與借入 SDK 不會被錯誤延長生命週期或強行 Dispose。

---

## 三、 建議與改善 (Suggestions)

1. **維持現有測試基準**：建議在後續整合測試（P1 剩餘切片）中，持續維持與本切片相同的 Moq Strict 模式與 Golden XML 比對機制。
2. **XML 註解繁體中文維護**：所有新增與修改的型別、方法均維持了高標準的繁體中文 AI 檔案註解與維護脈絡說明，符合專案規範，建議後續切片繼續保持。

---

## 四、 正面評價 (Positive Notes)

- **優異的防禦性編程 (Defensive Programming)**：在進入 CRM SDK 呼叫前，對 `entityName`, `toBeAttachedEntityId`, `documentBody`, `_limits` 進行了全面的防呆與邊界檢查。
- **嚴格遵循介面與不變性契約**：`AttachmentTransferLimits` 的屬性設為唯讀 (`{ get; }`)，且拒絕無界預設（預設提供 5 MiB / 32 Mi 字元 / 5000 列的安全本地預算），防止潛在的 DoS 或記憶體爆量風險。
- **完美的版控與重構紀律**：舊檔透過 `git mv` 移至 Core，新限制與測試獨立建置，且編譯結果達到嚴格的 0 警告、0 錯誤。

---

## 審查結論 (Recommendation)

**RECOMMENDATION: PASS**
