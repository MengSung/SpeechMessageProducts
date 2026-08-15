### 1. 一句話結論

在目前實際路徑中：
* **Gateway / Official Worker** 是目前設計的預設生產路徑（新架構）：產品端（Product Client）透過 HTTP/REST 呼叫 **Gateway**，Gateway 再透過 Named Pipe 啟動並控制獨立的 **Official Worker** 進程（`Crm82Worker` / `Crm91Worker`），由 Worker 使用微軟官方的 `CrmServiceClient` 建立與 D365 的連線。
* **Data8 / PowerPlatform.Dataverse.Client**（包含 `OnPremiseClient`）是專案中借用的第三方 WS-Trust/SOAP 實作，在 Dedicated Gateway 模式或單元測試（如 `LivePackage02` 測試）中作為連接器使用，或在舊版相容路徑中作為 fallback。
* **ToolUtility** 是舊版架構的連線工具類別，其 `CrmConnectionService` 透過呼叫 `PowerPlatform.Dataverse.Client` 的 `OnPremiseClient` 來建立連線，目前正處於被 Gateway/REST 介面逐步取代的過渡階段。

---

### 2. 逐段 Runtime Call Chain

#### 路徑 A：新架構路徑 (Gateway + Official Worker 模式)
1. **產品呼叫點**：產品服務呼叫 `IPackage01FeeReadClient` 等客戶端介面。
   * *檔案*：`SpeechMessage.Dynamics.ProductClient\FeeReads\Package01FeeReadClient.cs`
2. **HTTP 轉發**：`GatewayDynamicsOperationExecutor.ExecuteAsync` 將請求封裝為 JSON 並 POST 至 Gateway。
   * *檔案*：`SpeechMessage.Dynamics.ProductClient\Gateway\GatewayDynamicsOperationExecutor.cs` (行 63-122)
3. **Gateway 接收與路由**：Gateway 接收請求，通過 `CompositeConnectorRouter` 路由至對應的 `OfficialWorkerConnectorPool`。
   * *檔案*：`SpeechMessage.Dynamics.Gateway\Program.cs` (行 302-388)
   * *檔案*：`SpeechMessage.Dynamics.ControlPlane\Connectors\CompositeConnectorRouter.cs` (行 59-80)
4. **租約與執行分派**：`OfficialWorkerConnectorPool` 取得執行租約，並透過 `OfficialWorkerProfileExecutor` 進行 Named Pipe 通訊。
   * *檔案*：`SpeechMessage.Dynamics.ControlPlane\Connectors\OfficialWorkerConnectorPool.cs` (行 337-400)
   * *檔案*：`SpeechMessage.Dynamics.WorkerSupervisor\OfficialWorkerProfileExecutor.cs` (行 191-345)
5. **Worker 進程執行**：Worker 進程（如 `Crm91Worker`）從 Named Pipe 讀取請求，透過 `OfficialCrmServiceClientAdapter` 呼叫微軟官方 SDK。
   * *檔案*：`SpeechMessage.Dynamics.Crm91Worker\Program.cs` (行 32-61)
   * *檔案*：`SpeechMessage.Dynamics.Crm91Worker\OfficialCrmServiceClientAdapter.cs` (行 267-305)
6. **SDK 連線建立**：`OfficialCrmServiceClientFactory` 載入 `worker-profile.xml`，透過 `WindowsCredentialManagerProvider` 取得憑證，並建構 `CrmServiceClient`。
   * *檔案*：`SpeechMessage.Dynamics.Crm91Worker\OfficialCrmServiceClientFactory.cs` (行 55-91, 102-148)

#### 路徑 B：舊版/測試路徑 (ToolUtility + Data8 OnPremiseClient)
1. **呼叫點**：業務邏輯或測試（如 `LivePackage02Data8ListManagementFreshPreflightProbeTests`）呼叫 `CrmConnectionPool` 或直接呼叫 `CrmConnectionService`。
   * *檔案*：`ChurchReport.MemberInfo.Tests\LivePackage02Data8ListManagementFreshPreflightProbeTests.cs` (行 78)
