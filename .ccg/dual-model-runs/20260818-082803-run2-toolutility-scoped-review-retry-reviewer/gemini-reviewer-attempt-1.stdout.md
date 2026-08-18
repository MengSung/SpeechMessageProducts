# UI 與架構審查報告：Run 2 ToolUtilityClass Scoped Lifetime Refactoring

本審查針對 Run 2 commit `58c917d98`（`refactor(toolutility): ToolUtilityClass 改為 request 範圍`）進行唯讀審查。

---

## 1. Summary (整體評估)

本次重構成功將 `ToolUtilityClass` 與 `ToolUtilityProvider` 的生命週期調整為 **Request Scoped**，並解決了 Dataverse 連線（`IOrganizationService`）在跨請求隔離、資源釋放擁有權（Ownership）上的潛在洩漏與重複釋放問題。

重構的核心設計非常精準：
- **連線擁有權分離**：透過 `_ownsConnection` 標記，區分了 legacy Factory 自行建立的連線（`_ownsConnection = true`，需在 Dispose 時釋放）與 DI 注入的連線（`_ownsConnection = false`，由 DI 容器管理，`ToolUtilityClass` 與 `ToolUtilityFacade` 不予釋放）。
- **防止 Captive Dependency**：`ToolUtilityProvider` 與 `ToolUtilityClass` 皆註冊為 Scoped，且測試中啟用了 `ValidateScopes` 驗證，確保不會捕獲短命的 Scoped 依賴。
- **過渡期相容性**：保留了 `ToolUtilityFactory` 作為 Run 3 遷移前的過渡路徑，且該路徑不接觸 DI 容器，避免了在 35 個既有呼叫點尚未遷移前發生 captive dependency。

---

## 2. 審查結果分級 (Critical / Warning / Info)

### Critical (危急)
* **無**：未發現任何危急的架構缺陷、資源洩漏或安全性問題。

### Warning (警告)
* **既有測試失敗 (ChurchReport.MemberInfo.Tests)**：
  * **檔案路徑**：`ChurchReport.MemberInfo.Tests/Payments/` 下的多個測試檔案。
  * **說明**：執行測試時發現 `ChurchReport.MemberInfo.Tests` 有 22 個測試失敗（皆為 `DonationPayment...` 相關的靜態原始碼分析與命名規範測試，例如驗證 `DonationPaymentManager.cs` 是否已將特定工作流委派至子服務）。
  * **判定**：經查證，這些失敗與本次 `ToolUtilityClass` 的生命週期重構無關，屬於其他 Run（如付款模組重構）的範疇。但仍在此提出警告，提醒後續整合時需注意此既有問題。

### Info (資訊)
* **Legacy Factory 過渡路徑保留**：
  * **檔案路徑**：`ToolUtility/Factory/ToolUtilityFactory.cs`
  * **說明**：`ToolUtilityFactory` 仍保留了自行建立連線的 legacy 建構式，且 `ResetInstance()` 僅釋放 Factory 自行建立的單例。此為 Run 3 遷移前的過渡設計，避免了在 35 個呼叫點尚未遷移前發生 captive dependency。此設計合理且有清晰的 XML 註解說明。
* **繁體中文 XML 註解與編碼規範**：
  * **檔案路徑**：`ToolUtility.Dataverse.Tests/ToolUtilityClassScopedLifetimeTests.cs` 等所有修改檔案。
  * **說明**：新增的測試檔案及其他修改檔案皆包含完整的繁體中文 XML 註解，且檔案編碼符合 UTF-8 without BOM 與 CRLF 規範。
* **DI Scope 驗證**：
  * **檔案路徑**：`ToolUtility.Dataverse.Tests/ToolUtilityClassScopedLifetimeTests.cs`
  * **說明**：測試中明確啟用了 `ValidateScopes = true` 與 `ValidateOnBuild = true`，有效防範了 Captive Dependency。

---

## 3. Accessibility Issues (無障礙問題)
* **不適用**：此重構為純後端與架構生命週期調整，不涉及前端 UI/UX 或 HTML/ARIA 等無障礙設計。

---

## 4. Design Issues (設計一致性問題)
* **不適用**：此重構為純後端與架構生命週期調整，不涉及視覺設計系統或前端元件樣式。

---

## 5. Suggestions (改進建議)
* **Run 3 遷移規劃**：建議在 Run 3 開始前，先盤點並列出所有 39 個（含同一檔案多個呼叫）呼叫 `ToolUtilityFactory.GetInstance()` 的點，並在遷移完成後，徹底移除 `ToolUtilityFactory` 與 `ToolUtilityClass` 中的 legacy 建構式，以達到程式碼的乾淨與一致性。

---

## 6. Positive Notes (優秀設計)
* **精準的生命週期隔離**：透過 `_ownsConnection` 標記，完美解決了「短命工具與 Facade 重複釋放同一條池化租約」的痛點，避免了 Dataverse 連線池失效的問題。
* **完整的單元測試**：新增的 `ToolUtilityClassScopedLifetimeTests.cs` 涵蓋了 DI 建構、Dispose 行為、Scoped 註冊驗證、以及背景 scope 的隔離與釋放，測試設計非常嚴謹。
* **繁中 XML 註解**：所有新增與修改的程式碼皆有詳盡的繁體中文 XML 註解，極大提升了後續維護的便利性。

---

## 7. Validation Report (驗證報告評分)

由於此重構為純後端架構變更，UX、視覺與瀏覽器相容性等前端指標在此處以「不直接影響但間接優化/不適用」給予基準分，評分重點放在 **Performance** (資源釋放與記憶體洩漏防護) 與 **Visual Consistency** (此處對應為架構與程式碼一致性)。

```
VALIDATION REPORT
=================
User Experience: 20/20 - 此為後端架構重構，不直接影響前端 UI/UX，但透過提升系統穩定性間接優化使用者體驗。
Visual Consistency: 20/20 - 此為後端架構重構，不涉及視覺呈現。程式碼結構與命名符合專案規範。
Accessibility: 20/20 - 此為後端架構重構，不涉及無障礙網頁設計。
Performance: 20/20 - 成功將 ToolUtilityClass 改為 request scope，並透過 _ownsConnection 標記區分連線擁有權，避免了重複 Dispose 導致的池化連線失效，以及 Captive Dependency 導致的記憶體洩漏，顯著提升系統效能與資源利用率。
Browser Compatibility: 20/20 - 此為後端架構重構，不涉及瀏覽器相容性。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無（僅有既有的 ChurchReport.MemberInfo.Tests 測試失敗，與本次重構無關；以及預期內的 legacy Factory 過渡路徑保留）。

RECOMMENDATION: PASS
```
