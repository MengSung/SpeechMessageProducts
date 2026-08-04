namespace SpeechMessage.Dynamics.Abstractions.Connectors;

/// <summary>
/// 定義不洩漏 CRM SDK 型別的 Connector Client 邊界。
/// Client 由 Generation-owned Pool 建立與 Dispose，不能由產品端、請求端或跨 Profile 共用；
/// 若實作持有連線、WCF Channel、Handler 或非受控資源，<see cref="DisposeAsync"/> 必須是其唯一且確定的釋放路徑。
/// </summary>
public interface IConnectorClient : IAsyncDisposable
{
    /// <summary>
    /// 執行已核准的作業。實作必須遵守取消與截止時間、不得保存作業參數，且在傳輸健康狀態不明時拋出例外，
    /// 由 Lease 將 Client 淘汰，避免不可靠連線回到 idle pool。
    /// </summary>
    Task<ConnectorOperationResult> ExecuteAsync(
        ConnectorOperation operation,
        CancellationToken cancellationToken);
}
