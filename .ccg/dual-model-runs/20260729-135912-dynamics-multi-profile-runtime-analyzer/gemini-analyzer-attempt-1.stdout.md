# Dynamics crm82/crm91 多設定檔執行期 (Multi-Profile Runtime) 架構分析報告

本報告針對 `SpeechMessage.Dynamics` 專案從單一設定檔（Single-Profile）過渡至多設定檔（Multi-Profile）並行運作之里程碑進行架構與實作分析。

---

## 1. UX Analysis (使用者與維運影響分析)

*   **使用者體驗與用戶端隔離**：
    *   產品端（如 `ChurchReport`）完全不需感知後端 Dynamics 的實體 URL、版本（8.2 或 9.1）、認證憑證或傳輸協定。
    *   產品端僅需在請求中攜帶邏輯別名（`ProfileAlias`，例如 `crm82` 或 `crm91`）與預先註冊的 `capabilityOperationId`。
*   **維運人員體驗 (Operator UX)**：
    *   支援動態更新設定檔（如更新密碼、Token 憑證或調整並行限制），且更新過程採用「**Active-plus-one-draining**」機制，舊世代（Generation）在背景優雅釋放（Drain），新請求無縫導向新世代，達成零停機時間更新。
*   **行動端與桌面端體驗**：
    *   本調整屬於後端 Gateway 執行期優化，對前端無直接介面影響，但透過連線池複用與預熱機制，能顯著降低 API 延遲並提升回應穩定度。

---

## 2. Design Evaluation (設計系統與模式評估)

*   **一致性模式**：
    *   遵循「**Two-Host, One-Core**」設計原則。不論是 Central Gateway 還是 Local Gateway，皆共用相同的 `SpeechMessage.Dynamics.WebApi` 核心程式庫。
*   **不可變世代與安全邊界**：
    *   設定檔一旦載入即為不可變（Immutable）。任何異動皆會產生新的「執行期世代（Runtime Generation）」。
    *   敏感資訊（如密碼、Client Secret）僅能透過 `ISecretResolver` 於執行期動態解析，絕不寫入 JSON 設定檔、日誌或傳遞至用戶端。

---

## 3. Technical Considerations (技術架構與風險評估)

*   **元件結構調整**：
    *   目前 DI 容器中 `DynamicsHttpTransport`、`DynamicsWebApiClient` 等皆為圍繞單一 `DynamicsWebApiOptions` 的 Singleton/Scoped 實例。
    *   必須重構為由一個管理中心（`IDynamicsProfileManager`）統一管控多個獨立的設定檔世代實例。
*   **准入控制共享**：
    *   當不同的別名或世代指向同一個實體 Dynamics 組織時，必須共用同一個 `IOrganizationAdmissionManager` 以維持全域並行上限，但兩者的連線池、Token 快取與 Session 狀態必須完全隔離。

---

## 4. Options (替代方案評估)

| 方案 | 優點 | 缺點 | 評估結果 |
| :--- | :--- | :--- | :--- |
| **A. 每個請求動態建構 Client** | 實作簡單，不需管理複雜的生命週期。 | 無法複用連線池，每次請求皆需重新建立 TCP 連線與解析 Token，效能極差。 | **拒絕** |
| **B. 多設定檔獨立隔離，不共享准入** | 設定檔間完全獨立，生命週期管理單純。 | 若兩個別名指向同一個實體 CRM 組織，將導致並行限制加倍，可能觸發 CRM 服務保護限制。 | **拒絕** |
| **C. 世代隔離 + 實體組織准入橋接 (推薦)** | 兼顧連線與憑證的安全隔離，同時精準控制實體組織的總體流量上限。 | 實作複雜度較高，需精細處理世代交替時的 Drain 與 Dispose。 | **採納** |

---

## 5. Recommendation & Implementation Plan (推薦方案與實作細節)

### 5.1 全新類型與介面設計 (New Types & Interfaces)

1.  **`IDynamicsProfileManager` 與 `DynamicsProfileManager`**
    *   **職責**：全域 Singleton。負責維護所有邏輯別名（Alias）與其當前活動世代（Active Generation）及排空世代（Draining Generation）的對應關係。
    *   **核心方法**：`IDynamicsWebApiClient GetClient(string alias)`。
