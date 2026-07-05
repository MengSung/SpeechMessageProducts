namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 彙總一次針對 RichMenu 使用者狀態的到期 sweep。
/// report 刻意只公開計數，讓呼叫端可記錄或監控 sweep 成效，
/// 而不需要依賴特定 state store 的 record shape。
/// </summary>
public sealed class RichMenuExpirationSweepReport
{
    /// <summary>
    /// 建立 sweep report，包含掃描與成功還原的紀錄數。
    /// </summary>
    /// <param name="scannedCount">state store 回傳的已到期狀態紀錄數。</param>
    /// <param name="restoredCount">成功還原或解除指派的紀錄數。</param>
    public RichMenuExpirationSweepReport(int scannedCount, int restoredCount)
    {
        ScannedCount = scannedCount;
        RestoredCount = restoredCount;
    }

    /// <summary>
    /// 取得 sweep 期間掃描到的已到期紀錄數。
    /// </summary>
    public int ScannedCount { get; }

    /// <summary>
    /// 取得掃描紀錄中成功完成 RichMenu 還原或 unlink 的數量。
    /// </summary>
    public int RestoredCount { get; }
}
