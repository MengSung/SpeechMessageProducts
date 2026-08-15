# D365 連線架構與憑證來源分析報告

## 1. 一句話結論

目前分支的實際預設路徑是透過 **`ToolUtility`** 作為進入點，底層依賴自製移植至 .NET 10 的 **`PowerPlatform.Dataverse.Client`**（基於 **`Data8`** 的 WS-Trust SOAP 實作）以 `Embedded` 模式直接在進程內建立連線；而 **`Gateway` / `Official Worker`** 則是設計為獨立的 .NET Framework 4.8 進程，透過 Named Pipe IPC 與主進程通訊並使用微軟官方 SDK 進行連線，目前作為多版本相容性與隔離的替代分支，尚未完全取代預設的 legacy 呼叫。

---

## 2. Runtime Call Chain 追蹤

### 預設路徑：Embedded 模式 (ToolUtility -> Dataverse.Client/Data8)
從產品業務呼叫點到 D365 傳輸層的完整呼叫鏈如下：

1. **業務呼叫點**：呼叫 `IToolUtilityProvider.GetToolUtility()` 取得工具實例。
   - **檔案**：`ToolUtility\DependencyInjection\ToolUtilityProvider.cs` (第 30-33 行)
2. **Factory 取得單例**：`ToolUtilityProvider` 呼叫 `ToolUtilityFactory.GetInstance()`。
   - **檔案**：`ToolUtility\Factory\ToolUtilityFactory.cs` (第 50-69 行)
3. **初始化連線**：`ToolUtilityFactory` 實例化 `ToolUtilityClass`，其建構子呼叫 `InitializeCrmConnection()`。
   - **檔案**：`ToolUtility\ToolUtilityPartials\ToolUtilityClass.Core.cs` (第 97-106 行、第 156-163 行)
4. **建立 Client**：`InitializeCrmConnection` 呼叫 `_crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword)`。
   - **檔案**：`ToolUtility\ToolUtilityPartials\ToolUtilityClass.Core.cs` (第 162 行)
5. **實例化 OnPremiseClient**：`CrmConnectionService` 實例化 `OnPremiseClient`。
   - **檔案**：`ToolUtility\ConnectionOperations\CrmConnectionService.cs` (第 430-441 行)
6. **底層傳輸**：`OnPremiseClient`（位於 `PowerPlatform.Dataverse.Client` 專案）使用 `Data8` 的 WS-Trust XML 與 SOAP 協定直接向 D365 進行驗證與資料傳輸。

---

### 替代路徑：Official Worker 模式 (Control Plane -> IPC -> Crm82Worker)
當系統設定為 `DedicatedGateway` 或 `CentralGateway` 且指定 `OfficialCrm82Worker` 時：

1. **路由分發**：主進程透過 `ProfileRoutedOperationExecutor` 進行路由。
   - **檔案**：`SpeechMessage.Dynamics.ControlPlane\DependencyInjection\OfficialWorkerServiceCollectionExtensions.cs` (第 75-83 行)
2. **IPC 通訊**：透過 Named Pipe 將請求發送至獨立運作的 Worker 進程。
   - **檔案**：`SpeechMessage.Dynamics.Crm82Worker\Program.cs` (第 34-41 行)
3. **Worker 建立連線**：Worker 進程中的 `OfficialCrmServiceClientFactory` 載入 `worker-profile.xml`，並透過 `WindowsCredentialManagerProvider` 讀取認證。
   - **檔案**：`SpeechMessage.Dynamics.Crm82Worker\OfficialCrmServiceClientFactory.cs` (第 40-71 行)
4. **官方 SDK 傳輸**：實例化微軟官方的 `CrmServiceClient` 建立與 D365 的連線。
   - **檔案**：`SpeechMessage.Dynamics.Crm82Worker\OfficialCrmServiceClientFactory.cs` (第 89-98 行)

---

## 3. Endpoint 清單與用途

| Endpoint URL | 用途 | 來源位置 |
| --- | --- | --- |
| `https://{Organization}.speechmessage.com.tw/XRMServices/2011/Organization.svc` | D365 On-Premise 組織服務 SOAP 端點，用於資料查詢與 CRUD 操作。 | `ToolUtility\ToolUtilityPartials\ToolUtilityClass.Core.cs` (第 158 行) |
| `https://{Organization}.{Server}{BaseDiscoveryServiceAddress}` | D365 探索服務端點，用於 Federated 驗證模式下尋找組織。 | `ToolUtility\ConnectionOperations\CrmConnectionService.cs` (第 217-219 行) |
| `Gateway.Endpoint` (HTTPS URI) | 主進程與 Gateway 服務通訊的 API 端點（僅在 Gateway 模式啟用）。 | `docs/dynamics-connection-management-spec.md` (第 50 行) |

---

## 4. 連線參數表

