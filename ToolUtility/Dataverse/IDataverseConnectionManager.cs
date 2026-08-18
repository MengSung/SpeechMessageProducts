using System;
using System.Threading;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Dataverse 連線管理器的唯一應用程式入口。呼叫端只能取得 lease 與 metrics，
/// 不會接觸 raw client 或池內可變集合；Singleton 的生命週期由 DI 管理至應用程式關閉。
/// </summary>
public interface IDataverseConnectionManager : IDisposable
{
    /// <summary>依組合根設定的 Pool Key 取得一條短命租約。</summary>
    IClientLease Acquire(CancellationToken cancellationToken = default);

    /// <summary>取得所有 keyed 子池的 metrics 快照。</summary>
    DataversePoolMetrics GetMetrics();
}