2.  **`DynamicsProfileGeneration`**
    *   **職責**：封裝單一設定檔世代的完整執行期上下文。
    *   **成員**：包含專屬的 `DynamicsWebApiOptions`、`IDynamicsHttpTransport`、`DynamicsWebApiClient`、`AdfsOAuthTokenProvider`。
    *   **狀態**：`Active`、`Draining`、`Disposed`。
3.  **`IProfileRuntimeFactory` 與 `ProfileRuntimeFactory`**
    *   **職責**：負責解析特定設定檔的憑證，並建構 `DynamicsProfileGeneration` 實例。
4.  **`PhysicalOrgAdmissionBridge`**
    *   **職責**：全域 Singleton。維護 `CanonicalOrgKey -> IOrganizationAdmissionManager` 的對應。確保指向相同實體 CRM 的不同世代/別名共用同一個准入控制器。

### 5.2 需進行最小化修改的現有檔案 (Existing Files to Modify)

1.  **`SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`**
    *   修改 DI 註冊，將原本單一 Options 的 Singleton 註冊改為註冊 `IDynamicsProfileManager`、`IProfileRuntimeFactory` 與 `PhysicalOrgAdmissionBridge`。
    *   保留對舊有單一設定檔註冊的相容性支援（相容舊測試）。
2.  **`SpeechMessage.Dynamics.Gateway/Program.cs`**
    *   修改設定載入邏輯，自 `appsettings.json` 讀取設定檔字典（`Map<string, DynamicsWebApiOptions>`）並注入管理器。
3.  **`SpeechMessage.Dynamics.Gateway/DynamicsGatewayReadinessService.cs`**
    *   修改 Readiness 檢查邏輯，需遍歷所有活動中的設定檔世代進行 `WhoAmI` 預熱與健康檢查。

### 5.3 安全相容性策略 (Compatibility Strategy)

*   **預設別名回退**：若偵測到舊有的單一設定檔配置，自動將其註冊為別名為 `"default"` 的設定檔。
*   **相容性 DI 橋接**：在 DI 中保留 `IDynamicsWebApiClient` 的註冊，其實作改為向 `IDynamicsProfileManager` 請求 `"default"` 別名的 Client，確保現有單一設定檔的單元測試與整合測試不需修改即可正常執行。

### 5.4 不可變世代金鑰欄位與安全邊界 (Generation Key & Secrets)

*   **世代金鑰欄位**：由 `ProfileAlias`、`CeVersion`、`OrganizationBaseUri`、`AuthMode` 以及設定檔內容的 SHA256 雜湊值（Fingerprint）共同組成。
*   **敏感資訊隔離**：金鑰與雜湊計算中**不得**包含實際解析出的密碼或 Token 明文。僅能使用 Secret Reference 名稱參與雜湊，確保安全邊界。

### 5.5 別名路由對應機制 (Alias Routing)

*   Gateway 路由：`POST /v1/organizations/{alias}/operations/{capabilityOperationId}`。
*   Gateway 根據路徑中的 `{alias}` 呼叫 `_profileManager.GetClient(alias)`。
*   取得該別名當前 `Active` 狀態的 `DynamicsProfileGeneration`，並執行對應的 `ExecuteRegisteredOperationAsync`。

### 5.6 實體組織准入共享機制 (Shared Admission)

```
[Alias: crm82] ──> [Generation 1] ──┐
                                    ├──> [PhysicalOrgAdmissionBridge] ──> [Shared OrganizationAdmissionManager]
[Alias: crm91] ──> [Generation 1] ──┘
```
*   `PhysicalOrgAdmissionBridge` 將 `OrganizationBaseUri` 進行標準化處理（轉小寫、去除結尾斜線、解析網域），生成 `CanonicalOrgKey`。
*   透過 `ConcurrentDictionary.GetOrAdd(canonicalKey, _ => new OrganizationAdmissionManager(...))` 確保實體組織唯一性。
*   不同世代的 `DynamicsWebApiClient` 在發送請求前，皆向此共享的 `OrganizationAdmissionManager` 申請 Lease。

### 5.7 生命週期與替換步驟 (Lifecycle & Replacement)

