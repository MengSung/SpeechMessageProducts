using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>池內 client 的明確狀態。</summary>
public enum PooledClientState
{
    /// <summary>可被租借。</summary>
    Idle,
    /// <summary>目前由一條 lease 擁有。</summary>
    Leased,
    /// <summary>傳輸狀態不確定，等待歸還時淘汰。</summary>
    Faulted,
    /// <summary>底層資源已由 pool 銷毀。</summary>
    Disposed
}

/// <summary>
/// 封裝一個由 bounded pool 建立及擁有的 client，並以鎖保護狀態轉換。
/// 此型別不跨 pool 脫離；底層服務只會透過 IClientLease 暫時暴露。
/// </summary>
public sealed class PooledClient
{
    private readonly object _sync = new();
    private PooledClientState _state;

    /// <summary>建立由 pool 擁有的 client。</summary>
    public PooledClient(IOrganizationService service, DateTime? now = null)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        var timestamp = now ?? DateTime.UtcNow;
        LastUsedUtc = timestamp;
        // 新建 client 尚未經過 WhoAmI；第一次出借必須先驗證，避免把未確認通道交給呼叫端。
        LastValidatedUtc = DateTime.MinValue;
        _state = PooledClientState.Idle;
    }

    /// <summary>底層組織服務；只有 pool/lease 內部流程可使用。</summary>
    public IOrganizationService Service { get; }

    /// <summary>最後一次健康驗證時間。</summary>
    public DateTime LastValidatedUtc { get; private set; }

    /// <summary>最後一次租借或歸還時間。</summary>
    public DateTime LastUsedUtc { get; private set; }

    /// <summary>目前狀態。</summary>
    public PooledClientState State
    {
        get { lock (_sync) return _state; }
    }

    internal bool TryLease(DateTime now)
    {
        lock (_sync)
        {
            if (_state != PooledClientState.Idle)
                return false;
            _state = PooledClientState.Leased;
            LastUsedUtc = now;
            return true;
        }
    }

    internal bool MarkFaulted()
    {
        lock (_sync)
        {
            if (_state == PooledClientState.Disposed)
                return false;
            if (_state == PooledClientState.Faulted)
                return false;
            if (_state != PooledClientState.Leased)
                throw new InvalidOperationException("只有 leased client 可以標記故障。");
            _state = PooledClientState.Faulted;
            return true;
        }
    }

    internal bool ReturnHealthy(DateTime now)
    {
        lock (_sync)
        {
            if (_state != PooledClientState.Leased)
                return false;
            _state = PooledClientState.Idle;
            LastUsedUtc = now;
            return true;
        }
    }

    internal bool IsIdleExpired(DateTime now, TimeSpan timeout)
    {
        lock (_sync)
            return _state == PooledClientState.Idle && now - LastUsedUtc >= timeout;
    }

    internal void MarkValidated(DateTime now)
    {
        lock (_sync)
            LastValidatedUtc = now;
    }

    internal bool DisposeUnderlying()
    {
        lock (_sync)
        {
            if (_state == PooledClientState.Disposed)
                return false;
            _state = PooledClientState.Disposed;
        }

        try
        {
            (Service as IDisposable)?.Dispose();
        }
        catch
        {
            // pool shutdown/淘汰不可因單一 client 的 Dispose 例外而阻塞其他資源釋放。
        }
        return true;
    }
}
