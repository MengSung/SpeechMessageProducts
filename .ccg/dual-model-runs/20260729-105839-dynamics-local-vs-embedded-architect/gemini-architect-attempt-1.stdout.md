--- a/docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md
+++ b/docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md
@@ -9,3 +9,3 @@
-configuration for Visual Studio development, testing, or a deliberately isolated
-deployment. This changes the host location, not the connector/security contract.
+configuration for Visual Studio development, testing, or a deliberately isolated
+deployment. This changes the Gateway endpoint location, not the connector/security contract.
@@ -41,74 +41,41 @@
-## Product-selectable host mode
-
-Each of the five products owns a strict, versioned JSON configuration that
-chooses one startup-only mode:
-
-| Mode | Use | Rules |
-| --- | --- | --- |
-| `Gateway` | Default production mode. | The product sends its bounded operation to the authenticated Gateway REST API. |
-| `Embedded` | Visual Studio debugging/testing or an intentionally isolated product deployment. | The product references only `SpeechMessage.Dynamics.Embedded`, which hosts the same approved no-SDK runtime and capability contract in-process. It cannot reference the low-level Web API transport directly. |
-
-The mode is deployment controlled and validated before startup. It cannot be
-chosen by a caller, LINE ID, user account, browser session, request field, or
-feature toggle evaluated per request. `Embedded` has its own process-local
-HTTP/socket pool, but it still joins the same organization-admission coordinator
-and aggregate capacity plan as Gateway hosts. It is not a per-user connection
-pool or a capacity bypass.
-
-Representative non-secret product configuration:
-
-~~~json
-{
-  "$schema": "https://schemas.speechmessage.local/dynamics-access-product.v1.schema.json",
-  "DynamicsAccess": {
-    "SchemaVersion": 1,
-    "ExecutionMode": "Gateway",
-    "WorkloadSubjectId": "church-report-service",
-    "Gateway": {
-      "Endpoint": "https://dynamics-gateway.internal/",
-      "OrganizationAlias": "membership"
-    }
-  }
-}
-~~~
-
-Embedded mode uses the same schema but permits only a deployment-provisioned
-binding and admission-coordinator reference:
-
-~~~json
-{
-  "$schema": "https://schemas.speechmessage.local/dynamics-access-product.v1.schema.json",
-  "DynamicsAccess": {
-    "SchemaVersion": 1,
-    "ExecutionMode": "Embedded",
-    "WorkloadSubjectId": "church-report-service",
-    "Embedded": {
-      "ProductProfileBinding": "church-report-membership",
-      "OrganizationAdmissionCoordinatorRef": "dynamics-admission-production"
-    }
-  }
-}
-~~~
-
-Changing `ExecutionMode` creates a new host/runtime generation through
-replace-and-drain. The inactive branch, duplicate/unknown fields, raw CRM URI,
-credential/token, user/LINE/session field, dynamic override, or unsupported
-schema version is rejected before binding. Development JSON may point to a fake
-CRM fixture or local Gateway but must fail if it resolves a production secret or
-production organization identity.
-
-The product JSON is a startup binding document, not an authorization authority.
-In Gateway mode, workload identity is derived from the authenticated internal
-service principal and checked against the central product-profile registry; any
-editable `WorkloadSubjectId` mismatch fails startup/request admission. In
-Embedded mode, the binding/admission reference must be signed or verified against
-the same central registry before any CRM secret, profile runtime, or queue slot is
-resolved. If the signed manifest or central registry is unavailable, times out,
-or verification fails, Embedded startup fails closed / remains NotReady; local
-JSON is never sufficient authority to bind a production profile.
-Visual Studio Embedded fake-profile testing still uses a separate development
-trust anchor: an approved local development registry or signed Development
-manifest may bind only a fake endpoint and non-production organization identity.
-It can never validate a production binding, and its unavailability or invalid
-signature also leaves Embedded NotReady.
+## Product-selectable Gateway mode
+
+Each of the five products owns a strict, versioned JSON configuration that
+points to a Gateway endpoint. The execution mode is unified to `Gateway` across all environments, with the endpoint location determining the deployment topology:
+
+| Mode | Use | Rules |
+| --- | --- | --- |
+| `Central Gateway` | Production / Staging environments. | The product sends its bounded operation to the central authenticated Gateway REST API (e.g., internal DNS). |
+| `Local Gateway` | Visual Studio debugging/testing or isolated deployment. | The product sends its bounded operation to a local Gateway sidecar running on localhost. The product process remains completely decoupled from Dynamics SDKs. |
+
+The endpoint is deployment controlled and validated before startup. It cannot be
+chosen by a caller, LINE ID, user account, browser session, request field, or
+feature toggle evaluated per request.
+
+Representative non-secret product configuration (Central Gateway):
+
+~~~json
+{
+  "$schema": "https://schemas.speechmessage.local/dynamics-access-product.v1.schema.json",
+  "DynamicsAccess": {
+    "SchemaVersion": 1,
+    "WorkloadSubjectId": "church-report-service",
+    "ProfileAlias": "membership",
+    "Gateway": {
+      "Endpoint": "https://dynamics-gateway.internal/",
+      "TimeoutSeconds": 30
+    }
+  }
+}
+~~~
+
+Local Gateway mode uses the same schema but points to localhost:
+
+~~~json
+{
+  "$schema": "https://schemas.speechmessage.local/dynamics-access-product.v1.schema.json",
+  "DynamicsAccess": {
+    "SchemaVersion": 1,
+    "WorkloadSubjectId": "church-report-service-dev",
+    "ProfileAlias": "membership-dev",
+    "Gateway": {
+      "Endpoint": "http://localhost:5000/",
+      "TimeoutSeconds": 30
+    }
+  }
+}
+~~~
@@ -143,36 +110,29 @@
 ## Architecture
 
 ~~~mermaid
 flowchart LR
