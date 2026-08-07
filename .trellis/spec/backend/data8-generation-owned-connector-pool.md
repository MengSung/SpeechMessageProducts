# Data8 Generation-owned Connector Pool Contract

## 1. Scope / Trigger

當新增、調整或組合 `ConnectorKind.Data8` 的 Pool、Lease、Router、Profile replacement 或
Organization Admission 行為時，必須遵守本契約。

- Pool 隔離鍵固定為 `(ProfileAlias, GenerationId)`。
- Organization 總容量固定由既有 `IOrganizationAdmissionManager`／`IOrganizationAdmissionRegistry` 管理。
- 不得將 Data8、WCF、CRM SDK、Credential、Token、Cookie、Session、端點、OrganizationId 或請求可變資料公開至 Abstractions。
- 本規格不授權實作 Embedded、Dedicated Gateway、Central Gateway、Official Worker、Web API 或實機診斷；但任何已選取 `ConnectorKind.Data8` 的模式都必須遵循本 Pool／Lease 契約。
- `Data8` 是永久合法的 ConnectorKind。ChurchReport Lenovo 路線必須可由 deployment configuration
  選取 `Embedded + Data8` 或 `DedicatedGateway + Data8`；P8 的第一個雲端路線是
  `CentralGateway + Data8`。這三者不改變 `ProfileAlias`／`GenerationId`／admission／cleanup
  owner，也不允許 request-time fallback 到 Official Worker。

## 2. Signatures

```csharp
public interface IConnectorPool : IAsyncDisposable, IDisposable
{
    string ProfileAlias { get; }
    long GenerationId { get; }
    bool IsDraining { get; }
    Task<IConnectorLease> AcquireAsync(ConnectorOperation operation, CancellationToken cancellationToken);
    Task DrainAsync(CancellationToken cancellationToken = default);
}

public interface IConnectorLease : IAsyncDisposable, IDisposable
{
    string ProfileAlias { get; }
    long GenerationId { get; }
    Task<ConnectorOperationResult> ExecuteAsync(ConnectorOperation operation, CancellationToken cancellationToken);
    void MarkFaulted(Exception? cause = null);
}

public interface IConnectorRouter
{
    IConnectorPool Resolve(ResolvedProfile profile);
}
```

`IConnectorLease` 不可公開 `IConnectorClient`。執行必須經由 `ExecuteAsync`，使取消、逾時與傳輸例外能標記 Lease 為 faulted。

## 3. Contracts

1. `Data8ConnectorPool` 只接受 `ResolvedProfile.ConnectorKind == Data8`。
2. Acquire 順序是：檢查未 Drain → 建立有界 deadline CTS → 取得既有 Admission Permit → 取得 local Client slot → 取出或建立 Client → 建立 Lease。
3. Acquire 任一失敗時，反向釋放暫存 Client、local slot 與 Permit；deadline CTS 只存在於方法範圍。
4. 健康 Lease 的 Client 僅能回到來源 Pool；不得跨 Alias、Generation、Organization 或 ConnectorKind 共用。
5. `MarkFaulted`、取消、deadline 到期、傳輸例外或 Pool draining 時，Lease Dispose 必須 Dispose Client，絕不可入 idle queue。
6. Lease 必須由呼叫端以 `await using`／finally 釋放；Lease Dispose 先處理 Client，再於 finally 釋放 Permit，並彙整多個 cleanup failure。
7. Generation replacement 時，Registry 只能有一個 Active 與最多一個 Draining Pool；Draining Pool 拒絕新 Lease，待 active lease 歸零後 Dispose idle Client，再移除舊引用。
8. Router 只能使用 `ResolvedProfile.ConnectorKind`、`ProfileAlias` 與 `GenerationId`；request 不能指定 Connector、endpoint、credential 或 OrganizationId。

## 4. Validation & Error Matrix

| 條件 | 必要行為 |
| --- | --- |
| 非 Data8 Profile 註冊 Pool | 拋出 `ArgumentException`，不建立 Client |
| Router 收到非 Data8 或未登錄世代 | fail closed：`NotSupportedException` 或 `KeyNotFoundException`，不 fallback |
| 作業 deadline 已到 | `OperationCanceledException`；不得建立或回池 Client |
| Factory、local slot 或 Drain 期間 Acquire 失敗 | 釋放已取得 Permit 與 local slot；Dispose 暫存 Client |
| Lease 執行取消／逾時／例外 | 標記 faulted；Dispose 時淘汰 Client 並釋放 Permit |
| Drain 已開始 | 拒絕新 Lease；只等待既有 Lease；最後 Dispose idle Client |
| 多個 Client／Permit cleanup 同時失敗 | 繼續清理並回報 `AggregateException` |

## 5. Good / Base / Bad Cases

### Good

```csharp
await using var lease = await pool.AcquireAsync(operation, cancellationToken);
return await lease.ExecuteAsync(operation, cancellationToken);
```

### Base

同一 Organization 的兩個 Profile 使用不同 Pool 與不同 idle Client，但取得自同一個
`IOrganizationAdmissionManager`，因此共享總併發預算。

### Bad

```csharp
// 禁止：公開原始 Client，讓取消與故障繞過 Lease 的淘汰規則。
var client = lease.Client;
await client.ExecuteAsync(operation, cancellationToken);
```

## 6. Tests Required

任何實作或調整都必須覆蓋：

- 健康 Lease 歸還原 Generation 並正好釋放一次 Permit。
- `MarkFaulted` 的 Client 不回池且下次 Acquire 建立新 Client。
- Acquire cancellation／deadline 與 Execute cancellation／deadline 都釋放 Permit 並淘汰不可靠 Client。
- Drain 拒絕新 Lease、等待既有 Lease、再 Dispose idle Client。
- 不同 Profile 不共用 idle Client。
- 同 Organization 的不同 Profile 經 `OrganizationAdmissionRegistry` 實際共用同一預算。
- 重複 acquire/execute/dispose soak 回到 Client、Permit 與所有權計數基線。
- Router 對 Official Worker 或世代不符的 Profile fail closed。

## 7. Wrong vs Correct

### Wrong

以 `ProfileAlias` 作為 Organization 容量 key，或在每個 Pool 自建 `SemaphoreSlim` 當作總容量預算。
這會讓同一實體 Organization 因多個 Alias 或 replacement 放大可用併發。

### Correct

以 `(ProfileAlias, GenerationId)` 隔離可重用 Client；以既有 Admission Manager 的 canonical
Organization key 管理總容量。Pool 的 `SemaphoreSlim` 僅限制該 Pool 的 Client 容器大小，不能取代 Admission。
