# Dynamics Local Gateway 與 ChurchReport 下一階段架構分析報告

本報告以高風險整合系統架構師的角度，針對 `Dynamics Local Gateway` 與 `ChurchReport` 的整合架構、生命週期管理、資源洩漏防護及安全邊界進行深度審計，並規劃可直接以 TDD 執行的下一階段工作。

---

## 一、 架構審計與發現分級 (Findings)

### 1. Critical (危險 - 零容忍發布阻擋)

*   **無 Session 狀態下的快取鍵分裂與記憶體洩漏 (Churn Key Leak)**
    *   **檔案位置**：`SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs` 中的 `GetCurrentSessionId()`
    *   **問題描述**：當 `CurrentSession` 為 `null` 時（例如本機測試、背景排程或未授權的 API 呼叫），系統會使用 `NOSESSION_{MachineName}_{ThreadId}_{DateTime.UtcNow.Ticks}` 產生快取鍵。由於 `Ticks` 每次都不同，這會導致 `_memoryCache` 產生無限增長的 Churn Keys，且快取驅逐時未釋放舊的 `DonationPaymentManager` 實例，造成嚴重的記憶體洩漏。
    *   **影響**：Soak 測試或本機持續運行數小時後，將因 OOM (Out of Memory) 崩潰。

*   **Raw CRM Endpoint 洩漏至產品端 (Information Disclosure)**
    *   **檔案位置**：`SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
    *   **問題描述**：Gateway 執行成功後，Payload 中仍包含 `approvedWebApiRoot`。這將真實的 Dynamics 365 物理 Endpoint 暴露給了產品層（ChurchReport），違反了「信任邊界隔離」原則。
    *   **影響**：一旦產品端被入侵，攻擊者可直接獲取後端 Dynamics 實例的真實網址，繞過 Gateway 的流量控制與審計。

*   **缺乏伺服器端授權策略 (Missing Server-Side Authorization)**
    *   **檔案位置**：`SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
    *   **問題描述**：目前 Gateway 僅完成 Principal 映射（如 `IIS APPPOOL\ChurchReport` $\rightarrow$ `church-report-service`），但缺少 `Workload` $\rightarrow$ `Alias` $\rightarrow$ `Operation` 的細粒度授權檢查。任何通過驗證的 Workload 都可以向任意 Profile 發送請求。
    *   **影響**：ChurchReport 可以任意調用 `crm82` 的操作，或將 ChurchReport 的 Contact ID 送往不屬於它的組織，造成跨組織數據污染。

---

### 2. Warning (警告 - 需在 E2E 前修正)

*   **自建配置構造器阻斷環境變數覆蓋 (Configuration Hijacking)**
    *   **檔案位置**：`SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
    *   **問題描述**：`DonationPaymentManager` 內部使用 `static ConfigurationBuilder` 硬編碼讀取 `appsettings.json`。這導致 ASP.NET Host 的 `appsettings.Development.json`、環境變數（Environment Variables）與命令列參數（Command-line overrides）無法傳遞至 Dynamics 啟動引導程序。
    *   **影響**：無法在本機開發環境中靈活切換 Gateway Endpoint，且測試環境無法覆蓋配置。

*   **複雜參數的位元組估算失效 (Byte-Bound Bypass)**
    *   **檔案位置**：`ControlledOperationExecutor.cs` 中的 `EstimateEnvelopeBytes`
    *   **問題描述**：對於非字串類型的參數，系統固定估算為 `64 bytes`。若參數中包含大型巢狀 JSON 或 Base64 檔案，此估算將完全失效，導致流量准入控制（Admission Control）無法精確限制大型 Payload。
    *   **影響**：大流量下可能因單一巨大請求撐爆 Kestrel 緩衝區，繞過容量限制。

*   **AdfsOAuth 隱性自動啟用 Password Grant**
    *   **檔案位置**：`DonationDynamicsAccessBootstrap.cs` 第 225-231 行
    *   **問題描述**：當 `AuthMode=AdfsOAuth` 且 `ManifestOrRegistrySource=local-dev-manifest` 時，系統會自動將 `AllowLocalDevPasswordGrant` 設為 `true`。此字串守門機制不夠安全，容易在正式環境因配置錯誤而誤啟用 ROPC (Resource Owner Password Credentials) 流程。
    *   **影響**：違反「正式環境禁用 Password Grant」的安全不變量。

---

### 3. Info (資訊 - 架構優化建議)

*   **LineMessagingClient 未收歸 DI 容器管理**
    *   **檔案位置**：`DonationPaymentManager.cs` 建構子
    *   **問題描述**：`DonationPaymentManager` 每次被實例化時，都會手動 `new LineMessagingClient(channelAccessToken)`。雖然內部使用了共用 Workflow，但這仍會導致 HttpClient 實例的重複創建與潛在的 Socket 耗盡風險。
    *   **影響**：不利於 HttpClient 的連接池複用與確定性生命週期管理。

---

## 二、 建議架構與資料／生命週期流程

### 1. 資源生命週期與擁有者關係 (Resource Ownership)

```
[ ASP.NET Core Host ]
  │
  ├── (Singleton) IConfiguration ──> 注入至 ──> [ DonationPaymentManager ] (Transient/Scoped)
  │                                                   │
  │                                                   ├──> 持有 (SemaphoreSlim) _feeRefreshLock
  │                                                   └──> 引用 (Singleton) LineMessagingClient
  │
  └── (Hosted Service) DonationDynamicsAccessBootstrapLifetime
        │
        └── 擁有並管理 ──> [ DonationDynamicsAccessProcessHost ] (Process-Level Singleton)
                              │
                              └── 獨佔 ──> [ ServiceProvider ] (Active Generation)
                                            │
                                            ├──> DynamicsHttpTransport (HttpClient Handler)
                                            └──> AdfsOAuthTokenProvider
