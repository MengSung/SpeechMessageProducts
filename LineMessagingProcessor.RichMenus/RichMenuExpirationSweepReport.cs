/*
 * 檔案整體說明
 *
 * 目的與定位：彙總一次針對 RichMenu 使用者狀態的到期 sweep。 report 刻意只公開計數，讓呼叫端可記錄或監控 sweep 成效， 而不需要依賴特定 state store 的 record shape。
 *
 * 主要功能與流程：
 * 本檔宣告 RichMenuExpirationSweepReport。
 * - 建立 sweep report，包含掃描與成功還原的紀錄數。
 * - 取得掃描紀錄中成功完成 RichMenu 還原或 unlink 的數量。
 * 資料／選項欄位包含 RichMenuExpirationSweepReport、ScannedCount、RestoredCount，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs、
 * LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
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
