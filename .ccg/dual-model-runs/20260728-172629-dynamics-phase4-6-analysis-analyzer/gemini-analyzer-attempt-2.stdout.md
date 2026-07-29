# Dynamics Phase 4-6 實作與遷移架構分析報告

本報告由獨立資深安全、分散式系統、.NET 效能與遷移分析師提供，針對 Dynamics Phase 4 至 Phase 6 的無 SDK 存取計畫進行深度審查與設計規劃。

---

## 一、 關鍵發現與風險分類 (Critical / Warning / Info Findings)

### 1. Critical (發布阻礙與安全漏洞)

*   **C1: 呼叫端可控的 `WorkloadSubjectId` 導致多租戶/工作負載隔離失效**
    *   **檔案路徑**：`SpeechMessage.Dynamics.Gateway\Program.cs` (第 128-130 行)
    *   **符號**：`OperationHttpRequest.WorkloadSubjectId`
    *   **原因**：Gateway 目前直接信任並採用 HTTP 請求 Body 中傳入的 `WorkloadSubjectId`。任何能存取 Gateway 的呼叫者皆可任意偽造此識別碼，從而繞過准入配額限制、污染稽核日誌，甚至搶佔其他合法服務的佇列配額。這直接違反了 PRD 中「未經驗證/未對映的呼叫者必須在進入准入控制/CRM 前被拒絕」的硬性安全邊界。
*   **C2: 缺乏持久化分散式協調器 (`RequireDurableHostCoordinator` 為死開關)**
    *   **檔案路徑**：`SpeechMessage.Dynamics.Gateway\Program.cs` (第 96 行) 及 `SpeechMessage.Dynamics.Embedded\DependencyInjection\EmbeddedServiceCollectionExtensions.cs` (第 142 行)
    *   **符號**：`options.Admission.RequireDurableHostCoordinator = false`
    *   **原因**：目前系統中唯一實作的協調器為 `InMemoryRuntimeHostSlotCoordinator` (`IsDurable => false`)。在多副本（Gateway 叢集）或混合 Embedded 部署下，各進程無法共享准入狀態，導致 Dynamics 365 的總體併發限制（Aggregate Max In-Flight）極易被成倍突破。若將 `RequireDurableHostCoordinator` 設為 `true`，系統將因無 durable 實作而直接崩潰。
*   **C3: 殘留硬編碼 Dynamics 憑證與 SOAP 連線池**
    *   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Startup.cs` (第 302-353 行)
    *   **符號**：`ICrmConnectionPool` 註冊與 `SPEECHMESSAGE\Administrator` 預設帳號
    *   **原因**：ChurchReport 啟動時仍會註冊舊版 SOAP 連線池，且包含硬編碼的網域管理員帳號與預設 WCF 終端節點。這與 Phase 6「移除所有 WCF/SOAP 連線池與 Fallback 憑證」的目標衝突，且存在憑證洩漏風險。

### 2. Warning (效能與資源洩漏隱患)

*   **W1: `GatewayHttpClientFactory` 靜態字典無上限且無法處置**
    *   **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\Gateway\GatewayHttpClientFactory.cs` (第 24-25, 49-70 行)
    *   **符號**：`GatewayHttpClientFactory.Clients` (`ConcurrentDictionary<string, HttpClient>`)
    *   **原因**：該工廠使用靜態的 `ConcurrentDictionary` 快取 `HttpClient` 實例。當 Gateway 終端節點配置變更或進行熱重載（Reload）時，舊的 `HttpClient` 與底層的 `SocketsHttpHandler` 無法被主動 `Dispose`，將導致 Socket 殘留與記憶體洩漏。
*   **W2: 舊版 SDK DLL 依賴與 HintPath 殘留**
    *   **檔案路徑**：`SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj` (第 108 行)
    *   **符號**：`Microsoft.Crm.Sdk.Proxy` 參照與外部 HintPath
    *   **原因**：專案檔中仍包含指向外部資料夾的 SDK DLL 參照，這會繞過標準 NuGet 稽核，且在建置環境中可能引入不一致的二進位檔案。

### 3. Info (架構一致性建議)

*   **I1: 測試覆蓋率與真實環境不對等**
    *   **檔案路徑**：`SpeechMessage.Dynamics.SmokeTests\LiveDynamicsWebApiSmokeTests.cs`
    *   **原因**：目前的 Smoke 測試主要在 CE 9.1 上執行 v8.2 路由，這並不能完全代表 CE 8.2 真實伺服器的行為（例如 OData 查詢語法差異、ADFS 驗證挑戰）。應確保有獨立的 8.2 與 9.1 真機測試環境。

