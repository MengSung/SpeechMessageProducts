# 技術設計：四大產品 Dataverse 連線架構

對應 `prd.md`。本文件只描述技術設計，執行順序見 `implement.md`。

---

## ⚠️ 本次實作範圍（最小可行版）

**本文件描述的是完整目標架構。本次只實作其中最小的一塊：**

| 本次要做 | 對應章節 |
|---|---|
| `IOrganizationService` 註冊為 **Scoped**，容器負責歸還 | §3 資料流的簡化版 |
| Fault 不回池 | §5.3 規則的一部分 |
| `OnPremiseClient` 實作 `IDisposable`（若 Run 1 確認型別） | §5.2 |

**本次不做**：Gateway / Manager / KeyedPool 三層（§1、§2）、Reentrant Lease（§4）、
`PoolKey`（§6）、可觀測性（§9）、70+ singleton 呼叫點遷移（§7.3）。

理由：目前四個缺陷中，只有「借還不對稱」會讓產品**停止服務**；其餘為降級而非中斷。
Scoped 註冊即可完全消滅該故障模式，且不需要上述任何一層。

三層架構保留在本文件中，作為產品 B 接入時的設計依據 —— 屆時 Scoped 的實作換成
Gateway 即可，呼叫端不動。

> 採 Scoped（每 request 一條連線）等同 §10 表中的 **per-request lease**。
> 代價是池利用率較低、`MaxSize` 需接近尖峰併發 request 數；
> 好處是 §4 的 Reentrant Lease 整個不需要 —— 一個 request 本來就只有一條。

---

## 1. 架構與職責邊界

三層，各自回答一個不同的問題，且**生命週期不同**：

| 層 | DI 生命週期 | 它回答的問題 | 型別 |
|---|---|---|---|
| Gateway | **Scoped** | 這一個 request 借了沒？巢狀幾層？該還了嗎？ | `IDataverseGateway` |
| Manager | Singleton | 該不該建新的？壞了嗎？要不要重試／熔斷？關機怎麼收？ | `IDataverseConnectionManager` |
| Pool | Singleton | 我有幾條？誰在用？還能不能再給？ | `IKeyedClientPool` |

### 1.1 為何 Gateway 不能併入 Manager

Reentrant Lease 需要 `_lease` 與 `_depth` 兩個**請求級**欄位。Manager 是 Singleton，
若把這兩個欄位放進去，等同重演 `ToolUtilityClass.m_Crm2011OrganizationService` 的
缺陷 —— 程序級單例上掛可變的、屬於單一 request 的狀態，並行 request 會互相破壞。

唯一的替代方案是 Manager 內部使用 `AsyncLocal<T>`。本設計不採用，理由：

- 隱式狀態，不出現在任何簽章上，難測試、難推理
- 背景批次（`WebServiceConnector` 有多處 `Task.Run` 風格的深呼叫鏈）跨越
  `AsyncLocal` 邊界的行為不直觀
- 無 Dispose 保證；DI Scope 由容器保證在 request 結束時 Dispose

### 1.2 可見性規則

- `IDataverseGateway` 為 `public`
- `IDataverseConnectionManager`、`IKeyedClientPool`、`IClientLease`、`PooledClient`
  為 `internal`
- 業務組件只能看到 Gateway；池型別僅 `Startup` 可觸及

此規則以 `InternalsVisibleTo` 對測試組件開放。

## 2. 型別契約

