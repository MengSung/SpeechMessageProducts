using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.ControlPlane.Connectors;

/// <summary>
/// 依 deployment-owned profile alias 管理 Official Worker 的 generation-owned Pool。
/// 同一個 alias 在任何時刻最多只有一個 Active generation 與一個 Draining generation；
/// generation 替換不會改變既有 lease 的 ProfileAlias、GenerationId、CE 版本或 Organization
/// admission 邊界。Registry 只負責發佈與回收 Pool，worker process、pipe、runtime lease 和
/// admission permit 仍由各自的 Pool/Lease owner 決定生命週期。
/// </summary>
public sealed class OfficialWorkerConnectorPoolRegistry : IConnectorRouter, IAsyncDisposable, IDisposable
{
    private readonly object _gate = new();
    private readonly IProfileExecutionLeaseProvider _leaseProvider;
    private readonly Dictionary<string, OfficialWorkerConnectorPool> _active =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResolvedProfile> _activeProfiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OfficialWorkerConnectorPool> _draining =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// 建立一個使用既有 profile runtime manager 作為 admission/runtime lease provider 的 registry。
    /// Provider 不會被 registry Dispose；它的生命週期由宿主 DI composition 擁有。
    /// </summary>
    /// <param name="leaseProvider">提供 generation 驗證、runtime lease 與 Organization permit 的 provider。</param>
    /// <exception cref="ArgumentNullException">provider 為 null。</exception>
    public OfficialWorkerConnectorPoolRegistry(IProfileExecutionLeaseProvider leaseProvider)
    {
        _leaseProvider = leaseProvider ?? throw new ArgumentNullException(nameof(leaseProvider));
    }

    /// <summary>
    /// 解析 deployment-owned profile 的 Active Official Worker Pool。
    /// 首次解析會建立 Active generation；同 alias 的新 generation 會先同步封鎖舊 Pool 的
    /// 新 admission，再發佈新 Pool。若舊 Draining generation 尚未完成，拒絕第三代替換，
    /// 以維持 bounded 的 Active/Draining 資源上限並讓呼叫端明確處理 rollback 或 drain。
    /// </summary>
    /// <param name="profile">已由 resolver 驗證的不可變 profile snapshot。</param>
    /// <returns>與 profile alias/generation 完全相符的 Active Pool。</returns>
    /// <exception cref="ArgumentNullException">profile 為 null。</exception>
    /// <exception cref="InvalidOperationException">同一 alias 的上一個 generation 尚未 drain，或同 generation 的 profile 不一致。</exception>
    public IConnectorPool Resolve(ResolvedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ConnectorKind is not (ConnectorKind.OfficialCrm82Worker or ConnectorKind.OfficialCrm91Worker))
        {
            throw new NotSupportedException(
                "The official worker registry cannot resolve a non-official connector kind.");
        }

        lock (_gate)
        {
            ThrowIfDisposedLocked();

            if (_active.TryGetValue(profile.ProfileAlias, out var current))
            {
                if (current.GenerationId == profile.GenerationId)
                {
                    EnsureProfileIdentity(_activeProfiles[profile.ProfileAlias], profile);
                    return current;
                }

                if (_draining.ContainsKey(profile.ProfileAlias))
                {
                    throw new InvalidOperationException(
                        "The previous official worker generation is still draining.");
                }

                // 先完成純 scalar 的新 Pool 建構，再封鎖舊 generation；若新 profile
                // 驗證失敗，既有 Active generation 仍可服務，rollback 不會留下半完成狀態。
                var replacementPool = new OfficialWorkerConnectorPool(profile, _leaseProvider);
                current.BeginDrain();
                _draining.Add(profile.ProfileAlias, current);
                _active[profile.ProfileAlias] = replacementPool;
                _activeProfiles[profile.ProfileAlias] = profile;
                return replacementPool;
            }

            var replacement = new OfficialWorkerConnectorPool(profile, _leaseProvider);
            _active[profile.ProfileAlias] = replacement;
            _activeProfiles[profile.ProfileAlias] = profile;
            return replacement;
        }
    }

    /// <summary>
    /// 等待所有已標記 Draining 的 generation 完成，並釋放其 worker/runtime 資源。
    /// 只有成功完成 drain 的 entry 才會從 registry 移除；取消或清理失敗會保留 entry，
    /// 讓下一次呼叫可以重試而不會遺失仍被持有的資源。
    /// </summary>
    /// <param name="cancellationToken">等待 drain 的取消訊號。</param>
    public async Task DrainCompletedGenerationsAsync(CancellationToken cancellationToken = default)
    {
        KeyValuePair<string, OfficialWorkerConnectorPool>[] entries;
        lock (_gate)
        {
            ThrowIfDisposedLocked();
            entries = _draining.ToArray();
        }

        foreach (var entry in entries)
        {
            await entry.Value.DrainAsync(cancellationToken).ConfigureAwait(false);
            await entry.Value.DisposeAsync().ConfigureAwait(false);

            lock (_gate)
            {
                if (_draining.TryGetValue(entry.Key, out var current) &&
                    ReferenceEquals(current, entry.Value))
                {
                    _draining.Remove(entry.Key);
                }
            }
        }
    }

    /// <summary>
    /// 以固定順序 drain 並釋放所有 Active 與 Draining Pool。
    /// 清理會嘗試完成每一個 owner；多個 cleanup failure 會聚合後回報，避免第一個失敗
    /// 造成其餘 worker、pipe 或 permit 遺留。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        OfficialWorkerConnectorPool[] pools;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pools = _active.Values.Concat(_draining.Values).Distinct().ToArray();
            _active.Clear();
            _activeProfiles.Clear();
            _draining.Clear();
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

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more official worker connector pools failed to dispose.",
                failures);
        }
    }

    /// <summary>同步釋放 registry 擁有的 Pool；非同步清理仍由各 Pool 的 deterministic path 執行。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static void EnsureProfileIdentity(
        ResolvedProfile current,
        ResolvedProfile requested)
    {
        if (current.GenerationId != requested.GenerationId ||
            !string.Equals(current.ProfileAlias, requested.ProfileAlias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.OrganizationAlias, requested.OrganizationAlias, StringComparison.OrdinalIgnoreCase) ||
            current.OrganizationId != requested.OrganizationId ||
            current.CeVersion != requested.CeVersion ||
            current.ConnectorKind != requested.ConnectorKind ||
            !string.Equals(current.CredentialReference, requested.CredentialReference, StringComparison.Ordinal) ||
            current.Pool != requested.Pool ||
            current.Operation != requested.Operation)
        {
            throw new InvalidOperationException(
                "The requested profile does not match the published official worker generation.");
        }
    }

    private void ThrowIfDisposedLocked()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OfficialWorkerConnectorPoolRegistry));
        }
    }
}
