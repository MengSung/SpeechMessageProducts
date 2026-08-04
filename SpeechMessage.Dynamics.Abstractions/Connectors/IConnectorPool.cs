namespace SpeechMessage.Dynamics.Abstractions.Connectors;

/// <summary>
/// 定義由單一 <c>(ProfileAlias, GenerationId)</c> 隔離的 Connector Pool。
/// Pool 只可保存該世代的可重用 Client；它不可保存請求、使用者、OrganizationId、Credential、Token 或 Session，
/// 並且必須把 Organization 層級容量完全委派給既有 Admission Manager。
/// </summary>
public interface IConnectorPool : IAsyncDisposable, IDisposable
{
    /// <summary>取得此 Pool 所屬的部署端 Profile Alias。</summary>
    string ProfileAlias { get; }

    /// <summary>取得此 Pool 所屬的不可變 Profile Generation 識別碼。</summary>
    long GenerationId { get; }

    /// <summary>
    /// 指出此 Pool 是否已開始 Drain。開始 Drain 後必須 fail closed 拒絕新 Lease；
    /// 已持有的 Lease 可在其既有截止時間內結束，並在歸還時淘汰 Client。
    /// </summary>
    bool IsDraining { get; }

    /// <summary>
    /// 在既有 Organization Admission Manager 成功核發 Permit 後取得一個 Lease。
    /// 若建立、取消、逾時或 Drain 發生，實作必須回復 local slot、Dispose 暫存 Client 並釋放 Permit。
    /// </summary>
    Task<IConnectorLease> AcquireAsync(ConnectorOperation operation, CancellationToken cancellationToken);

    /// <summary>
    /// 開始確定性的世代 Drain：先拒絕新 Lease，再等待現有 Lease 釋放，最後 Dispose 全部 idle Client。
    /// 呼叫端提供的取消只取消等待，不可撤銷已啟動的 Drain 或遺留背景資源。
    /// </summary>
    Task DrainAsync(CancellationToken cancellationToken = default);
}
