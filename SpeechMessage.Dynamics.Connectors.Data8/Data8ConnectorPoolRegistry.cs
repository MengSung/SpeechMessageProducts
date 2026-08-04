using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.ControlPlane.Capacity;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 管理各 Profile Alias 的 Active 與最多一個 Draining Data8 Pool Generation。
/// Registry 的隔離鍵是 <c>(ProfileAlias, GenerationId)</c>；不同 Alias 絕不共用 Client，即使它們指向同一
/// Organization。相同 Organization 的總併發仍由外部注入且可共用的 <see cref="IOrganizationAdmissionManager"/>
/// 強制管理，Registry 本身不建立第二套容量預算或保存 Credential、Token、Session、端點與請求內容。
/// </summary>
public sealed class Data8ConnectorPoolRegistry : IConnectorRouter, IAsyncDisposable, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// 登錄新的 Data8 Profile Generation；同 Alias 的舊 Active Pool 會先原子地進入 Drain，
    /// 然後新 Pool 才成為唯一可路由的 Active Generation。每個 Alias 最多容許一個 Draining Generation，
    /// 以避免連續重載無限制累積 Client、Permit 或未完成 Drain Task。
    /// </summary>
    public Data8ConnectorPool Register(
        ResolvedProfile profile,
        IOrganizationAdmissionManager admissionManager,
        IData8ConnectorClientFactory factory,
        int minSize,
        int maxSize)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ConnectorKind != ConnectorKind.Data8)
        {
            throw new ArgumentException("Only Data8 profiles can be registered.", nameof(profile));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_slots.TryGetValue(profile.ProfileAlias, out var existing))
            {
                if (existing.Active.GenerationId == profile.GenerationId)
                {
                    return existing.Active;
                }

                if (existing.Draining is not null)
                {
                    throw new InvalidOperationException("A profile cannot replace two generations concurrently.");
                }

                existing.Draining = existing.Active;
                _ = existing.Draining.DrainAsync();
                existing.Active = CreatePool(profile, admissionManager, factory, minSize, maxSize);
                return existing.Active;
            }

            var active = CreatePool(profile, admissionManager, factory, minSize, maxSize);
            _slots.Add(profile.ProfileAlias, new Slot(active));
            return active;
        }
    }

    /// <summary>
    /// 只解析目前 Active 且 Generation 完全相同的 Data8 Pool。舊世代與非 Data8 ConnectorKind 均 fail closed，
    /// 使請求不可能在 replacement 後重回舊 Client、改用其他 Connector 或跨世代共享可變連線狀態。
    /// </summary>
    public IConnectorPool Resolve(ResolvedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ConnectorKind != ConnectorKind.Data8)
        {
            throw new NotSupportedException("No Data8 fallback exists for a non-Data8 profile.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_slots.TryGetValue(profile.ProfileAlias, out var slot) &&
                slot.Active.GenerationId == profile.GenerationId)
            {
                return slot.Active;
            }
        }

        throw new KeyNotFoundException("No active Data8 pool is registered for the resolved profile generation.");
    }

    /// <summary>
    /// 等待所有已完成 replacement 的 Draining Generation 清理完畢，並在成功後移除 Registry 對舊 Pool 的引用。
    /// 呼叫端取消只中斷等待；每個 Pool 仍持有自身 Drain Task 並最終釋放 idle Client，避免 Registry 產生
    /// fire-and-forget 資源生命週期。
    /// </summary>
    public async Task DrainCompletedGenerationsAsync(CancellationToken cancellationToken = default)
    {
        List<Data8ConnectorPool> draining;
        lock (_gate)
        {
            ThrowIfDisposed();
            draining = _slots.Values
                .Where(static slot => slot.Draining is not null)
                .Select(static slot => slot.Draining!)
                .ToList();
        }

        foreach (var pool in draining)
        {
            await pool.DrainAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            foreach (var slot in _slots.Values)
            {
                if (slot.Draining is not null && slot.Draining.IsDraining)
                {
                    slot.Draining = null;
                }
            }
        }
    }

    /// <summary>
    /// Drain 並 Dispose 所有 Active 與 Draining Pool。Registry 不擁有外部 Admission Manager 或 Factory，
    /// 因此不會錯誤釋放同 Organization 的其他 Profile 容量登錄；它只處理自身建立的 Pool 與 Client。
    /// 所有 Pool 都會被嘗試清理，失敗會彙整後回報，避免第一個錯誤遮蔽後續資源洩漏。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        List<Data8ConnectorPool> pools;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pools = _slots.Values
                .SelectMany(static slot => slot.Draining is null
                    ? [slot.Active]
                    : new[] { slot.Active, slot.Draining })
                .ToList();
            _slots.Clear();
        }

        List<Exception>? failures = null;
        foreach (var pool in pools)
        {
            try
            {
                await pool.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: 1 })
        {
            throw failures[0];
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures);
        }
    }

    /// <summary>以同步方式等待相同的 Registry 清理流程，不啟動未受管理的背景工作。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static Data8ConnectorPool CreatePool(
        ResolvedProfile profile,
        IOrganizationAdmissionManager admissionManager,
        IData8ConnectorClientFactory factory,
        int minSize,
        int maxSize)
        => new(profile, admissionManager, factory, minSize, maxSize);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Data8ConnectorPoolRegistry));
        }
    }

    /// <summary>
    /// 保存一個 Alias 的有限世代槽位。此型別只保存 Pool 物件參考，不保存 request 或 security state；
    /// Draining 參考在 <see cref="DrainCompletedGenerationsAsync"/> 成功後立即移除，以限制記憶體保留時間。
    /// </summary>
    private sealed class Slot
    {
        /// <summary>建立一個以指定 Active Pool 初始化的有限世代槽位。</summary>
        public Slot(Data8ConnectorPool active) => Active = active;

        /// <summary>取得或設定唯一可接受新 Lease 的世代。</summary>
        public Data8ConnectorPool Active { get; set; }

        /// <summary>取得或設定等待既有 Lease 釋放的舊世代；同一時間最多一個。</summary>
        public Data8ConnectorPool? Draining { get; set; }
    }
}