2. **連線建立**：`CrmConnectionService.CreateOnPremiseClient` 被呼叫。
   * *檔案*：`ToolUtility\ConnectionOperations\CrmConnectionService.cs` (行 430-439)
3. **實體建構**：直接實例化 `PowerPlatform.Dataverse.Client.OnPremiseClient`。
   * *檔案*：`PowerPlatform.Dataverse.Client\OnPremiseClient.cs` (行 118-131)
4. **SOAP 傳輸**：`OnPremiseClient` 透過 WCF 載入 WSDL (`url + "?wsdl&sdkversion="`) 並使用 WS-Trust 進行驗證與 SOAP 呼叫。
   * *檔案*：`PowerPlatform.Dataverse.Client\OnPremiseClient.cs` (行 139-187)

---

### 3. Endpoint 清單與各自用途／來源

1. **Gateway REST API Endpoint**
   * **URL**：`https://dynamics-gateway.internal/` (生產預設，見 `dynamics-access-gateway-design.md` 行 68) 或 `https://localhost:7244` (開發環境，見 `appsettings.Development.json` 行 13)
   * **用途**：產品服務呼叫 Gateway 的統一入口。
   * **來源**：產品設定檔中的 `DynamicsAccess:Gateway:Endpoint`。
2. **D365 SOAP Organization Service Endpoint**
   * **URL**：`https://{host}:{port}/{orgName}/XRMServices/2011/Organization.svc`
   * **用途**：實際與 Dynamics 365 進行 SOAP/WCF 通訊的端點。
   * **來源**：
     * 新架構：由部署工具寫入 Worker 目錄下的 `worker-profile.xml`（包含 `hostName`, `port`, `name`, `useSsl` 等屬性，由 `XmlWorkerProfileStore.cs` 載入）。
     * 舊架構：來自 `appsettings.json` 中的 `DynamicsConnectionManagement:OrganizationCatalog:{alias}:ServiceUri` 或資料庫/設定檔傳入的 `adUrl`。
3. **ADFS / OAuth Token Endpoint**
   * **URL**：由 ADFS/IFD 探索或 `worker-profile.xml` 中的 `homeRealm` (例如 ADFS 的 STS 終端節點) 決定。
   * **用途**：用於 Claims-based / IFD 驗證時取得 Windows Identity Token。
   * **來源**：`worker-profile.xml` 中的 `<identity mode="WindowsCredentialReference" reference="..." homeRealm="..." />`。

---

### 4. 連線參數表

| 參數名稱 | 來源 | 是否敏感 | 消費者 (Consumer) | 用途 |
| :--- | :--- | :--- | :--- | :--- |
| `OrganizationBaseUri` / `ServiceUri` | `appsettings.json` / `worker-profile.xml` | 否 | `OfficialCrmServiceClientFactory` / `OnPremiseClient` | 指定 D365 組織的基礎 URL 或 SOAP 服務網址 |
| `ExpectedOrganizationId` | `appsettings.json` / `worker-profile.xml` | 否 | `OfficialCrmServiceClientAdapter` | 用於驗證連線成功後回傳的 Organization ID 是否吻合，防止連錯環境 |
| `CredentialReference` | `worker-profile.xml` | 否 | `WindowsCredentialManagerProvider` | Windows 憑證管理員中的 Target Name 鍵值，用以查詢實際帳密 |
| `Username` | Windows Credential Manager / 設定檔 | 否 (帳號名稱) | `CrmServiceClient` / `OnPremiseClient` | 登入 D365/ADFS 的使用者帳號 (格式通常為 `Domain\Username`) |
| `Password` | Windows Credential Manager / 設定檔 | **是** (已遮罩) | `CrmServiceClient` / `OnPremiseClient` | 登入 D365/ADFS 的密碼 |
| `HomeRealm` | `worker-profile.xml` | 否 | `CrmServiceClient` | IFD 驗證所需的 Home Realm URL |

---

### 5. 帳號與密碼來源說明

