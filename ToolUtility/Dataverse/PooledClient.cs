using System;
using Microsoft.Xrm.Sdk;
using PowerPlatform.Dataverse.Client;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 定義 pool 內 client 的唯一狀態機。狀態轉換全由 <see cref="PooledClient"/> 的同步鎖保護，
/// 防止同一條連線同時交給兩個 request，並讓故障或釋放後的連線永遠無法重新租借。
/// </summary>
public enum PooledClientState
{
    /// <summary>client 不帶任何前一位呼叫者的可重用狀態，正等待 pool 租借。</summary>
    Idle,
    /// <summary>client 正由唯一 lease 擁有；cleanup 與 shutdown 不得銷毀它。</summary>
    Leased,
    /// <summary>傳輸或隔離狀態不可信；歸還時只能淘汰，不得回到 Idle。</summary>
    Faulted,
    /// <summary>底層可釋放資源已由 pool 的淘汰或關閉路徑處理，狀態不可逆。</summary>
    Disposed
}

/// <summary>
/// 封裝一個由 bounded pool 建立及擁有的 client，並以鎖保護狀態轉換。
/// client 的最長生命週期僅至閒置逾時、故障淘汰或 pool 關閉；只有 pool 可呼叫
/// <see cref="DisposeUnderlying"/>。此型別不跨 pool 脫離，底層服務只經由短命 lease 暫時暴露，
/// 且在回到 Idle 前清除已知的 impersonation 身分，避免跨 request／使用者重用狀態。
/// </summary>
public sealed class PooledClient
{
    private readonly object _sync = new();
    private PooledClientState _state;
    private bool _disposeWhenReturned;

    /// <summary>
    /// 建立由 pool 擁有的 client，初始狀態固定為 Idle。呼叫端交出的 service 之後
    /// 只能由 pool 的故障淘汰或關閉流程釋放，lease 本身絕不直接 Dispose 底層連線。
    /// </summary>
    public PooledClient(IOrganizationService service, DateTime? now = null)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        var timestamp = now ?? DateTime.UtcNow;
        LastUsedUtc = timestamp;
        // 新建 client 尚未經過 WhoAmI；第一次出借必須先驗證，避免把未確認通道交給呼叫端。
        LastValidatedUtc = DateTime.MinValue;
        _state = PooledClientState.Idle;
    }

    /// <summary>
    /// 取得底層組織服務；只有 pool／lease 的受控流程可使用。任何使用者、profile 或
    /// request 特有狀態都必須在歸還前移除，否則 client 會被標記 Faulted 並確定性淘汰。
    /// </summary>
    public IOrganizationService Service { get; }

    /// <summary>取得最後一次 WhoAmI 健康驗證時間，供 pool 判定下次租借是否需要重驗。</summary>
    public DateTime LastValidatedUtc { get; private set; }

    /// <summary>取得最後一次租借或歸還時間，供 pool 在不做網路 I/O 的情況下判定閒置淘汰。</summary>
    public DateTime LastUsedUtc { get; private set; }

    /// <summary>取得受鎖保護的目前狀態；讀值可供 metrics 使用，但不可繞過狀態轉換。</summary>
    public PooledClientState State
    {
        get { lock (_sync) return _state; }
    }

    /// <summary>僅在 client 為 Idle 時原子地轉為 Leased，確保同一時間只存在一條 lease。</summary>
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

    /// <summary>把仍由 lease 擁有的 client 標示為故障，使歸還流程只能淘汰它。</summary>
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

    /// <summary>
    /// 將健康 lease 歸還為 Idle。狀態轉換前會在同一把鎖內清除已知的 CallerId，
    /// 因此下一個 request 不可能觀察到前一個呼叫者的 impersonation 身分；清除失敗時
    /// 轉為 Faulted 並回傳 <see langword="false"/>，交由 pool 確定性淘汰。若 cleanup 已在
    /// 此 lease 之前選中 client，亦會在這個唯一歸還點轉為 Faulted，避免它再次回到 Idle。
    /// </summary>
    internal bool ReturnHealthy(DateTime now)
    {
        lock (_sync)
        {
            if (_state != PooledClientState.Leased)
                return false;
            if (_disposeWhenReturned)
            {
                _state = PooledClientState.Faulted;
                return false;
            }
            if (!TryClearCallerId())
            {
                _state = PooledClientState.Faulted;
                return false;
            }
            _state = PooledClientState.Idle;
            LastUsedUtc = now;
            return true;
        }
    }

    /// <summary>判斷 client 是否仍為 Idle 且超過可設定的最大閒置時間。</summary>
    internal bool IsIdleExpired(DateTime now, TimeSpan timeout)
    {
        lock (_sync)
            return _state == PooledClientState.Idle && now - LastUsedUtc >= timeout;
    }

    /// <summary>記錄成功健康驗證時間，使租借熱路徑可在健康間隔內避免重複網路驗證。</summary>
    internal void MarkValidated(DateTime now)
    {
        lock (_sync)
            LastValidatedUtc = now;
    }

    /// <summary>
    /// 由 pool 的唯一擁有者釋放底層資源。若 cleanup 與 Acquire 交錯而 client 已轉為
    /// Leased，會記錄歸還時淘汰、拒絕釋放並回傳 <see langword="false"/>；歸還流程再依狀態
    /// 決定淘汰，以免中斷正由另一個 request 使用的連線。
    /// </summary>
    internal bool DisposeUnderlying()
    {
        lock (_sync)
        {
            // Cleanup 可能在 Acquire 已成功 TryLease 後才進入此處；leased client
            // 仍由呼叫端擁有，不能中斷它，但歸還時必須淘汰，避免已選中的過期 client 重新入池。
            if (_state == PooledClientState.Disposed)
                return false;
            if (_state == PooledClientState.Leased)
            {
                _disposeWhenReturned = true;
                return false;
            }
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

    /// <summary>
    /// 清除會隨 pooled client 傳播的 impersonation 身分。只有本組件已參考的
    /// OnPremiseClient 具備可寫 CallerId；其他 IOrganizationService 不做假設或 dynamic
    /// 反射。setter 或回讀失敗時採 fail-closed，讓該 client 不會回到可被別人租借的 Idle。
    /// </summary>
    private bool TryClearCallerId()
    {
        if (Service is not OnPremiseClient onPremiseClient)
            return true;

        try
        {
            onPremiseClient.CallerId = Guid.Empty;
            return onPremiseClient.CallerId == Guid.Empty;
        }
        catch
        {
            // 清除失敗代表身分邊界不可信，呼叫端會把 client 標記 Faulted 並淘汰。
            return false;
        }
    }
}
