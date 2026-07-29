An analysis of the architecture has been performed. The recommended decision is to **remove the Embedded execution path entirely** and standardize on the **Gateway architecture** (using a Central Gateway in production and Local Gateway sidecars for development/isolated deployment).

Below is the unified diff patch that documents this architectural decision, including the detailed analysis, rationale, configuration models, and debugging workflows.

```diff
--- /dev/null
+++ b/docs/dynamics-local-vs-embedded-analysis.md
@@ -0,0 +1,150 @@
+# 架構分析報告：Local Gateway 評估與 Dynamics 連線模式決策
+
+## 1. 分析 (Analysis) - 當前架構評估與發現分類
+
+針對 `SpeechMessageProducts` 儲存庫中 `ChurchReport` 產品連接 Dynamics 365 9.1 On-Premises/IFD 環境的現狀，我們將發現分類如下：
+
+### 🔴 Critical (關鍵缺陷/風險)
+1. **.NET 10 與舊版 WS-Trust 驗證的相容性衝突**：
+   - `ChurchReport` 目標框架為 .NET 10，而 D365 9.1 On-Premises/IFD 依賴 WS-Trust 進行驗證。
+   - 官方 SDK（如 `CrmServiceClient`）在 .NET Core/.NET 5+ 上對 WS-Trust 的支援極為有限，迫使 Embedded 模式必須使用自訂的無 SDK Web API 實作（`SpeechMessage.Dynamics.WebApi`）或第三方的 Data8 WS-Trust 專案。這帶來了極大的維護成本與安全合規風險。
+2. **連線池所有權混亂與伺服器過載**：
+   - 在 Embedded 模式下，每個產品進程（共 4-10 個產品）都擁有自己獨立的實體連線池。這會導致 Dynamics 伺服器連線數暴增，且無法進行全域的流量限制（Throttling）與容量限制（Bounded Capacity）。
+3. **資源與記憶體洩漏風險**：
+   - `DonationDynamicsAccessBootstrap.cs` 中的 `EmbeddedProviders`（`ConcurrentDictionary<string, IServiceProvider>`）為進程級靜態快取，缺乏適當的處置（Disposal）與排空（Drain）路徑。當組態變更或 Profile 輪替時，舊的 `IServiceProvider`（及其內部的 `HttpClient`、Socket、Timer）永遠不會被釋放，違反了確定性釋放（Deterministic Disposal）的原則。
+
+### ⚠️ Warning (警告/潛在風險)
+1. **憑證洩漏與安全邊界模糊**：
+   - Embedded 模式要求產品進程直接接觸並處理 Dynamics 的敏感憑證（如帳號密碼、Client Secret 等），違反了最小權限原則與憑證隔離要求。
+2. **隱性自動啟用本機開發密碼授權**：
+   - `DonationDynamicsAccessBootstrap.cs` 中，當 `AuthMode=AdfsOAuth` 且 `ManifestOrRegistrySource=local-dev-manifest` 時，會自動將 `AllowLocalDevPasswordGrant` 設為 `true`。這種僅靠字串比對的守門機制容易在正式環境中因組態錯誤而誤用，建議改用明確的環境旗標（如 `IsDevelopment()`）。
+
+### ℹ️ Info (架構資訊)
+1. **本機網路跳躍延遲可忽略不計**：
+   - 產品與 Local Gateway 之間的本機 loopback (localhost) HTTP 呼叫延遲通常小於 1-2 毫秒，相對於 Dynamics OData/SOAP 呼叫本身的數十毫秒延遲，此效能折衷完全可以忽略。
+2. **開發體驗優化**：
+   - 透過 Visual Studio 2026 的多專案啟動設定，開發人員可以輕鬆同時啟動產品與 Local Gateway，並在獨立的進程中觀察 Dynamics 連線日誌。
+
+---
+
+## 2. 架構決策 (Architecture Decision)
+
+### 決定性推薦架構
+**徹底移除 Embedded 模式，全面採用 Gateway 架構。在生產環境部署 Central Gateway，在本機開發與隔離部署中使用 Local Gateway sidecars。**
+
+#### 決策理由 (Rationale)
+1. **完全移除第三方 WS-Trust 依賴**：
+   - 將 Gateway 託管在 .NET Framework 4.8 的獨立進程中，可以直接使用微軟官方的 `Microsoft.CrmSdk.XrmTooling.CoreAssembly` 與 `CrmServiceClient`，完美支援 D365 9.1 On-Premises/IFD 驗證，徹底擺脫第三方 Data8 專案。
+2. **統一的連線池治理**：
+   - 不論是生產環境的 Central Gateway 還是開發環境 of Local Gateway，都擁有實體連線池的唯一所有權，實現了容量限制（Bounded Capacity）、確定性釋放（Deterministic Disposal）和跨產品的連線複用。
+3. **強大的安全與憑證隔離**：
+   - 產品進程（如 `ChurchReport`）完全不需要接觸 Dynamics 的敏感憑證，所有憑證都安全地隔離在 Gateway 進程中。
+4. **簡化的開發與偵錯體驗**：
+   - 開發人員只需啟動 Local Gateway 專案，即可透過 localhost 進行觀察和偵錯。Dynamics 的連線狀態、API 呼叫日誌都集中在 Local Gateway 中，不會與產品進程的日誌混雜。
+
+### 拒絕的替代方案 (Rejected Alternatives)
+1. **替代方案 A：同時保留 Local Gateway 和 Embedded 模式**
+   - *拒絕理由*：這會帶來雙重維護負擔。我們必須同時維護兩套完全不同的執行路徑（進程內的 Web API 呼叫 vs 進程外的 HTTP 呼叫）。此外，為了讓 Embedded 支援 .NET 10，我們仍必須保留自訂的 Web API 實作或第三方 WS-Trust 庫，這與「移除第三方 Data8 專案、改用官方 SDK」的目標相衝突。
+2. **替代方案 B：僅將 Embedded 保留為延遲/實驗性選項**
+   - *拒絕理由*：這只是延遲了架構決策，並沒有解決根本問題。保留實驗性的 Embedded 意味著我們仍需在程式碼庫中保留相關的抽象層和條件分支，增加了系統的複雜度。
+
+### 假設與潛在副作用 (Assumptions & Side Effects)
+- **假設**：本機開發環境允許運行多個進程（產品進程 + Local Gateway sidecar 進程）。
+- **副作用**：開發人員在 Visual Studio 2026 中需要配置多專案啟動，或在本機背景運行 Local Gateway 服務。
+
+---
+
+## 3. 實作與配置計劃 (Implementation & Configuration Plan)
+
+### 3.1 推薦的產品 JSON/組態模型
+
+在 Gateway 架構下，產品（如 `ChurchReport`）的 `appsettings.json` 將變得非常乾淨，完全不包含 Dynamics 憑證：
+
+```json
+{
+  "DynamicsAccess": {
+    "Package01FeeReadsEnabled": true,
+    "ExecutionMode": "Gateway",
+    "ProfileAlias": "jesus-prod",
+    "Gateway": {
+      "Endpoint": "http://localhost:5001",
+      "ApiPrefix": "/v1"
+    }
+  }
+}
+```
+
+在生產環境中，只需將 `Endpoint` 改為 Central Gateway 的內部 DNS 名稱即可（例如 `https://dynamics-gateway.internal`）。
+
+而 Local Gateway 的組態則包含實際的 Dynamics 連線資訊（僅在 Gateway 進程中）：
+```json
+{
+  "CrmConnection": {
+    "Url": "https://crm.yourdomain.com/orgname",
+    "AuthType": "Claims",
+    "Username": "domain\\user",
+    "Password": "secret_password"
+  }
+}
+```
+
+### 3.2 推薦的 Visual Studio 2026 啟動/偵錯工作流程
+1. **多專案啟動設定 (Multiple Startup Projects)**：
+   - 在 VS 2026 中，將方案設定為「Multiple Startup Projects」。
+   - 設定 `SpeechMessage.Dynamics.Gateway` (Local Gateway) 與 `ChurchReport` 同時啟動。
+2. **本機 Sidecar 模式**：
+   - Local Gateway 啟動後監聽 `http://localhost:5001`。
+   - `ChurchReport` 啟動後，其 `DonationDynamicsAccessBootstrap` 會讀取組態，並透過 HTTP client 連接至 Local Gateway。
+3. **獨立觀察與偵錯**：
+   - 開發人員可以在 Local Gateway 的控制台或日誌中，清晰地觀察到每一次 OData/SOAP 請求的執行時間、傳回的資料以及連線池的狀態。
+
+### 3.3 元件重用與變更說明
+- **可重複使用**：
+  - `DonationDedicationFeeFormService` 和 `Package01FeeReadClient`：這些產品層的服務完全不需要修改，因為它們依賴於 `IDynamicsOperationExecutor` 抽象。
+  - `SpeechMessage.Dynamics.ProductClient`：這是產品端用來與 Gateway 通訊的 HTTP client，完全可以保留並繼續使用。
+- **必須改變**：
+  - `DonationDynamicsAccessBootstrap`：移除 `CreateEmbeddedExecutor`、`ProcessHost.GetOrCreateEmbeddedExecutor` 以及所有與 Embedded 相關的憑證橋接邏輯。
+  - 移除 `SpeechMessage.Dynamics.Embedded` 專案。
+  - 移除 `SpeechMessage.Dynamics.WebApi` 專案（或將其功能合併至 Gateway 中）。
+  - 移除 `PowerPlatform.Dataverse.Client` 自訂專案及第三方 Data8 WS-Trust 依賴。
+
+---
+
+## 4. 關鍵約束與考量 (Considerations)
+
+### 4.1 關鍵安全性/隔離/生命週期約束
+1. **Session 隔離與憑證隔離**：
+   - Gateway 必須確保不同產品、不同使用者的請求在連線池中是嚴格隔離的。使用官方 `CrmServiceClient` 時，必須確保不會因為連線複用而導致 CallerId 或使用者上下文的混淆。
+2. **容量限制 (Bounded Capacity)**：
+   - Gateway 的連線池必須設定最大連線數上限，防止突發流量將 Dynamics 伺服器壓垮。
+3. **確定性釋放 (Deterministic Disposal)**：
+   - 當 Gateway 停止或重新載入組態時，必須確保舊的連線池和 `HttpClient` 被優雅地關閉並釋放資源，避免 Socket 洩漏。
+
+### 4.2 保留、延遲或移除 Embedded 的明確條件
+- **移除 Embedded 的條件（目前已滿足）**：
+  1. 官方 SDK 支援（如 `CrmServiceClient`）在產品目標框架（.NET 10）上無法直接且穩定地支援舊版 D365 9.1 On-Premises/IFD 驗證。
+  2. 營運上需要集中管理多個產品的連線池，以避免 Dynamics 伺服器過載。
+  3. 產品進程需要與敏感的 Dynamics 憑證進行物理隔離。
+  4. Local Gateway sidecar 能夠提供同等簡單且更易於觀察的本機開發偵錯體驗。
+- **延遲/保留 Embedded 的條件（若未來發生）**：
+  1. 產品與 Dynamics 之間的通訊存在極度嚴苛的延遲要求（例如單次呼叫必須小於 1 毫秒），且本機 loopback 的 1-2 毫秒延遲被證實是效能瓶頸。
+  2. 部署環境受到嚴格限制，不允許運行多個進程或 sidecar 容器。
```
