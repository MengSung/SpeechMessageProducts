using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 將唯一已登錄的 Data8 Pool 對應至相同的 Profile Generation。
/// 這是部署端 ConnectorKind 的 fail-closed 邊界：非 Data8 Profile、Alias 不符或世代不符時絕不 fallback，
/// 因而不會讓要求改變 Connector、端點、Credential 或跨 Profile 取得連線。
/// </summary>
public sealed class Data8ConnectorRouter : IConnectorRouter
{
    private readonly IConnectorPool _pool;

    /// <summary>
    /// 建立只管理單一已驗證 Data8 Generation 的 Router。
    /// Pool 的生命週期由組合根或 Registry 擁有；Router 不會 Dispose 外部傳入的 Pool，避免雙重釋放。
    /// </summary>
    public Data8ConnectorRouter(IConnectorPool pool)
        => _pool = pool ?? throw new ArgumentNullException(nameof(pool));

    /// <summary>
    /// 僅在已解析 Profile 指定 Data8 且 Alias、Generation 完全相同時回傳 Pool。
    /// 未登錄 ConnectorKind 必須讓上層明確處理，不能以 Data8 作為隱性相容性或容錯路徑。
    /// </summary>
    public IConnectorPool Resolve(ResolvedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ConnectorKind != ConnectorKind.Data8)
        {
            throw new NotSupportedException("The resolved profile does not select the Data8 connector.");
        }

        if (!string.Equals(profile.ProfileAlias, _pool.ProfileAlias, StringComparison.Ordinal) ||
            profile.GenerationId != _pool.GenerationId)
        {
            throw new KeyNotFoundException("No Data8 pool is registered for the resolved profile generation.");
        }

        return _pool;
    }
}
