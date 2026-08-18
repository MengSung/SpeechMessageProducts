namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Bounded pool 的不可變快照。快照只包含計數，不保留 client、租約或使用者資料，
/// 因此可安全由診斷端點讀取且不延長任何連線生命週期。
/// </summary>
public sealed class DataversePoolMetrics
{
    /// <summary>目前 idle client 數。</summary>
    public int Idle { get; init; }

    /// <summary>目前 leased client 數。</summary>
    public int Leased { get; init; }

    /// <summary>累計被標記為故障的 client 數。</summary>
    public long Faulted { get; init; }

    /// <summary>目前等待租借的請求數。</summary>
    public int Waiting { get; init; }

    /// <summary>累計 Acquire 逾時次數。</summary>
    public long AcquireTimeouts { get; init; }

    /// <summary>累計建立的 client 數。</summary>
    public long Created { get; init; }

    /// <summary>累計因故障、健康檢查、閒置或 shutdown 而淘汰的 client 數。</summary>
    public long Discarded { get; init; }

    /// <summary>累計成功取得租約次數。</summary>
    public long TotalAcquires { get; init; }

    /// <summary>累計成功歸還租約次數。</summary>
    public long TotalReleases { get; init; }

    /// <summary>目前 keyed 子池數。</summary>
    public int SubPoolCount { get; init; }
}
