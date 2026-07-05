// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/RichMenuDecision.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenuDecision
// 主要成員：Assign、Remove、MenuKey、Unlink、Priority、Ttl、Reason、None
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 表示 RichMenu policy 在呼叫 LINE API 前回傳的 outcome。
/// decision 仍維持 provider-neutral：使用應用程式 menu key、priority、選填 TTL 與 reason，
/// 讓 orchestrator 能可預期地挑出勝出的 action。
/// </summary>
public sealed class RichMenuDecision
{
    /// <summary>
    /// 建立標準化 policy decision。
    /// 使用 static factory methods 可讓 assign 與 unlink 語意保持明確。
    /// </summary>
    private RichMenuDecision(string? menuKey, bool unlink, RichMenuDecisionPriority priority, TimeSpan? ttl, string reason)
    {
        MenuKey = menuKey;
        Unlink = unlink;
        Priority = priority;
        Ttl = ttl;
        Reason = reason;
    }

    /// <summary>
    /// 取得要指派的應用程式層級 menu key。
    /// no-op 與 unlink decisions 會是 null。
    /// </summary>
    public string? MenuKey { get; }

    /// <summary>
    /// 取得此 decision 是否要求 orchestrator 移除使用者 RichMenu 連結。
    /// </summary>
    public bool Unlink { get; }

    /// <summary>
    /// 取得多個 policies 回傳競爭 actions 時用來比較的 decision priority。
    /// </summary>
    public RichMenuDecisionPriority Priority { get; }

    /// <summary>
    /// 取得此指派的選填有效期限。
    /// 若有提供，assignment workflow 會寫入 state，讓後續 sweep 能還原前一個選單。
    /// </summary>
    public TimeSpan? Ttl { get; }

    /// <summary>
    /// 取得供診斷、log 與測試斷言使用的可讀 reason。
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// 取得可重用 decision，表示 orchestrator 應保持目前 RichMenu 不變。
    /// </summary>
    public static RichMenuDecision None { get; } = new(null, false, RichMenuDecisionPriority.None, null, "none");

    /// <summary>
    /// 建立指派指定應用程式 menu key 的 decision。
    /// </summary>
    /// <param name="menuKey">稍後會解析成 LINE richMenuId 的應用程式層級 key。</param>
    /// <param name="priority">此 decision 相對其他 policies 的強度。</param>
    /// <param name="reason">說明 policy 為何選擇此選單的診斷 reason。</param>
    /// <param name="ttl">此指派的選填暫時有效期限。</param>
    public static RichMenuDecision Assign(string menuKey, RichMenuDecisionPriority priority, string reason, TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(menuKey))
        {
            throw new ArgumentException("Menu key is required.", nameof(menuKey));
        }

        return new RichMenuDecision(menuKey.Trim(), false, priority, ttl, string.IsNullOrWhiteSpace(reason) ? "assign" : reason.Trim());
    }

    /// <summary>
    /// 建立移除使用者目前 RichMenu 指派的 decision。
    /// </summary>
    /// <param name="priority">此 unlink decision 相對其他 policies 的強度。</param>
    /// <param name="reason">說明 policy 為何要求 unlink 的診斷 reason。</param>
    public static RichMenuDecision Remove(RichMenuDecisionPriority priority, string reason)
        => new(null, true, priority, null, string.IsNullOrWhiteSpace(reason) ? "unlink" : reason.Trim());
}