-  Products["5--10 Product Services"] -->|"mTLS + workload JWT"| Gateway["Dynamics Access Gateway"]
-  Gateway --> Policy["Product / Capability / Alias Policy"]
-  Products -->|"Embedded (trusted startup JSON)"| Embedded["Embedded host adapter"]
-  Embedded --> Policy
-  Policy --> Pool["Profile Runtime Pool"]
-  Pool --> Connector["Private no-SDK Web API Connector"]
-  Connector --> CE82["Dynamics CE 8.2"]
-  Connector --> CE91["Dynamics CE 9.1"]
-
-  Secrets["Secret Provider"] --> Pool
-  Admission["Organization admission coordinator\nepoch + host slots + queue budget"] <--> Pool
-  Telemetry["Audit, Metrics, Health"] <--> Gateway
-  Telemetry <--> Embedded
-  Telemetry <--> Pool
+  Products["5--10 Product Services (.NET 10)"] -->|"HTTP REST"| Gateway["Dynamics Access Gateway (.NET Framework 4.8)"]
+  Gateway --> Policy["Product / Capability / Alias Policy"]
+  Policy --> Pool["Profile Runtime Pool"]
+  Pool --> Connector["Official CrmServiceClient Connector"]
+  Connector --> CE82["Dynamics CE 8.2"]
+  Connector --> CE91["Dynamics CE 9.1"]
+
+  Secrets["Secret Provider"] --> Pool
+  Admission["Organization admission coordinator\nepoch + host slots + queue budget"] <--> Pool
+  Telemetry["Audit, Metrics, Health"] <--> Gateway
+  Telemetry <--> Pool
 ~~~
 
 The existing **SpeechMessageProducts.sln** is planned to receive this new
 Dynamics project group:
 
 | Project | Responsibility |
 | --- | --- |
 | SpeechMessage.Dynamics.Abstractions | DTO-only contracts and error/capability abstractions; no CRM SDK types. |
