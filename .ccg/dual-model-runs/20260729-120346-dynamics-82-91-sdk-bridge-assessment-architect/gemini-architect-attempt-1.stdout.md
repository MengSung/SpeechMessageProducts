An analysis of the Dynamics 365 CE 8.2 / 9.1 SDK Bridge architecture has been performed. Below is the comprehensive assessment report formatted as a Unified Diff Patch, proposing the creation of a new documentation file `docs/dynamics-82-91-sdk-bridge-assessment.md` containing the detailed findings, architecture decisions, and migration plans.

```diff
--- /dev/null
+++ b/docs/dynamics-82-91-sdk-bridge-assessment.md
@@ -0,0 +1,265 @@
+# Dynamics 365 CE 8.2 / 9.1 SDK Bridge 架構評估報告
+
+## 1. 審查發現分類 (Reviewer Findings)
+
+### 🔴 Critical (關鍵風險)
+* **檔案路徑**: `PowerPlatform.Dataverse.Client/OnPremiseClient.cs`
+  * **風險說明**: 該類別實作了 `IOrganizationService` 但**未實作 `IDisposable`**。底層的 WCF 通道（`ChannelFactory` 與 `IClientChannel`）在連線釋放時無法被正確關閉（`Close`/`Abort`）。這會導致 `CrmConnectionPool` 在清理閒置連線時無法釋放底層 TCP 連線，在高併發環境下會迅速導致 Socket 耗盡（Socket Exhaustion）與記憶體洩漏，造成系統崩潰。
+  * **架構影響**: 雖然此專案作為 .NET 10 的 WS-Trust 橋樑解決了連線問題，但其生命週期管理的缺失使其在生產環境中存在極高的不穩定性。
+* **檔案路徑**: `ToolUtility/ToolUtility.csproj`
+  * **風險說明**: 專案直接參考了非官方且 README 中明示「無官方支援（best effort only）」的 Data8 開源專案。這在企業級整合中存在極高的維護風險，一旦遇到 ADFS 安全更新或 .NET 執行期重大變更，將面臨無人維護的困境。
+
+### 🟡 Warning (次要風險)
+* **檔案路徑**: `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
+  * **風險說明**: 目前 Web API 方案因 ADFS 未註冊 OAuth Client/Redirect URI 且拒絕 password grant 而被阻擋。這使得系統被迫退回到 WS-Trust/SOAP 舊路徑，增加了對 Data8 專案的依賴時間。
+* **檔案路徑**: `ToolUtility/ConnectionOperations/CrmConnectionService.cs`
+  * **風險說明**: 若未來嘗試在單一 .NET Framework Worker 中同時載入 CE 8.2 與 CE 9.1 的官方 SDK，會因為 `Microsoft.Xrm.Sdk.dll` 的版本衝突（v8.x vs v9.x）導致執行期載入失敗或行為異常。
+
+### 🟢 Info (架構資訊)
+* **檔案路徑**: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
+  * **說明**: 該專案實作了 no-SDK Web API 客戶端，利用 `HttpClient` 呼叫 OData v8.2/v9.1 端點，方向正確，能有效擺脫 SDK 依賴，但需等待 ADFS 驗證管道打通。
+
+---
+
+## 2. 問題直接回答 (Direct Answers to Questions 1-3)
+
+### Q1: Dynamics CE 8.2 是否固有地需要 Data8 專案來與 ASP.NET Core / .NET 10 整合？
+**回答**: **否，Data8 專案並非固有需要。**
+* Dynamics CE 8.2 提供了標準的 OData v4 Web API（端點為 `/api/data/v8.2/`），可以直接使用 .NET 10 的 `HttpClient` 進行呼叫，不需要任何 SDK 組件。
+* Data8 專案（`PowerPlatform.Dataverse.Client`）僅是一個**相容性橋樑**，用來在 .NET 10 環境下透過 WS-Trust/SOAP 協定存取 Dynamics。
+* 目前之所以需要它，是因為當前基礎架構的驗證限制（ADFS 未註冊 OAuth 用戶端、不支援 password grant、Web API 走 NTLM 會被導向 IFD），導致 Web API 無法使用，只能退而求其次使用 WS-Trust/SOAP。一旦驗證條件（如 OAuth 註冊）就緒，即可擺脫對 Data8 專案的依賴。
+
+### Q2: 支援 CE 8.2 與 CE 9.1 的最安全架構是什麼？
+**回答**: 採用 **Local Gateway (處理程序邊界隔離)** 架構。
+* **產品應用程式 (.NET 10)**：完全不直接參考 Dynamics SDK 或 Data8 專案，而是透過 HTTP/gRPC 呼叫 Local Gateway。
+* **Local Gateway (.NET 10)**：作為一個獨立的服務，負責路由與驗證。
+* **後端 Worker 處理程序**：
+  * **CE 9.1**：如果支援 OAuth，可以直接在 Gateway 中使用官方的 `Microsoft.PowerPlatform.Dataverse.Client` (`ServiceClient`)，或者使用 Direct Web API。
+  * **CE 8.2**：如果仍受限於 WS-Trust/SOAP 驗證，可以在 Gateway 旁部署一個 **.NET Framework 4.8 Worker 處理程序**，該 Worker 使用官方的 `Microsoft.CrmSdk.XrmTooling.CoreAssembly` (`CrmServiceClient`)，透過 WCF/WS-Trust 與 CE 8.2 進行通訊。這樣可以完全避免在 .NET 10 中載入不支援的舊版 WCF/WS-Trust 套件。
+
+### Q3: 官方 SDK 相容性設計中，應使用單一還是獨立 Worker？
+**回答**: **強烈建議在初期使用獨立的、版本鎖定 (version-pinned) 的 Worker 處理程序。**
+* **二進位/版本風險**：官方的 `Microsoft.Xrm.Sdk.dll` 和 `Microsoft.Crm.Sdk.Proxy.dll` 在 v8.x (CE 8.2) 和 v9.x (CE 9.1) 之間存在重大變更。如果在同一個 .NET Framework 應用程式中同時載入這兩個版本的 SDK，會發生組件版本衝突（Assembly Version Conflicts），即使使用 `bindingRedirect` 也可能因為 API 簽章變更或行為差異而導致執行期錯誤。
+* **驗證風險**：CE 8.2 IFD 依賴舊版的 ADFS WS-Trust 1.3 驗證，而 CE 9.1 可能已啟用現代 OAuth。混合在同一個 Worker 中，WCF 安全通道的設定與 ADFS 憑證驗證邏輯可能會互相干擾。
+* **合併前必須測試的項目**：
+  1. **組件相容性測試**：驗證 v9.x 的 SDK 是否能完全向下相容並成功呼叫 CE 8.2 的 SOAP 端點（`Organization.svc`）。
+  2. **驗證協商測試**：測試單一 Worker 是否能同時處理 CE 8.2 的 WS-Trust 驗證與 CE 9.1 的 OAuth/WS-Trust 驗證，且不會發生 ADFS Token 衝突或 Session 污染。
+  3. **連線池與通道生命週期測試**：在高併發下，測試 WCF 通道是否會因為版本差異導致連線洩漏或 Socket 耗盡。
+
+---
+
+## 3. 相容性與風險評估表 (Compatibility & Risk Table)
+
+| 整合方案 | CE 8.2 相容性 | CE 9.1 相容性 | 驗證要求 | 主要風險與限制 |
+| :--- | :--- | :--- | :--- | :--- |
+| **Direct Web API** | 相容 (v8.2) | 相容 (v9.1) | OAuth Bearer (ADFS) | 🔴 **需真機驗證**: 需在 ADFS 註冊 Client ID 與 Redirect URI，且不支援 password grant。 |
+| **Official `ServiceClient` (.NET 10)** | 不相容 (WS-Trust) | 相容 | OAuth / ClientSecret | 🔴 **限制**: 在 .NET 10 上不支援舊版 WS-Trust/SOAP 驗證，無法直接連線 CE 8.2 IFD。 |
+| **Official .NET Framework Worker** | 相容 (v8.x SDK) | 相容 (v9.x SDK) | Windows AD / WS-Trust | 🟡 **風險**: 需維護額外的 .NET Framework 4.8 處理程序，且兩版本 SDK 存在組件衝突風險。 |
+| **Data8 .NET 10 Bridge (當前)** | 相容 | 相容 | WS-Trust / SOAP | 🔴 **關鍵風險**: `OnPremiseClient` 未實作 `IDisposable`，存在 Socket 耗盡與記憶體洩漏風險。 |
+
+---
+
+## 4. 架構元件與流程圖 (Component & Process Diagram)
+
+```mermaid
+graph TD
+    subgraph Product Applications (.NET 10)
+        CR[ChurchReport] -->|HTTP/gRPC| GW[Local Gateway]
+    end
+
+    subgraph Local Gateway (.NET 10)
+        GW -->|Route 9.1| WA[Direct Web API Client]
+        GW -->|Route 8.2 Fallback| D8[Data8 OnPremiseClient]
+        GW -->|Route 8.2 Target| WProxy[Worker Proxy Client]
+    end
+
+    subgraph Out-of-Process Workers
+        WProxy -->|IPC/gRPC| FW[Net48 Worker Process]
+        FW -->|Official SDK v8.x| CRM82[Dynamics CE 8.2 IFD]
+    end
+
+    WA -->|OAuth Bearer| CRM91[Dynamics CE 9.1]
+    D8 -->|WS-Trust/SOAP| CRM82
+```
+
+---
+
+## 5. 決策與移除準則 (Decisions & Removal Criteria)
+
+### 立即決策 (Immediate Decision)
+* **決策**: **保留 (Retain)** 簽入的 Data8 專案。
+* **理由**: 目前 `ChurchReport` 與 `ToolUtility` 仍直接依賴 `PowerPlatform.Dataverse.Client.OnPremiseClient` 來與 CE 8.2 進行連線。立即刪除會導致建置中斷與連線失效。
+
+### 最終狀態決策與可衡量的移除準則 (Final-State Decision & Measurable Removal Criteria)
+* **最終狀態**: 完全移除 Data8 專案。
+* **可衡量的移除關卡 (Gates)**:
+  1. **Gateway 部署驗證**: Local Gateway 部署完成，且產品應用程式已完全切換為呼叫 Gateway 的 HTTP API，不再直接參考 `ToolUtility` 中的舊版 CRM 連線方法。
+  2. **驗證管道打通**:
+     * *方案 A (Web API)*: Dynamics 8.2/9.1 伺服器與 ADFS 完成設定，註冊了 OAuth Client ID 與 Redirect URI，且 Web API 能夠透過 OAuth Bearer Token 正常存取。
+     * *方案 B (Official SDK Worker)*: 已建立基於 .NET Framework 4.8 的官方 SDK Worker，能夠穩定處理 CE 8.2 的 WS-Trust/SOAP 連線。
+  3. **斷開產品依賴**: 重構 `ToolUtility`，移除對 `PowerPlatform.Dataverse.Client` 專案的參考，並刪除 `CrmConnectionService.CreateOnPremiseClient` 方法。此時專案應能成功建置。
+  4. **原始碼移除**: 從方案中移除 `PowerPlatform.Dataverse.Client.csproj` 專案目錄，並清理相關的 WCF/WS-Trust NuGet 套件。
+
+---
+
+## 6. 需真機驗證之結論標記 (Real-Server Validation Flags)
+
+* 🔍 **[VALIDATION-01] ADFS OAuth 授權碼流程註冊**: 必須在實際的 ADFS 伺服器上執行 `Add-AdfsClient` 註冊 Client ID 與 Redirect URI，並驗證 .NET 10 Web API 是否能成功取得 Access Token。
+* 🔍 **[VALIDATION-02] CE 8.2 Web API 相容性**: 必須驗證 CE 8.2 的 Web API 是否支援產品所需的所有 CRUD 與 FetchXML 查詢操作，因為舊版 Web API 的功能集可能少於 SOAP 端點。
+* 🔍 **[VALIDATION-03] WCF 通道釋放與 Socket 監控**: 在使用 Data8 專案期間，必須在測試環境監控 TCP 連線狀態（使用 `netstat`），驗證 `OnPremiseClient` 未實作 `IDisposable` 是否已導致 Socket 殘留。
```
