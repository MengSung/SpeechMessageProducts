// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuAliasNotFoundException
// 主要成員：RichMenuAliasId
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 表示 LINE 沒有回傳指定 alias id 的 RichMenu alias。
/// 專用例外讓 provisioning 程式可以分辨「可建立的 missing alias」與「應回報為同步錯誤的其他 provider failure」。
/// </summary>
public sealed class LineRichMenuAliasNotFoundException : Exception
{
    /// <summary>
    /// 建立指定 LINE RichMenu alias id 不存在的例外。
    /// </summary>
    /// <param name="richMenuAliasId">向 LINE 查詢的 alias id。</param>
    public LineRichMenuAliasNotFoundException(string richMenuAliasId)
        : base($"RichMenu alias '{richMenuAliasId}' was not found.")
    {
        RichMenuAliasId = richMenuAliasId;
    }

    /// <summary>
    /// 取得不存在的 LINE RichMenu alias id。
    /// </summary>
    public string RichMenuAliasId { get; }
}
