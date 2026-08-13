# P7.4 認獻單讀取 disabled boundary：實作審查報告

本審查報告針對目前 Git 工作區中 P7.4 ChurchReport 認獻單 typed-read boundary 的實作進行 UX、效能、隔離性、生命週期管理及程式碼品質的完整評估。

---

## VALIDATION REPORT
=================
**User Experience**: 18/20 - 防禦性設計（fail-closed）與原子替換確保了前端 UI 不會呈現部分加載或損毀的資料。cancellation 支援確保了使用者取消請求時能即時釋放資源，提升響應性。
**Visual Consistency**: 14/20 - 程式碼結構與命名規範符合專案標準，但**嚴重的檔案編碼問題導致註解出現大量亂碼**，嚴重影響程式碼的一致性與可讀性。
**Accessibility**: 20/20 - 此為後端 API 邊界實作，不直接涉及 HTML/ARIA，但其 fail-closed 設計確保了錯誤狀態能正確回傳給前端，讓前端能呈現無障礙的錯誤提示。
**Performance**: 19/20 - 採用 async/await 與 `ConfigureAwait(false)`，無同步等待（sync-over-async），且在 gate=false 時完全不初始化 options 與 ProcessHost，避免了不必要的記憶體與連線池開銷。
**Browser Compatibility**: 19/20 - DTO 設計乾淨，不包含 CRM SDK 特有的型別（如 Entity, OptionSetValue），確保了與前端/瀏覽器端對接時的序列化相容性。

**TOTAL SCORE: 90/100**

**ISSUES FOUND:**
- **[Critical] 檔案編碼錯誤導致中文註解嚴重亂碼**：`DonationDynamicsAccessBootstrap.cs`、`DonationBookingReadService.cs` 等多個新實作與修改的檔案中，中文註解出現大量亂碼，影響程式碼審查與後續維護。
- **[Warning] 靜態原始碼合約測試過於脆弱**：`DonationBookingReadBoundaryContractTests` 採用讀取實體檔案並比對字串的方式進行驗證，若程式碼格式（如空格、換行）微調，測試將會失敗。

**RECOMMENDATION: PASS** (實作邏輯正確無誤，但必須修正編碼與亂碼問題)

---

## 1. Summary (整體評估)

本次實作成功建立了 P7.4 認獻單讀取的 disabled boundary。實作完全符合設計規範：
- **雙重 Gate 控制**：`Package01DedicationBookingReadEnabled` 嚴格依賴 `Package01FeeReadsEnabled`，且預設值皆為 `false`。
- **資源隔離與延遲載入**：當 Gate 為 `false` 時，完全不進行 options 綁定、不解析 `ProcessHost`，亦不建立 typed client，確保了 disabled 狀態下的零資源消耗。
- **防禦性與原子性**：`DonationBookingReadService` 進行了極為嚴格的 DTO 欄位驗證，若有任何一筆資料不合規即 fail-closed；`DonationBookingReadModelAdapter` 則在非同步讀取與對應完成後，才原子替換（atomic replacement）UI model 的 list，避免了 partial publication 的風險。

唯一的重大缺陷在於**檔案編碼問題導致的註解亂碼**，這必須在合併前予以修正。

---

## 2. Accessibility & Integration Issues (整合與無障礙問題)

由於此任務為後端 API 邊界實作，無直接的前端 UI 元素，但從整合角度來看：
- **[Info] 錯誤傳播與 Fail-Closed**：`DonationBookingReadService.ValidateAndMap` 在驗證失敗時拋出 `InvalidOperationException`。前端整合時，需確保 API Controller 有統一的 Exception Filter 來捕捉此異常，並轉換為友善的 HTTP 400/500 錯誤響應，以利前端無障礙輔具（如螢幕閱讀器）能正確向使用者播報錯誤提示。

---

## 3. Design & Code Quality Issues (設計與程式碼品質問題)

### 🔴 Critical: 檔案編碼錯誤導致中文註解嚴重亂碼
- **受影響檔案**：
  - `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
  - `SpeechMessageProducts.ChurchReport/Services/DonationBookingReadService.cs`
  - `ChurchReport.MemberInfo.Tests/Services/DonationBookingReadBoundaryContractTests.cs`
  - `ChurchReport.MemberInfo.Tests/Services/DonationBookingReadServiceTests.cs`
  - `SpeechMessageProducts.ChurchReport/appsettings.json`
  - `docs/superpowers/plans/2026-08-13-p74-dedication-booking-read-boundary.md`
- **原因分析**：上述檔案在儲存時可能未使用 BOM (Byte Order Mark) 的 UTF-8 格式，導致在繁體中文 Windows 環境下被系統預設的 CP950 (Big5) 編碼解析，造成所有中文註解與異常訊息（如 `"?曆??啣?????"`）損毀。
- **後果**：嚴重影響程式碼的可讀性、後續維護性以及自動化工具的解析。

### ⚠️ Warning: 靜態原始碼合約測試過於脆弱 (Fragile Test)
- **受影響檔案**：`ChurchReport.MemberInfo.Tests/Services/DonationBookingReadBoundaryContractTests.cs`
- **原因分析**：`Bootstrap_contains_all_three_supported_connection_mode_routes_for_dedication_booking_read` 與 `Embedded_request_guard_allowlist_contains_the_dedication_booking_read_operation` 測試中，使用 `File.ReadAllText` 讀取 C# 原始碼並進行字串比對（例如 `source.Should().Contain("OperationIds.PaymentsDedicationRetrieveByContact")`）。
- **後果**：若未來開發人員對 `DonationDynamicsAccessBootstrap.cs` 進行格式化（如調整空格、換行、修改註解），這些測試將會無預警失敗，增加維護成本。

---

## 4. Suggestions (改進建議)

### 1. 修正檔案編碼 (針對 Critical 問題)
建議將所有受影響的 `.cs`、`.json` 與 `.md` 檔案重新儲存為 **UTF-8 with BOM** (在 Visual Studio 中可透過 `File -> Advanced Save Options` 選擇 `Unicode (UTF-8 with signature) - Codepage 65001` 進行儲存)。

### 2. 重構靜態合約測試 (針對 Warning 問題)
建議改用反射（Reflection）或實際的 DI 容器解析測試，來驗證 `RequestGuard` 的 allowlist 與路由邏輯。例如：
```csharp
// 範例：透過反射取得 RequestGuard 內部允許的 Operation 列表進行驗證，而非比對原始碼字串
var executor = host.GetOrCreateEmbeddedExecutor(options, configuration);
// 驗證 executor 是否能正確執行 PaymentsDedicationRetrieveByContact 操作
```

---

## 5. Positive Notes (值得肯定之處)

1. **極佳的隔離性設計**：`DonationBookingReadService` 與 `DonationBookingReadModelAdapter` 完全不依賴 CRM SDK 的實體（Entity），且與 `ToolUtility` I/O 隔離，為未來的單元測試提供了極佳的便利性。
2. **原子替換（Atomic Replacement）**：`DonationBookingReadModelAdapter.PopulateAsync` 的實作非常安全，先完成所有非同步讀取與對應，最後才替換 `model.DedicationBookingList`，且在 cancellation 發生時保持原 list 不變，避免了 UI 呈現髒資料。
3. **完整的生命週期管理**：`DonationDynamicsAccessProcessHost` 作為 DI singleton，其 `DisposeAsync` 實作考慮了並行與冪等性（idempotent），能安全地釋放內部的 `ServiceProvider` 與連線資源。
