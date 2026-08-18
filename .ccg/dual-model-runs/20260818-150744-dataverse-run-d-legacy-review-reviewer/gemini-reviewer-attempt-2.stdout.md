以下是針對 **Run D Dataverse legacy path removal** 的 UI/程式碼品質審查報告：

### 1. 整體評估 (Summary)
本次重構成功移除了 `ToolUtilityClass` 對原始 CRM 連線的建立、擁有與釋放邏輯，並將 legacy 單例工廠 `ToolUtilityFactory` 改造為僅持有不具狀態的 `AmbientGatewayOrganizationService` 代理。此設計成功解決了 captive scoped dependency 的風險，並透過 lazy current-request accessor 與短壽命 scope 機制，確保在無 HTTP 請求的背景執行緒中也能安全、確定性地釋放連線資源，避免了跨請求/使用者的連線與身分洩漏。

---

### 2. 審查發現 (Findings)

#### Critical (嚴重)
* **無**：本次重構完全符合 PRD 與設計要求，未發現關鍵的資源洩漏、 captive dependency 或架構缺陷。

#### Warning (警告)
* **`ToolUtilityClass.Core.cs` 中殘留未使用的私有設定屬性**
  * **檔案路徑**：`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs` (第 41-46 行)
  * **說明**：雖然 `InitializeCrmConnection` 已被移除，但 `SERVER`、`PORT`、`ORGANIZATION`、`USERNAME`、`PASSWORD`、`DOMAIN` 等私有屬性仍保留在程式碼中。這些屬性目前已無任何實質作用，建議在後續的清理階段中予以移除，以維持程式碼的乾淨度。

#### Info (資訊)
* **徹底清除 `m_OrganizationService` 程式碼參照**
  * **說明**：已成功刪除 `public OrganizationServiceProxy m_OrganizationService;` 欄位。經全專案檢索，除了註解與歷史說明文件外，所有 `.cs` 程式碼中對該欄位的引用已完全清除，死分支已安全收斂。
* **ActivityAttachment 恆假分支安全收斂**
  * **檔案路徑**：`ToolUtility/ToolUtilityPartials/ToolUtilityClass.ActivityAttachment.cs` (第 62-93 行)
  * **說明**：已移除 `CRM_TYPE == "DYNAMICS365"` 的恆假條件分支，僅保留原本 else 分支的 `m_Crm2011OrganizationService` 呼叫，符合編譯期常數優化要求。
* **`CrmConnectionPool.cs` 確定性刪除與 Adapter 保留**
  * **說明**：`CrmConnectionPool.cs` 已被刪除，且 `ConnectionPoolStatsAdapter.cs` 作為相容性實作被正確保留，其 `AcquireConnection` 等方法在被呼叫時會拋出 `NotSupportedException`，引導呼叫端改用 `IDataverseGateway`。
* **單例工廠生命週期安全化**
  * **檔案路徑**：`ToolUtility/Factory/ToolUtilityFactory.cs`
  * **說明**：`ToolUtilityFactory` 僅保存 `AmbientGatewayOrganizationService`，不持有任何 request-scoped 資源，避免了 captive dependency。

---

### 3. 建議事項 (Suggestions)
1. **清理死碼**：如 Warning 所述，建議在下一個重構週期中，將 `ToolUtilityClass.Core.cs` 中殘留的私有設定屬性（如 `SERVER`、`PORT` 等）徹底移除。
2. **單元測試覆蓋**：新增的 `ToolUtilityFactoryAmbientGatewayTests` 測試設計非常優良，精確驗證了有/無 request 時的 scope 建立與釋放行為，建議將此類測試模式推廣至其他 legacy 轉接層的重構驗證中。
