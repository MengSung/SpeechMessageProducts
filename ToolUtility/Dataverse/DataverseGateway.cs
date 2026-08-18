using System;
using System.Threading;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Scoped Gateway 的 reentrant 實作。
/// 它只持有 scoped lease 參考，不持有 raw client；lease 由 manager pool 決定底層 client 的最終釋放。
/// </summary>
public sealed class DataverseGateway : IDataverseGateway, IDisposable
{
    private readonly IDataverseConnectionManager _manager;
    private IClientLease _lease;
    private int _depth;
    private int _disposed;

    /// <summary>建立目前 request scope 專屬的 Gateway。</summary>
    public DataverseGateway(IDataverseConnectionManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>
    /// 在目前 Gateway 範圍執行不回傳值的 Dataverse 操作。外層呼叫取得一次 lease，巢狀呼叫只增加深度；
    /// Trace 僅記錄進出深度，絕不攔截、吞沒或改寫底層例外與 lease 的 faulted 決策。
    /// </summary>
    public void Execute(Action<IOrganizationService> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        Execute<object>(service => { work(service); return null; });
    }

    /// <summary>
    /// 在目前 scope 的唯一 lease 上執行 Dataverse 操作。lease 會在最外層 finally 中歸還，例外則先標為
    /// faulted；啟用 Trace 時只於這些既有邊界記錄 reentrant 深度，以驗證同一 request 不會重複租借 client。
    /// </summary>
    public T Execute<T>(Func<IOrganizationService, T> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        ThrowIfDisposed();

        if (_depth == 0)
            _lease = _manager.Acquire();
        _depth++;
        var trace = DataverseTrace.Current;
        if (trace?.Enabled == true)
            trace.GatewayExecuteEnter(_depth);
        try
        {
            return work(_lease.Service);
        }
        catch
        {
            _lease.MarkFaulted();
            throw;
        }
        finally
        {
            if (trace?.Enabled == true)
                trace.GatewayExecuteExit(_depth);
            _depth--;
            if (_depth == 0)
            {
                var lease = _lease;
                _lease = null;
                lease?.Dispose();
            }
        }
    }

    /// <summary>歸還尚未歸還的 scoped lease；不銷毀 manager 或其長命 pool。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _depth = 0;
        var lease = _lease;
        _lease = null;
        lease?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(DataverseGateway));
    }
}
