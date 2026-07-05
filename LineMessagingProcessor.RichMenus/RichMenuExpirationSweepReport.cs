// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenuExpirationSweepReport
// 主要成員：ScannedCount、RestoredCount
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