#### 新架構 (Official Worker)
* **帳號與密碼來源**：儲存於執行 Worker 進程之 Windows 帳戶的 **Windows 憑證管理員 (Windows Credential Manager)** 中。
* **讀取機制**：Worker 啟動時，`XmlWorkerProfileStore` 載入 `worker-profile.xml` 取得 `CredentialReference`（例如 `dynamics-sunnyvalechback`）。接著 `WindowsCredentialManagerProvider` 呼叫 Windows API `CredReadW` 讀取該 Generic Credential，將密碼載入為 `SecureString`，並解析出 `UserName` 與 `Domain`。
* **安全性**：密碼在記憶體中以 `SecureString` 形式存在，且僅在 `OfficialCrmServiceClientFactory.Create` 內短暫解密傳遞給 `CrmServiceClient`，隨即進行 `ZeroMemory` 清理，避免明文密碼寫入設定檔或日誌。

#### 舊架構 / 測試路徑
* **帳號與密碼來源**：來自設定檔（如 `appsettings.json`）或測試環境變數（如 `CRM_PASSWORD`）。
* **讀取機制**：直接以明文字串傳遞給 `OnPremiseClient` 的建構子。

---

### 6. 不確定或只能由部署環境確認的部分

1. **Windows Credential Manager 的權限與配置**：由於憑證管理員是 Per-User 的，部署時必須確保執行 `Crm82Worker.exe` / `Crm91Worker.exe` 的 Windows 服務帳戶（或 IIS AppPool 識別身分）與寫入憑證的帳戶為同一個，否則 `CredReadW` 會回傳找不到憑證的錯誤。
2. **ADFS 網域與 STS 信任關係**：IFD 模式下的 `homeRealm` 必須與部署現場的 ADFS 伺服器配置完全一致，且 ADFS 必須信任該 Worker 發出的 WS-Trust 請求。這部分無法單從程式碼靜態確認，需於部署現場進行連線測試。
3. **Worker 可執行檔的 SHA-256 雜湊值**：Gateway 設定檔（`appsettings.json`）中的 `WorkerExecutableSha256` 必須與實際部署的 `Crm82Worker.exe` / `Crm91Worker.exe` 檔案雜湊完全一致，否則 Gateway 會拒絕啟動 Worker。此值需在編譯部署時動態更新。

---

### 7. 圖面建議

```
+-----------------------------------------------------------------------------------+
|                                 Product Service                                   |
|  [Package01FeeReadClient]                                                         |
+-----------------------------------------------------------------------------------+
                                         |
                        (HTTP POST /v1/organizations/...)
                                         v
+-----------------------------------------------------------------------------------+
|                             Dynamics Access Gateway                               |
|  - Program.cs (Kestrel / IIS Auth)                                                |
|  - CompositeConnectorRouter                                                       |
|  - OfficialWorkerConnectorPool                                                    |
+-----------------------------------------------------------------------------------+
                                         |
                             (Named Pipe / IPC Channel)
                                         v
+-----------------------------------------------------------------------------------+
|                         Official Worker Process (net48)                           |
|  [Crm91Worker.exe / Crm82Worker.exe]                                              |
|                                                                                   |
|   +--------------------------+             +----------------------------------+   |
|   |  XmlWorkerProfileStore   |             | WindowsCredentialManagerProvider |   |
|   |  (worker-profile.xml)    |             | (CredReadW API)                  |   |
|   +--------------------------+             +----------------------------------+   |
|                 |                                           |                     |
|                 | (CredentialReference)                     | (Decrypt to SecureString)
|                 +--------------------+----------------------+                     |
|                                      |                                            |
|                                      v                                            |
|                     +----------------------------------+                          |
|                     | OfficialCrmServiceClientFactory  |                          |
|                     | (CrmServiceClient)               |                          |
|                     +----------------------------------+                          |
+-----------------------------------------------------------------------------------+
                                       |
                            (SOAP / WS-Trust / ADFS)
                                       v
+-----------------------------------------------------------------------------------+
|                               Dynamics 365 CE                                     |
+-----------------------------------------------------------------------------------+
```

#### 主要替代分支 (Alternative Branch)
* **Dedicated Gateway (Data8) 模式**：產品服務 -> Gateway -> `Data8ProfileRuntime` -> `OnPremiseClient` (WS-Trust / SOAP) -> Dynamics 365 (不啟動外部 Worker 進程，直接在 Gateway 進程內透過 WCF 連線)。
