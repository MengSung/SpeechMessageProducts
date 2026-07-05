// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs
// 所屬區塊：LINE Messaging Processor 的 ASP.NET Core DI 與整合測試區塊。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineMessagingProcessorOptions
// 主要成員：ChannelAccessToken、ApiBaseUri
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.AspNetCore;

/// <summary>
/// ASP.NET Core 專案註冊 LINE processor 時使用的選項。只放 LINE 技術設定，不放任何產品業務設定。
/// </summary>
public sealed class LineMessagingProcessorOptions
{
    public string ChannelAccessToken { get; set; } = string.Empty;

    public string ApiBaseUri { get; set; } = "https://api.line.me/v2";
}
