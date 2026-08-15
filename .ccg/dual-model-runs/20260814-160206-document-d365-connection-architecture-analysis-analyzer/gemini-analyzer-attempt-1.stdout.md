根據您提供的專案原始碼與架構文件，以下為針對 Dynamics 365 (D365) 連線路徑、端點、憑證來源與架構設計的完整分析報告。

---

### 1. 一句話結論

在目前分支的實際路徑中，**`PowerPlatform.Dataverse.Client` (Data8)** 是目前唯一在 .NET 10 上完整測試並實際承載業務流量的底層傳輸（`ConnectorKind.Data8`），它直接在產品進程內透過 WS-Trust 協定連線 D365；**`ToolUtility`** 是舊版連線工具，目前正處於被新架構逐步淘汰的遷移期；而 **`Official Worker`**（`Crm82Worker` / `Crm91Worker`）則是作為擴充點保留的獨立 .NET Framework 4.8 進程，使用官方 `CrmServiceClient` 並透過具名管道 (Named Pipe) IPC 溝通，但目前處於未經真機驗證（`evidence-pending`）的停用狀態。

---

### 2. 逐段 Runtime Call Chain

#### 路線 A：新架構路徑（目前預設且唯一啟用的 Data8 傳輸）
1. **產品呼叫點**：產品端（如 `ChurchReport`）呼叫 `ProductClient` 的業務方法。
   * *檔案與行號*：`SpeechMessage.Dynamics.ProductClient\MemberInfo\Package02ContactProfileClient.cs`
2. **業務執行器**：`ProductClient` 呼叫 `IDynamicsOperationExecutor` 的 `ExecuteAsync`。
   * *檔案與行號*：`SpeechMessage.Dynamics.ProductClient\Gateway\GatewayDynamicsOperationExecutor.cs`
3. **路由與控制面**：`ProfileRoutedOperationExecutor` 透過 `IConnectorRouter`（實作為 `CompositeConnectorRouter`）依據 `ResolvedProfile.ConnectorKind` 進行路由。
   * *檔案與行號*：`SpeechMessage.Dynamics.ControlPlane\Runtime\ProfileRoutedOperationExecutor.cs`
   * *檔案與行號*：`SpeechMessage.Dynamics.ControlPlane\Connectors\CompositeConnectorRouter.cs`
4. **連接池與租約**：路由到 `Data8ConnectorPool`，借出 `Data8ConnectorLease`。
   * *檔案與行號*：`SpeechMessage.Dynamics.Connectors.Data8\Data8ConnectorPool.cs`
5. **連線工廠**：`Data8ConnectorPool` 透過 `OnPremiseData8ConnectorClientFactory` 建立 `IOrganizationService`。
   * *檔案與行號*：`SpeechMessage.Dynamics.Connectors.Data8\OnPremiseData8ConnectorClientFactory.cs`
6. **底層傳輸**：建立 `OnPremiseClient`（來自 `PowerPlatform.Dataverse.Client` 專案），直接連線至 D365 On-Premises 的 `Organization.svc`。
   * *檔案與行號*：`PowerPlatform.Dataverse.Client\OnPremiseClient.cs`

#### 路線 B：舊架構路徑（Legacy ToolUtility 遷移期過渡路徑）
1. **產品呼叫點**：產品端直接呼叫 `ToolUtilityClass` 或 `ToolUtilityFacade` 的方法。
   * *檔案與行號*：`ToolUtility\Core\ToolUtilityFacade.cs` (或 `ToolUtilityClass.cs`)
2. **連線工廠**：呼叫 `CrmClientFactory.Create` 建立 `ICrmClient`。
   * *檔案與行號*：`ToolUtility\Factories\CrmClientFactory.cs` 第 58-87 行
3. **Adapter 包裝**：
   * 若 `CrmConnection:Type` 為 `Dataverse`，則回傳 `DataverseServiceClientAdapter`（包裝了 `Microsoft.PowerPlatform.Dataverse.Client.ServiceClient`）。
     * *檔案與行號*：`ToolUtility\Adapters\DataverseServiceClientAdapter.cs` 第 60-70 行
   * 若為 `Legacy`，則回傳 `LegacyOrganizationServiceAdapter`（包裝了舊的 WCF 連線）。
     * *檔案與行號*：`ToolUtility\Adapters\LegacyOrganizationServiceAdapter.cs`

#### 路線 C：官方 Worker 備用路徑（未啟用，擴充點）
1. **路由與控制面**：`CompositeConnectorRouter` 路由到 `OfficialWorkerConnectorPool`。
   * *檔案與行號*：`SpeechMessage.Dynamics.ControlPlane\Connectors\CompositeConnectorRouter.cs`
