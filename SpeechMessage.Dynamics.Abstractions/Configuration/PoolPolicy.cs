namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 宣告一個 Profile generation 的連線/Worker Pool 邊界。數值由 Resolver 驗證後複製到
/// 不可變 <see cref="ResolvedProfile"/>；Pool 實作者不可保存這個可變 options 參考，否則
/// 組態變更可能造成並行請求共用錯誤大小或 timeout，導致資源與 Session 隔離失效。
/// </summary>
public sealed class PoolPolicy
{
    /// <summary>預熱的最小資源數；零代表不主動建立連線。</summary>
    public int MinSize { get; set; } = 0;

    /// <summary>同一 Profile generation 可同時持有的最大資源數。</summary>
    public int MaxSize { get; set; } = 20;

    /// <summary>閒置資源在被決定性處置前可保留的分鐘數。</summary>
    public int IdleTimeoutMinutes { get; set; } = 10;

    /// <summary>等待可用 Lease 的上限秒數；超時不得留下 queue entry 或 Permit。</summary>
    public int AcquireTimeoutSeconds { get; set; } = 15;

    /// <summary>取得 Lease 時是否執行有界健康檢查；失敗的 Lease 必須逐出，不可歸還 Pool。</summary>
    public bool HealthCheckOnAcquire { get; set; } = true;
}
