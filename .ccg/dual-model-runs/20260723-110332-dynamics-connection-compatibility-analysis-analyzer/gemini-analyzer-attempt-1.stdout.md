# Dynamics 365 8.2 與 9.1 連線相容性分析報告

本報告針對系統平台中 `PowerPlatform.Dataverse.Client` 專案連線至 Dynamics 365 Customer Engagement 8.2 與 9.1 On-Premises 實例的相容性進行評估，並分析是否應以無 SDK 的 Web API 連線取代現有的 SDK/SOAP 架構。

---

## 1. Conclusion (結論)

經過對原始碼與架構的審查，得出以下核心結論：
1. **現有連線方式為 SOAP/WCF 混合驗證**：目前專案並非使用 Web API，而是透過自訂的 `OnPremiseClient` 實作 `IOrganizationService`，在內部利用 WS-Trust 1.3 協定（針對 IFD/Federation）與 SSPI（針對 AD 驗證）進行 SOAP 呼叫。
2. **存在嚴重的連線與資源洩漏風險 (Critical)**：`OnPremiseClient` 未實作 `IDisposable`，導致 `CrmConnectionPool` 在釋放或銷毀連線時，無法正確關閉底層的 WCF 通道與 TCP 連線，這在生產環境中會導致 Socket 耗盡與記憶體洩漏。
3. **單一進程多 Profile 隔離失效 (Critical)**：目前的 `CrmConnectionPool` 在 `Startup.cs` 中被註冊為全域單例 (Singleton)，且僅支援單一連線設定。若在單一進程中同時存取 8.2 與 9.1 兩個不同的 CRM 實例，會導致連線混雜與資料越權存取（Data Leakage）。
4. **長期策略應轉向 Web API-first**：雖然 Dynamics 365 8.2 的 Web API 存在部分功能落差（如 Batch 處理限制與 OAuth 驗證依賴 ADFS），但為了擺脫 .NET 10 對 WCF 舊版套件的依賴，長期而言採用基於 `HttpClient` 的 Web API 是更穩健的去 SDK 化方案。

---

## 2. Source evidence (原始碼證據)

### 證據 A：`PowerPlatform.Dataverse.Client.csproj`
* **路徑**：`PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`
* **關鍵內容**：
  * 專案目標框架為 `<TargetFramework>net10.0</TargetFramework>`。
  * 依賴 `Microsoft.PowerPlatform.Dataverse.Client` (v1.1.32) 以及多個 WCF 相關套件（如 `System.ServiceModel.Federation` v10.0.652802, `System.ServiceModel.Http`）。
  * 說明文件指出其為 WS-Trust 相容的用戶端，專為 .NET 10 連線至 On-Premises IFD 實例設計。

### 證據 B：`OnPremiseClient.cs` 的生命週期缺失
* **路徑**：`PowerPlatform.Dataverse.Client/OnPremiseClient.cs`
* **關鍵內容**：
  * `public class OnPremiseClient : IOrganizationService` **未實作 `IDisposable`**。
  * 內部成員 `private readonly IOrganizationService _service;` 在 `ConnectFederated` 時為 WCF 通道，在 `ConnectAD` 時為 `ADAuthClient`（亦未實作 `IDisposable`）。

### 證據 C：`CrmConnectionPool.cs` 的無效釋放
* **路徑**：`ToolUtility/ConnectionOperations/CrmConnectionPool.cs`
* **關鍵內容**：
  * 在 `DisposeConnection` 方法中（第 406-425 行）：
    ```csharp
    (connection?.Service as IDisposable)?.Dispose();
    ```
  * 由於 `connection.Service` 是 `OnPremiseClient`，而該類別未實作 `IDisposable`，此轉型結果永遠為 `null`，導致底層 WCF 通道與連線永遠無法被 Dispose。

### 證據 D：`Startup.cs` 的單例註冊
* **路徑**：`SpeechMessageProducts.ChurchReport/Startup.cs`
* **關鍵內容**：
  * `ICrmConnectionPool` 被註冊為全域單例（第 302-349 行）：
    ```csharp
    services.AddSingleton<ICrmConnectionPool>(sp => { ... });
    ```
  * 僅讀取單一 `CrmConnection` 設定區段，無法在同一個進程中隔離多個具名的 CRM 設定檔。

---

## 3. Compatibility matrix (相容性矩陣)

| 連線方案 / 伺服器版本 | Dynamics 365 8.2 On-Premises | Dynamics 365 9.1 On-Premises | 評估與限制說明 |
| :--- | :--- | :--- | :--- |
| **OnPremiseClient (WS-Trust / SOAP)** | **相容 (需驗證)** | **相容** | 依賴 WCF 與 ADFS。8.2 需確認 ADFS 版本與加密演算法相容性。 |
| **OnPremiseClient (AD / SSPI)** | **相容 (需驗證)** | **相容** | 依賴 Windows 驗證。在 Linux 容器部署時需 .NET 7+ 且配置複雜。 |
| **ServiceClient (Modern SDK)** | **不相容** | **相容** | `ServiceClient` 預設不支援舊版 On-Premises 的 WS-Trust 驗證。 |
| **Direct Web API (HttpClient)** | **部分相容** | **完全相容** | 8.2 Web API 存在功能落差（如 Batch 限制），且必須在 ADFS 啟用 OAuth 2.0。 |

---

## 4. Risks (風險評估)

### [Critical] 資源與連線洩漏 (Connection & Socket Leak)
* **說明**：由於 `OnPremiseClient` 未實作 `IDisposable`，連線池在清理閒置連線或進行物件銷毀時，無法關閉底層的 WCF 通道。這會導致 TCP 連線持續殘留，在系統高負載時會迅速耗盡伺服器的 Ephemeral Ports，導致 `SocketException` 並使應用程式崩潰。

