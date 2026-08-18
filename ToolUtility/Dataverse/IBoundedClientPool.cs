using System;
using System.Threading;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 以 Pool Key 分隔的 bounded client pool。應用程式只能取得 lease，不能取得 pool 內部集合。
/// </summary>
public interface IBoundedClientPool : IDisposable
{
    /// <summary>取得指定隔離鍵的 client 租約。</summary>
    IClientLease Acquire(DataverseConnectionKey key, CancellationToken cancellationToken = default);

    /// <summary>讀取目前 pool 的計數快照。</summary>
    DataversePoolMetrics GetMetrics();
}
