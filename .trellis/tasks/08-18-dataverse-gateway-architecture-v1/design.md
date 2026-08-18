# 技術設計：產品 A 完整實現 Dataverse 連線架構圖（v1）

對應 `prd.md`。執行順序見 `implement.md`。

## 1. 核心槓桿：`IOrganizationService` 就是現成的接縫

這是整個設計能收斂的原因，也是與上一輪最大的不同。

`ToolUtilityFacade` 的 19 個 lazy 子服務全部長這樣（`ToolUtilityFacade.cs:148-168`）：

```csharp
_queryService = new Lazy<IEntityQueryService>(() => new EntityQueryService(_logger, _organizationService));
_crudService  = new Lazy<IEntityCrudService>(() => new EntityCrudService(_logger, _organizationService));
// ... 共 19 個
```

也就是說，**整個 ToolUtility 體系對外部世界的唯一依賴，就是一個 `IOrganizationService`**。

所以我們不去改上層，而是**換掉那個 `IOrganizationService` 是什麼**：

```
今天：  ToolUtilityClass ← PooledOrganizationService（建構時租一條，整個 request 佔著）
目標：  ToolUtilityClass ← GatewayOrganizationService（自己不持有連線，每次操作跟 Gateway 借）
```

換掉之後：

| 不需要改的東西 | 數量 |
|---|---|
| `ToolUtilityClass` 公開 API 與其呼叫點 | 3126 次 |
| `ToolUtilityFacade` 與 19 個子服務 | 0 檔 |
| `m_Crm2011OrganizationService` 的參照 | 52 處 |
| `TraceByLevel` 呼叫點 | 160 個 |
| 注入 `ICrmConnectionPool` 的 Controller | 16 個 |

**這就是把 per-request 改成 per-operation 的全部代價。**

## 2. 目標架構

```
┌──────────────────────────────────────────────────────────────┐
│ Scoped（每個 HTTP request 一份）                              │
│                                                               │
│  Controller / Service / ViewModel                             │
│      └─ ToolUtilityClass          ← 公開 API 不變             │
│           └─ ToolUtilityFacade + 19 子服務   ← 完全不動        │
│                └─ IOrganizationService                        │
│                     = GatewayOrganizationService  ★新         │
│                          └─ IDataverseGateway     ★新         │
│                               · Execute / Execute<T>          │
│                               · Reentrant depth 計數           │
│                               · 自己不持有 client               │
└──────────────────────────────────────────────────────────────┘
                              │ 每次操作 Acquire / Return
┌──────────────────────────────────────────────────────────────┐
│ Singleton（每個 Worker Process 一份）                          │
│                                                               │
│  IDataverseConnectionManager   ★新                            │
│    · 唯一入口，應用程式看不到 raw client                        │
│    · 解析 Pool Key（Product+Env+OrgUrl+EffectiveIdentity）     │
│    · Create / Health(WhoAmI) / Fault / Dispose                │
│    · Timeout / Metrics / Shutdown Cleanup                     │
│         └─ IBoundedClientPool（Keyed）  ★新                    │
│              · 每個 Key 一個子池（今天恆 1）                    │
│              · MinSize / MaxN / AcquireTimeout                 │
│                / IdleTimeout / HealthInterval                  │
│              · PooledClient 狀態機                             │
│                Idle → Leased → Faulted → Disposed             │
│                   └─ IClientLease  ★新                         │
│                        └─ OnPremiseClient（既有）              │
│                                                               │
│  IToolUtilityTracer（既有）                                    │
└──────────────────────────────────────────────────────────────┘
```

★新 = 本任務建立。其餘沿用。

## 3. 型別契約

全部放在 **`ToolUtility` 組件**（`ToolUtility/Dataverse/`），不得寫進 ChurchReport ——
產品 B / C / D 要能直接引用。

### 3.1 Pool Key（圖 ⑤ / ⑩）

```csharp
/// 池化連線的分割鍵。相同鍵的 client 可安全互換；不同鍵絕不共用。
public readonly struct DataverseConnectionKey : IEquatable<DataverseConnectionKey>
{
    public string Product { get; }            // "ChurchReport" / "HappyGroup20" / ...
    public string Environment { get; }        // "prod" / "test"
    public string OrganizationUrl { get; }
    public string EffectiveIdentity { get; }  // 今天恆為服務帳號；未來為 impersonation 對象
}
```

`EffectiveIdentity` 今天由組態的服務帳號填入，因此**恆產生同一個 key → 恆 1 個子池**。
key 結構今天就存在，未來開 impersonation 時只換填入的值，不動池的程式碼。

### 3.2 Lease（圖 ③）

```csharp
public interface IClientLease : IDisposable
{
    IOrganizationService Service { get; }
    void MarkFaulted();
}
```

三條鐵則由實作保證，並各有一個測試：

1. **只接受自己建立的 client** —— lease 持有建立它的池的 token，歸還時比對
2. **拒絕重複釋放** —— `Dispose()` 冪等，第二次是 no-op（不是擲例外，避免 finally 連鎖）
3. **拒絕並行共用同一條 client** —— client 離開池即標記 Leased，池中不再存在該實例

