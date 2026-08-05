using System.Collections.ObjectModel;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.ControlPlane.Connectors;

/// <summary>
/// 將 deployment-owned <see cref="ConnectorKind"/> 對應到各自的 generation-aware Connector Router。
/// 此類別是 Data8 與 Official Worker 共用的唯一分派 seam；它不擁有子 Router、Pool、Worker、Pipe、
/// Credential 或 Admission Manager，因此不會在 Dispose 時重複釋放外部生命週期。所有 request-time 的
/// connector、CE version、endpoint、OrganizationId 與 credential 值都不會進入這個類別。
/// </summary>
public sealed class CompositeConnectorRouter : IConnectorRouter
{
    private readonly IReadOnlyDictionary<ConnectorKind, IConnectorRouter> _routers;

    /// <summary>
    /// 建立不可變 Connector Router 快照。建構期間會驗證每個 ConnectorKind 與子 Router，完成後不再
    /// 讀取呼叫端 dictionary；如此設定 reload 或呼叫端修改不會在同一 generation 內漂移路由。
    /// 不相容的 CE/profile 關係仍由 ProfileResolver 與子 Router 驗證，這裡不猜測或建立 fallback。
    /// </summary>
    /// <param name="routers">每個部署端 ConnectorKind 對應的已註冊 Router。</param>
    /// <exception cref="ArgumentNullException">routers 或任一子 Router 為 null。</exception>
    /// <exception cref="ArgumentException">包含未知 ConnectorKind 的註冊。</exception>
    public CompositeConnectorRouter(
        IReadOnlyDictionary<ConnectorKind, IConnectorRouter> routers)
    {
        ArgumentNullException.ThrowIfNull(routers);

        var snapshot = new Dictionary<ConnectorKind, IConnectorRouter>();
        foreach (var pair in routers)
        {
            if (!Enum.IsDefined(pair.Key))
            {
                throw new ArgumentException(
                    "Connector router registration contains an unknown connector kind.",
                    nameof(routers));
            }

            snapshot.Add(
                pair.Key,
                pair.Value ?? throw new ArgumentNullException(nameof(routers)));
        }

        _routers = new ReadOnlyDictionary<ConnectorKind, IConnectorRouter>(snapshot);
    }

    /// <summary>
    /// 依 immutable Profile 的 ConnectorKind 選取子 Router，並確認子 Router 回傳的 Pool 仍與
    /// Profile 的 Alias／Generation 完全一致。未登錄、未知或回傳錯誤 generation 時立即 fail closed；
    /// 絕不嘗試 Data8、另一個 Official Worker、Embedded、Dedicated 或其他 transport 作為 fallback。
    /// </summary>
    /// <param name="profile">已由部署端 resolver 驗證的 immutable Profile snapshot。</param>
    /// <returns>與指定 ConnectorKind、ProfileAlias、GenerationId 對應的 Pool。</returns>
    /// <exception cref="ArgumentNullException">profile 為 null。</exception>
    /// <exception cref="NotSupportedException">ConnectorKind 未註冊或未知。</exception>
    /// <exception cref="InvalidOperationException">子 Router 回傳 null 或錯誤的 generation pool。</exception>
    public IConnectorPool Resolve(ResolvedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!Enum.IsDefined(profile.ConnectorKind) ||
            !_routers.TryGetValue(profile.ConnectorKind, out var childRouter))
        {
            throw new NotSupportedException(
                "The resolved connector kind is not registered; no connector fallback exists.");
        }

        var pool = childRouter.Resolve(profile) ??
            throw new InvalidOperationException("The registered connector router returned no pool.");
        if (!string.Equals(pool.ProfileAlias, profile.ProfileAlias, StringComparison.Ordinal) ||
            pool.GenerationId != profile.GenerationId)
        {
            throw new InvalidOperationException(
                "The registered connector router returned a pool for a different profile generation.");
        }

        return pool;
    }
}