1.  **偵測變更**：設定檔更新事件觸發。
2.  **建構新世代**：`ProfileRuntimeFactory` 建立 Generation N+1。
3.  **預熱 (Warm-up)**：對 Generation N+1 執行 `WhoAmI` 測試，確認連線與憑證可用。
4.  **原子替換**：將別名指向 Generation N+1（使用 `Volatile.Write` 或 `Interlocked` 替換指標）。
5.  **排空舊世代 (Drain)**：將 Generation N 標記為 `Draining`，停止接受新請求。允許在途請求（In-flight leases）在指定時間內（如 30 秒）完成。
6.  **限制最大排空數**：強制執行 **Active-plus-one-draining**。若在 Generation N 尚未排空完成前又收到新更新，直接拒絕該次更新，或強制中止（Abort）最舊的世代。
7.  **釋放資源 (Dispose)**：排空完成後，依序呼叫 Generation N 的 `HttpClient.Dispose()` 與 `SocketsHttpHandler.Dispose()`。

### 5.8 優先撰寫之測試案例 (TDD Red Tests)

1.  **`Should_Route_To_Correct_Profile_Client_By_Alias`**
    *   **預期失敗原因**：尚未實作多設定檔路由，系統僅能解析單一預設 Client。
2.  **`Should_Share_Admission_Manager_When_Base_Uri_Is_Equivalent`**
    *   **預期失敗原因**：兩個不同別名的 Client 各自建立了獨立的 `OrganizationAdmissionManager`，導致並行限制無法合併計算。
3.  **`Should_Dispose_Old_HttpClient_After_Drain_Timeout`**
    *   **預期失敗原因**：替換設定檔後，舊世代的連線池與 Handler 未被釋放，導致 Socket 與記憶體洩漏。

### 5.9 並行開發檔案分配 (Parallel Work Decomposition)

為避免多個 `ccg-implement` 代理人衝突，檔案修改權限分配如下：

*   **Agent A (核心管理與 DI)**
    *   可寫入檔案：
        *   `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileManager.cs` (新)
        *   `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileManager.cs` (新)
        *   `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
*   **Agent B (工廠與准入橋接)**
    *   可寫入檔案：
        *   `SpeechMessage.Dynamics.WebApi/Runtime/IProfileRuntimeFactory.cs` (新)
        *   `SpeechMessage.Dynamics.WebApi/Runtime/ProfileRuntimeFactory.cs` (新)
        *   `SpeechMessage.Dynamics.WebApi/Capacity/PhysicalOrgAdmissionBridge.cs` (新)
*   **Agent C (Gateway 整合與測試)**
    *   可寫入檔案：
        *   `SpeechMessage.Dynamics.Gateway/DynamicsGatewayReadinessService.cs`
        *   `SpeechMessage.Dynamics.Gateway/Program.cs`
        *   `SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs` (新)

---

## 6. Findings & Risk Classification (發現與風險評估)

### CRITICAL (嚴重風險)
*   **記憶體與 Socket 洩漏風險**：若在世代替換時未確實呼叫舊世代 `SocketsHttpHandler` 的 `Dispose()`，將導致底層 TCP 連線持續殘留，最終耗盡系統 Socket 埠。
*   **准入限制失效風險**：若 `PhysicalOrgAdmissionBridge` 的 Key 標準化邏輯不夠嚴密（例如大小寫未對齊或 IP 與 FQDN 未能識別為同一組織），將導致多個設定檔繞過並行限制，對 Dynamics 伺服器造成過載。

### WARNING (警告事項)
*   **排空逾時阻礙更新**：若在途請求因網路延遲無法在排空期限內完成，且未實作強制中止機制，將導致舊世代無法被回收。必須設定硬性排空截止時間（Hard Drain Timeout）。
*   **多執行緒競爭**：在切換活動世代指標時，若未使用執行緒安全的操作（如 `Volatile`），可能導致部分請求在短時間內仍被分流至舊世代，引發非預期的行為。

### INFO (一般資訊)
*   **預熱機制優化**：新世代在正式接單前，應至少完成一次 `WhoAmI` 查詢以確保 Token 取得管道暢通，避免首筆請求因冷啟動超時。
