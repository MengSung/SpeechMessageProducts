# D365 連線架構與憑證來源分析報告

本報告針對 `D:\音訊科技產品\系統平台\SpeechMessageProducts` 專案中，目前分支實際建立 Dynamics 365 (D365) 連線的路徑、Endpoint、憑證來源與架構進行詳細分析。

---

## 1. 一句話結論

在目前實際的業務路徑中，產品端（.NET 10）不直接使用 `Microsoft.PowerPlatform.Dataverse.Client` 或 `ToolUtility` 直連 D365，而是透過 **Gateway** 進行 HTTP REST 呼叫；Gateway 透過具名管道 (Named Pipe IPC) 驅動獨立的 **Official Worker** 進程（`Crm82Worker` 或 `Crm91Worker`，運行於 .NET Framework 4.8），並在 Worker 內部使用微軟官方的 **`CrmServiceClient`** (XrmTooling) 建立與 D365 的連線。`ToolUtility` 中的 `DataverseServiceClientAdapter` 與 `Data8` 目前僅作為保留選項或未啟用的替代分支。

---

## 2. 逐段 Runtime Call Chain

當產品端需要呼叫 D365 時，其完整的 runtime 呼叫鏈如下：

1. **產品端發起請求**：
   - 產品端（如 `ChurchReport`）透過 HTTP REST 呼叫 `SpeechMessage.Dynamics.Gateway`。
2. **Gateway 接收與路由**：
   - `SpeechMessage.Dynamics.Gateway` 接收請求，並由 `WorkerSupervisor` 進行管理。
   - `WorkerSupervisor` 啟動對應版本的 Worker 進程（`Crm82Worker` 或 `Crm91Worker`），並建立具名管道（Named Pipe）通訊。
3. **Worker 進程啟動與初始化**：
   - **進入點**：`SpeechMessage.Dynamics.Crm91Worker\Program.cs` (第 32-61 行) 或 `Crm82Worker\Program.cs`。
   - **載入設定檔**：`Program.cs` 建立 `XmlWorkerProfileStore`，讀取與執行檔相鄰的 `worker-profile.xml`（`Program.cs` 第 36-38 行）。
   - **建立憑證提供者**：`Program.cs` 建立 `WindowsCredentialManagerProvider`（`Program.cs` 第 39 行）。
   - **建立 Factory**：建立 `OfficialCrmServiceClientFactory`（`Program.cs` 第 37-39 行）。
4. **建立 D365 連線**：
   - `OfficialCrmServiceClientFactory.Create` 被呼叫（`OfficialCrmServiceClientFactory.cs` 第 55-91 行）。
   - **載入設定**：`_profileStore.Load` 載入 `WorkerProfileSettings`（`OfficialCrmServiceClientFactory.cs` 第 57-60 行）。
   - **讀取憑證**：`ReadCredential` 呼叫 `_credentialProvider.Read(credentialReference)`，透過 Win32 API `CredReadW` 從 Windows 憑證管理員中讀取帳密（`OfficialCrmServiceClientFactory.cs` 第 177-186 行，`WindowsCredentialManagerProvider.cs` 第 43-109 行）。
   - **實例化 `CrmServiceClient`**：
     - 若為 AD 驗證：呼叫 `new CrmServiceClient(credential, authType: AuthenticationType.AD, ...)`（`OfficialCrmServiceClientFactory.cs` 第 116-124 行）。
     - 若為 IFD 驗證：呼叫 `new CrmServiceClient(userId, password, domain, homeRealm, ...)`（`OfficialCrmServiceClientFactory.cs` 第 133-144 行）。
   - **包裝為 Adapter**：建立 `OfficialCrmServiceClientAdapter` 包裝 `CrmServiceClient`（`OfficialCrmServiceClientFactory.cs` 第 68-72 行）。
5. **執行業務操作**：
   - Worker 透過具名管道接收 Gateway 傳來的操作指令。
   - 呼叫 `Package01FeeQueryOperation` 執行具體業務（例如 FetchXML 查詢），並透過 `IOrganizationService`（由 `CrmServiceClient` 實作）發送請求至 D365 Endpoint。

---

## 3. Endpoint 清單與各自用途／來源

* **D365 Organization Service Endpoint**
  * **用途**：與 D365 進行 SOAP/WCF 通訊，執行資料查詢與異動。
  * **來源**：由 `worker-profile.xml` 中的 `HostName`、`Port`、`OrganizationName` 與 `UseSsl` 動態組裝而成。
  * **格式**：
    * AD 模式：`http[s]://{HostName}:{Port}/{OrganizationName}/XRMServices/2011/Organization.svc`
    * IFD 模式：`https://{OrganizationName}.{HostName}:{Port}/XRMServices/2011/Organization.svc`
