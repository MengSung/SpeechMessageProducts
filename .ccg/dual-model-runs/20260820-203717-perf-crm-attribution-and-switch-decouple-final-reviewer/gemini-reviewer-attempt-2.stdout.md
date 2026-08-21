# 程式碼審查報告：Perf CRM 歸因與 Session 診斷開關解耦重構

本報告針對任務 `perf-crm-attribution-and-switch-decouple-final` 的完整實作與未提交修正進行審查。審查重點包含 `AmbientGatewayOrganizationService` 的裝飾鏈解析、無 Request 時的 Fallback Scope 生命週期、DI 診斷型別的編譯隔離、測試替身合規性，以及繁體中文註解與編碼規範。

---

## 審查結果摘要

| 級別 | 數量 | 說明 |
| :--- | :---: | :--- |
| **Critical (阻擋發佈)** | 0 | 無發現任何阻擋發佈的嚴重缺陷或資源洩漏風險。 |
| **Warning (建議修正)** | 0 | 實作完全符合設計規範，無潛在的架構偏離風險。 |
| **Info (架構說明)** | 3 | 針對裝飾鏈解析、測試條件編譯與資源生命週期進行架構確認。 |

---

## 詳細審查意見

### Info (架構說明)

#### 1. 完整 `IOrganizationService` 裝飾鏈解析確認
* **檔案路徑**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
* **資料流分析**：
  在 `Run<T>` 方法中，當 `requestServices` 存在時，程式碼已正確修改為：
  ```csharp
  if (requestServices != null)
  {
      return work(requestServices.GetRequiredService<IOrganizationService>());
  }
  ```
  此修改確保了系統會解析目前 Scope 的完整 `IOrganizationService` 裝飾鏈（包含 Debug 模式下的 `TimedOrganizationService`），而非繞過裝飾器直接解析 `IDataverseGateway`。這解決了先前 `[Perf]` 歸因時間為零、而 JSONL 卻有 CRM 操作記錄的資料流斷裂問題。

#### 2. Fallback Scope 生命週期與無狀態設計合規
* **檔案路徑**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
* **生命週期分析**：
  * 當無 HTTP Request 時，`Run<T>` 透過 `using var scope = _scopeFactory.CreateScope();` 建立短命 Scope。`using` 語法保證了不論操作成功或擲出例外，該 Scope 都會被 deterministic Dispose，釋放其持有的 scoped gateway 與連線租約。
  * `AmbientGatewayOrganizationService` 類別僅持有 `_requestServicesAccessor` 與 `_scopeFactory` 兩個無狀態的解析委派，絕無快取或保存任何 `HttpContext`、`IServiceScope`、`IClientLease`、原始 CRM Client 或使用者／租戶狀態，完全符合跨 Request 與跨租戶的隔離性要求。

#### 3. 測試合約與條件編譯隔離
* **檔案路徑**：
  * `ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs`
  * `ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs`
* **測試設計分析**：
  * `GatewayArchitectureTests.cs` 中的 `Ambient_service_creates_and_releases_scope_without_http_context` 測試，忠實地在 `ServiceCollection` 中註冊了兩層邊界（`IOrganizationService` 代理包住 scoped `IDataverseGateway`），並透過 `TrackingGateway` 驗證了無 Request 時的 Scope 釋放契約。
  * `ToolUtilityFactoryAmbientGatewayTests.cs` 中涉及 `TimedOrganizationService` 的測試已正確使用 `#if DEBUG` 進行包裹。由於 `TimedOrganizationService` 僅在 Debug 模式下編譯，此舉避免了 Release 模式下的編譯失敗，同時確保了測試替身不會在跨測試或跨 Request 間洩漏狀態。

---

## 格式與編碼驗證

* **繁體中文註解**：所有本次修改的 `.cs` 檔案（包含 `AmbientGatewayOrganizationService.cs` 與測試檔案）皆已補上詳盡且具可維護性的繁體中文 XML 註解，說明其設計意圖、不變量與資源邊界。
* **檔案編碼**：經確認，所有修改檔案皆維持 **UTF-8 無 BOM**、**CRLF 換行**，且以 **final CRLF** 結尾，完全符合 `AGENTS.md` 的編碼規範。
