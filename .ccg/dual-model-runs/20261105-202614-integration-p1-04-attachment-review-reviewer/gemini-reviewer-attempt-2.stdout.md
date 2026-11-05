# 整合 P1-04 附件切片 (Attachment Slice) 程式碼與架構審查報告

本報告針對 P1-04 附件切片（`AttachmentService`、`IAttachmentService`、`AttachmentTransferLimits` 以及相關 Characterization 與 Safety 測試、Caller Route 測試）進行獨立的 UI / 服務層架構與程式碼品質審查。

審查分類標準：**Critical**（嚴重缺陷／安全性漏洞）、**Warning**（潛在風險／設計瑕疵）、**Info**（建議改進／風格優化）。

---

## 一、 摘要 (Summary)

本次 P1-04 附件重構將舊版 `AttachmentService` 成功移植至 `ToolUtility.Core` 雙目標架構中，同時嚴格遵守了不可更換命名空間、保持 `ref IOrganizationService` 簽章、不自行 Dispose 外部 SDK 等既有契約。

在安全性與邊界防護方面，本切片表現極為優秀：
1. 實作了有界預算（`AttachmentTransferLimits`），對單檔位元組數、每頁 Base64 編碼字元總數與最大列數進行了嚴格防護。
2. 針對 Base64 字串進行線性掃描（不額外配置解碼記憶體），正確驗證 padding 與有效符號，防止大檔案或畸形字串攻擊。
3. 保持了與舊 host 的向下相容性，同時消除了舊實作中可能造成記憶體殘留或非預期 SDK 參考保留的問題。

---

## 二、 發現與分類 (Findings)

### 1. Critical (嚴重)
* **無 (None)**：本次審查未發現違反安全性契約、記憶體洩漏、或破壞外部 API 簽章的 Critical 級別缺陷。

### 2. Warning (警告)
* **W1: `DownloadAttachment` 中 Base64 字串驗證與實質解碼的時機落差**
  * **檔案路徑**：`ToolUtility.Core/AttachmentOperations/AttachmentService.cs`
  * **情境與說明**：在 `DownloadAttachment` 期間，程式碼會透過 `ValidateBody(body)` 對 Base64 進行字元與長度驗證，但下載回傳的 `EntityCollection` 中的 `documentbody` 仍保持原始字串。雖然這保留了原有的回傳格式契約，但當呼叫端後續對該 Base64 進行 `Convert.FromBase64String` 時，若來源資料在 CRM 端已被污染或格式邊緣合規但無法順利解碼，錯誤將延遲至呼叫端爆發。
  * **建議**：建議在 `ValidateBody` 內或下載驗證階段考慮加入選擇性的輕量解碼探測（或在規格文件明載呼叫端的解碼責任），以確保資料完整性。

### 3. Info (建議／資訊)
* **I1: 錯誤訊息的統一性與封裝**
  * **檔案路徑**：`ToolUtility.Core/AttachmentOperations/AttachmentService.cs`
  * **情境與說明**：所有例外均被攔截並重新包裝為固定英文訊息（如 `"The attachments could not be downloaded."`），並保留原 `InnerException`。這符合資安規範（不外洩內部組織 ID 或查詢細節），但對於除錯時的日誌追蹤可能會增加定位難度。由於設計已明列「Core 非最終 host 通知邊界，不引入新 logger」，此作法在架構上合理，但呼叫端需確保有妥善記錄 `InnerException`。
* **I2: 條件編譯 `#if NETFRAMEWORK` 與現代 C# API 的相容性**
  * **檔案路徑**：`ToolUtility.Core/AttachmentOperations/AttachmentService.cs` 與 `AttachmentTransferLimits.cs`
  * **情境與說明**：程式碼針對 `.NET Framework 4.6.2` 與 `.NET 10` 分別使用了 `ArgumentNullException` 與 `ArgumentOutOfRangeException` 的現代靜態檢查 API 巨集。這展現了高品質的雙目標相容性維護，建議後續版本升級時持續確保巨集分支的單元測試涵蓋率。

---

## 三、 建議與改進 (Suggestions)

1. **維持測試基準**：現有的 `AttachmentCharacterizationTests`、`AttachmentSafetyTests` 與 `AttachmentCallerRouteTests` 涵蓋了非常嚴密的邊界條件（如空 bytes、錯 parent、並行 A/B 隔離、weak ctor 弱參照回收等），應在後續 P1-05 ~ P1-12 的推進中作為迴歸測試的黃金標準。
2. **容量預算文件化**：`AttachmentTransferLimits` 預設的 5 MiB raw、32 Mi 字元、5000 列是本地安全預算。建議在部署至正式 D365 環境前，配合主機端的組織政策進行調整與驗證。

---

## 四、 正面評價 (Positive Notes)

* **嚴格遵守契約**：完全保留了原 namespace `ToolUtilityNameSpace.AttachmentOperations`、`IAttachmentService` 介面簽章，以及 `ref IOrganizationService` 傳遞方式，未強迫呼叫端改寫架構。
* **記憶體與資源隔離**：成功移除舊構造式中不必要的 logger 與 SDK 參考保留，確保實例可被 GC 回收，且不會在 `using` 結束時錯誤處置（Dispose）外部傳入的共用 SDK 連線。
* **高效的 Base64 驗證**：`ValidateBody` 採用線性掃描與 `long` 計數，避免了巨型附件在下載驗證時頻繁配置記憶體所造成的效能損耗。

---

## 審查結語 (Recommendation)

- **審查結果**：**[PASS]** (本切片品質優良，符合 P1 階段的安全與架構設計規範，可進入後續整合流程。)
