以下是針對 Run 2 commit HEAD（`refactor(toolutility): ToolUtilityClass 改為 request 範圍`）的架構與程式碼品質審查報告。

---

# UI & 架構審查報告：ToolUtility Scoped 生命週期重構

## 1. 總體評估 (Summary)
本次審查針對 Run 2 commit `58c917d98` 進行。重構完全符合設計規範，成功將 `ToolUtilityClass` 與 `ToolUtilityProvider` 調整為 Request Scoped 生命週期，並妥善處理了 `IOrganizationService` 的資源擁有權與釋放邏輯，有效防止了 Dataverse 連線洩漏與 Captive Dependency 問題。同時保留了 Legacy Factory 路徑以維持相容性，並新增了完整的單元測試進行驗證。整體實作品質優良，架構設計清晰。

---

## 2. 關鍵問題 (Critical Findings)
* **無**：未發現任何關鍵性架構缺陷、資源洩漏或 Captive Dependency 問題。

---

## 3. 警告事項 (Warning Findings)
* **既有測試失敗（與本次重構無關）**
  * **檔案路徑**：`ChurchReport.MemberInfo.Tests` 專案
  * **說明**：在測試執行中，`ChurchReport.MemberInfo.Tests` 專案有 22 個測試失敗（主要與 `DonationPaymentManager` 的服務抽離與命名規範有關）。經查證，這些測試失敗屬於既有問題，本次重構並未修改任何付款相關程式碼，且本次重構專屬的 `ToolUtility.Tests` (63 通過) 與 `ToolUtility.Dataverse.Tests` (11 通過) 皆全數成功。
  * **建議**：建議在後續的付款模組維護任務中修復這些既有測試，避免干擾後續 CI/CD 流程。

---

## 4. 一般資訊與設計亮點 (Info Findings)
* **資源擁有權隔離設計良好**
  * **檔案路徑**：`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs`
  * **說明**：新增的 DI 建構式將 `_ownsConnection` 設為 `false`，並在 `Dispose` 中僅於 `_ownsConnection == true` 時釋放連線。這確保了由 DI 容器注入的 `IOrganizationService` 不會被 `ToolUtilityClass` 提前釋放，而是由 ASP.NET Core Request Scope 統一管理生命週期。
* **Facade 避免重複釋放共用連線**
  * **檔案路徑**：`ToolUtility/Core/ToolUtilityFacade.cs`
  * **說明**：移除了 `(_organizationService as IDisposable)?.Dispose();` 的呼叫，避免了 Facade 子服務誤釋放由 DI 容器管理的共用連線，防止了連線池租約失效與 ObjectDisposedException 的問題。
* **相容性過渡設計 (Legacy Factory)**
  * **檔案路徑**：`ToolUtility/Factory/ToolUtilityFactory.cs`
  * **說明**：保留了 `ToolUtilityFactory` 的單例路徑，該路徑不解析 DI 容器，而是自行建立連線（`_ownsConnection = true`），這為尚未遷移的 39 個呼叫點提供了安全的相容性，避免了 Captive Dependency，並計畫於 Run 3 遷移完成後移除。
* **完整的 Scope 驗證測試**
  * **檔案路徑**：`ToolUtility.Dataverse.Tests/ToolUtilityClassScopedLifetimeTests.cs`
  * **說明**：新增了 4 個針對 Scoped 生命週期的測試，並在測試中啟用了 `ValidateScopes = true` 與 `ValidateOnBuild = true`，確保在容器建置與解析時不會發生 Captive Dependency，且驗證了背景 Scope 的獨立連線與釋放契約。
* **符合編碼與文件規範**
  * **說明**：所有新增與修改的檔案皆符合 UTF-8 without BOM 與 CRLF 規範，且包含詳盡的繁體中文 XML 註解，說明了設計意圖與資源生命週期。

---

## 5. 建議事項 (Suggestions)
* **建議於 Run 3 儘速完成剩餘呼叫點的遷移**：目前系統中仍有 39 處呼叫 `ToolUtilityFactory.GetInstance()` 的地方。雖然目前有 Legacy Factory 路徑維持相容性，但仍建議在 Run 3 中將這些呼叫點全數遷移至 DI 注入，以完全發揮 Scoped 連線池的優勢並移除過渡程式碼。

---

## 6. 優秀實作點 (Positive Notes)
* **測試設計非常嚴謹**：特別是 `BackgroundScope_UsesIndependentConnectionAndDisposesOnlyItsLease` 測試，精準地模擬了背景工作自建 DI Scope 的情境，並驗證了連線的獨立性與釋放行為，是非常優秀的單元測試範例。
* **註解詳盡且意圖清晰**：程式碼中的繁體中文註解清楚地說明了 `_ownsConnection` 的設計意圖，這對於後續維護人員理解資源擁有權非常有幫助。