---

## 二、 Phase 4 至 Phase 6 安全 TDD 實施順序與依賴關係

為確保系統在不破壞現有功能的狀況下安全演進，必須遵循以下嚴格的依賴順序進行測試驅動開發（TDD）：

```
[Phase 4: 基礎設施硬化]
  │
  ├── 1. 實作 SQL 租約控制平面 (SpeechMessageDynamicsControlPlane)
  ├── 2. 實作 SqlRuntimeHostSlotCoordinator (IsDurable => true)
  ├── 3. 實作 Windows Negotiate 驗證與伺服器端 WorkloadSubjectId 對映
  └── 4. 撰寫多主機併發與租約過期隔離區 (Quarantine) 容錯測試 (TDD Red/Green)
  │
[Phase 5: 漸進式遷移]
  │
  ├── 5. 啟用 Feature Flag (Package01FeeReadsEnabled => true)
  ├── 6. 遷移第一個唯讀流量 (ChurchReport Donation Fee Reads) 至 Web API 管道
  └── 7. 驗證雙管道並行與回滾機制 (Rollback Proof)
  │
[Phase 6: SDK 徹底清除]
  │
  ├── 8. 移除 ChurchReport 與 ToolUtility 中的 ICrmConnectionPool 及 WCF 參照
  ├── 9. 刪除 PowerPlatform.Dataverse.Client 專案
  └── 10. 將 Verify-NoDynamicsSdk.ps1 提升為 CI/CD 唯讀強制阻斷閘門 (FailOnFindings)
```

---

## 三、 SQL Server 分散式租約控制平面設計

為實現跨多個 Gateway 與 Embedded 實例的 aggregate 准入控制，設計獨立的控制平面資料庫 `SpeechMessageDynamicsControlPlane`。**此資料庫嚴禁與 Dynamics 組織資料庫或 MSCRM_CONFIG 部署於同一 Schema，且不得修改任何 Dynamics 原生資料。**

### 1. 資料表 Schema 設計

```sql
-- 建立租約管理表
CREATE TABLE dbo.RuntimeHostLeases (
    LeaseNamespace VARCHAR(128) NOT NULL,
    HostInstanceId VARCHAR(128) NOT NULL,
    FencingToken BIGINT NOT NULL,
    ExpiresAtUtc DATETIME2(3) NOT NULL,
    QuarantinedUntilUtc DATETIME2(3) NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_RuntimeHostLeases_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_RuntimeHostLeases_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_RuntimeHostLeases PRIMARY KEY CLUSTERED (LeaseNamespace, HostInstanceId)
);

CREATE NONCLUSTERED INDEX IX_RuntimeHostLeases_Namespace_Expires 
ON dbo.RuntimeHostLeases (LeaseNamespace, ExpiresAtUtc);
```

### 2. 原子化租約獲取 (TryAcquire) 交易邏輯

使用 `SERIALIZABLE` 隔離層級或 `UPDLOCK, HOLDLOCK` 確保 Check-then-Act 的原子性，防止超額核發租約：