### 3.3 Client 狀態機（圖 ④）

```
        建立
          │
          ▼
      ┌───────┐  TryRent   ┌────────┐
      │ Idle  │ ─────────► │ Leased │
      └───────┘            └────────┘
          │  ▲                 │   │
   IdleTimeout │  Return(Healthy)│   │ MarkFaulted
   / Shutdown  └─────────────────┘   │
          │                          ▼
          │                     ┌─────────┐
          └────────────────────►│ Disposed│◄──── Faulted 一律不回池
                                └─────────┘
```

健康檢查：`Idle` 超過 `HealthInterval` 未驗證者，出借前以 `WhoAmI` 驗證；失敗即 `Disposed`。

### 3.4 Bounded Pool（圖 ④ / ⑧）

```csharp
public interface IBoundedClientPool : IDisposable
{
    IClientLease Acquire(DataverseConnectionKey key, CancellationToken ct = default);
    DataversePoolMetrics GetMetrics();
}
```

內部：`ConcurrentDictionary<DataverseConnectionKey, SubPool>`，每個 `SubPool` 各自持有
`SemaphoreSlim(MaxN)`、idle 集合、計數。

### 3.5 Connection Manager（圖 ③ Singleton 那格）

```csharp
public interface IDataverseConnectionManager
{
    IClientLease Acquire(CancellationToken ct = default);   // key 由 Manager 自己解析
    DataversePoolMetrics GetMetrics();
}
```

Manager 是**唯一**知道怎麼組出 key、怎麼建 client 的地方。它把 `IBoundedClientPool` 包起來，
應用程式拿不到 raw client，只拿得到 `IClientLease.Service`（而那是池化 client 的介面）。

### 3.6 Gateway（圖 ②／⑥／⑦）

```csharp
public interface IDataverseGateway
{
    void Execute(Action<IOrganizationService> work);
    T Execute<T>(Func<IOrganizationService, T> work);
}
```

Scoped 實作，含 reentrant 深度計數：

```csharp
internal sealed class DataverseGateway : IDataverseGateway, IDisposable
{
    private readonly IDataverseConnectionManager _manager;
    private IClientLease _lease;
    private int _depth;

    public T Execute<T>(Func<IOrganizationService, T> work)
    {
        if (_depth++ == 0) _lease = _manager.Acquire();
        try     { return work(_lease.Service); }
        catch   { _lease.MarkFaulted(); throw; }
        finally { if (--_depth == 0) { _lease.Dispose(); _lease = null; } }
    }
}
```

`_depth` 是實例欄位，而 Gateway 是 Scoped → **計數天然不跨 request**（圖 ⑥ 的要求）。

### 3.7 Gateway 支撐的 `IOrganizationService`（本設計的接縫）

```csharp
internal sealed class GatewayOrganizationService : IOrganizationService
{
    private readonly IDataverseGateway _gateway;

    public Entity Retrieve(string n, Guid id, ColumnSet cs)
        => _gateway.Execute(svc => svc.Retrieve(n, id, cs));
    // Associate / Create / Delete / Disassociate / Execute / RetrieveMultiple / Update 同理
}
```

**它自己不持有任何連線**，因此被誰持有多久都無所謂 —— 這正是解決 R17 的關鍵性質。

## 4. DI 註冊

```csharp
// ToolUtility 組件內的 ServiceCollectionExtensions
services.AddSingleton<IBoundedClientPool, BoundedClientPool>();
services.AddSingleton<IDataverseConnectionManager, DataverseConnectionManager>();
services.AddScoped<IDataverseGateway, DataverseGateway>();
services.AddScoped<IOrganizationService, GatewayOrganizationService>();   // 取代 PooledOrganizationService
services.AddScoped<ToolUtilityClass>(sp => new ToolUtilityClass(...));    // 不變
services.AddScoped<IToolUtilityProvider, ToolUtilityProvider>();          // 不變
services.AddSingleton<ICrmConnectionPool>(sp => new ConnectionPoolStatsAdapter(
    sp.GetRequiredService<IDataverseConnectionManager>()));               // 只為 F4 的 GetStats()
```

最後一行是相容層：`BaseChurchController.cs:1063` 唯一在用的是 `GetStats()`，
用一個薄 adapter 接到新的 Metrics，**16 個 Controller 建構式因此零改動**。

## 5. 五個參數（圖 ⑧）

沿用既有 `CrmConnection` 組態區段並補齊：

| 參數 | 預設 | 依據 |
|---|---|---|
| `MinSize` | 3 | 至少 1，避開冷啟動 |
| `MaxN` | 20 | Little's Law：尖峰併發 × 平均持有時間，再留 5～10 倍餘裕 |
| `AcquireTimeout` | 30s | 必須小於前端／反向代理逾時 |
| `IdleTimeout` | 10min | 回收閒置，但不抖動 |
| `HealthInterval` | 5min | 僅在出借前檢查，不背景輪詢 |