* **ADFS / Token Endpoint (IFD 模式)**
  * **用途**：用於 WS-Trust 驗證，取得 ADFS 發行的 Security Token。
  * **來源**：由 `worker-profile.xml` 中的 `HomeRealm` 參數指定。
  * **格式**：由 `CrmServiceClient` 內部根據 `HomeRealm` 與 ADFS Discovery 機制自動解析與呼叫（例如 `https://adfs.domain.com/adfs/services/trust/13/usernamemixed`）。

---

## 4. 連線參數表

| 參數名稱 | 來源 | 是否敏感 | 消費者 (Consumer) | 說明 |
| :--- | :--- | :--- | :--- | :--- |
| `HostName` | `worker-profile.xml` | 否 | `CrmServiceClient` | D365 伺服器主機名稱或網域 |
| `Port` | `worker-profile.xml` | 否 | `CrmServiceClient` | 連線埠（例如 80, 443, 5555） |
| `OrganizationName` | `worker-profile.xml` | 否 | `CrmServiceClient` | D365 組織名稱 (Unique Name) |
| `ExpectedOrganizationId` | `worker-profile.xml` | 否 | `OfficialCrmServiceClientAdapter` | 預期的 Organization GUID，用於 WhoAmI 驗證 |
| `UseSsl` | `worker-profile.xml` | 否 | `CrmServiceClient` | 是否使用 HTTPS/SSL |
| `AuthenticationMode` | `worker-profile.xml` | 否 | `OfficialCrmServiceClientFactory` | 驗證模式（`ActiveDirectory` 或 `Ifd`） |
| `IdentityMode` | `worker-profile.xml` | 否 | `OfficialCrmServiceClientFactory` | 身分識別模式（`HostIdentity` 或 `WindowsCredentialReference`） |
| `CredentialReference` | `worker-profile.xml` | 否 | `WindowsCredentialManagerProvider` | Windows 憑證管理員中的 Target Name 識別碼 |
| `HomeRealm` | `worker-profile.xml` | 否 | `CrmServiceClient` | IFD 驗證使用的 HomeRealm URI |
| `UserName` | Windows 憑證管理員 | 否 | `CrmServiceClient` / `NetworkCredential` | 登入帳號（格式可能為 `domain\user` 或 `user@domain`） |
| `Password` | Windows 憑證管理員 | **是** | `CrmServiceClient` / `NetworkCredential` | 登入密碼（以 `SecureString` 載入，使用後立即抹除） |

---

## 5. 帳號與密碼來源說明

* **來源位置**：Windows 憑證管理員 (Windows Credential Manager) 的 **普通憑證 (Generic Credentials)**。
* **讀取機制**：
  * 程式碼不包含任何寫死的帳號密碼，也不從 XML 設定檔中讀取明文密碼。
  - `WindowsCredentialManagerProvider.cs` 透過 P/Invoke 呼叫 Windows 原生 API `CredReadW`，傳入 `CredentialReference`（例如 `SpeechMessage/Dynamics/CrmConnection`）作為 Target Name。
  - 成功讀取後，將 `UserName` 欄位解析為帳號與網域。
  - 將 `CredentialBlob`（密碼的二進位資料）逐字元讀入並建構為 `SecureString`。
  - **安全防護**：在 `SecureString` 建立後，立即呼叫 `ZeroMemory` 抹除記憶體中的 `CredentialBlob` 暫存區，並呼叫 `FreeCredential` 釋放 Windows API 配置的記憶體，避免密碼殘留在記憶體中。

---

## 6. 不確定或只能由部署環境確認的部分

* **實際的 `worker-profile.xml` 內容**：此檔案是由部署工具（如 `New-DynamicsOfficialWorkerDeployment.ps1`）在部署時動態產生，並放置於 Worker 執行檔目錄下。其實際的 `HostName`、`OrganizationName`、`CredentialReference` 等值必須在部署環境中確認。
* **Windows 憑證管理員中的憑證**：必須由系統管理員 (Operator) 在執行 Worker 進程的 Windows 帳戶下，手動或透過腳本將正確的 D365 帳密寫入 Windows 憑證管理員，且 Target Name 必須與 `worker-profile.xml` 中的 `CredentialReference` 完全一致。
* **ADFS 伺服器與 HomeRealm 的實際 URL**：在 IFD 模式下，ADFS 的具體 Endpoint 必須由部署環境的 ADFS 設定決定。

---

## 7. 圖面建議

架構圖應包含以下元件與流程：

