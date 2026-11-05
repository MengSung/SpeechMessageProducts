# 整合 P1-04 Attachment 切片：資安、架構與品質審查報告

本報告針對 `P1-04 Attachment` 相關之 C# 程式碼切片（包含 `AttachmentService`、`IAttachmentService`、`AttachmentTransferLimits` 以及相關測試案例）進行獨立唯讀審查。

---

## 1. 摘要 (Summary)
本次 P1-04 附件切片重構將原先相依於舊版 `ToolUtility` 的附件操作成功轉移至 `ToolUtility.Core` 雙目標架構（`.NET Framework 4.6.2` 與 `.NET CoreApp 10.0`），同時維持了原有的 namespace `ToolUtilityNameSpace.AttachmentOperations` 與 API 簽章。

整體設計在**資源有界性 (Bounded Resources)**、**記憶體隔離**、**非破壞性借入 SDK (Ref-based SDK borrowing)** 以及 **Base64 線性驗證效能**上表現極佳。測試案例覆蓋率高，且透過黃金 XML 檔案 (Golden XML) 嚴格保護了 CRM `Entity` 結構與查詢條件。

---

## 2. 審查發現分類 (Categorized Findings)

### **[Critical] 嚴重問題**
* **無 (None)**：在目前提供的程式碼與測試快照中，未發現會直接導致記憶體洩漏、資安漏洞或破壞既有相容性的嚴重瑕疵。所有對舊版 constructor (`object logger, IOrganizationService organizationService`) 的相容呼叫均正確導向具備安全預算的建構子，並明確防堵了無界記憶體風險。

---

### **[High / Warning] 警告與注意事項**
1. **[Warning] Base64 解碼與極端空白的邊界處理 (Design Consistency & Performance)**
   - **位置**：`ToolUtility.Core/AttachmentOperations/AttachmentService.cs` (`ValidateBody` 方法)
   - **說明**：實作中採用不建立解碼副本的線性掃描 (`foreach (var character in body)`) 來驗證 Base64 結構與計算 raw bytes 長度。雖然此設計極大化了記憶體效能並避免不必要的分配，但在處理極端長字串或特殊空白字元時，呼叫端仍需確保輸入字串不會超出預期。現有測試已透過 `512` 組隨機位元組加上 `1536` 種變體進行跨 Framework BCL 比對（驗證 `net48` 與 `net10` 的行為一致性），確保不合法 padding（如 `tCo==`）會被決策性拒絕。
   - **建議**：維持現狀，並確保未來若擴充更大檔案支援時，`MaximumAttachmentBytes` 限制能與 D365 實際網路傳輸上限保持同步。

2. **[Warning] `ref IOrganizationService crmService` 參數合約的生命週期依賴 (Code Quality)**
   - **位置**：`ToolUtility.Core/AttachmentOperations/AttachmentService.cs`
   - **說明**：沿用舊架構以 `ref` 傳入 `IOrganizationService` 的設計。雖然程式碼已明確加上註解與斷言，確保不釋放 (Dispose) 外部借入的 SDK，但若調用端在多執行緒環境下於操作期間異動該 `ref` 變數指向的物件，仍可能造成非預期的並行競態（Concurrency Race Condition）。
   - **建議**：呼叫端應嚴格遵守「呼叫期間不得更換其參照」的契約。現有的 `ConcurrentRefScopes_NoCrossTalkAndDrain` 測試已在 1000 次並行情境下驗證無狀態共用之安全性。

---

### **[Info] 資訊與優良實踐**
1. **嚴格的 Nullability 與型別安全**：
   - 專案全面啟用 `#nullable enable`，且在介面與實作中對 `string?` 與必要參數進行了完善的條件式檢查（透過 `#if NETFRAMEWORK` 與 `ArgumentNullException.ThrowIfNull` 分支相容）。
2. **防禦性程式設計 (Fail-Closed)**：
   - 當 `crmService.Create(annotation)` 回傳 `Guid.Empty` 時，會立即丟擲 `InvalidOperationException`，拒絕未確認識別的盲目重試，防止重複建立附註。
3. **記憶體無狀態設計**：
   - 服務本身僅保存不可變的 `AttachmentTransferLimits`，不快取使用者資料或租戶狀態，非常適合高併發的微服務或共用工具層情境。

---

## 3. 建議改進事項 (Suggestions)
- 鑑於 `.NET Framework 4.6.2` 與 `.NET Core` 在部分 BCL 邊界行為（如 Base64 寬鬆解析）上存在微小差異，建議在未來的 CI 管線中持續保留 `CoreTargetFrameworkTests`，以確保雙目標編譯輸出與型別詮釋（Metadata）完全一致。

---

## 4. 正面評價 (Positive Notes)
* **測試覆蓋完善**：包含 `AttachmentCharacterizationTests`、`AttachmentSafetyTests` 與 `AttachmentCallerRouteTests`，從單元特徵、例外防護到真實公開委派鏈均有扎實的 Mock 與 Moq Strict 驗證。
* **嚴謹的 XML 結構保護**：透過 `GoldenQueryAssertions` 比對 `attachment-download.xml` 及上傳 XML，確保 CRM 查詢條件與七欄位屬性在重構前後絲毫不差。
* **文件與註解落實**：程式碼內部附帶詳細的繁體中文註解與設計脈絡說明，有助於長期維護。

---

## 5. 總結評分與推薦 (Validation Report & Recommendation)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 保持原有 API 簽章與行為，對呼叫端完全透明且相容。
Visual Consistency: N/A (UI-less backend component)
Accessibility: N/A (Backend component)
Performance: 19/20 - 採用無解碼副本的線性 Base64 掃描，最大程度減少記憶體配置與 GC 壓力。
Browser Compatibility: 20/20 - 成功達成雙目標支援 (.NET Framework 4.6.2 / .NET Core 10.0)。

TOTAL SCORE: 59/60 (換算標準化：98.3/100)

ISSUES FOUND:
- 無重大資安或架構缺陷 (Critical: 0, Warning: 2 均屬設計預期內的合約約束)。

RECOMMENDATION: PASS
```