| 參數名稱 | 來源 | 是否敏感 | 消費者 |
| --- | --- | --- | --- |
| `CrmConnection:Server` | `appsettings.json` (預設: `speechmessage.com.tw`) | 否 | `ToolUtilityClass` (Core.cs 第 48 行) |
| `CrmConnection:Port` | `appsettings.json` (預設: `7777`) | 否 | `ToolUtilityClass` (Core.cs 第 49 行) |
| `CrmConnection:Organization` | `appsettings.json` (預設: `jesus`) | 否 | `ToolUtilityClass` (Core.cs 第 50 行) |
| `CrmConnection:Username` | `appsettings.json` (預設: `Administrator@speechmessage.com.tw`) | 否 | `ToolUtilityClass` (Core.cs 第 51 行) |
| `CrmConnection:Password` | User Secrets / 環境變數 `CRM_PASSWORD` | 是 (已遮罩) | `ToolUtilityClass` (Core.cs 第 52 行) |
| `CrmConnection:Domain` | `appsettings.json` (預設: `DYNAMICS-365`) | 否 | `ToolUtilityClass` (Core.cs 第 53 行) |
| `adUsername` (寫死) | 程式碼內寫死為 `@"SPEECHMESSAGE\Administrator"` | 否 | `ToolUtilityClass` (Core.cs 第 159 行) |
| `CredentialReference` | `worker-profile.xml` (僅限 Worker 模式) | 否 | `OfficialCrmServiceClientFactory` (第 141 行) |

---

## 5. 帳號與密碼來源說明

### 帳號來源
- **預設路徑**：在 `ToolUtilityClass.InitializeCrmConnection` 中，連線帳號被寫死為 `@"SPEECHMESSAGE\Administrator"`。
- **設定路徑**：可透過 `CrmConnection:Username` 進行自訂，預設值為 `Administrator@speechmessage.com.tw`。

### 密碼來源
- 密碼解析由 `ResolveRequiredSecret("CrmConnection:Password", "CRM_PASSWORD")` 執行：
  1. 優先讀取 `IConfiguration` 中的 `CrmConnection:Password`（通常配置於 `appsettings.json` 或開發環境的 User Secrets）。
  2. 若未配置或值為預設預留字 `REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT`，則改為讀取系統環境變數 **`CRM_PASSWORD`**。
  3. 若兩者皆無有效值，系統將拋出 `InvalidOperationException` 阻斷啟動。

### Worker 模式憑證來源
- Worker 進程不直接接觸明文密碼，而是讀取 `worker-profile.xml` 中的 `CredentialReference` 識別碼，並透過 `WindowsCredentialManagerProvider` 向作業系統的 **Windows 認證管理員 (Credential Manager)** 查詢對應的 Windows 帳密憑證。

---

## 6. 部署環境確認事項

1. **實際連線模式**：需確認部署環境 `appsettings.json` 中的 `ConnectionMode` 設定。若為 `Embedded`，則走 Data8 連線；若為 `DedicatedGateway` 或 `CentralGateway`，則走 Worker/Gateway 路由。
2. **環境變數**：必須確保部署伺服器上已設定環境變數 `CRM_PASSWORD`，或該運作帳號的 User Secrets 內有正確的密碼。
3. **Windows 認證管理員**：若啟用 Worker 模式，必須在部署主機的 Windows 認證管理員中，預先建立與 `CredentialReference` 對應的通用憑證（Generic Credentials）。

---

## 7. 架構圖建議

以下為 Dynamics 365 連線架構的 Mermaid 示意圖，呈現了預設的 Embedded 路徑與替代的 Worker 路徑：

```mermaid
flowchart TD
    subgraph ChurchReport 主進程 (NET 10)
        A[業務呼叫點 / Controller] -->|GetToolUtility| B(IToolUtilityProvider)
        B -->|GetInstance| C[ToolUtilityFactory]
        C -->|Create| D[ToolUtilityClass]
        
        D -->|ConnectionMode = Embedded| E[CrmConnectionService]
        E -->|CreateOnPremiseClient| F[OnPremiseClient <br>PowerPlatform.Dataverse.Client]
        F -->|WS-Trust SOAP / Data8| G[Dynamics 365 On-Premise]
        
        D -->|ConnectionMode = Gateway| H[ProfileRoutedOperationExecutor]
    end

    subgraph IPC 邊界
        H -->|Named Pipe| I[OfficialWorkerProcessHost]
    end

    subgraph Official Worker 獨立進程 (NET 4.8)
        I --> J[OfficialCrmServiceClientFactory]
        K[(worker-profile.xml)] -->|CredentialReference| J
        L[(Windows Credential Manager)] -->|Read Credential| J
        J -->|Create Client| M[CrmServiceClient <br>Official Microsoft SDK]
        M -->|AD / IFD Auth| G
    end

    style G fill:#f9f,stroke:#333,stroke-width:2px
    style F fill:#bbf,stroke:#333,stroke-width:1px
    style M fill:#bbf,stroke:#333,stroke-width:1px
```