-| SpeechMessage.Dynamics.WebApi | Direct OData v4 transport, authentication, capability validation, and profile runtime pool. |
-| SpeechMessage.Dynamics.Gateway | Internal REST API, workload authentication, authorization policy, operations, health and telemetry. |
-| SpeechMessage.Dynamics.Embedded | The only supported in-process product host adapter; strict mode binding and the same controlled runtime/operation contract. |
+| SpeechMessage.Dynamics.Gateway | Internal REST API, workload authentication, authorization policy, operations, health, telemetry, and official CrmServiceClient connector. |
 | SpeechMessage.Dynamics.Tests | Isolation, lifecycle, contract, resilience, and performance tests. |
 | SpeechMessage.Dynamics.SmokeTests | Opt-in non-production CE 8.2/9.1 verification. |
 
-Products normally use the Gateway OpenAPI/HTTP contract. An embedded exception
-references only the Embedded adapter, never the low-level connector; neither
-mode receives CRM credentials or CRM SDK types.
+Products normally use the Gateway OpenAPI/HTTP contract. No product project
+receives CRM credentials or CRM SDK types.
@@ -477,12 +437,10 @@
 ## Final dependency rule
 
 ~~~text
-Gateway mode:  Product -> Gateway REST contract -> Gateway -> HttpClient/OData v4 -> Dynamics
-Embedded mode: Product -> Embedded adapter -> HttpClient/OData v4 -> Dynamics
+Central Gateway mode: Product (.NET 10) -> HTTP REST -> Central Gateway (.NET Framework 4.8) -> CrmServiceClient -> Dynamics
+Local Gateway mode:   Product (.NET 10) -> HTTP REST (localhost) -> Local Gateway (.NET Framework 4.8) -> CrmServiceClient -> Dynamics
 ~~~
 
-The final solution must contain no project reference to a DLL under
-D:\?唾?蝘??Ｗ?\蝟餌絞撟喳\Dynamics 365 SDK DLL, no CRM SDK package/type
-dependency in production **or test**, and no CRM 2011 OrganizationData.svc
-fallback.
+The final solution must contain no project reference to a Dynamics SDK DLL or package in the product projects (e.g., ChurchReport). All Dynamics SDK dependencies (such as Microsoft.CrmSdk.XrmTooling.CoreAssembly) are strictly isolated within the Gateway project.
@@ -498,1 +456,110 @@
 - [Do not use the OData v2 endpoint](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/best-practices/business-logic/do-not-use-odata-v2-endpoint)
