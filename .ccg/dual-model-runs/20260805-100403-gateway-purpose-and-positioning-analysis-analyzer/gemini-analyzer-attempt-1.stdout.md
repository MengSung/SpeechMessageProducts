以下是針對 Dynamics Gateway 定位與 `ToolUtility` 架構關係的深度分析報告：

### 1. 已證實的現況

*   **產品端合約限制**：根據 `docs/dynamics-connection-management-spec.md`，產品端（如 `ChurchReport`）對 Dynamics 的配置僅暴露 `ConnectionMode`、`ProfileAlias` 以及選填的 `Gateway` 端點設定。產品端**不持有**也不配置 `OrganizationId`、`ConnectorKind`、CRM 實際端點、憑證或連線池參數。
*   **正交矩陣關係**：`ConnectionMode`（運行位置：`Embedded`、`DedicatedGateway`、`CentralGateway`）與 `ConnectorKind`（底層技術：`Data8`、`OfficialCrm82Worker`、`OfficialCrm91Worker`）是完全正交的兩個維度。例如，在 `Embedded` 模式下，底層可以使用 `Data8` 連接器；在 `DedicatedGateway` 模式下，也可以配置底層使用 `Data8` 或微軟官方 Worker。
*   **安全防護機制**：不論何種模式，執行前均會通過 `RequestGuard`。若請求參數中包含 `organizationId`、`connectorKind`、`credential`、`endpoint` 或 `fetchXml` 等保留字，將直接觸發 `400 Bad Request` 或 `403 Forbidden`（Fail Closed 政策）。
*   **ToolUtility 的現狀**：`ToolUtility` 內部強烈依賴 `IOrganizationService` 介面（例如 `ToolUtilityFacade` 的建構子直接接收 `IOrganizationService`）。目前 `ChurchReport` 的 `appsettings.json` 中，`ConnectionMode` 設為 `DedicatedGateway`，但 `Package01FeeReadsEnabled` 設為 `false`，這代表目前實際上仍是 100% 走舊有的 `WebServiceConnector -> ToolUtility -> Data8 -> D365` 嵌入式路徑，Gateway 尚未在生產環境中實際承接核心業務流量。

---

### 2. Gateway 真正存在的理由

Gateway 的存在並非單純為了「多一個轉發層」，而是為了解決以下核心架構痛點：

*   **進程與穩定性隔離**：微軟官方的 CRM SDK（特別是舊版 WCF/ChannelFactory）在 .NET 環境中極易發生記憶體洩漏、執行緒阻塞甚至導致整個進程崩潰。透過 Gateway（特別是配合獨立的 Worker 進程），可以將這些不穩定的 SDK 邏輯隔離在主產品進程之外。
*   **現代化架構解耦**：產品主程式（如 `ChurchReport`）可以升級至現代的 .NET 版本（如 .NET 10），而不需要在主程式中引用任何相容性極差的舊版微軟 CRM SDK 程式庫。SDK 的依賴被完全封裝在 Gateway 與 .NET Framework 4.8 Worker 中。
*   **集中式憑證與安全邊界**：在 `CentralGateway` 模式下，所有 Dynamics 租戶的敏感憑證（帳密、Client Secret）均集中管理於 Gateway 後端，產品端完全接觸不到憑證，消除了憑證外洩與跨租戶越權存取的風險。
*   **全域准入與流量控制**：當多個產品或多個租戶同時存取 Dynamics 時，Gateway 作為統一入口，能實施全域的連線池管理、並發限制（Backpressure）與流量調配，防止 Dynamics 因超載而拒絕服務。

---

### 3. 為何不是借連接器

使用者設想的「從 Gateway 借出連接器（如 `IOrganizationService` 或連線租約），再由產品直接存取 D365」在架構上是**不可行且被禁止的**：

*   **物理與進程限制**：`IOrganizationService` 是記憶體中的 .NET 對象，當 Gateway 與產品運行在不同進程（或不同伺服器）時，無法透過 HTTP 網路傳遞該對象的記憶體實例。
*   ** deliberate policy（故意為之的政策）**：為了確保 **Session 零洩漏（Zero Session Leakage）** 與 **憑證安全邊界**，Gateway 絕不向外借出底層連線。Gateway 的設計是「服務化」而非「連線池代理」。產品端只能發送標量參數的業務請求，由 Gateway 在其安全邊界內代為執行並返回結果。

---

### 4. ToolUtility 與 Gateway 的相容性結論

*   **結論**：**目前的 `ToolUtility` 無法直接切換使用 Gateway 作為後端。**
*   **型別合約不匹配（Type-Contract Mismatch）**：
    *   `ToolUtility` 內部的 142 個方法（如 `RetrieveMultiple`、`Create`、`Update` 等）全部是基於微軟 SDK 的 `IOrganizationService` 介面進行操作。
    *   Gateway 的對外合約（`IDynamicsOperationExecutor`）只接受 `OperationExecutionRequest` 並返回 `OperationExecutionResult`，**不暴露**任何 `IOrganizationService` 實例，且 `RequestGuard` 會主動封鎖任何傳入 `fetchXml` 或原始查詢的請求。
    *   因此，如果產品繼續使用現有的 `ToolUtility`，就只能選擇 `Embedded` 模式搭配 `Data8`（因為 Data8 的 `OnPremiseClient` 實作了 `IOrganizationService`），而無法切換到 `Gateway` 模式。

---

### 5. 未來產品的 2–3 種方案與取捨

若未來新產品需要類似 `ToolUtility` 的便利性，有以下三種可行設計：

