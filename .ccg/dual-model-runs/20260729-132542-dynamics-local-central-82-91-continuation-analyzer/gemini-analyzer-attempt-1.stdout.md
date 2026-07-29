# Dynamics Local/Central Gateway 8.2/9.1 延續性設計分析報告

本報告針對 Dynamics Local/Central Gateway 8.2/9.1 整合方案的延續實作（基於 `.trellis/tasks/07-23-dynamics-connection-compatibility` 任務）進行深入的 UI/UX、設計系統與前端架構評估，並提供具體、按風險排序的實作建議。

---

## 1. UX Analysis (使用者體驗與影響評估)

### 使用者旅程與體驗影響
- **透明的後端切換**：本設計採用「單一 ProductClient 契約，透過端點配置切換 Central/Local 拓撲」的模式。對於終端使用者（如 ChurchReport 的操作人員）而言，後端 Dynamics 365 版本的升級（從 8.2 到 9.1）或部署拓撲的調整（中央網關 vs 本地開發網關）是完全無感的。這避免了因系統升級導致的介面中斷或操作邏輯變更。
- **穩定性與效能提升**：透過網關層的連線池管理與組織級准入控制（Admission Control），能有效防止並發請求過載 Dynamics 伺服器，減少因 Dynamics 服務保護限制（Service Protection Limits）導致的 HTTP 429 (Too Many Requests) 錯誤，從而提升前端應用的回應速度與穩定性。

### 輔助功能與安全性考量 (Accessibility & Security)
- **零憑證洩漏風險**：產品端 JSON 配置檔（如 `appsettings.json`）被嚴格限制不得包含任何 Dynamics 憑證、Token 或原始 CRM URL。這確保了開發人員在本地偵錯或配置時，不會因設定檔外洩而暴露生產環境的敏感資訊。
- **Fail-Closed (安全關閉) 機制**：當網關與 Dynamics 之間的連線、憑證驗證或協調器租約失效時，系統會立即 fail-closed，使網關狀態轉為 `NotReady` 並拒絕後續請求。這在 UX 上需要有明確的錯誤代碼與友善的提示，避免前端無限期等待或產生非預期的錯誤。

---

## 2. Design Evaluation (設計系統與一致性評估)

### 執行模式的一致性
- **維持現有 Enum 契約**：規格明確禁止為了區分 Central 與 Local 網關而發明新的 `DynamicsExecutionMode` enum 值。應維持現有的 `Gateway` 與 `Embedded` 雙模式設計。
- **以端點 (Endpoint) 區分拓撲**：
  - **Central Gateway**：`Gateway.Endpoint` 指向內部共享的網關服務（如 `https://dynamics-gateway.internal/`）。
  - **Local Gateway**：`Gateway.Endpoint` 指向本地運行的網關進程（如 `https://localhost:7244/`）。
  這符合設計系統的簡潔性與一致性，避免了配置項目的冗餘。

### 產品配置合約的重構
- **Embedded 模式的遺留清理**：目前 `ProductDynamicsOptions` 中的 `EmbeddedModeOptions` 仍包含 `OrganizationWebApiBaseUri`、`CeVersion`、`SecretReference` 等原始連線細節。這與「產品 JSON 不得包含敏感資訊」的合約相違背。必須將其簡化為僅包含 `ProductProfileBinding` 與 `OrganizationAdmissionCoordinatorRef`，並透過部署資訊清單（Manifest）或中央註冊表進行驗證。

---

## 3. Technical Considerations (前端與架構技術考量)

### 關鍵發現分類 (Critical / Warning / Info)

#### **Critical (關鍵阻礙因素)**
1. **`System.Security.Cryptography.Xml` 漏洞阻礙**
   - **路徑**：`PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`
   - **理由**：NuGet 報告該套件（版本 10.0.9 依賴項）存在五個高嚴重性漏洞。在將 Data8 作為網關的 CE 8.2 臨時相容性傳輸通道之前，此漏洞是**發布阻礙點 (Release Blocker)**，必須立即修復。
2. **單一 Profile 限制**
   - **路徑**：`SpeechMessage.Dynamics.Gateway/Program.cs`
   - **理由**：目前網關主機僅載入單一 `DynamicsWebApiOptions` 配置，尚未實作支援 `crm82` 與 `crm91` 的多 profile 路由器。這會導致無法在單一網關實例中同時支援 8.2 與 9.1 版本的路由。
3. **產品配置驗證不足**
   - **路徑**：`SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs`
   - **理由**：目前的 `ProductDynamicsOptions` 驗證僅檢查非空值，尚未強制執行絕對 HTTPS、受限 API 前綴（如 `/v1`）以及拒絕非活動分支的合約。

#### **Warning (警告事項)**
1. **Data8 `OnPremiseClient` 資源洩漏風險**
   - **路徑**：`PowerPlatform.Dataverse.Client` 相關實作
   - **理由**：Data8 的 `OnPremiseClient` 實作了 `IOrganizationService` 但未實作 `IDisposable`。現有的 `CrmConnectionPool` 釋放邏輯無法保證其底層的 WCF 通道與處理常式（Handles）已被正確關閉，在高負載下存在通訊端與記憶體洩漏風險。
2. **Embedded 模式配置漂移**
   - **路徑**：`SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs`
   - **理由**：`EmbeddedModeOptions` 仍保留了原始的組織與驗證細節，未與最新的安全合約對齊。