2. **進程監督**：`WorkerSupervisor` 啟動獨立的 .NET Framework 4.8 Worker 進程（`Crm82Worker.exe` 或 `Crm91Worker.exe`）。
   * *檔案與行號*：`SpeechMessage.Dynamics.WorkerSupervisor\OfficialWorkerProfileExecutor.cs`
3. **IPC 溝通**：透過 Named Pipe IPC 將請求傳遞給 Worker 進程。
   * *檔案與行號*：`SpeechMessage.Dynamics.WorkerHost` 專案
4. **Worker 內部連線**：Worker 進程中的 `OfficialCrmServiceClientFactory` 建立官方的 `CrmServiceClient`。
   * *檔案與行號*：`SpeechMessage.Dynamics.Crm91Worker\OfficialCrmServiceClientFactory.cs` 第 102-140 行
   * *檔案與行號*：`SpeechMessage.Dynamics.Crm82Worker\OfficialCrmServiceClientFactory.cs` 第 73-110 行

---

### 3. Endpoint 清單與各自用途／來源

| Endpoint URL | 用途 | 來源設定鍵 |
| --- | --- | --- |
| `https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc` | 聖谷行道會 (公司研發) D365 9.1 On-Premises Organization Service (WCF SOAP) 端點 | `CrmConnection:ServerUrl`<br>`CrmConnection:OrganizationCatalog:sunnyvalechback:ServiceUri` |
| 其他 31 個組織（如 `speechmessage-ce82`、`speechmessage-ce91`、`elijah`、`david` 等） | 尚未配置 ServiceUri，選取時會直接 fail closed | `CrmConnection:OrganizationCatalog:[Alias]:ServiceUri` (目前為空) |

---

### 4. 連線參數表

| 參數名稱 | 來源 | 是否敏感 | 消費者 (Consumer) | 說明 |
| --- | --- | --- | --- | --- |
| `CrmConnection:Type` | `appsettings.json` | 否 | `CrmClientFactory` | 決定使用 `Dataverse` (ServiceClient) 或 `Legacy` (LegacyOrganizationServiceAdapter) |
| `CrmConnection:ConnectionString` | `appsettings.json` | 是 | `CrmClientFactory` / `DataverseServiceClientAdapter` | 官方 `ServiceClient` 的連線字串 |
| `CrmConnection:ServerUrl` | `appsettings.json` | 否 | `CrmClientFactory` / `LegacyOrganizationServiceAdapter` | 舊版 WCF SOAP 服務端點 URL |
| `CrmConnection:Domain` | `appsettings.json` | 否 | `CrmClientFactory` / `LegacyOrganizationServiceAdapter` | AD 網域 |
| `CrmConnection:Username` | `appsettings.json` | 否 | `CrmClientFactory` / `LegacyOrganizationServiceAdapter` | AD 帳號 |
| `CrmConnection:Password` | `appsettings.json` / User Secrets / 環境變數 | 是 | `CrmClientFactory` / `LegacyOrganizationServiceAdapter` | AD 密碼 |
| `CrmConnection:OrganizationCatalog` | `appsettings.json` | 否 | `CrmConnectionEmbeddedProfileMapper` / `ConfigurationProfileResolver` | 組織目錄，包含各組織的 GUID、版本與 ServiceUri |
| `DynamicsAccess:ProfileAlias` | `appsettings.json` | 否 | `ProductClient` / `ControlPlane` | 產品端唯一的選擇鍵，用來解析對應的 Profile |
| `DynamicsAccess:ConnectionMode` | `appsettings.json` | 否 | `ProductClient` / `ControlPlane` | 決定執行模式 (`Embedded` / `DedicatedGateway` / `CentralGateway`) |
| `DynamicsAccess:Gateway:Endpoint` | `appsettings.json` | 否 | `ProductClient` | Gateway 模式下的 HTTP API 端點 |

---

### 5. 帳號與密碼來源說明

* **帳號識別設定**：
  * 帳號為 `SPEECHMESSAGE\Administrator`（或依部署環境配置的 AD 帳號）。
  * 來源：`appsettings.json` 中的 `CrmConnection:Username`。
* **密碼／Secret 來源**：
  * 密碼在 `appsettings.json` 中預設為 `"REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT"`。
  * **開發環境**：使用 .NET 的 **User Secrets** 機制（`secrets.json`），將 `CrmConnection:Password` 設定在開發人員本機，避免將密碼提交至 Git 倉庫。
  * **正式／部署環境**：透過 **環境變數**（Environment Variables）或 **Secret Store**（如 Azure Key Vault、Windows 憑證管理員等）注入。

---

### 6. 不確定或只能由部署環境確認的部分