```sql
-- 傳入參數: @LeaseNamespace, @HostInstanceId, @MaxHosts, @LeaseTtlMs
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;

DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
DECLARE @ActiveCount INT = 0;
DECLARE @NewFencingToken BIGINT = 0;

-- 1. 清理與統計當前 Namespace 下未過期且未在隔離區的活躍租約數
SELECT @ActiveCount = COUNT(*)
FROM dbo.RuntimeHostLeases WITH (UPDLOCK, HOLDLOCK)
WHERE LeaseNamespace = @LeaseNamespace
  AND (ExpiresAtUtc > @Now OR QuarantinedUntilUtc > @Now)
  AND HostInstanceId <> @HostInstanceId;

-- 2. 檢查是否已達最大主機上限
IF @ActiveCount >= @MaxHosts
BEGIN
    ROLLBACK TRANSACTION;
    -- 回傳空結果，表示獲取失敗
    SELECT NULL AS FencingToken;
    RETURN;
END;

-- 3. 產生單調遞增的 Fencing Token (可使用 Sequence 或時間戳/自增值)
-- 這裡使用基於 Unix 時間戳微秒的單調值作為 Fencing Token
SET @NewFencingToken = CAST(DATEDIFF_BIG(MICROSECOND, '1970-01-01', SYSUTCDATETIME()) AS BIGINT);

-- 4. 插入或更新租約
DECLARE @ExpiresAtUtc DATETIME2(3) = DATEADD(MILLISECOND, @LeaseTtlMs, @Now);

MERGE dbo.RuntimeHostLeases WITH (HOLDLOCK) AS Target
USING (SELECT @LeaseNamespace AS LeaseNamespace, @HostInstanceId AS HostInstanceId) AS Source
ON (Target.LeaseNamespace = Source.LeaseNamespace AND Target.HostInstanceId = Source.HostInstanceId)
WHEN MATCHED AND (Target.ExpiresAtUtc <= @Now OR Target.FencingToken < @NewFencingToken) THEN
    UPDATE SET 
        FencingToken = @NewFencingToken,
        ExpiresAtUtc = @ExpiresAtUtc,
        UpdatedAtUtc = @Now
WHEN NOT MATCHED THEN
    INSERT (LeaseNamespace, HostInstanceId, FencingToken, ExpiresAtUtc)
    VALUES (Source.LeaseNamespace, Source.HostInstanceId, @NewFencingToken, @ExpiresAtUtc);

COMMIT TRANSACTION;

-- 回傳成功獲取的租約資訊
SELECT @NewFencingToken AS FencingToken, @ExpiresAtUtc AS ExpiresAtUtc;
```

### 3. 租約續期 (TryRenew) 與過期隔離區 (Quarantine) 行為

*   **續期防線**：續期時必須比對 `FencingToken`。若資料庫中的 Token 已大於當前持有值，說明該租約已被其他接替主機搶佔，續期必須失敗並**立即關閉（Fail-Closed）**該主機的准入通道。
*   **隔離區機制 (Quarantine)**：當主機異常離線或租約過期時，該 `HostInstanceId` 釋放的配額在 `ExpiresAtUtc + SettlementMargin`（例如 5 秒）內不得分配給新主機，以防止時鐘偏移或未完成的 Outbound 請求造成併發超載。

---

## 四、 Gateway 驗證與授權設計 (防租戶/工作負載洩漏)

為徹底解決 `WorkloadSubjectId` 可被呼叫端任意提交的問題，必須引入基於 Windows 整合驗證（Negotiate/Kerberos）的伺服器端對映機制：

```
[Product Client] 
   │ (Negotiate / Windows Workload Identity)
   ▼
[Gateway IIS / Kestrel]
   │ 1. 驗證 Windows Principal (HttpContext.User)
   │ 2. 提取 Windows 帳號名稱 (例如: SPEECHMESSAGE\ChurchReportSvc)
   │ 3. 伺服器端唯讀對映表 (Principal -> WorkloadSubjectId)
   ▼
[Workload Mapping Layer]
   │ 成功 -> 綁定 WorkloadSubjectId = "church-report-service"
   │ 失敗 -> 立即回傳 403 Forbidden (不進入准入佇列)
   ▼
[Admission & CRM Dispatch]
```

### 安全防護要點：
1.  **拒絕 Body 傳入識別碼**：`OperationHttpRequest` 移除 `WorkloadSubjectId` 欄位。若 Body 中包含此屬性，JSON 解析器（設定為 `MissingMemberHandling.Error`）應直接拋出 400 Bad Request。
2.  **隔離區與指標脫敏**：`WorkloadSubjectId` 在寫入 Prometheus 指標或 NLog 稽核日誌前，必須經過標準化與雜湊處理（例如 `SHA256(WorkloadSubjectId + Salt)`），嚴禁將原始 Principal 名稱、Token 或 Session Cookie 寫入任何外部儲存體。

---

## 五、 HttpClient 與 Handler 生命週期管理與防洩漏設計

為消除 `GatewayHttpClientFactory` 的靜態快取危害，改用 .NET 推薦的 `IHttpClientFactory` 結合具名客戶端（Named Client）管理生命週期：

### 1. 註冊與配置 (Startup.cs)

```csharp
services.AddHttpClient("DynamicsGatewayClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.ExpectContinue = false;
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    UseCookies = false,             -- 徹底防止 Cookie 跨請求洩漏
    AllowAutoRedirect = false,      -- 停用自動重導向，防止 SSRF
    PreAuthenticate = false,        -- 停用預身分驗證，改由 Request 級別 Authorization 標頭控制
    PooledConnectionLifetime = TimeSpan.FromMinutes(5), -- 定期回收連線以響應 DNS 變更
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    MaxConnectionsPerServer = 16    -- 限制單一伺服器最大連線數
});
```