+
+## 架構分析：本地網關 (Local Gateway) 對比 內嵌模式 (Embedded)
+
+### 1. 現有架構評估 (Analysis)
+目前設計試圖同時支援 `Gateway`（進程外 HTTP）與 `Embedded`（進程內直接呼叫 Web API）兩種執行模式。雖然這提供了部署彈性，但引入了嚴重的架構摩擦：
+- **目標框架衝突**：產品服務（如 `ChurchReport`）目標框架為 .NET 10，而 D365 9.1 On-Premises/IFD 的官方 SDK `Microsoft.CrmSdk.XrmTooling.CoreAssembly` 與 `CrmServiceClient` 必須運行在 .NET Framework 4.8。若在 .NET 10 產品進程內直接載入該 SDK（即 `Embedded` 模式），將會面臨嚴重的依賴衝突與運行時不穩定。
+- **憑證與資源洩漏**：進程內執行會將 Dynamics 憑證暴露給產品進程，且多個產品實例會各自建立獨立的連線池，違反了 Session 隔離與憑證隔離的釋放阻擋條件（Release Blockers）。
+- **維護成本加倍**：必須同時維護進程外 HTTP 轉發與進程內直接呼叫兩套完全不同的 DI 註冊與執行邏輯。
+
+### 2. 架構決策 (Architecture Decision)
+**決策建議**：**方案 1 - 移除 Embedded 模式，生產環境統一使用 Central Gateway，開發與隔離部署則使用 Local Gateway 邊車 (Sidecar)。**
+
+*   **決策理由**：
+    *   **SDK 隔離**：將官方 .NET Framework 4.8 SDK 限制在獨立的 Gateway 進程（Central 或 Local）中，使 .NET 10 產品服務與舊型 Microsoft SDK 完全解耦。
+    *   **單一執行路徑**：產品在所有環境中都只使用 HTTP 用戶端（`ProductClient`）與 Gateway 通訊。開發與生產環境的唯一差異僅在於 Gateway 的 Endpoint 位址（localhost 對比 內部 DNS）。
+    *   **集中式連線池管理**：連線池完全由 Gateway 進程擁有，避免產品進程產生資源洩漏，並確保確定性釋放。
+    *   **嚴格的安全邊界**：Dynamics 憑證僅配置於 Gateway 中，產品進程完全接觸不到敏感憑證。
+*   **拒絕的替代方案**：
+    *   *方案 2（保留兩者）*：因目標框架不相容、憑證洩漏風險高、以及維護成本過大而拒絕。
+    *   *方案 3（延期 Embedded）*：因 Local Gateway 已能完美替代開發偵錯需求，保留 Embedded 只會增加架構不確定性而拒絕。
+*   **假設前提**：
+    *   Local Gateway 可在開發環境中作為 sidecar 進程或本地服務輕鬆啟動。
+*   **潛在副作用**：
+    *   引入了 localhost 的 HTTP 網路跳躍（約 1-2ms），但相對於 Dynamics 遠端呼叫的延遲（50-200ms），此開銷可忽略不計。
+
+### 3. 實施計劃 (Implementation Plan)
+
+#### 步驟 1：移除 Embedded 專案與引用
+- 從方案中刪除 `SpeechMessage.Dynamics.Embedded` 專案。
+- 移除 `ChurchReport` 等產品專案對 `SpeechMessage.Dynamics.Embedded` 的所有引用。
+- 移除第三方 Data8 WS-Trust 專案（`PowerPlatform.Dataverse.Client`）。
+
+#### 步驟 2：重構 Gateway 以支援 Local/Central 模式
+- 更新 `SpeechMessage.Dynamics.Gateway` 專案，使其運行於 .NET Framework 4.8（或相容的宿主環境），以載入官方的 `CrmServiceClient`。
+- 實作輕量化的本地啟動設定，使 Gateway 能在本地（Local Gateway）以開發人員的 Windows 整合驗證 (IWA) 或本地開發憑證運行。
+
+#### 步驟 3：更新產品組態
+- 統一產品的組態 Schema，僅保留 `Gateway` 節點。
+- 更新 `ChurchReport` 的 `appsettings.Development.json`，將 `Gateway.Endpoint` 指向 `http://localhost:5000/`（Local Gateway）。
+
+#### 步驟 4：設定 Visual Studio 2026 啟動工作流程
+- 在 VS 方案屬性中設定「多個啟動專案」：
+  1. `SpeechMessage.Dynamics.Gateway` (啟動)
+  2. `ChurchReport` (啟動)
+
+### 4. 考量事項 (Considerations)
+
+#### 效能 (Performance)
+- **連線複用**：Local/Central Gateway 維持對 Dynamics 的長壽命實體連線池。產品與 Gateway 之間則使用輕量化的 localhost HTTP 連線。
+- **中繼資料快取**：CSDL 中繼資料在 Gateway 層級進行快取，避免多個產品實例重複查詢 Dynamics。
+
+#### 安全性與隔離性 (Security & Isolation)
+- **憑證隔離**：開發人員無需在本地儲存生產環境 of Dynamics 憑證。Local Gateway 可透過 IWA 使用開發人員自身的 AD 帳號。
+- **Session 隔離**：Gateway 確保每個 HTTP 請求的 Dynamics 呼叫皆為無狀態，不共享使用者 Session。
+
+#### 可維護性 (Maintainability)
+- **單一程式庫**：產品團隊僅需維護 HTTP 用戶端合約。任何 Dynamics SDK 的升級或驗證機制變更皆隔離於 Gateway 專案內。
+- **Phase 4-6 移轉影響**：由於產品程式碼在開發與生產環境完全一致，移轉至 Phase 5/6 僅需部署 Central Gateway 並更新 DNS Endpoint，移轉風險極低。