```

*   **確定性清理 (Deterministic Cleanup)**：
    *   `DonationPaymentManager` 必須實作 `IDisposable`，在 `Dispose` 中釋放 `_feeRefreshLock`。
    *   `LineMessagingClient` 改由 DI 容器以 `Singleton` 註冊，其內部的 `HttpClient` 生命週期由 `IHttpClientFactory` 或容器託管，不隨 `DonationPaymentManager` 的創建與銷毀而重複構造。

### 2. 請求執行與安全授權流程 (Request Execution & Auth Flow)

```
[ ChurchReport Client ]
       │
       │ (1) ExecuteAsync(Request)
       ▼
[ ControlledOperationExecutor ]
       │
       │ (2) 步驟一：參數白名單過濾 (Allowed Parameters)
       │ (3) 步驟二：精確估算位元組 (Serialize & Measure Bytes)
       │ (4) 步驟三：伺服器端授權策略 (IWorkloadAuthorizationPolicy)
       │     - 檢查 WorkloadSubjectId 是否允許存取 ProfileAlias + Operation
       │
       ├─── [ 授權失敗 ] ──> 回傳 403 Forbidden / InvalidParameter
       │
       │ (5) 步驟四：向 SqlRuntimeHostSlotCoordinator 申請租約 (Acquire Lease)
       ▼
[ SqlRuntimeHostSlotCoordinator ]
       │
       │ (6) 執行 SQL Serializable Transaction (sp_getapplock)
       │     - 驗證 Schema 與 Epoch
       │
       ├─── [ 租約取得失敗 / NotReady ] ──> 回傳 503 Service Unavailable
       │
       │ (7) 步驟五：執行 Web API 呼叫 (Outbound Dispatch)
       ▼
[ Dynamics Web API ] ──> (8) 剝離 approvedWebApiRoot ──> [ 回傳邏輯結果與業務 Payload ]
```

---

## 三、 TDD 實施切片 (TDD Implementation Slices)

### 切片 1：Configuration Ownership & DI 收歸 (基礎阻礙清除)
*   **目標**：消除 `DonationPaymentManager` 的自建 `ConfigurationBuilder`，改用 Host 注入，並將 `LineMessagingClient` 收歸 DI。
*   **涉及檔案**：
    *   `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
    *   `SpeechMessageProducts.ChurchReport/Startup.cs`
*   **RED 測試**：
    *   在 `DonationPaymentProcessorGatewayAdapterTests.cs` 中，新增測試 `DonationPaymentManager_Should_Read_Configuration_From_Injected_Provider`。在未放置 `appsettings.json` 於執行目錄，但注入了 Mock `IConfiguration` 的情況下，驗證其能正確讀取 `Gateway:Endpoint`。
*   **GREEN 實作**：
    *   修改 `DonationPaymentManager` 建構子，接受 `IConfiguration` 與 `LineMessagingClient` 注入，移除靜態 `m_ConfigurationBuilder`。

### 切片 2：Session 快取硬化與 Churn Key 修正 (記憶體洩漏防護)
*   **目標**：修正無 Session 時的快取鍵分裂，並在快取驅逐時確定性釋放資源。
*   **涉及檔案**：
    *   `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
    *   `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
*   **RED 測試**：
    *   新增 `SessionEvictionDisposalTests.cs`：
        1. 模擬 `CurrentSession` 為 `null`，連續呼叫 `DonationPaymentManager` 屬性 100 次，驗證 `_memoryCache` 中僅存在一個固定鍵（如 `STATIC_NO_SESSION_KEY`），而非產生 100 個不同的 Churn Keys。
        2. 手動觸發快取驅逐（Eviction），驗證被驅逐的 `DonationPaymentManager` 實例之 `Dispose` 方法被調用，且內部的 `SemaphoreSlim` 被釋放。
