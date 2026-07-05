// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/RichMenuUserState.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenuUserState
// 主要成員：LineUserId、CurrentMenuKey、PreviousMenuKey、ExpiresAt、UpdatedAt
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 保存單一 LINE 使用者的 RichMenu 指派狀態。
/// 此狀態讓暫時性選單指派可在稍後還原或移除，
/// 不必向 LINE 查詢只有應用程式才知道的業務 context。
/// </summary>
public sealed class RichMenuUserState
{
    /// <summary>
    /// 建立描述使用者目前 RichMenu 與選填到期時間的 state record。
    /// </summary>
    /// <param name="lineUserId">擁有此 state record 的 LINE userId。</param>
    /// <param name="currentMenuKey">目前指派給使用者的應用程式層級 menu key。</param>
    /// <param name="previousMenuKey">到期後要還原的應用程式層級 menu key；若無則為 null。</param>
    /// <param name="expiresAt">目前指派到期的 UTC-aware 時間點。</param>
    /// <param name="updatedAt">此 state record 最後寫入的時間。</param>
    public RichMenuUserState(
        string lineUserId,
        string currentMenuKey,
        string? previousMenuKey,
        DateTimeOffset? expiresAt,
        DateTimeOffset updatedAt)
    {
        LineUserId = lineUserId;
        CurrentMenuKey = currentMenuKey;
        PreviousMenuKey = previousMenuKey;
        ExpiresAt = expiresAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// 取得 LINE Messaging API link/unlink 操作使用的 LINE userId。
    /// </summary>
    public string LineUserId { get; }

    /// <summary>
    /// 取得應用程式目前視為此使用者作用中的 menu key。
    /// 此 key 稍後會透過 <see cref="ILineRichMenuIdCache"/> 解析成 provider richMenuId。
    /// </summary>
    public string CurrentMenuKey { get; }

    /// <summary>
    /// 取得 <see cref="ExpiresAt"/> 經過後應還原的 menu key。
    /// null 代表應解除使用者連結，讓使用者回到 LINE 預設行為。
    /// </summary>
    public string? PreviousMenuKey { get; }

    /// <summary>
    /// 取得暫時性指派的到期時間。
    /// 永久指派會將此值保留為 null，並被 expiration sweeps 忽略。
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// 取得此指派狀態最後更新時間。
    /// 此欄位可供 audit logs 使用，也可給需要可預期排序欄位的 store 使用。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }
}