```csharp
// ── 業務程式唯一可見的型別 ────────────────────────────────
public interface IDataverseGateway
{
    T Execute<T>(Func<IOrganizationService, T> work);
    void Execute(Action<IOrganizationService> work);

    Task<T> ExecuteAsync<T>(
        Func<IOrganizationService, CancellationToken, Task<T>> work,
        CancellationToken ct = default);
}

// ── 政策層 ────────────────────────────────────────────────
internal interface IDataverseConnectionManager
{
    IClientLease Acquire(PoolKey key, CancellationToken ct);
    PoolStats Stats { get; }
}

// ── 資源層 ────────────────────────────────────────────────
internal interface IKeyedClientPool : IDisposable
{
    bool TryRent(PoolKey key, TimeSpan wait, out PooledClient client);
    void ReturnHealthy(PooledClient client);   // 清狀態後回池
    void ReturnFaulted(PooledClient client);   // 銷毀，不回池
    PoolStats Stats { get; }
}

// ── 租約 ──────────────────────────────────────────────────
internal interface IClientLease : IDisposable
{
    PoolKey Key { get; }
    IOrganizationService Service { get; }
    void MarkFaulted(Exception ex);
}

// ── 池的 key ──────────────────────────────────────────────
public readonly record struct PoolKey(
    string Product,             // "好牧人1.5"
    string Environment,         // "prod"
    string OrganizationUrl,     // "https://sunnyvalechback.../Organization.svc"
    string EffectiveIdentity);  // 今日恆為服務帳號；未來可為 "svc#<userId>"
```

`PoolStats`：`TotalClients` / `Active` / `Idle` / `Waiting` / `TotalRent` /
`TotalReturn` / `FaultedCount` / `TimeoutCount` / `HealthFailCount` / `SubPoolCount`。

## 3. 一次操作的資料流

```
HTTP request
  → 容器建立 scope，注入 IDataverseGateway（此時無 client）
  → 業務程式呼叫 gateway.Execute(work)
      → key = keyResolver.Resolve()
      → depth == 0 或 lease.Key != key  →  manager.Acquire(key, ct)
            → pool.TryRent(key, AcquireTimeout, out client)
                  · 有閒置：取出；距上次健檢 > HealthInterval 才 WhoAmI
                  · 無閒置且未達 MaxSize：建立新 client
                  · 已達 MaxSize：等待；逾時 → TimeoutException
      → work(lease.Service)
      → 正常結束：depth 歸零時 lease.Dispose() → 清 CallerId → ReturnHealthy
      → 例外  ：lease.MarkFaulted(ex) → ReturnFaulted → 真正關閉通道
  → scope 結束，容器 Dispose Gateway（雙保險：若 lease 仍在則此時歸還）
```

client 被持有的區間僅為 `Acquire` 至 `Dispose`，量級為數十至數百毫秒；
request 的其餘時間（驗證、組 View、序列化）不佔用連線。

## 4. Reentrant Lease

```csharp
internal sealed class DataverseGateway : IDataverseGateway, IDisposable
{
    private readonly IDataverseConnectionManager _manager;
    private readonly IPoolKeyResolver _keyResolver;
    private IClientLease _lease;
    private int _depth;

    public T Execute<T>(Func<IOrganizationService, T> work)
    {
        var key = _keyResolver.Resolve();

        // key 不同不可重用 —— 未來開啟 per-user impersonation 時，
        // 重用跨身分的 lease 等同 impersonation 洩漏。
        if (_depth == 0 || _lease.Key != key)
        {
            if (_depth > 0) throw new InvalidOperationException(
                "巢狀 Execute 不可跨 PoolKey。");
            _lease = _manager.Acquire(key, CancellationToken.None);
        }

        _depth++;
        try                 { return work(_lease.Service); }
        catch (Exception e) { _lease.MarkFaulted(e); throw; }
        finally
        {
            if (--_depth == 0) { _lease.Dispose(); _lease = null; }
        }
    }

    // 雙保險：request scope 結束時，若 lease 因任何原因仍在手上則強制歸還
    public void Dispose()
    {
        if (_lease != null) { _lease.Dispose(); _lease = null; _depth = 0; }
    }
}
```

**巢狀時 `MarkFaulted` 的語意**：內層標記後外層仍會再標記一次，`MarkFaulted` 必須
冪等（僅記錄第一個例外並設旗標）。旗標一旦設立，`Dispose` 走 `ReturnFaulted`。

Gateway 為 Scoped，`_depth` / `_lease` 不跨 request；本設計不支援單一 request 內
跨執行緒並行使用同一個 Gateway（若未來需要，須改為明確的多 lease 模型）。

## 5. Fault 路徑與通道關閉

### 5.1 現況缺陷