*   **GREEN 實作**：
    *   在 `GetCurrentSessionId()` 中，若 `session == null`，回傳固定常數 `NOSESSION_SHARED_KEY`。
    *   讓 `DonationPaymentManager` 實作 `IDisposable`。在 `InMemoryDataContextSmallGroup` 的 `PostEvictionCallback` 中，將 `subValue` 轉型為 `IDisposable` 並執行 `Dispose()`。

### 切片 3：Gateway 授權策略與 Raw Endpoint 隔離 (安全邊界硬化)
*   **目標**：實作伺服器端授權，並在回傳結果中剝離真實 CRM 地址。
*   **涉及檔案**：
    *   `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
    *   `SpeechMessage.Dynamics.WebApi/Runtime/IWorkloadAuthorizationPolicy.cs` (新增)
*   **RED 測試**：
    *   在 `ControlledOperationExecutorTests.cs` 中：
        1. 新增測試 `ExecuteAsync_Should_Return_Forbidden_When_Workload_Not_Authorized_For_Profile`。
        2. 新增測試 `ExecuteAsync_Should_Not_Expose_ApprovedWebApiRoot_In_Result`。
*   **GREEN 實作**：
    *   在 `ControlledOperationExecutor.ExecuteAsync` 中，調用 `IWorkloadAuthorizationPolicy.Authorize(workload, profile, operation)`，若未授權則回傳 `DynamicsErrorCodes.Unauthorized`。
    *   在回傳 `OperationExecutionResult` 前，將 `approvedWebApiRoot` 設為 `null` 或從傳輸 DTO 中移除。

### 切片 4：精確位元組估算 (容量控制硬化)
*   **目標**：防止大型/巢狀 JSON 繞過容量限制。
*   **涉及檔案**：
    *   `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
*   **RED 測試**：
    *   在 `ControlledOperationExecutorTests.cs` 中，傳入一個包含複雜巢狀 Dictionary（總長度超過 10KB）的 `OperationExecutionRequest`，驗證 `EstimateEnvelopeBytes` 回傳的估算值與實際序列化後的位元組數誤差在 5% 以內，而非固定估算為 64 bytes。
*   **GREEN 實作**：
    *   修改 `EstimateEnvelopeBytes`，若參數值非字串，使用 `System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value).Length` 進行精確測量。

---

## 四、 本機開發與 SQL Coordinator 驗證方案

### 1. 本機 Kestrel 開發身分驗證 (Development-only Auth)
由於本機 Kestrel 無法直接取得 IIS App-Pool 的 Windows 整合驗證（Negotiate），為了不弱化正式環境的安全設定，採用以下方案：

*   **實作機制**：
    *   在 Gateway 中實作 `LocalDevAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>`。
    *   僅當 `IWebHostEnvironment.IsDevelopment()` 為 `true` 時，才註冊此 Scheme。
    *   該 Handler 讀取請求標頭 `X-LocalDev-Mock-Principal`。若標頭值為 `IIS APPPOOL\\ChurchReport`，則在本機環境下將其映射為具備該 Claim 的 ClaimsPrincipal。
    *   **正式環境防線**：若在非 Development 環境下偵測到該標頭，直接忽略並回傳 401，確保正式環境強制走 Windows 整合驗證或 ADFS Bearer Token。

### 2. 本機無 SQL Server Engine 時的 Durable Coordinator 處理
*   **嚴禁使用 In-Memory 假裝完成**：`SqlRuntimeHostSlotCoordinator` 必須在真實的 SQL 環境下驗證其 Serializable Transaction 與 `sp_getapplock` 行為。
*   **本機配置方案**：
    *   **方案 A (優先)**：使用 **SQL Server Express LocalDB**。連接字串配置為：
        `"Server=(localdb)\\MSSQLLocalDB;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;Encrypt=false;"`
        LocalDB 隨 Visual Studio 預設安裝，不需運行背景 Windows 服務，且支援完整的 T-SQL 鎖定語意。
    *   **方案 B**：使用本機 Docker 運行輕量化 `mcr.microsoft.com/mssql/server` 容器。
*   **Readiness 驗證**：
    *   Gateway 啟動時，`DynamicsGatewayReadinessService` 會嘗試連線並執行 Schema 檢查。若 LocalDB 未創建或連線失敗，Gateway 必須保持 `NotReady` 狀態（Fail-Closed），不可自動降級為 In-Memory Coordinator。

---

## 五、 驗證矩陣 (Verification Matrix)