```
+-----------------------------------------------------------------------------------+
|                                   .NET 10 進程                                    |
|                                                                                   |
|   +------------------+             HTTP REST             +--------------------+   |
|   |   ChurchReport   | --------------------------------> |  Dynamics Gateway  |   |
|   |   (產品業務端)   |                                   | (WorkerSupervisor) |   |
|   +------------------+                                   +--------------------+   |
+--------------------------------------------------------------------|--------------+
                                                                     |
                                                                     | Named Pipe (IPC)
                                                                     v
+-----------------------------------------------------------------------------------+
|                             .NET Framework 4.8 進程                               |
|                                                                                   |
|   +---------------------------------------------------------------------------+   |
|   |                               Official Worker                             |   |
|   |                                                                           |   |
|   |   +----------------------------------+                                    |   |
|   |   |  OfficialCrmServiceClientFactory | <------+                           |   |
|   |   +----------------------------------+        |                           |   |
|   |                    |                          | 讀取設定                  |   |
|   |                    | 建立                     |                           |   |
|   |                    v                          |                           |   |
|   |   +----------------------------------+   +----------------------------+   |   |
|   |   |         CrmServiceClient         |   |     worker-profile.xml     |   |   |
|   |   +----------------------------------+   +----------------------------+   |   |
|   |                    |                                                      |   |
|   |                    | 讀取憑證                                             |   |
|   |                    v                                                      |   |
|   |   +----------------------------------+                                    |   |
|   |   | WindowsCredentialManagerProvider |                                    |   |
|   |   +----------------------------------+                                    |   |
|   +--------------------|------------------------------------------------------+   |
+------------------------|----------------------------------------------------------+
                         |
                         | Win32 API (CredReadW)
                         v
        +----------------------------------+
        |    Windows Credential Manager    |
        |       (儲存 D365 帳號與密碼)      |
        +----------------------------------+
                         |
                         | SOAP / WS-Trust (Organization.svc)
                         v
        +----------------------------------+
        |        Dynamics 365 CE           |
        |      (8.2 / 9.1 On-Premises)     |
        +----------------------------------+
```

* **主要元件**：
  * **ChurchReport (.NET 10)**：發起業務請求。
  * **Dynamics Gateway / WorkerSupervisor (.NET 10)**：接收請求並管理 Worker 進程。
  * **Official Worker (.NET Framework 4.8)**：獨立的 Worker 進程，包含 `Crm82Worker` 與 `Crm91Worker`。
  * **worker-profile.xml**：Worker 的設定檔，包含連線參數與憑證 Target Name。
  * **Windows Credential Manager**：Windows 憑證管理員，安全儲存帳密。
  * **Dynamics 365 CE**：目標 D365 系統。
* **主要箭頭與參數**：
  * `ChurchReport` -> `Dynamics Gateway`：HTTP REST 請求。
  * `Dynamics Gateway` -> `Official Worker`：Named Pipe IPC 通訊。
  * `Official Worker` -> `worker-profile.xml`：讀取 `HostName`, `OrganizationName`, `CredentialReference` 等參數。
  * `Official Worker` -> `Windows Credential Manager`：透過 `CredentialReference` 查詢帳密。
  * `Official Worker` -> `Dynamics 365 CE`：使用 `CrmServiceClient` 透過 SOAP/WS-Trust 協定進行連線。
* **替代分支**：
  * **Dataverse ServiceClient (Modern SDK)**：在 `ToolUtility` 中有實作，但目前未啟用。
  * **Data8 (WS-Trust 第三方套件)**：在 `ToolUtility` 中有實作，但目前未啟用。

---

## 8. 審查發現分類 (Reviewer Findings)

### [Critical] 憑證安全管理機制
* **檔案路徑**：`SpeechMessage.Dynamics.WorkerHost\WindowsCredentialManagerProvider.cs`
* **說明**：系統採用 Windows Credential Manager 儲存敏感的 D365 帳密，並在讀取後立即使用 `ZeroMemory` 抹除記憶體中的明文密碼。此設計符合高安全標準，部署時必須確保執行 Worker 的 Windows 帳戶已正確設定該憑證，且 `CredentialReference` 必須精確匹配。

### [Warning] 跨平台與架構限制
* **檔案路徑**：`SpeechMessage.Dynamics.Crm91Worker\Program.cs`、`SpeechMessage.Dynamics.Crm82Worker\Program.cs`
* **說明**：由於微軟官方的 `CrmServiceClient` 只能穩定運行於 .NET Framework 4.8，因此系統被迫採用 IPC Worker 架構。這增加了部署與維護的複雜度（需要管理獨立的進程與具名管道）。未來若有機會升級至 D365 雲端或支援 OAuth 的 ADFS 版本，應評估遷移至 `Microsoft.PowerPlatform.Dataverse.Client` (ServiceClient) 以簡化架構。

### [Info] 未啟用的替代分支
* **檔案路徑**：`ToolUtility\Adapters\DataverseServiceClientAdapter.cs`
* **說明**：專案中已存在 `DataverseServiceClientAdapter` 與 `Data8` 相關實作，但目前在生產環境的主線路徑中並未啟用。這些程式碼應妥善標記或維護，避免開發人員誤用。