`OnPremiseClient` 宣告為 `class OnPremiseClient : IOrganizationService`，**未實作
`IDisposable`**。因此既有 `CrmConnectionPool.DisposeConnection()` 內的
`(Service as IDisposable)?.Dispose()` 永遠不執行 —— 底層 WCF 通道從未被關閉。

### 5.2 解法

`PowerPlatform.Dataverse.Client/` 是本 repo 內的原始碼，直接修改：

1. `OnPremiseClient` 實作 `IDisposable`
2. `Dispose()` 依 `_service`（`OnPremiseClient.cs:67`）的實際執行期型別收尾：
   - 若為 `ICommunicationObject`：`State == Faulted` 時 `Abort()`，否則帶逾時
     `Close()`，`Close()` 失敗再 `Abort()`
   - 若為 `IDisposable`：`Dispose()`
3. 檢查 `ADAuthClient` 是否另持有需釋放的資源（token / handle）

> **實作前必須先驗證 `_service` 在 `ConnectAD` 與 `ConnectFederated` 兩條路徑下的
> 實際型別**（`OnPremiseClient.cs:171` / `:254`）。設計假設其一為 WCF channel proxy，
> 另一為 `ADAuthClient`；若不符需回到本節修正。

### 5.3 Pool 的三條硬規則

1. 只接受**自己建立**的 `PooledClient`（以池產生的 token 識別，非物件參照比對）
2. 拒絕重複歸還（`PooledClient` 帶狀態旗標，第二次歸還直接拋出並記錄）
3. 拒絕並行共用（`Leased` 狀態的 client 不得再被 `TryRent` 取出）

規則 1、2 直接消滅既有 `ReleaseConnection` 的 semaphore 超賣缺陷。

## 6. Keyed Pool

內部為 `ConcurrentDictionary<PoolKey, SubPool>`。今日 `EffectiveIdentity` 恆為服務
帳號，故字典恆 1 筆，行為與單一池相同，成本為一次字典查詢。

**延後項目**（不在本次實作，但介面已預留）：

- 全域上限（壓在所有子池之上，非僅 per-key）
- 子池閒置回收（使用者離線後子池必須消失）
- LRU 淘汰（撞到全域上限時趕走最久未用的子池）

未來若開啟 per-user impersonation，`MaxSize` 語意將從 per-process 變為
per-identity，上述三項必須同時補齊，否則 N 位線上使用者將產生 N 個子池。

**清 `CallerId` 不因分池而省** —— 分池是第一道防線，清狀態是第二道；key 若算錯，
清狀態仍能兜底。

## 7. 相容性與遷移

### 7.1 `ToolUtility` 既有 API 保留

`ToolUtility` 保持無狀態 Facade，方法簽章不動。呼叫端改為在 Gateway callback 內
呼叫：

```csharp
// 之前
var svc = GetConnection();
try { return ToolUtility.RetrieveXxx(svc, id); }
finally { ReleaseConnection(svc); }

// 之後
return _gateway.Execute(svc => ToolUtility.RetrieveXxx(svc, id));
```

### 7.2 `ref IOrganizationService` 簽章

部分既有方法採 `ref` 傳遞，例如
`RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService service, ...)`。
在 callback 內需引入區域變數：

```csharp
_gateway.Execute(svc => {
    var s = svc;
    return ToolUtility.RetrieveMemberListCollectionByListIdCrm2011(ref s, listId);
});
```

> **遷移前必須逐一確認這些 `ref` 方法是否會在內部**重新指派**該參數。**
> 若會，重新指派將在 callback 結束後遺失，且可能指向未受池管理的連線。
> 對於會重新指派的方法，必須先改為不重新指派或改用回傳值。

### 7.3 `m_Crm2011OrganizationService` 的移除

該欄位為 `public`，70+ 讀取點分布於 Controllers / WebServiceConnector / ViewModels /
Tools / Services。移除策略見 `implement.md`，原則為：

1. 先將欄位改為 `internal` 並加 `[Obsolete]`，讓編譯器列出全部呼叫點
2. 依模式分類批次遷移
3. 全部遷移後刪除欄位與 `ToolUtilityClass.Core.cs:144` 的建立邏輯

