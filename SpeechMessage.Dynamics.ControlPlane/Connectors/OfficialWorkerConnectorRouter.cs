using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.ControlPlane.Connectors;

/// <summary>
/// 將單一已驗證的 Official CRM Worker Pool 綁定到它唯一可服務的 ConnectorKind 與 CE 版本。
/// Router 不擁有傳入的 Pool，因此不會重複 Dispose generation-owned Worker、Process、Pipe、Admission
/// permit 或 credential owner。它只接受 ProfileResolver 的 immutable snapshot，絕不讀取 request 來決定
/// SDK、CE version、endpoint、OrganizationId 或 fallback。
/// </summary>
public sealed class OfficialWorkerConnectorRouter : IConnectorRouter
{
    private readonly ConnectorKind _connectorKind;
    private readonly CeVersion _ceVersion;
    private readonly IConnectorPool _pool;

    /// <summary>
    /// 建立只服務一種 Official Worker 的 Router。建構時拒絕 Data8 或未知 kind，確保 Official 8.2 與
    /// Official 9.1 必須各自擁有獨立的 Router/Pool generation，而不是共用 SDK assembly、process、pipe
    /// 或 mutable runtime state。
    /// </summary>
    /// <param name="connectorKind">已由部署設定固定的 Official Worker ConnectorKind。</param>
    /// <param name="pool">相同 profile alias 與 generation 的已登錄 Worker Pool。</param>
    /// <exception cref="ArgumentOutOfRangeException">connectorKind 不是官方 8.2／9.1 Worker。</exception>
    /// <exception cref="ArgumentNullException">pool 為 null。</exception>
    public OfficialWorkerConnectorRouter(
        ConnectorKind connectorKind,
        IConnectorPool pool)
    {
        _connectorKind = connectorKind;
        _ceVersion = GetRequiredCeVersion(connectorKind);
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <summary>
    /// 只在 Profile 同時符合預期 ConnectorKind、CE version、alias 與 generation 時回傳 Pool。
    /// 任一不符都在 Acquire 前 fail closed，因而不會取得 admission permit、啟動 Worker、開啟 pipe 或
    /// 接觸 credential；也絕不改用 Data8、另一個 Official Worker 或任何 Embedded/Gateway transport。
    /// </summary>
    /// <param name="profile">部署端 resolver 輸出的 immutable Profile snapshot。</param>
    /// <returns>與 Profile 完全相同 generation 的 Official Worker Pool。</returns>
    /// <exception cref="ArgumentNullException">profile 為 null。</exception>
    /// <exception cref="NotSupportedException">ConnectorKind 或 CE version 不相容。</exception>
    /// <exception cref="KeyNotFoundException">Pool 不屬於 Profile 的 alias 或 generation。</exception>
    public IConnectorPool Resolve(ResolvedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ConnectorKind != _connectorKind || profile.CeVersion != _ceVersion)
        {
            throw new NotSupportedException(
                "The resolved profile is not compatible with this official worker connector.");
        }

        if (!string.Equals(profile.ProfileAlias, _pool.ProfileAlias, StringComparison.Ordinal) ||
            profile.GenerationId != _pool.GenerationId)
        {
            throw new KeyNotFoundException(
                "No official worker pool is registered for the resolved profile generation.");
        }

        return _pool;
    }

    /// <summary>
    /// 將兩個允許的 Official Worker kind 對應到它們唯一可服務的 CE version。未知值不使用預設或
    /// 猜測的版本，避免新 enum 值在沒有 package-lock／lifecycle 證據時被無聲納入既有 Worker 路徑。
    /// </summary>
    /// <param name="connectorKind">欲登錄的 ConnectorKind。</param>
    /// <returns>此 Worker 可服務的唯一 CE version。</returns>
    /// <exception cref="ArgumentOutOfRangeException">connectorKind 不是受支援的 Official Worker。</exception>
    private static CeVersion GetRequiredCeVersion(ConnectorKind connectorKind)
        => connectorKind switch
        {
            ConnectorKind.OfficialCrm82Worker => CeVersion.Ce82,
            ConnectorKind.OfficialCrm91Worker => CeVersion.Ce91,
            _ => throw new ArgumentOutOfRangeException(
                nameof(connectorKind),
                connectorKind,
                "Only Official CRM 8.2 and 9.1 worker connectors can be registered.")
        };
}