| 測試層級 | 驗證對象 | 驗證命令 / 方法 | 預期結果 |
| :--- | :--- | :--- | :--- |
| **單元測試 (Unit)** | 1. Churn Key 修正<br>2. 精確位元組估算 | `dotnet test --filter "FullyQualifiedName~SessionEvictionDisposalTests"` | 1. 無 Session 時快取鍵不分裂。<br>2. 複雜物件估算精確。 |
| **整合測試 (TestServer)** | 1. LocalDev Mock 驗證<br>2. Raw Endpoint 隔離 | 使用 `Microsoft.AspNetCore.TestHost` 模擬 Gateway 啟動，發送 Mock 標頭。 | 1. 成功映射為 ChurchReport Principal。<br>2. 回傳 Payload 不含 `approvedWebApiRoot`。 |
| **本機測試 (localhost)** | 1. LocalDB 租約取得<br>2. Schema 自動驗證 | 啟動 Local Gateway 專案，連線至 `(localdb)\MSSQLLocalDB`。 | 1. 自動建立租約資料表。<br>2. 成功取得 Fencing Token。 |
| **環境測試 (WinRM/VM)** | 1. CE 9.1 VM 連通性<br>2. WhoAmI 驗證 | 執行本機 PowerShell 腳本，透過 WinRM 呼叫 `D365APP01`。 | 1. 驗證 5985 埠可達。<br>2. 取得遠端 Web API 的 WhoAmI 回應。 |
| **相容性測試 (CE 8.2 / 9.1)** | 1. `crm82` (CE 8.2)<br>2. `sunnyvalechback-prod` (CE 9.1) | 透過 Gateway 分別向兩個 Profile 發送請求。 | 1. 兩者 Transport 與 Token 隔離。<br>2. 真實憑證缺失時 `fail closed`。 |
| **穩定性測試 (Leak/Soak)** | 1. HttpClient 洩漏<br>2. Semaphore 洩漏 | 模擬 10,000 次奉獻表單刷新與快取過期。 | 1. 記憶體與 Socket 數量保持恆定。<br>2. 無未釋放的 Semaphore 鎖。 |

---

## 六、 依賴順序與回滾規劃

### 1. 實施步驟與 RED/GREEN 驗證

1.  **步驟一：重構 ChurchReport 配置與 DI 註冊**
    *   *RED*：刪除本機 `appsettings.json`，執行 `TaishinProviderTests`，因找不到配置而失敗。
    *   *GREEN*：改用 Host 注入 `IConfiguration`，測試通過。
2.  **步驟二：修正 Churn Key 與快取驅逐釋放**
    *   *RED*：編寫 `SessionEvictionDisposalTests`，驗證無 Session 時產生了多個快取鍵，且驅逐時未調用 `Dispose`。
    *   *GREEN*：實作固定鍵回傳與 `PostEvictionCallback` 釋放，測試通過。
3.  **步驟三：本機 LocalDB 配置與 Schema 驗證**
    *   *RED*：啟動 Gateway，連接字串指向不存在的資料庫，驗證 Readiness 服務拋出異常且 Gateway 處於 `NotReady`。
    *   *GREEN*：配置指向 `(localdb)\MSSQLLocalDB`，啟動時自動執行 `SchemaSql`，Gateway 變為 `Ready`。
4.  **步驟四：實作伺服器端授權與 Endpoint 隔離**
    *   *RED*：使用 `church-report-service` 請求 `crm82` 的操作，請求成功（未授權漏洞）；且成功回應中包含 `approvedWebApiRoot`。
    *   *GREEN*：加入 `IWorkloadAuthorizationPolicy` 攔截，回傳 403；並在結果中剝離 Endpoint 地址。

### 2. 回滾點 (Rollback Points)
*   **回滾點 A (切片 1-2 完成後)**：若重構 DI 導致舊有 MVC Controller 啟動失敗，立即回滾至 `DonationPaymentManager` 的自建配置版本，保持舊有相容性。
*   **回滾點 B (切片 3-4 完成後)**：若 SQL Coordinator 在 LocalDB 上出現鎖定死結（Deadlock），立即將連接字串切回本機測試專用的 `InMemoryRuntimeHostSlotCoordinator`，但此狀態不可提交至 Master 分支。

---

## 七、 暫時無法宣告完成的 Gate (Phase 4～6 Blocker)

以下項目在本輪工作完成前，**絕不能**宣告完成：

1.  **真實環境的 ADFS Authorization Code 流程驗證**：目前本機僅能以 `local-dev-manifest` 模擬 Password Grant，真實環境的憑證與 OAuth 授權碼流程尚未在 `D365APP01` 上通過驗證。
2.  **跨主機時鐘偏移的 Fencing 隔離測試**：尚未在兩台獨立 Gateway 實機上驗證當資料庫時鐘存在 $\pm 1$ 秒偏差時，`SqlRuntimeHostSlotCoordinator` 的 Quarantine 隔離機制是否 100% 精確。
3.  **Durable Audit Intent Ledger**：寫入操作的冪等性帳本（Idempotency Ledger）尚未實作，無法保證在 `OutcomeUnknown`（如網路中斷）時的交易安全。
