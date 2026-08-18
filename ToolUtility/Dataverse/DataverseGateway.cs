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

    /// <inheritdoc />
    public void Execute(Action<IOrganizationService> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        Execute<object>(service => { work(service); return null; });
    }

    /// <inheritdoc />
    public T Execute<T>(Func<IOrganizationService, T> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        ThrowIfDisposed();

        if (_depth == 0)
            _lease = _manager.Acquire();
        _depth++;
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