### 7.4 遷移期間的雙軌

`CrmConnectionPool` 與新 Pool 不並存。新 Pool 完成後，`CrmConnectionPool` 改為
內部委派至新 Pool 的薄殼，待 23 個呼叫點遷移完畢再刪除。此舉使每批遷移可獨立驗證。

## 8. 設定

```jsonc
"DataversePool": {
  "Product": "好牧人1.5",
  "Environment": "prod",
  "MinSize": 3,
  "MaxSize": 12,
  "AcquireTimeoutSeconds": 10,
  "IdleTimeoutMinutes": 10,
  "HealthIntervalSeconds": 30
}
```

- 修正既有錯配：`appsettings.Production.json` 的 `ConnectionPool` 區段未被
  `Startup.cs:308` 讀取。新設定統一為 `DataversePool`，並以 `IOptions<T>` 繫結、
  於啟動時驗證（`MinSize >= 1`、`MaxSize >= MinSize`、逾時為正值）。
- `MaxSize` 之估算：`需要併發數 ≈ 每秒請求數 × 平均處理秒數`，再留 5～10 倍餘裕。
- **部署拓撲**：CRM 端實際併發 = Σ(產品 × 主機台數 × MaxSize)。擴充實例時
  `MaxSize` 必須同步除以台數。
- `AcquireTimeout` 必須小於前端／反向代理逾時，否則使用者先看到 502。
- 密碼移出版控，改由環境變數或 secret provider 提供。

## 9. 可觀測性

- `PoolStats` 經健康檢查端點輸出（既有 `services.AddHealthChecks()` 已在
  `Startup.cs:356`，新增一個 `dataverse-pool` check）。
- 下列事件記錄結構化日誌：租借逾時、健檢失敗、fault 淘汰、拒絕歸還、子池建立與回收。
- 池滿等待時間以直方圖記錄，作為 `MaxSize` 調整依據。

## 10. 取捨與被否決的替代方案

| 方案 | 否決理由 |
|---|---|
| 單一長壽 client（不用池） | `OnPremiseClient` 非執行緒安全，並行必然損毀 |
| 每次操作新建 client 後 Dispose | WS-Trust 交握成本高，且既有 Dispose 為 no-op |
| Manager 直接暴露 `Acquire`/`Release` | 成對呼叫的 API 形狀，與現況同類的洩漏必然重現 |
| Manager + `AsyncLocal` 取代 Gateway | 隱式狀態、背景執行緒邊界不直觀、無 Dispose 保證 |
| per-request lease（整個 request 佔一條） | 池利用率低、`MaxSize` 需求接近併發 request 數 |
| 立即改走 Web API / `DynamicsAccess` | ADFS ClientId 尚未確認註冊，WhoAmI 未驗證通過 |

## 11. Rollout 與 Rollback

**Rollout** 分三段，每段可獨立驗證與回退（詳見 `implement.md`）：

1. 建立新三層 + 測試（不動既有程式碼，零風險）
2. 遷移 23 個池化呼叫點
3. 消滅 70+ 個 singleton 呼叫點

**Rollback 點**：

- 第 1 段：新程式碼未被任何呼叫端使用，直接還原 commit 即可
- 第 2 段：`CrmConnectionPool` 薄殼仍在，還原呼叫端 commit 即回到舊行為
- 第 3 段：以模式分批，每批一個 commit；任一批出問題僅還原該批

**不可回退的點**：`OnPremiseClient` 實作 `IDisposable`（第 1 段）會改變連線關閉
行為。此變更需在測試環境先驗證連線關閉不影響既有長時間批次作業。

## 12. 未來延伸（不在本次範圍）

- per-user impersonation（`EffectiveIdentity` 展開 + 全域上限 + 子池回收 + LRU）
- Web API 傳輸（`DynamicsAccess:ExecutionMode`），屆時僅需替換 Gateway 的實作，
  呼叫端不動
- 產品 B / C / D 套用同一模式，各自獨立服務帳號與 `PoolKey`
