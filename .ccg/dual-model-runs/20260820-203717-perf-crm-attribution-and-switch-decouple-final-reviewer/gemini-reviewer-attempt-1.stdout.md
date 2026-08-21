# CCG 審查報告：Perf CRM 歸因與 Session 診斷開關解耦重構

本報告針對 `perf-crm-attribution-and-switch-decouple-final` 任務的完整實作與未提交修正進行程式碼與資料流審查。審查重點在於確保 CRM 歸因的正確性、跨 Request 隔離安全性、資源生命週期的確定性釋放，以及 Release 模式下的編譯與註冊隔離。

---

## 審查結論摘要
* **Critical（阻擋發佈的嚴重問題）**：0 項
* **Warning（建議修正的潛在風險）**：0 項
* **Info（架構說明與細節提示）**：2 項

---

## 詳細審查報告

### 1. Ambient 代理與裝飾鏈解析 (AmbientGatewayOrganizationService)
* **分類**：Info
* **檔案路徑**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
* **審查結果**：**通過**
* **具體資料流分析**：
  * **裝飾鏈解析**：在 `Run<T>` 方法中，當 `requestServices` 存在時，程式碼已修正為：
    ```csharp
    return work(requestServices.GetRequiredService<IOrganizationService>());
    ```
    這確保了代理會解析目前 Scope 的完整 `IOrganizationService` 裝飾鏈（包含 Debug 模式下的 `TimedOrganizationService` 裝飾器），而非繞過裝飾器直接取得 `IDataverseGateway`。這徹底解決了先前因繞過裝飾器導致 `RequestProfiler` 漏記 CRM 時間（`crm{n=0,ms=0}`）的歸因錯誤問題。
  * **無 Request Fallback Scope**：當 `requestServices` 為 `null` 時，程式碼採用 `using var scope = _scopeFactory.CreateScope();` 建立短命 Scope，並在其內解析 `IOrganizationService`。`using` 語法保證了無論操作成功或發生例外，該 Scope 都會被確定性釋放（Deterministic Dispose），且該代理類別本身不保存任何 `HttpContext`、`scope`、`lease`、`raw client`、`identity` 或 `tenant state`，完全符合跨 Request 隔離與防洩漏契約。
  * **註解與編碼**：檔案頂部與各成員皆已補上詳盡的繁體中文 XML 註解，說明量測不變量與資源生命週期，且檔案維持 UTF-8 無 BOM、CRLF 格式。

---

### 2. 診斷開關與計時裝飾器之編譯隔離 (Startup & Diagnostics)
* **分類**：Info
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Startup.cs`
  * `SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedOrganizationService.cs`
  * `SpeechMessageProducts.ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs`
* **審查結果**：**通過**
* **具體資料流分析**：
  * **編譯隔離**：`TimedOrganizationService` 與 `SessionDiagnosticsSwitch` 均被整個 `#if DEBUG` 預處理指令包裹。
  * **DI 註冊隔離**：在 `Startup.ConfigureServices` 中，Debug 診斷開關的設定與 `TimedOrganizationService` 裝飾器的替換註冊邏輯皆置於 `#if DEBUG` 區塊內。在 Release 編譯下，這些診斷型別不會被編譯，亦不會進行任何診斷裝飾器的註冊，確保 Release 模式的純淨度與高效能。
  * **Session 診斷解耦**：`SessionDiagnosticsSwitch.Enabled` 已正確與 `_diagnosticTraceOptions.SessionVerbose` 綁定，與主效能追蹤開關 `Enabled` 達成解耦，且預設值為 `false`，防止了非預期的 Session 敏感資訊洩漏至 `Trace.log`。

---

### 3. 迴歸測試與測試替身 (Tests)
* **分類**：Info
* **檔案路徑**：
  * `ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs`
  * `ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs`
* **審查結果**：**通過**
* **具體資料流分析**：
  * **真實 DI 圖反映**：`GatewayArchitectureTests.cs` 中的 `Ambient_service_creates_and_releases_scope_without_http_context` 測試已補上詳細的繁體中文 XML 註解與 `<remarks>`。測試中註冊了 `IOrganizationService` 代理與 `IDataverseGateway`，忠實反映了正式環境的兩層邊界，並透過 `TrackingGateway` 驗證無 HttpContext 時 fallback scope 的確定性釋放。
  * **編譯相容性**：`ToolUtilityFactoryAmbientGatewayTests.cs` 中涉及 `TimedOrganizationService` 的測試已加上 `#if DEBUG` 包裹，避免在 Release 模式下因找不到該 Debug-only 型別而導致編譯失敗。測試替身設計良好，無跨測試或跨 Request 的狀態洩漏風險。

---

## 審查判定
本工作樹的修改完整滿足 PRD 與設計文件之要求，程式碼註解詳實且符合繁體中文與編碼規範，無任何阻擋發佈之缺陷。本任務已具備提交與合併條件。