`per-operation` 之後，client 被持有的時間從「整個 request（約 125ms）」降到「單次 CRM 操作（約 120ms 內的一段）」，
因此 `MaxN` 的需求會**下降**而非上升（圖 ⑦ 的表格）。既有預設值維持即可，不需調大。

## 6. 淘汰 `PooledOrganizationService` 與 `CrmConnectionPool`

- `PooledOrganizationService` → 由 `GatewayOrganizationService` 取代，**刪除**。
- `CrmConnectionPool`（586 行）→ 其能力（semaphore、idle cleanup、health、stats）
  移植進 `BoundedClientPool` 並加上 keyed 與顯式狀態機，**刪除**。
- `ICrmConnectionPool` → **保留介面**，改由薄 adapter 實作（見 §4），只為 `GetStats()`。

## 7. 那 20 個 legacy 持有者怎麼辦（R17 的解法）

問題：`ToolUtilityFactory.GetInstance()` 回傳程序級單例，而它被 13 個 session 鍵快取
（30 分鐘）持有 —— 這是上一輪讓任務不收斂的東西。

**解法：不動快取，改變那個單例持有的 `IOrganizationService` 是什麼。**

```csharp
/// 解析「當前 request scope」的 gateway；自己不持有任何連線與 scope。
internal sealed class AmbientGatewayOrganizationService : IOrganizationService
{
    private readonly IHttpContextAccessor _http;
    private readonly IServiceScopeFactory _scopeFactory;

    private T Run<T>(Func<IOrganizationService, T> work)
    {
        var rs = _http.HttpContext?.RequestServices;
        if (rs != null)
            return rs.GetRequiredService<IDataverseGateway>().Execute(work);

        // 背景執行緒沒有 HttpContext：自建短生命週期 scope，用完即釋放
        using var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDataverseGateway>().Execute(work);
    }
}
```

`ToolUtilityFactory` 建立單例時注入這個實例。於是：

- 那 20 個持有者持有的是「一個不持有連線的代理」→ 被快取 30 分鐘也無害
- 每次操作解析到的都是**當前** request 的 gateway → 絕不跨 request 共用 client
- 13 個 session 快取一行都不用改 → 本任務因此有界

**這是刻意採用的 service locator，範圍限縮在一個類別。**
在註解中明確記載：它是 legacy 持有者的過渡橋樑，待 session 快取重新設計後可移除。

## 8. 圖 ⑨ 三條技術債的清除方式

| 圖上的債 | 清除方式 |
|---|---|
| `ToolUtilityClass` 是程序級 singleton | 已於前置任務清除（Scoped） |
| `public IOrganizationService m_Crm2011OrganizationService` 公開 raw client | 欄位保留（52 處參照不動），但**指向 gateway 代理**，代理不持有連線 → 應用程式再也拿不到 raw client |
| `CreateOnPremiseClient()` 繞過建構 | `ToolUtilityClass` 的 `InitializeCrmConnection()` 與 legacy 自建建構式**整個刪除**；建立 client 的唯一位置變成 Manager |

另外依 F3 刪除恆為 null 的 `m_OrganizationService` 欄位與其 24 處死分支。

## 9. 風險與緩解

| 風險 | 影響 | 緩解 |
|---|---|---|
| per-operation 增加 Acquire/Return 次數 | 吞吐下降 | Acquire 是行程內 semaphore + 字典查找，非建立連線；圖 ⑦ 已評估為可接受 |
| `LegacyOrganizationServiceAdapter` 的 `as OrganizationServiceProxy` 得到 null | 行為改變 | F2 已驗證：今天傳 `PooledOrganizationService` 時本來就是 null，無回歸 |
| ambient 解析在背景工作取不到 HttpContext | 例外 | §7 已含 fallback；A11 測試守住 |
| 巢狀 `Execute` 深度計數錯誤 → lease 洩漏 | 池耗盡 | `finally` 保證遞減；A5 / A10 測試守住 |
| Manager 為 Singleton 卻需 Scoped 服務 | captive dependency | Manager 只依賴組態與 pool，不注入任何 Scoped；以 `ValidateScopes`/`ValidateOnBuild` 測試守住 |
| 一次改動面過大 | 難以回退 | 5 個 Run，前兩個純新增不接線，第三個才切換；每 Run 一個 commit |

## 10. Rollout 與 Rollback

```
Run A  型別契約與 Pool 核心      純新增，不接線 → 可隨時丟棄
Run B  Manager + Gateway + 代理  純新增，不接線 → 可隨時丟棄
Run C  切換 DI（唯一的切換點）    ← 不可回退點
Run D  清除 legacy 連線建立      依賴 Run C
Run E  參數／Metrics／收尾        依賴 Run D
```

**不可回退點只有一個：Run C 把 `IOrganizationService` 從 `PooledOrganizationService`
換成 `GatewayOrganizationService`。** 該 Run 之後必須先完成人工回歸再進 Run D。

Run A / Run B 完成時系統行為完全不變（新型別沒有人用），這是刻意的設計，
讓最危險的切換只發生在一個明確的點上。
