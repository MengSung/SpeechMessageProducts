// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/RichMenuContext.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenuContext
// 主要成員：LineUserId、Roles、ReceivedText、CurrentMenuKey
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 攜帶 RichMenu policies 做 decision 時可能需要的所有使用者與訊息事實。
/// context 刻意使用角色、屬性等應用程式概念，讓 policies 不必直接依賴資料庫 entity 或 LINE SDK payload 型別。
/// </summary>
public sealed class RichMenuContext
{
    /// <summary>
    /// 建立單次 LINE 使用者互動的 policy evaluation context。
    /// </summary>
    /// <param name="lineUserId">正在評估的 LINE userId。</param>
    /// <param name="roles">選填角色名稱，供 role-based policies 使用。</param>
    /// <param name="receivedText">選填 LINE 傳入文字，通常供 trigger policies 使用。</param>
    /// <param name="currentMenuKey">選填目前已指派給使用者的應用程式層級 menu key。</param>
    /// <param name="attributes">選填額外 key/value 事實，供自訂 policies 使用。</param>
    public RichMenuContext(
        string lineUserId,
        IReadOnlySet<string>? roles = null,
        string? receivedText = null,
        string? currentMenuKey = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(lineUserId))
        {
            throw new ArgumentException("LINE user id is required.", nameof(lineUserId));
        }

        LineUserId = lineUserId.Trim();
        Roles = roles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReceivedText = receivedText;
        CurrentMenuKey = currentMenuKey;
        Attributes = attributes ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// 取得將傳入 assignment 與 unlink workflows 的 LINE userId。
    /// </summary>
    public string LineUserId { get; }

    /// <summary>
    /// 取得 policy implementations 可使用的角色名稱。
    /// 預設 comparer 不分大小寫，避免應用程式角色大小寫差異影響 decisions。
    /// </summary>
    public IReadOnlySet<string> Roles { get; }

    /// <summary>
    /// 取得可能觸發 RichMenu 切換的訊息文字。
    /// </summary>
    public string? ReceivedText { get; }

    /// <summary>
    /// 取得應用程式目前已知的 menu key。
    /// </summary>
    public string? CurrentMenuKey { get; }

    /// <summary>
    /// 取得應用程式提供給自訂 policy logic 使用的額外事實。
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }
}
