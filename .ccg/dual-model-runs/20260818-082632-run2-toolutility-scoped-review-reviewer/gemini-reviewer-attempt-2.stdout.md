# 程式碼審查報告：ToolUtilityClass 生命週期重構 (Run 2)

本報告針對 Run 2 的提交（`refactor(toolutility): ToolUtilityClass 改為 request 範圍`）進行架構與程式碼品質審查。由於本次重構屬於後端服務生命週期與相依性注入（DI）的調整，無前端 UI 變更，因此審查維度將對應至後端架構設計、API 易用性、防錯設計與程式碼一致性。

---

## 驗證報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience (開發者體驗與 API 設計): 19/20 - 透過 DI 註冊與 Provider 模式，使呼叫端獲取工具類的方式更加標準化，減少了手動管理連線的負擔。
Visual Consistency (程式碼風格與設計一致性): 19/20 - 遵循了專案的編碼規範（UTF-8, CRLF），繁體中文註解詳盡，且明確區分了 legacy 與 DI 兩條路徑。
Accessibility (API 易用性與防錯設計): 20/20 - 透過 _ownsConnection 欄位與建構式重載，精確控制了連線的釋放權，防止了連線洩漏與重複釋放的風險。
Performance (效能與資源管理): 20/20 - 採用 Scoped 生命週期，使同一個 request 內共用同一個連線租約，避免了頻繁建立連線的開銷，同時在 request 結束時能被及時釋放。
Browser Compatibility (DI 容器相容性與測試驗證): 18/20 - 測試中啟用了 ValidateScopes 與 ValidateOnBuild，確保了 DI 容器的健康度。唯一需要注意的是 ChurchReport.MemberInfo.Tests 中有 22 個測試失敗，雖然與本次重構無關，但仍需關注。

TOTAL SCORE: 96/100

ISSUES FOUND:
- [Info] ChurchReport.MemberInfo.Tests 專案中有 22 個測試失敗。經分析，這些失敗主要集中在命名規範與 DonationPayment 相關的測試，與本次 ToolUtility 生命週期重構無關（ToolUtility 自身的單元測試與 Dataverse 測試均 100% 通過）。建議在後續 Run 中安排修復。
- [Info] 檔案讀取時存在編碼解碼不一致的現象。雖然檔案本身已正確使用 UTF-8 without BOM 編碼，但在特定工具鏈或環境下讀取時會被誤判為 CP950 (Big5) 導致顯示亂碼。這不影響編譯與執行，但建議團隊在開發環境中統一編輯器與終端機的編碼設定。

RECOMMENDATION: PASS
```

---

## 1. 摘要 (Summary)

本次重構成功將 `ToolUtilityClass` 與 `ToolUtilityProvider` 的生命週期調整為 Request Scoped，解決了在高併發環境下可能發生的跨請求連線混亂與資源洩漏問題。

主要亮點包括：
- **生命週期隔離**：透過將服務註冊為 Scoped，確保每個 HTTP 請求或背景 Scope 擁有獨立的 `ToolUtilityClass` 實例與 `IOrganizationService` 連線租約。
- **精確的擁有權管理**：引入 `_ownsConnection` 標記，區分了 DI 容器注入（不釋放連線，由 DI 容器管理）與 Legacy 手動建構（釋放連線）兩種情境，避免了重複釋放（Double Dispose）或連線洩漏。
- **防範 Captive Dependency**：Legacy 工廠 `ToolUtilityFactory` 刻意不與 DI 容器互動，避免了 Singleton 捕獲 Scoped 依賴的架構陷阱。
- **完整的測試覆蓋**：新增的單元測試涵蓋了 Scope 驗證、生命週期釋放契約以及背景 Scope 隔離，且全部通過。

---

## 2. 易用性與防錯設計 (Accessibility / API Usability Issues)

*未發現 Critical 或 Warning 等級的易用性問題。*

* **Info**: 為了防止開發人員在後續維護中誤用建構式，建議在 `ToolUtilityClass` 的 Legacy 建構式上標記 `[Obsolete("請優先使用 DI 注入建構式，此建構式僅供 Legacy 工廠過渡使用。")]`，以提供編譯期的警告提示。

---

## 3. 設計一致性 (Design Issues)

* **Info - 檔案編碼與解碼不一致**：
  * **檔案路徑**：`ToolUtilityClass.Core.cs`, `ToolUtilityFacade.cs` 等修改檔案。
  * **說明**：雖然檔案本身已正確使用 `UTF-8 without BOM` 編碼，但在特定工具鏈或環境下讀取時會被誤判為 `CP950 (Big5)` 導致顯示亂碼。這不影響編譯與執行，但建議團隊在開發環境中統一編輯器與終端機的編碼設定。

---

## 4. 建議 (Suggestions)

* **Info - 既有測試失敗追蹤**：
  * **檔案路徑**：`ChurchReport.MemberInfo.Tests` 專案。
  * **說明**：在執行測試時，發現該專案有 22 個測試失敗（主要與 `DonationPayment` 的命名規範與業務邏輯相關）。雖然這些失敗與本次 `ToolUtility` 重構無關，但建議在 Run 3 或後續的重構階段中，將這些失敗的測試納入修復範疇，以確保整體 CI/CD 流程的健康。

---

## 5. 肯定之處 (Positive Notes)

* **正確的 Facade 釋放邏輯**：`ToolUtilityFacade` 移除了對 `_organizationService` 的釋放，正確地將其視為「借用者」而非「擁有者」，避免了歸還已失效連線租約的風險。
* **DI 驗證機制**：在單元測試中啟用了 `ValidateScopes = true` 與 `ValidateOnBuild = true`，這能有效在開發階段攔截任何潛在的生命週期配置錯誤。
* **嚴格遵守規範**：所有修改的檔案均符合 UTF-8 without BOM 與 CRLF 的換行規範，且繁體中文 XML 註解詳盡，有助於後續維護。
