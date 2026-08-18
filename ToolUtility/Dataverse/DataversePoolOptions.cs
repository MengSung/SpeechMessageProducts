using System;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Keyed bounded pool 的容量、逾時、閒置回收與健康檢查設定。
/// 設定物件本身不擁有連線；連線的最長生命週期由 pool 的 shutdown 或淘汰路徑決定。
/// </summary>
public sealed class DataversePoolOptions
{
    /// <summary>每個子池在首次使用時預熱的最小 client 數量。</summary>
    public int MinSize { get; set; } = 3;

    /// <summary>每個子池允許同時租借的最大 client 數量。</summary>
    public int MaxN { get; set; } = 20;

    /// <summary>等待可用 client 的最長時間。</summary>
    public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>idle client 超過此時間且池高於 MinSize 時會被淘汰。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>idle client 超過此時間未驗證時，下一次出借前執行 WhoAmI 健康檢查。</summary>
    public TimeSpan HealthInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>驗證設定可供 pool 在建構時 fail fast。</summary>
    public void Validate()
    {
        if (MinSize < 1)
            throw new ArgumentOutOfRangeException(nameof(MinSize), "MinSize 必須至少為 1。 ");
        if (MaxN < MinSize)
            throw new ArgumentOutOfRangeException(nameof(MaxN), "MaxN 必須大於或等於 MinSize。 ");
        if (AcquireTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(AcquireTimeout));
        if (IdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(IdleTimeout));
        if (HealthInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HealthInterval));
    }
}