### [Critical] 跨 Profile 資料洩漏與連線混雜 (Cross-Profile Data Leakage)
* **說明**：`CrmConnectionPool` 被註冊為單例且缺乏具名隔離。若系統同時需要存取 8.2 與 9.1 實例，同一個連線池會混雜兩個實例的連線。這會導致發往 8.2 的請求被發送到 9.1 伺服器，或者因為 WCF `ChannelFactory` 快取未隔離，導致 A 租戶的請求攜帶了 B 租戶的安全 Token，造成嚴重的安全性越權漏洞。

### [Warning] TLS 與加密演算法不相容 (TLS & Cryptography Incompatibility)
* **說明**：.NET 10 與 9.x SDK 強制使用 TLS 1.2+。若 Dynamics 365 8.2 伺服器或其 ADFS 尚未升級至支援 TLS 1.2，連線將在握手階段被拒絕。此外，舊版 ADFS 使用的 SHA-1 簽章憑證可能被 .NET 10 的安全原則封鎖。

### [Warning] Web API 遷移時的驗證限制 (OAuth Dependency)
* **說明**：若要將連線遷移至 Web API，必須使用 OAuth 2.0 驗證。然而，On-Premises 環境預設使用 Windows NTLM 或 WS-Trust。若客戶未在 ADFS 上配置 OAuth 終端節點，將無法直接使用 `HttpClient` 進行連線。

---

## 5. Recommended architecture (建議架構)

為了徹底解決連線洩漏、實現多設定檔隔離，並為未來的去 SDK 化鋪路，建議採用以下**設定驅動的具名適配器架構**：

```
[ 應用程式服務 ]
       │
       ▼
[ ICrmClientFactory ] ─── 讀取 ───► [ appsettings.json (多 Profile 設定) ]
       │
       ├─► 建立 (Profile: D365_82) ──► [ LegacyOrganizationServiceAdapter ] ──► SOAP (8.2)
       │
       └─► 建立 (Profile: D365_91) ──► [ DataverseServiceClientAdapter ]   ──► SOAP/OAuth (9.1)
```

### 1. 設定檔結構設計 (appsettings.json)
支援多個具名的 CRM 設定檔，並明確區分連線類型與驗證模式：
```json
{
  "CrmConnections": {
    "Profiles": {
      "D365_82": {
        "Type": "Legacy",
        "ServerUrl": "https://crm82.speechmessage.com.tw/XRMServices/2011/Organization.svc",
        "Domain": "SPEECHMESSAGE",
        "Username": "Administrator",
        "Password": "...",
        "MinPoolSize": 3,
        "MaxPoolSize": 10
      },
      "D365_91": {
        "Type": "Dataverse",
        "ConnectionString": "AuthType=ClientSecret;Url=https://crm91.speechmessage.com.tw;ClientId=...;ClientSecret=..."
      }
    }
  }
}
```

### 2. 具名連線池工廠 (Named Connection Pool Factory)
* 實作 `ICrmConnectionPoolFactory`，取代單一的 `ICrmConnectionPool` 單例。
* 根據傳入的 Profile Name（如 `"D365_82"` 或 `"D365_91"`）建立並快取獨立的 `CrmConnectionPool` 實例，確保連線、Token 與憑證在記憶體中完全隔離。

### 3. 階段性遷移計畫 (Phased Migration)
* **第一階段：修復生命週期與隔離 (立即執行)**
  1. 讓 `OnPremiseClient` 實作 `IDisposable`，並在 `Dispose` 中安全地關閉底層的 WCF 通道（處理 `ICommunicationObject` 的 Close/Abort 邏輯）。
  2. 引入 `ICrmConnectionPoolFactory`，將 8.2 與 9.1 的連線池完全隔離。
* **第二階段：驗證環境 OAuth 支援度**
  1. 評估並在 8.2 與 9.1 的 ADFS 伺服器上啟用 OAuth 2.0 支援。
* **第三階段：實作 Web API 適配器**
  1. 開發 `WebApiCrmClient` 實作 `ICrmClient`，內部使用 `HttpClient` 與 OData v4。
  2. 優先將唯讀與簡單的 CRUD 操作切換至 Web API，保留複雜交易於 SOAP。
* **第四階段：完全去 SDK 化**
  1. 移除 `PowerPlatform.Dataverse.Client` 專案與所有 WCF 依賴，全面改用 Web API。

---

## 6. Validation tests (驗證測試建議)

在正式部署前，必須針對真實伺服器進行以下驗證測試：

1. **連線釋放測試 (Leak Test)**：
   * **方法**：在高併發下反覆呼叫 `AcquireConnection` 與 `ReleaseConnection`，並使用 `netstat` 監控 TCP 連線數，確保閒置連線被清理時，TCP 連線與 WCF 通道確實關閉，無 Socket 殘留。
2. **跨租戶隔離測試 (Isolation Test)**：
   * **方法**：同時啟動 8.2 與 9.1 的連線，交替執行查詢，驗證回傳的資料是否嚴格屬於各自的實體，且無安全 Token 交叉污染。
3. **TLS 握手測試 (TLS Handshake Test)**：
   * **方法**：在僅啟用 TLS 1.2 的用戶端環境下，嘗試連線至 8.2 伺服器，驗證連線是否成功，以確認伺服器端的 TLS 相容性。
4. **ADFS Token 解析測試 (WS-Trust Parsing Test)**：
   * **方法**：針對 8.2 的 ADFS 進行聯合驗證，驗證 `System.ServiceModel.Federation` 是否能正確解析舊版 ADFS 發行的 SAML 憑證。
