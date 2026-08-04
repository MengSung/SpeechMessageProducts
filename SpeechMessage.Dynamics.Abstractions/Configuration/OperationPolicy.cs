namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 宣告 Profile generation 的單一 operation 時限與重試邊界。後續 Connector 必須以每次
/// 呼叫擁有的 CancellationTokenSource 執行，並在 finally dispose；此設定本身不持有 CTS、
/// Timer、Task 或 Session，故不能成為資源洩漏來源。
/// </summary>
public sealed class OperationPolicy
{
    /// <summary>單一 outbound operation 的最大秒數。</summary>
    public int TimeoutSeconds { get; set; } = 35;

    /// <summary>符合 operation registry 規則時可執行的最大重試次數。</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>第一個重試延遲的毫秒數；後續策略仍須受總 timeout 限制。</summary>
    public int RetryBaseDelayMs { get; set; } = 200;
}
