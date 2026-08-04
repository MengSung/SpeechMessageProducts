# Dynamics 365 連線管理規格書

> 版本：1.0　｜　日期：2026-08-04　｜　狀態：待審核
> 對應計劃書：`docs/dynamics-connection-management-plan.md`
> 取代：`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 中與本文衝突的段落

---

## 0. 目標與非目標

### 0.1 本規格要解決的問題

建立一個**集中管理 Dynamics 365 連線的機制**，讓多個產品在存取多個 Organization 時，能夠：

1. **不用每次重建 Connection** —— 連線池化，節省驗證握手與 metadata 取得的時間
2. **節省資源** —— 有界的連線數量，不隨產品數量線性成長
3. **用完一定歸還** —— 明確的借出／歸還生命週期

同時滿足三條不可妥協的底線：

| 底線 | 定義 |
|---|---|
| **無 Session Leakage** | 任何 Request、使用者、租戶、Token 或可變狀態，不得跨 Request 或跨產品共用 |
| **無 Memory Leakage** | 所有 Lease、Permit、Timer、Stream、Handle、Process、Task 與 Cancellation Registration 都有單一擁有者，且確定釋放 |
| **用完歸還** | 健康資源歸還 Pool；故障資源必須淘汰，不得再進 Pool |

### 0.2 非目標

- 本規格**不**要求移除 Data8。Data8 是永久保留的合法 Connector。
- 本規格**不**要求使用 SQL。SQL 只是分散式協調器的一種可選實作。
- 本規格**不**要求 Central Gateway。Central Gateway 是三種部署方式之一，不是強制。
- 本規格**不**涵蓋 Dynamics 業務邏輯設計；只涵蓋連線的取得、治理與釋放。

---

## 1. 三種部署方式（永久支援，非過渡）

`ConnectionMode` 決定**共用核心跑在哪個進程**，不決定**要不要治理**。三種模式的治理層完全相同。

| 模式 | 共用核心位置 | 網路跳躍 | 適用情境 |
|---|---|---|---|
| `Embedded` | 產品進程內 | 無 | 開發（VS 按 F5）、最低延遲、單一產品部署 |
| `DedicatedGateway` | 獨立進程，與單一產品一起部署 | HTTPS | 需要故障隔離但不需跨產品共用 |
| `CentralGateway` | 獨立服務，多產品共用 | HTTPS | 多產品、集中治理與資源重用 |

**規則 1.1**　三種模式都是正式部署選項。`Embedded` 不是「開發專用」，`DedicatedGateway` 也能上正式環境。

**規則 1.2**　`localhost` 位址只有在產品與 Dedicated Gateway 位於同一主機或同一網路命名空間時才成立。跨容器／跨主機必須使用內部服務位址。

**規則 1.3**　`Gateway.Endpoint` 是條件式設定：`Embedded` 模式下必須不存在或被忽略；其他兩種模式下必須是合法的絕對 HTTPS URI（無 user-info、無 query、無 fragment）。

---

## 2. 型別契約

### 2.1 列舉

```csharp
namespace SpeechMessage.Dynamics.Abstractions.Execution;

/// <summary>共用核心的部署位置。不影響治理行為。</summary>
public enum ConnectionMode
{
    Embedded = 0,
    DedicatedGateway = 1,
    CentralGateway = 2
}

/// <summary>傳輸實作的選擇。由 Profile 固定，不可由 Request 指定。</summary>
public enum ConnectorKind
{
    Data8 = 0,
    OfficialCrm82Worker = 1,
    OfficialCrm91Worker = 2
}

/// <summary>Dynamics CE 版本。</summary>
public enum CeVersion
{
    Ce82 = 0,
    Ce91 = 1
}
```

**規則 2.1**　`ConnectionMode` 與 `ConnectorKind` 是兩個獨立維度，任意組合皆合法（除非 §5.2 的相容性規則否決）。

### 2.2 產品端設定

```csharp
public sealed class ProductDynamicsOptions
{
    /// <summary>共用核心跑在哪裡。</summary>
    public ConnectionMode ConnectionMode { get; init; } = ConnectionMode.Embedded;

    /// <summary>唯一的產品選擇鍵。產品只能提供這個值。</summary>
    public string ProfileAlias { get; init; } = string.Empty;