#### 方案 A：任意通用 CRM 存取（Arbitrary Generic CRM Access）
*   **設計**：Gateway 開放通用的 CRUD 與 FetchXML 執行端點，產品端可自由傳送任意查詢與實體操作。
*   **評估**：
    *   *Session/租戶隔離*：**Critical**。極差。產品端若能發送任意 FetchXML，極易因程式碼漏洞或惡意注入導致跨租戶數據越權存取。
    *   *憑證邊界*：良好（憑證仍在 Gateway）。
    *   *資源清理*：中等。
    *   *效能與分配*：較差。任意查詢無法在 Gateway 端進行預編譯或優化，且容易產生大對象分配（LOH）。
    *   *API 演進*：極難。CRM Schema 的任何變更都會直接破壞產品端程式碼。
    *   *測試負擔*：極高。需要測試無限種查詢組合。
    *   *遷移成本*：最低。產品端幾乎不需改動即可直接調用。

#### 方案 B：註冊業務/能力操作（Registered Business/Capability Operations）
*   **設計**：產品端不能發送任意查詢。所有 CRM 操作必須先在 Gateway 的 `OperationRegistry` 中註冊為具體的「能力」（如 `read-fee-by-id`），產品端僅傳遞操作 ID 與參數，由 Gateway 內部安全執行。
*   **評估**：
    *   *Session/租戶隔離*：極佳。Gateway 可嚴格審查該操作是否符合當前租戶權限。
    *   *憑證邊界*：極佳。
    *   *資源清理*：極佳。Gateway 可針對特定操作進行精確的資源釋放與超時控制。
    *   *效能與分配*：極佳。可針對特定操作進行連線池優化與結果快取。
    *   *API 演進*：極佳。CRM 欄位變更只需在 Gateway 內部修改對應的註冊操作，產品端合約保持不變。
    *   *測試負擔*：低。只需針對已註冊的有限操作進行單元與整合測試。
    *   *遷移成本*：極高。必須將 `ToolUtility` 的 142 個方法逐一重構為註冊操作。

#### 方案 C：混合型具型別 SDK/外觀（Hybrid Typed SDK/Facade）
*   **設計**：提供一個產品端 SDK Facade，內部封裝分支邏輯。在 `Embedded` 模式下直接調用本地 `IOrganizationService`；在 `Gateway` 模式下，將強型別方法（如 `GetFee(id)`）自動轉換為對應的 Gateway 註冊操作請求。
*   **評估**：
    *   *Session/租戶隔離*：良好。
    *   *憑證邊界*：良好。
    *   *資源清理*：良好。
    *   *效能與分配*：中等（多了一層轉換開銷）。
    *   *API 演進*：良好。SDK Facade 作為緩衝層，隔離了底層傳輸細節。
    *   *測試負擔*：中等。需要同時測試本地與遠端兩種執行路徑。
    *   *遷移成本*：中等。產品端程式碼只需改為呼叫此 Facade，不需直接面對 HTTP 請求。

---

### 6. 建議方向

*   **推薦採用「方案 C（混合型具型別 Facade）」作為過渡，並以「方案 B（註冊業務操作）」為最終目標。**
*   **理由**：
    1.  方案 C 允許現有的 `ToolUtility` 逐步進行重構，將其方法封裝進 Facade 中，在不影響現有業務邏輯的前提下，逐步將底層的直接 SQL/FetchXML 查詢替換為向 Gateway 發送的註冊操作。
    2.  這能同時兼顧開發的便利性（產品端仍看得到強型別的 Facade 方法）與系統的安全性（底層不暴露任何 Raw Session 或連線租約，且完全符合 `RequestGuard` 的安全規範）。

---

### 7. 需要詢問使用者的一個最高價值問題

> **「我們是否準備好在未來的產品中，完全放棄『在前端/產品端編寫任意 FetchXML 與通用 CRUD 查詢』的自由度，轉而強制執行『所有 CRM 存取必須先在 Gateway 註冊為業務能力（Capability Operation）』的架構規範？」**

*   *說明*：這是決定未來產品架構走向的關鍵決策。如果答案是「是」，則未來的重構方向將是全面轉向能力註冊制；如果答案是「否」，則我們必須在 Gateway 上設計一套極其複雜且高風險的安全審查機制，以允許受限的通用查詢。

---

### 8. 證據（檔案與行號）

*   `docs/dynamics-connection-management-spec.md`：
    *   第 62-83 行：定義 `ConnectionMode`、`ConnectorKind` 與 `CeVersion` 的正交關係。
    *   第 116-131 行：定義 `IDynamicsOperationExecutor` 與 `OperationExecutionRequest` 的合約，證實不暴露 `IOrganizationService`。
    *   第 155-172 行：定義 `IRequestGuard` 及其 Fail Closed 規則（封鎖 `fetchXml` 等參數）。
*   `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`：
    *   第 10-37 行：說明 `ConnectionMode` 與 `ConnectorKind` 的永久部署選擇，以及 `RequestGuard` 的執行規範。
*   `ToolUtility/Core/ToolUtilityFacade.cs`：
    *   第 83-100 行：`ToolUtilityFacade` 的建構子強烈依賴 `IOrganizationService` 實例。
*   `ToolUtility/ConnectionOperations/CrmConnectionService.cs`：
    *   第 430-441 行：`CreateOnPremiseClient` 返回 `OnPremiseClient`（Data8 實作），該實作繼承自 `IOrganizationService`。
*   `SpeechMessageProducts.ChurchReport/appsettings.json`：
    *   第 590-601 行：配置 `ConnectionMode` 為 `DedicatedGateway`，但 `Package01FeeReadsEnabled` 設為 `false`。
