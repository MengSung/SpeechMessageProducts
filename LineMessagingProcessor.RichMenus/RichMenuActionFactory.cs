// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenuActionFactory
// 主要成員：SwitchToAlias
// 引用命名空間：Line.Messaging
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 建立可在 RichMenu aliases 之間切換使用者選單的 LINE template actions。
/// 將 action 建立集中在此處，可讓需要 LINE <c>richmenuswitch</c> action type 的應用程式選單，
/// 共用一致的 alias 驗證與 postback data 驗證規則。
/// </summary>
public static class RichMenuActionFactory
{
    /// <summary>
    /// 建立指向指定 alias id 的 RichMenu switch action。
    /// </summary>
    /// <param name="aliasId">provisioning 期間設定的 LINE RichMenu alias id。</param>
    /// <param name="data">使用者點擊 action 時，LINE webhook 回傳的 postback data。</param>
    /// <param name="label">選填標籤，供會顯示 action 文字的 client 使用。</param>
    public static RichMenuSwitchTemplateAction SwitchToAlias(string aliasId, string data, string? label = null)
        => Switch(aliasId, data, label);

    /// <summary>
    /// 建立已驗證的 <see cref="RichMenuSwitchTemplateAction"/>。
    /// </summary>
    /// <param name="aliasId">LINE 會解析成目前 richMenuId 的 alias id。</param>
    /// <param name="data">必要的 postback data payload。</param>
    /// <param name="label">選填顯示標籤；未提供時會送出空字串。</param>
    public static RichMenuSwitchTemplateAction Switch(string aliasId, string data, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(aliasId))
        {
            throw new ArgumentException("Alias id is required.", nameof(aliasId));
        }

        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentException("Postback data is required.", nameof(data));
        }

        return new RichMenuSwitchTemplateAction(aliasId.Trim(), data.Trim(), label ?? string.Empty);
    }
}