#### **Info (一般說明)**
1. **WinRM 遠端管理限制**
   - **理由**：`192.168.50.10` 與 `192.168.50.20` 的 Port 5986 (HTTPS) 目前關閉，僅開啟 5985 (HTTP)。在未啟用 HTTPS 加密或 Kerberos 強制加密前，絕不能透過 WinRM 傳送任何敏感憑據。

---

## 4. Options (替代方案與權衡)

### 關於 CE 8.2 傳輸通道的替代方案

| 方案 | 優點 | 缺點 / 權衡 | 決策 |
| --- | --- | --- | --- |
| **方案 A：繼續在網關進程內直接使用 Data8** | 實作最簡單，無需額外的進程間通訊 (IPC)。 | 存在 WCF 通道洩漏風險，且受限於 `System.Security.Cryptography.Xml` 漏洞。 | **拒絕**。不符合安全與資源隔離合約。 |
| **方案 B：引入獨立的 Data8 Legacy Worker 進程** | 資源完全隔離。Worker 進程可定期回收，徹底解決 WCF 洩漏問題。 | 增加系統複雜度與 IPC 效能開銷。 | **推薦作為臨時過渡方案**。必須設定明確的移除期限。 |
| **方案 C：直接使用 Web API v8.2 (經 ADFS OAuth 驗證)** | 無需任何 SDK 依賴，完全符合 No-SDK 目標。 | 需要 ADFS 配合配置 OAuth Client 與 Redirect URI，可行性需視環境而定。 | **首選目標**。應優先進行可行性驗證。 |
| **方案 D：.NET Framework 4.8 Worker (使用官方 CrmServiceClient)** | 使用微軟官方 SDK，相容性最高。 | 需要維護 .NET Framework 執行期環境。 | **備用方案**。僅在方案 C 無法實施時採用。 |

---

## 5. Recommendation (推薦的執行順序與具體步驟)

為確保系統安全與架構合規，建議依據以下步驟進行 TDD 增量開發與實作：

### 步驟 1：修復安全漏洞與加強產品端配置驗證 (TDD 第一增量)
1. **修復漏洞**：
   - 修改 `PowerPlatform.Dataverse.Client.csproj`，顯式將 `System.Security.Cryptography.Xml` 升級至安全版本（如 `8.0.1` 或更高）。
2. **加強驗證**：
   - 修改 `ProductDynamicsOptions.cs`，為 `GatewayModeOptions` 的 `Endpoint` 加上自訂驗證，強制其必須為絕對 HTTPS 網址，且不得包含 CRM 原始路徑（如 `Organization.svc`）。
   - 在 `ProductClientServiceCollectionExtensions.cs` 中啟用 `ValidateOnStart()`。
3. **編寫測試**：
   - 在 `ProductModeOptionsTests.cs` 中新增測試，驗證當配置非 HTTPS 端點、包含敏感憑據或無效 API 前綴時，系統在啟動時立即失敗（Fail-Closed）。

### 步驟 2：重構網關以支援多 Profile 隔離路由
1. **定義多 Profile 配置結構**：
   - 建立 `DynamicsGatewayOptions`，包含 `Profiles` 字典（Key 為 `ProfileAlias`，Value 為 `DynamicsWebApiOptions`）。
2. **實作 `ProfileRuntimeManager`**：
   - 建立 `ProfileRuntime` 類別，封裝每個 profile 專屬的 `HttpClient`、`SocketsHttpHandler`、`TokenProvider` 與 `MetadataCache`。
   - 透過 `ProfileRuntimeKey`（包含 alias、generation、version 等）確保 `crm82` 與 `crm91` 的執行期狀態完全隔離，絕不共享連線或 Token。
3. **實作 Replace-and-Drain 邏輯**：
   - 當配置更新時，建立新世代的 `ProfileRuntime`，並將舊世代標記為 draining，在所有在途請求完成後進行 `Dispose()`。

### 步驟 3：建立隔離的 Data8 Legacy Worker 進程
1. **建立獨立專案**：
   - 建立 `SpeechMessage.Dynamics.LegacyWorker` 專案（作為獨立的主控台應用程式）。
2. **實作 IPC 通訊**：
   - 網關透過 localhost HTTP 與 Worker 通訊。Worker 內部僅負責執行 Data8 SOAP 請求，不維護狀態。
3. **實作確定性回收**：
   - 網關管理 Worker 進程的生命週期，定期重啟 Worker 以釋放可能累積的 WCF 通訊端與記憶體。

### 步驟 4：安全 VM/WinRM 驗證與 Fail-Closed 實作
1. **WinRM 安全配置**：
   - 必須在 D365 VM 上啟用 Port 5986 (HTTPS WinRM)，並配置憑證驗證。
2. **協調器 Fail-Closed**：
   - 修改 `SqlRuntimeHostSlotCoordinator.cs`，當無法與協調器資料庫或 VM 取得聯繫時，立即拒絕發放租約，使網關保持 `NotReady` 狀態。

### 步驟 5：執行驗證與回滾演練
1. **執行測試命令**：
   ```powershell
   # 執行所有單元與整合測試
   dotnet test SpeechMessage.Dynamics.Tests --configuration Release
   # 執行隔離與資源洩漏測試
   dotnet test SpeechMessage.Dynamics.Tests --filter "FullyQualifiedName~Phase4IsolationSoakTests"
   ```
2. **回滾順序定義**：
   - 若新網關部署失敗，立即將產品端的 Feature Flag 切回舊的 `ToolUtility` 直接連線路徑。
   - 確保舊的網關實例在協調器中的租約過期（Quarantine 期間）後，才允許新實例重新嘗試取得 slot。