### 2. 避免 Task/Timer 記憶體洩漏的處置設計
*   **避免非同步洩漏**：在 `AdfsOAuthTokenProvider` 中，獲取 Token 的非同步工作必須綁定 `CancellationToken`。當 Gateway 停止或 Profile 卸載時，必須觸發 Token 取消，防止未完成的 `Task` 續接（Continuation）持有已釋放的服務參照。
*   **清除快取與 Timer**：任何用於快取 Metadata 或 Token 的 `MemoryCache`，必須在 Profile Generation 變更時呼叫 `Dispose()`。

---

## 六、 SDK/WCF/SOAP 消費者盤點與 Phase 6 移除順序

根據 `no-sdk-source-roots.json` 與 `phase0-organization-call-matrix.json` 的盤點，剩餘的舊版 SDK 消費者與移除順序規劃如下：

### 1. 盤點清單與遷移風險

| 專案 / 檔案路徑 | 依賴型別 / 參照 | 遷移風險 | 遷移策略 |
| :--- | :--- | :--- | :--- |
| `PowerPlatform.Dataverse.Client` | `IOrganizationService`, WS-Trust, WCF | **極高** (底層為 SOAP 協定) | Phase 6 徹底刪除此專案，不留任何程式碼。 |
| `ToolUtility\ConnectionOperations\CrmConnectionPool.cs` | `OnPremiseClient`, WCF Channel | **高** (影響所有舊版資料庫存取) | 逐步將其方法替換為 `IDynamicsOperationExecutor` 呼叫。 |
| `SpeechMessageProducts.ChurchReport\Startup.cs` | `ICrmConnectionPool` 註冊 | **中** (啟動期依賴) | 移除註冊，改為註冊 `DynamicsAccess` 閘道客戶端。 |

### 2. Phase 6 移除與清理順序

1.  **步驟一**：完成 `ChurchReport` 中 `DonationFeeQueryService` 的 Web API 遷移（Phase 5 唯讀驗證）。
2.  **步驟二**：將 `ToolUtility` 中所有與 `IOrganizationService` 相關的 Query 類別（如 `FetchXmlQueryService`、`EntityQueryService`）重構，改為呼叫 `IDynamicsOperationExecutor`。
3.  **步驟三**：從 `SpeechMessageProducts.ChurchReport.csproj` 中移除 `Microsoft.Crm.Sdk.Proxy` 的 DLL 參照。
4.  **步驟四**：刪除整個 `PowerPlatform.Dataverse.Client` 專案目錄，並自 `SpeechMessageProducts.sln` 中移除。
5.  **步驟五**：修改 `eng/no-sdk-source-roots.json`，將 `mode` 改為 `"failing-gate"`，並在 CI 流程中將 `Verify-NoDynamicsSdk.ps1` 設為強制阻斷關卡。

---

## 七、 驗證缺口與誠實性宣告指標

若要在評審會議中誠實地宣告 Phase 4、5、6 的完成，必須消除以下驗證缺口：

1.  **缺口一：缺乏真實的 CE 8.2 環境驗證**
    *   *不誠實行為*：僅在 CE 9.1 伺服器上使用 `/api/data/v8.2/` 路由通過測試，即宣稱相容 8.2。
    *   *驗證要求*：必須在獨立的實體/VM `D365APP01` (安裝 Dynamics 8.2) 上執行完整的 Smoke 測試套件，並取得包含 `WhoAmI` 組織 GUID 的真實回應。
2.  **缺口二：未進行多主機併發與時鐘偏移測試**
    *   *不誠實行為*：僅在單機使用 `InMemoryRuntimeHostSlotCoordinator` 通過測試。
    *   *驗證要求*：必須模擬兩台 Gateway 主機，在資料庫時鐘存在 $\pm 1$ 秒偏移的狀況下，驗證 `SqlRuntimeHostSlotCoordinator` 能精確鎖定最大主機數，且過期主機被正確隔離（Quarantine）。
3.  **缺口三：未驗證 Feature Flag 回滾 (Rollback) 效能**
    *   *不誠實行為*：啟用 `Package01FeeReadsEnabled` 後，未在生產負載下測試關閉該 Flag 時的連線釋放速度。
    *   *驗證要求*：執行壓測並在途中將 Flag 設為 `false`，斷言所有發往 Web API 管道的請求立即中斷，且舊版 SOAP 管道能無縫接管流量，無 Socket 殘留。