1. **其他組織的 `ServiceUri`**：除了 `sunnyvalechback` 之外，其餘 31 個組織在 `appsettings.json` 中均未配置 `ServiceUri`。這些組織的實際連線端點必須在部署環境的 `appsettings.Production.json` 或環境變數中配置，否則在載入 Profile 時會直接 fail closed。
2. **正式環境的憑證與密碼**：正式環境的 AD 帳號密碼或 Client Secret 儲存在何處（例如 Windows 憑證管理員、Azure Key Vault 或 Kubernetes Secrets），無法從原始碼中直接確認，必須由部署環境的 `CredentialProvider` 實作與環境變數配置來決定。
3. **官方 Worker 的真機連線可行性**：官方 Worker (`OfficialCrm82Worker` / `OfficialCrm91Worker`) 的真機連線相容性目前標記為 `evidence-pending`，因為在 SDK 驗證邊界（WS-Trust / ADFS 驗證）上仍有未解決的阻礙，實際是否能成功連線必須在部署環境中進行真機驗證。

---

### 7. 圖面建議

架構圖應包含以下元件與流程：

```
+---------------------------------------------------------------------------------+
|                               ChurchReport (產品進程)                            |
|  [ProductClient] ──(ProfileAlias: sunnyvalechback)──> [IDynamicsOperationExecutor] |
+-------------------------------------------------------┬-------------------------+
                                                        │
                                                        ▼
+---------------------------------------------------------------------------------+
|                            Dynamics ControlPlane (控制面)                        |
|  [RequestGuard] (驗證請求，阻擋自訂 OrgId/Credential/FetchXML)                    |
|                                                       │
|  [ProfileResolver] (讀取 OrganizationCatalog 取得 ServiceUri 與 ConnectorKind)   |
|                                                       │
|  [ConnectorRouter] (依據 ConnectorKind 進行路由分流)                              |
+-----------------------┬-------------------------------┬-------------------------+
                        │                               │
             (ConnectorKind == Data8)        (ConnectorKind == OfficialWorker)
                        │                               │
                        ▼                               ▼
+---------------------------------------+ +---------------------------------------+
|          Data8ConnectorPool           | |      OfficialWorkerConnectorPool      |
|  (管理 OnPremiseClient 租約)           | |  (管理 Worker 進程與 IPC 租約)         |
|                                       | |                                       |
|  [OnPremiseClient] (Data8 SDK)        | |  [WorkerSupervisor] (net10)           |
|  (跑在 .NET 10 產品進程內)             | |            │ (Named Pipe IPC)         |
|                                       | |            ▼                         |
|                                       | |  [CrmWorker.exe] (net48 獨立進程)      |
|                                       | |  (使用官方 CrmServiceClient)           |
+-----------------------┬---------------+ +---------------------┬-----------------+
                        │                                       │
                        │ (WS-Trust SOAP)                       │ (CrmServiceClient SOAP)
                        ▼                                       ▼
+---------------------------------------------------------------------------------+
|                          Dynamics 365 On-Premises 系統                          |
|  Endpoint: https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/...    |
+---------------------------------------------------------------------------------+
```

---

### 8. 安全與架構審查發現 (Reviewer Findings)

#### 🔴 Critical
* **參數越權與 Fallback 漏洞風險**：產品端與請求中嚴禁攜帶 `OrganizationId`、`ConnectorKind`、`Credential`、`ServiceUri` 或 `FetchXML`，必須由 `RequestGuard` 進行 fail-closed 攔截，以防止跨租戶越權與 request-time fallback 漏洞。
* **敏感資訊洩漏風險**：帳密等敏感資訊嚴禁寫死在 `appsettings.json` 中，必須透過 User Secrets (開發環境) 或環境變數/Secret Store (生產環境) 注入。

#### 🟡 Warning
* **官方 Worker 未驗證風險**：官方 Worker (`OfficialCrm82Worker` / `OfficialCrm91Worker`) 目前處於 `evidence-pending` 狀態，其 WS-Trust / ADFS 驗證在 net48 獨立進程中尚未通過真機驗證，若要啟用此分支需進行實機測試。
* **未配置端點風險**：`OrganizationCatalog` 中除了 `sunnyvalechback` 之外，其餘 31 個組織均未配置 `ServiceUri`，在未配置前選取這些組織會直接導致連線失敗 (fail closed)。

#### 🟢 Info
* **Data8 傳輸定位**：`Data8` (即 `PowerPlatform.Dataverse.Client` 專案中的 `OnPremiseClient`) 是目前唯一在 .NET 10 上完整測試並承載業務流量的 `ConnectorKind`。
* **ToolUtility 淘汰狀態**：`ToolUtility` 專案目前處於被新架構逐步淘汰的遷移期。