    /// <summary>僅 DedicatedGateway / CentralGateway 模式使用。</summary>
    public GatewayEndpointOptions? Gateway { get; init; }
}

public sealed class GatewayEndpointOptions
{
    public string Endpoint { get; init; } = string.Empty;   // 絕對 HTTPS URI
    public string ApiPrefix { get; init; } = "/v1";
    public int MaxResponseBytes { get; init; } = 2_097_152;
    public int RequestTimeoutSeconds { get; init; } = 35;
}
```

**規則 2.2**　產品端設定**只有這三個欄位**。`OrganizationId`、`ConnectorKind`、`CredentialReference`、CRM endpoint 一律不得出現在產品設定中。

### 2.3 操作契約

```csharp
public interface IDynamicsOperationExecutor
{
    Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed class OperationExecutionRequest
{
    public string ProfileAlias { get; init; } = string.Empty;
    public string CapabilityOperationId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, OperationParameterValue> Parameters { get; init; }
        = new Dictionary<string, OperationParameterValue>();
    public string? IdempotencyKey { get; init; }
}
```

**規則 2.3**　`OperationExecutionRequest` 的公開表面**不得**包含 `OrganizationId`、`ConnectorKind`、`Credential`、`Endpoint`、FetchXML 文字、OData 查詢字串或任何 CRM schema 識別碼。這是編譯期保證，不是執行期檢查。

---

## 3. 統一操作邊界

三種 `ConnectionMode` 都必須通過同一組守門元件。

### 3.1 元件職責

| 元件 | Embedded | Dedicated / Central | 職責 |
|---|---|---|---|
| `EmbeddedHostAdapter` | ✔ | — | 以同進程方法呼叫共用核心，無 HTTP 跳躍 |
| `GatewayHttpsApi` | — | ✔ | 驗證呼叫者身分、限制要求大小與逾時 |
| `DynamicsOperationContract` | ✔ | ✔ | 只接受 ProfileAlias 與已註冊操作 |
| `RequestGuard` | ✔ | ✔ | 拒絕任何自訂 OrganizationId／ConnectorKind／Credential |

**規則 3.1**　`EmbeddedHostAdapter` 省略的只有 HTTP 傳輸；`DynamicsOperationContract` 與 `RequestGuard` 一個都不能少。

### 3.2 RequestGuard 檢查項

```csharp
public interface IRequestGuard
{
    RequestGuardResult Inspect(OperationExecutionRequest request, RequestOrigin origin);
}
```

必須拒絕的情形（fail closed）：

| # | 情形 | 錯誤碼 | HTTP |
|---|---|---|---|
| G1 | 參數名稱命中保留字（`organizationId`／`connectorKind`／`credential`／`endpoint`／`fetchXml`） | `request.reserved-parameter` | 400 |
| G2 | `ProfileAlias` 為空或超過 128 字元 | `request.invalid-profile-alias` | 400 |
| G3 | `CapabilityOperationId` 未註冊 | `operation.not-registered` | 403 |
| G4 | 呼叫者未通過身分驗證（Gateway 模式） | `caller.unauthenticated` | 401 |
| G5 | 呼叫者無權使用此 ProfileAlias | `caller.profile-forbidden` | 403 |
| G6 | 要求本體超過 `MaxRequestBytes` | `request.too-large` | 413 |

**規則 3.2**　G1～G6 必須在讀取要求本體、解析 Profile、取得 Permit 或建立任何連線之前完成。

---

## 4. Profile、Organization 與憑證

### 4.1 三份設定的分工

| 設定 | 內容 | 不得包含 |
|---|---|---|
| `DynamicsProfiles` | ProfileAlias → OrganizationAlias、CeVersion、ConnectorKind、CredentialReference、Pool／容量／逾時／重試政策 | 憑證值、Token |
| `OrganizationCatalog` | OrganizationAlias → FriendlyName、UniqueName、OrganizationId、State、ServiceUri | 憑證值、Token、ConnectorKind |
| `CredentialProvider` | CredentialReference → 實際憑證 | — |

**規則 4.1**　`OrganizationCatalog` 只保存**身分事實**。它是 `Get-CrmOrganization` 的落地結果，不含任何秘密。

**規則 4.2**　`CredentialProvider` 分環境實作：開發環境可讀 Configuration／User Secrets；正式環境必須接 Environment 或 Secret Store。Token 與 Credential 永遠不寫入 Catalog 或 Profile。

### 4.2 設定範例

```jsonc
// Gateway 端（或 Embedded 模式下的產品端）
"DynamicsProfiles": {
  "sunnyvalechback": {
    "OrganizationAlias": "sunnyvalechback",
    "CeVersion": "Ce91",
    "ConnectorKind": "Data8",
    "CredentialReference": "dynamics-sunnyvalechback",
    "Pool": {
      "MinSize": 3,
      "MaxSize": 20,
      "IdleTimeoutMinutes": 10,
      "AcquireTimeoutSeconds": 15,
      "HealthCheckOnAcquire": true
    },
    "Operation": {
      "TimeoutSeconds": 35,
      "MaxRetries": 2,
      "RetryBaseDelayMs": 200
    }
  }
},

"OrganizationCatalog": {
  "sunnyvalechback": {
    "FriendlyName": "聖谷行道會(公司研發)",
    "UniqueName": "sunnyvalechback",
    "OrganizationId": "bfb92ead-3705-f011-8143-00155d006608",
    "State": "Enabled",
    "ServiceUri": "https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc"
  },
  "elijah":        { "UniqueName": "elijah",        "OrganizationId": "2db26a2f-7f55-f111-8cb3-00155d007916", "State": "Enabled" },
  "david":         { "UniqueName": "david",         "OrganizationId": "37412ebe-db54-f111-8cb3-00155d007916", "State": "Enabled" },
  "solomon":       { "UniqueName": "solomon",       "OrganizationId": "6fed4bf7-a945-f111-8cb0-00155d007915", "State": "Enabled" },
  "speechmessage": { "UniqueName": "speechmessage", "OrganizationId": "80e1da32-96c8-4678-be37-9cf2cd0a8697", "State": "Enabled" }
}
```

### 4.3 Profile Resolver

```csharp
public interface IProfileResolver
{
    bool TryResolve(string profileAlias, out ResolvedProfile profile, out string error);
}

public sealed record ResolvedProfile(
    string ProfileAlias,
    string OrganizationAlias,
    Guid OrganizationId,
    CeVersion CeVersion,
    ConnectorKind ConnectorKind,
    string CredentialReference,
    PoolPolicy Pool,
    OperationPolicy Operation,
    long GenerationId);
```

必須 fail closed 的情形：

| # | 情形 | 錯誤碼 |
|---|---|---|
| R1 | ProfileAlias 不存在於 `DynamicsProfiles` | `profile.not-found` |
| R2 | 對應的 OrganizationAlias 不存在於 `OrganizationCatalog` | `organization.not-found` |
| R3 | Organization `State != Enabled` | `organization.disabled` |
| R4 | `OrganizationId` 為 `Guid.Empty` 或全 0／全 f 佔位值 | `organization.identity-placeholder` |
| R5 | CeVersion 與 ConnectorKind 不相容（見 §5.2） | `profile.connector-incompatible` |
| R6 | CredentialReference 無法解析 | `credential.unresolvable` |

**規則 4.3**　`ProfileAlias` 是**唯一的產品選擇鍵**。同一個 Organization 若要同時啟用不同 Connector，必須建立不同的 ProfileAlias（例如 `sunnyvalechback` 與 `sunnyvalechback-official`）。禁止在單一 Request 內切換 Connector。

---

## 5. Connector 路由

### 5.1 Router 契約

```csharp
public interface IConnectorRouter
{
    IConnectorPool Resolve(ResolvedProfile profile);
}
```

**規則 5.1**　`IConnectorRouter` 只讀 `ResolvedProfile.ConnectorKind`。它**不接受**任何 Request-time 參數、不做 fallback、不重試到另一個 Connector。

### 5.2 相容性矩陣

| ConnectorKind | Ce82 | Ce91 | 說明 |
|---|---|---|---|
| `Data8` | ✔ | ✔ | 同進程 WS-Trust，per-instance sdkVersion |
| `OfficialCrm82Worker` | ✔ | ✘ | net48 進程，鎖 XrmTooling 8.2.0.5 |
| `OfficialCrm91Worker` | ✘ | ✔ | net48 進程，鎖 XrmTooling 9.1.1.65 |

**規則 5.2**　不相容組合在 Profile 載入時（不是 Request 時）就必須拒絕，錯誤碼 `profile.connector-incompatible`。

---

## 6. 世代、容量與隔離治理

### 6.1 Profile Generation

**規則 6.1**　`ResolvedProfile` 一旦發布即不可變。任何設定變更必須建立**新世代**（`GenerationId + 1`）。

**規則 6.2**　同一 ProfileAlias 同時最多存在「1 個 Active ＋ 1 個 Draining」世代。第三個世代的建立請求必須先等前一個 Draining 收斂。

**規則 6.3**　舊世代停止接受新 Lease，但保留既有 Lease 直到歸零，然後才 Dispose 其 Pool 與所有連線物件。

### 6.2 Admission Control

```csharp
public interface IAdmissionController
{
    Task<IAdmissionPermit> AcquireAsync(
        AdmissionKey key, int envelopeBytes, CancellationToken ct);
}
```

**規則 6.4**　佇列必須有界（`LocalQueueCapacity`）。佇列滿時立即拒絕，錯誤碼 `admission.queue-full`，**不得無上限累積**。

**規則 6.5**　等待必須有逾時（`QueueAdmissionTimeoutSeconds`）。逾時拒絕，錯誤碼 `admission.timeout`。

**規則 6.6**　`IAdmissionPermit` 實作 `IDisposable`。Dispose 必定釋放容量，即使中途拋出例外。

### 6.3 Organization Capacity 與 Pool 的關係

**規則 6.7**　這是兩個不同的切分維度：

| 維度 | 鍵 | 意義 |
|---|---|---|
| **Organization Capacity** | `OrganizationId` | 同一個實體 Organization 的**聚合總預算**。不同 ProfileAlias 指向同一 Org 時共用同一份預算 |
| **實體 Pool** | `(ProfileAlias, GenerationId)` | 實際的連線物件容器。按世代隔離，防止不同 Connector／憑證／設定混用 |

### 6.4 Coordinator

**規則 6.8**　協調器分兩種實作：

| 實作 | 適用 | 是否必需 |
|---|---|---|
| `InMemoryCapacityCoordinator` | Embedded、DedicatedGateway、單節點 Central | **預設值** |
| `IDistributedCapacityCoordinator` | 多節點 Central Gateway | 僅在該情境需要 |

**規則 6.9**　**SQL 不是本架構的必要條件。** `SqlCapacityCoordinator` 只是 `IDistributedCapacityCoordinator` 的一種可替換實作。Embedded 與 Dedicated 模式下不得要求任何資料庫。

---

## 7. 借出、執行、歸還與故障清理

### 7.1 Lease 契約

```csharp
public interface IConnectorPool
{
    Task<IConnectorLease> AcquireAsync(CancellationToken ct);
}

public interface IConnectorLease : IAsyncDisposable
{
    IOrganizationService Service { get; }
    long GenerationId { get; }
    void MarkFaulted(Exception? cause);   // 標記後歸還時一律淘汰
}
```

### 7.2 生命週期（每一次操作都必須完整走完）

```
① 驗證      ProfileAlias、Organization State、版本相容性　→ 不合法即 Fail Closed
② 取得 Permit  有界等待 ＋ 逾時
③ 借出      從「當下 Active Generation」的 Pool 借出 Connection 或 Worker Slot
④ 執行      建立獨立 Request Context，呼叫 D365 Organization Service
⑤ 判定      結果與資源是否健康？
     健康  → 重設必要狀態，歸還原 Profile Generation 的 Pool
     故障／取消／逾時 → 標記失效並淘汰：Dispose 連線／終止 Worker
⑥ finally  釋放 Permit、清除 Cancellation Registration、清除 Request Context
```

**規則 7.1**　`③ 借出`必須在`② 取得 Permit`**之後**才解析 Active Generation。排隊期間不得持有任何連線物件、Runtime 或 Token Provider —— 否則舊世代無法收斂。

**規則 7.2**　`④ 執行`每次建立獨立的 Request Context。使用者身分、Token、Session **絕對不得**放進 Pool 或連線物件。

**規則 7.3**　故障資源**永遠不得**回到 Pool。`MarkFaulted` 後歸還一律走淘汰路徑。

**規則 7.4**　`⑥ finally` 必須無條件執行。即使 `③`～`⑤` 任一步拋出例外，Permit 與 Cancellation Registration 都要釋放；多個清理失敗必須彙總（`AggregateException`）回報，不得互相遮蔽。

**規則 7.5**　操作完成後不得留下 Process、Timer、Handle、Task 或 Cancellation Registration。

---

## 8. 錯誤碼總表

| 錯誤碼 | HTTP | 分類 | 可重試 |
|---|---|---|---|
| `request.reserved-parameter` | 400 | Guard | ✘ |
| `request.invalid-profile-alias` | 400 | Guard | ✘ |
| `request.too-large` | 413 | Guard | ✘ |
| `caller.unauthenticated` | 401 | Guard | ✘ |
| `caller.profile-forbidden` | 403 | Guard | ✘ |
| `operation.not-registered` | 403 | Guard | ✘ |
| `profile.not-found` | 404 | Resolver | ✘ |
| `organization.not-found` | 404 | Resolver | ✘ |
| `organization.disabled` | 409 | Resolver | ✘ |
| `organization.identity-placeholder` | 500 | Resolver | ✘ |
| `profile.connector-incompatible` | 500 | Resolver | ✘ |
| `credential.unresolvable` | 500 | Resolver | ✘ |
| `admission.queue-full` | 429 | Admission | ✔（含 Retry-After） |
| `admission.timeout` | 429 | Admission | ✔（含 Retry-After） |
| `pool.acquire-timeout` | 503 | Pool | ✔ |
| `connector.transport-failure` | 502 | Connector | ✔（有界） |
| `operation.timeout` | 504 | Execution | ✔（有界） |
| `operation.cancelled` | 499 | Execution | ✘ |

**規則 8.1**　所有錯誤回應**不得**包含 CRM hostname、Organization endpoint、GUID、CredentialReference、Token、Cookie 或連線字串。僅回傳錯誤碼、關聯 ID 與人類可讀但無敏感內容的訊息。

---

## 9. 監控與安全

### 9.1 必要指標

| 指標 | 標籤 | 用途 |
|---|---|---|
| `dynamics_profile_generation` | profileAlias, generationId, state | 世代收斂是否正常 |
| `dynamics_admission_wait_ms` | profileAlias（p50/p95/p99） | 排隊延遲 |
| `dynamics_admission_rejected_total` | profileAlias, reason | 拒絕原因分布 |
| `dynamics_pool_size` | profileAlias, generationId, state(idle/active) | 池使用率 |
| `dynamics_lease_acquire_ms` | profileAlias（p50/p95/p99） | 借出延遲 |
| `dynamics_lease_evicted_total` | profileAlias, reason | 淘汰率（洩漏的早期訊號） |
| `dynamics_worker_restart_total` | profileAlias, reason | Worker 回收（僅 Official） |
| `dynamics_operation_duration_ms` | profileAlias, operationId | 端到端延遲 |

### 9.2 遙測禁止事項

**規則 9.1**　Metrics、Trace、Log **一律不得**記錄 Credential、Token、Cookie、Session ID、使用者識別、LINE ID、CRM endpoint、Organization GUID 或完整敏感 Payload。

**規則 9.2**　Health Check 只輸出 ProfileAlias、GenerationId、狀態與有界的容量指標。

---

## 10. 測試要求

### 10.1 契約與守門

| 測試 | 驗證 |
|---|---|
| `RequestGuard_rejects_reserved_parameters` | G1 六個保留字全部拒絕 |
| `RequestGuard_rejects_before_body_read` | G1～G6 在讀本體前完成 |
| `Contract_has_no_crm_surface` | 反射檢查 `OperationExecutionRequest` 公開表面無 CRM 型別 |
| `Product_cannot_reference_connector_projects` | 產品專案不得直接參考 Connector 實作專案 |

### 10.2 Profile 解析

| 測試 | 驗證 |
|---|---|
| `Resolver_fails_closed_on_unknown_alias` | R1 |
| `Resolver_fails_closed_on_disabled_organization` | R3 |
| `Resolver_rejects_placeholder_organization_id` | R4（全 0 與全 f） |
| `Resolver_rejects_incompatible_connector` | R5（Official82 × Ce91 等組合） |

### 10.3 世代與容量

| 測試 | 驗證 |
|---|---|
| `Generation_at_most_one_active_and_one_draining` | 規則 6.2 |
| `Generation_old_pool_disposed_after_lease_zero` | 規則 6.3 |
| `Admission_queue_full_rejects_immediately` | 規則 6.4 |
| `Admission_permit_released_on_exception` | 規則 6.6 |
| `Permit_acquired_before_generation_resolved` | 規則 7.1 |
| `Capacity_shared_across_profiles_of_same_organization` | 規則 6.7 |
| `InMemory_coordinator_sufficient_without_database` | 規則 6.9 |

### 10.4 借還與洩漏

| 測試 | 驗證 |
|---|---|
| `Healthy_lease_returns_to_same_generation_pool` | 規則 7.3 反向 |
| `Faulted_lease_never_returns_to_pool` | 規則 7.3 |
| `Cancelled_operation_evicts_and_releases_permit` | 規則 7.4 |
| `Cleanup_failures_aggregate_without_masking` | 規則 7.4 |
| `No_process_timer_handle_task_left_after_operation` | 規則 7.5 |
| `Soak_repeated_acquire_release_no_monotonic_growth` | 記憶體／Handle／Thread 趨勢（沿用既有 soak 框架） |
| `Two_organizations_same_process_do_not_share_state` | 無 Session Leakage |
| `Ce82_and_Ce91_pools_coexist_in_one_process` | Data8 雙版本共存（見 §11.1） |

### 10.5 三模式等價性

| 測試 | 驗證 |
|---|---|
| `Embedded_and_Gateway_produce_identical_results` | 同一操作在兩種模式下結果一致 |
| `Embedded_enforces_same_guard_rules` | Embedded 不是繞過治理的後門 |
| `Embedded_requires_no_gateway_endpoint` | 規則 1.3 |

---

## 11. 已知技術風險與必要修正

### 11.1 Data8 `_sdkMajorVersion` 是 static

```csharp
// PowerPlatform.Dataverse.Client/OnPremiseClient.cs:69-70
private static readonly string _sdkVersion;
private static readonly int _sdkMajorVersion;   // ← 全進程共用
```

用於 `url + "?wsdl&sdkversion=" + _sdkMajorVersion`（WSDL 探索）。

**影響**：8.2 與 9.1 在同一進程共存時，兩者送出相同的 `sdkversion`。

**修正**：改為實例欄位，建構子可選傳入；`ResolvedProfile.CeVersion` 決定其值。

**驗證前提**：先以瀏覽器測試 `?wsdl&sdkversion=8` 與 `=9` 對 8.2 伺服器的回應。若兩者皆正常，此修正可降為選用。

### 11.2 Data8 `OnPremiseClient` 未實作 `IDisposable`

```csharp
public class OnPremiseClient : IOrganizationService   // 沒有 IDisposable
{
    var channel = client.ChannelFactory.CreateChannel();
    return channel;                                    // 從未關閉
}
```

**影響**：WCF channel 與 ChannelFactory 從未 `Close()`／`Abort()`。既有 `CrmConnectionPool` 中的 `(connection?.Service as IDisposable)?.Dispose()` 是 no-op。

**這是規則「無 Memory Leakage」目前唯一不成立的地方，屬必修項。**

**修正**：讓 `OnPremiseClient` 實作 `IDisposable`，保存 channel 與 factory 參考，Dispose 時依序 `Close()`（失敗則 `Abort()`）。

### 11.3 授權與維護責任

Data8 為 MIT 授權、原始碼 55 檔／6,919 行已在本 repo 內；上游最後發布為 2.4.2（2024-05-29），至今未有新版。**維護責任歸本專案**，此為採用 Data8 的既定代價。

---

## 12. 與現有程式碼的對應

| 規格元件 | 現有資產 | 動作 |
|---|---|---|
| `ConnectionMode` | `DynamicsExecutionMode`（Gateway/Embedded） | 擴充為三值並改名 |
| `ConnectorKind` | 無 | 新增 |
| `IDynamicsOperationExecutor` | `Abstractions/Operations/` | 保留 |
| `DynamicsOperationContract` | `Abstractions` + `ProductClient` | 保留 |
| `RequestGuard` | `Gateway/Security/*Authorizer` | 抽出並補齊 G1 |
| `IProfileResolver` | `ControlPlane/Runtime/DynamicsProfile*` | 重構為 Alias→Catalog 兩段解析 |
| `OrganizationCatalog` | 無 | 新增（`Get-CrmOrganization` 落地） |
| `IAdmissionController` | `ControlPlane/Capacity/OrganizationAdmissionManager` | 保留 |
| `Profile Generation Manager` | `ControlPlane/Runtime/DynamicsProfileRuntimeManager` | 保留 |
| `InMemoryCapacityCoordinator` | `ControlPlane/Capacity/InMemoryRuntimeHostSlotCoordinator` | 保留，升為預設 |
| `SqlCapacityCoordinator` | `ControlPlane/Capacity/SqlRuntimeHostSlotCoordinator` | 保留，降為可選 |
| `IConnectorRouter` | `ControlPlane/Runtime/ProfileRoutedOperationExecutor` | 重構 |
| `Data8ConnectorPool` | `ToolUtility/ConnectionOperations/CrmConnectionPool` | 抽出、世代化、修 §11.2 |
| `OfficialWorkerPool` | `WorkerSupervisor`＋`WorkerHost`＋`WorkerProtocol`＋兩個 Worker（63 檔） | 保留，接進 Router |
| `EmbeddedHostAdapter` | `Embedded/EmbeddedServiceCollectionExtensions`（目前必拋例外） | 重寫 |

---

## 13. 驗收條件

本規格視為實作完成，當且僅當：

1. 三種 `ConnectionMode` 對同一操作產生相同結果，且共用同一組 Guard／Resolver／Admission／Pool 程式碼
2. `ConnectorKind` 至少 `Data8` 一種實作通過全部 §10 測試；`OfficialCrm82Worker`／`OfficialCrm91Worker` 可作為擴充點保留
3. §10.4 全部通過，含 soak 無單調成長
4. §11.2 已修正並有測試佐證
5. Embedded 模式在 Visual Studio 2026 按 F5 可直接執行其受控離線組合根，無需額外進程、雜湊或資料庫；外部 CE
   執行由使用者在 P6 後整合驗收時安排
6. P6 程式與離線測試完成後，至少一個真實 Organization（`sunnyvalechback`）完成 legacy／Embedded／Dedicated
   端到端真機驗證、結果一致性及 p50／p95／p99 比較
7. §9.2 遙測禁止事項通過掃描

---

## 14. P4.1 組織 Catalog 登錄與選取

`CrmConnection:OrganizationCatalog` 是已知 CE Organization identity 的唯一設定來源。每個 key 是可由
`DynamicsAccess:ProfileAlias` 選取的固定 alias；entry 保存 FriendlyName、UniqueName、OrganizationId、
State、CeVersion 與（已知時）ServiceUri。產品 request、Session、controller 與 operation payload 不得攜帶這些欄位。

- 2026-08-04 已登錄 5 個 CE 9.1 與 27 個 CE 8.2 組織；Disabled 項目保留稽核身分，但禁止新 Profile 解析。
- `speechmessage` 同時存在於 CE 8.2／9.1，必須選 `speechmessage-ce82` 或 `speechmessage-ce91`，不可依名稱猜測版本。
- OrganizationId 與 CeVersion 不是連線授權。若 entry 尚無已核准的 HTTPS ServiceUri，mapper 必須在任何 permit、
  Data8 client、WCF channel 或 credential 使用前 fail closed；不得套用別的 profile 的 endpoint。
- Embedded factory 只採 Catalog 的 ServiceUri，完全不回讀舊 CrmConnection transport URI；因此改一個
  `ProfileAlias` 即可切換已完整配置的組織，也不會把 alias 已切換的 request 誤送至舊組織。
