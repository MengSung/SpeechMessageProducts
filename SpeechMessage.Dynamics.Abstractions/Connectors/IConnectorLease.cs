namespace SpeechMessage.Dynamics.Abstractions.Connectors;

/// <summary>
/// 表示由單一 Profile Generation 擁有的一次 Connector 使用權。
/// Lease 是 Client 與 Organization Admission Permit 的唯一生命週期擁有者；呼叫端必須以
/// <c>await using</c> 或 finally 確定釋放。實作必須保證釋放動作冪等，並且不保存使用者、Session、
/// Credential、Token 或請求內容，避免跨 Profile、Organization 或請求的狀態洩漏。
/// </summary>
public interface IConnectorLease : IAsyncDisposable, IDisposable
{
    /// <summary>取得建立此 Lease 的部署端 Profile Alias。</summary>
    string ProfileAlias { get; }

    /// <summary>取得建立此 Lease 的不可變 Profile Generation 識別碼。</summary>
    long GenerationId { get; }

    /// <summary>
    /// 以受 Lease 管控的方式執行已核准的 Connector 作業。
    /// 實作會以作業截止時間建立短生命週期的 linked cancellation token source，並在方法結束時釋放它；
    /// 遇到取消、逾時或傳輸例外時，必須將 Lease 標記為故障，使後續 Dispose 淘汰 Client 而非歸還 idle pool。
    /// 這個方法刻意不暴露原始 Client，避免呼叫端繞過故障標記與資源隔離契約。
    /// </summary>
    Task<ConnectorOperationResult> ExecuteAsync(
        ConnectorOperation operation,
        CancellationToken cancellationToken);

    /// <summary>
    /// 將 Lease 標記為故障；Dispose 時 Client 必須被確定 Dispose，絕不可重新進入 idle pool。
    /// <paramref name="cause"/> 僅供呼叫端語意表達，實作不得保留例外物件，避免例外圖保留端點、Credential
    /// 或其他敏感資源。
    /// </summary>
    void MarkFaulted(Exception? cause = null);
}
