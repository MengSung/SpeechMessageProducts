# 1. Analysis (架構評估與問題分析)

在 `SpeechMessageProducts.ChurchReport` 系統中，`ListManager` 是一個以 Session Key 快取的長生命週期（Session-scoped）物件。然而，`IOrganizationService`（CRM 服務實例）通常是 Request-scoped 的短生命週期物件，且與特定的使用者請求上下文綁定。

目前 `ListManager` 提供了以下 service-aware overload：
```csharp
public void SetupIntegrateData(String ListEntityId, IOrganizationService organizationService)
```
此設計存在以下架構缺陷：
1. **狀態混合與隔離性破壞風險**：該方法雖然接收了 operation-local 的 `organizationService`，但其內部仍從 `ListManager` 的實例欄位（如 `m_Account`, `m_Password`, `LoginType`, `m_SelectDate`）讀取帳號、密碼、登入型態與日期。由於 `ListManager` 快取在 Session 中，若多個併發請求同時存取或修改這些實例欄位，將導致嚴重的**跨使用者狀態洩漏（Cross-user state leakage）**與競態條件。
2. **生命週期混亂**：將短生命週期的 `IOrganizationService` 傳入長生命週期的 `ListManager` 中，容易導致服務被意外持有、快取或在錯誤的時間點被處置（Dispose），破壞了服務的生命週期邊界。
3. **無正式產品呼叫端**：該 overload 目前在正式產品代碼中並無呼叫端，僅存在於回歸測試中。

---

# 2. Architecture Decision (架構決策)

為了在不破壞二進位相容性的前提下，徹底消除上述安全隱患，我們決定採用**最小修正方案**：

*   **決策**：保留 `ListManager.SetupIntegrateData(string, IOrganizationService)` 的 API 簽章以維持二進位相容性，但在該方法內部**無條件拋出 `InvalidOperationException`**。
*   **Rationale (設計理由)**：
    *   **Fail-Closed 安全防禦**：在讀取任何實例欄位或進行任何 CRM I/O 之前立即拒絕執行，確保系統在不安全的路徑上「預設關閉」。
    *   **消除狀態洩漏**：完全不讀取 `ListManager` 的實例欄位，徹底阻斷因 Session 欄位共享而導致的跨使用者資料洩漏。
    *   **符合既有測試契約**：既有的回歸測試（如 `DownloadIntegrateDataIsolationTests`）預期此方法在無法安全傳遞服務時拋出 `InvalidOperationException`，且驗證 A/B marker 服務未被保存或處置。此修正能完美通過既有測試。
*   **Rejected Alternatives (被否決的替代方案)**：
    *   *方案 A：直接刪除該 overload*。被否決，因為這會破壞二進位相容性，若有外部組件依賴此簽章將導致執行期連結錯誤。
    *   *方案 B：修改簽章以傳入所有必要參數（如帳密、日期等）*。被否決，因為這本應是無狀態的 `DownloadIntegrateData` 的職責，`ListManager` 作為 Session 快取物件不應提供此無狀態包裝，且修改簽章同樣會破壞二進位相容性。
*   **Assumptions (假設)**：假設未來所有需要 operation-local 服務的場景，都將直接使用明確、不可變的 `DownloadIntegrateData` 入口，而非透過 `ListManager` 進行中轉。
*   **Potential Side Effects (潛在副作用)**：若未來有新功能試圖呼叫此 overload，將在執行期立即拋出異常。這是有意為之的防禦性設計，能強迫開發者改用正確的無狀態呼叫鏈。

---

# 3. Implementation Plan (實作計畫)

### 實作步驟
1. 在 `ListManager.cs` 中，定位到 `SetupIntegrateData(String ListEntityId, IOrganizationService organizationService)` 方法。
2. 移除其內部呼叫 `m_DownloadIntegrateData.SetupIntegrateData` 的邏輯。
3. 在 `ArgumentNullException.ThrowIfNull(organizationService)` 之後，無條件拋出 `InvalidOperationException`，並在異常訊息中說明拒絕原因與替代方案。

### Unified Diff Patch

```diff
--- a/SpeechMessageProducts.ChurchReport/Models/ListManager.cs
+++ b/SpeechMessageProducts.ChurchReport/Models/ListManager.cs
@@ -280,19 +280,10 @@
         public void SetupIntegrateData(String ListEntityId, IOrganizationService organizationService)
         {
             ArgumentNullException.ThrowIfNull(organizationService);
-
-            // service-aware 頝臬?敹??刻?撖?session ?梯”?遣蝡?legacy output ?閫?ToolUtility
-            // 銋?憪晷?€?皜貊?摰?fail closed嚗?甇?WeeklyReportEntityId 銝??session
-            // 閫???神???踹?憭望???隞霈鋡怠?蝥?request 閫€撖?霈??€?
-
-            // service ????method parameter 敺€銝??銝??蝯虫遙雿?雿??喃蝙 finally
-            // ???支?銝??剁?? session 敹怠???ListManager ?航??鋡怠隞?雿??€?
-
-            m_DownloadIntegrateData.SetupIntegrateData(
-                m_Account,
-                m_Password,
-                LoginType,
-                m_SelectDate,
-                ListEntityId,
-                string.Empty,
-                ref m_ListSmallGroupWeeklyReport,
-                organizationService);
+            throw new InvalidOperationException(
+                "ListManager.SetupIntegrateData(string, IOrganizationService) is disabled to prevent cross-user state leakage from session instance fields. " +
+                "Please use DownloadIntegrateData directly with an explicit, immutable operation context.");
         }
```

---

# 4. Considerations (架構考量與審查判定)

### [Critical] 跨使用者隔離 (Cross-user Isolation)
*   **評估**：若保留原有的委派呼叫，`ListManager` 會將其內部的 `m_Account`、`m_Password` 等 mutable 實例欄位傳遞給下游。在多執行緒或併發請求下，這些欄位極易被其他請求覆寫，導致 A 使用者的 CRM 服務實例使用了 B 使用者的憑證進行查詢，造成嚴重的越權與資料洩漏。
*   **判定**：無條件拋出 `InvalidOperationException` 徹底切斷了此不安全路徑，確保實例欄位絕不會在 operation-local 流程中被讀取，達成了強隔離性。

### [Critical] Service Ownership & Lifecycle
*   **評估**：`IOrganizationService` 的生命週期應由 Request 容器管理。若允許 `ListManager` 接收此服務，即使不寫入欄位，也難以保證下游的 `DownloadIntegrateData` 或其關聯的 `IdentityConverter` 不會意外持有該服務的引用。
*   **判定**：透過 Fail-Closed 拒絕此呼叫，確保 `ListManager` 絕不參與任何 `IOrganizationService` 的生命週期傳遞，避免了服務被非預期 Dispose 或洩漏的風險。

### [Warning] API 易用性與編譯期警告
*   **評估**：雖然此修正維持了二進位相容性，但對於不知情的開發者來說，呼叫此方法會在執行期拋出異常，這降低了 API 的易用性。
*   **建議**：應在該方法上加上 `[Obsolete("Use DownloadIntegrateData directly.", true)]` 屬性。這能在編譯期直接阻斷新的呼叫，同時保留簽章以維持舊組件的二進位相容性。

### [Info] Fault/Timeout Cleanup
*   **評估**：由於此路徑被無條件拒絕，在發生錯誤或逾時時，完全不會有任何狀態寫入 `ListManager` 實例、`ToolUtilityFactory`、`static` 欄位或 `AsyncLocal` 中，因此無需進行任何額外的狀態清理工作，達成了零殘留的安全性。
