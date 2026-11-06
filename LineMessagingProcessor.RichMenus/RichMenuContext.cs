/*
 * 檔案整體說明
 *
 * 目的與定位：攜帶 RichMenu policies 做 decision 時可能需要的所有使用者與訊息事實。 context 刻意使用角色、屬性等應用程式概念，讓 policies 不必直接依賴資料庫 entity 或 
 * LINE SDK payload 型別。
 *
 * 主要功能與流程：
 * 本檔宣告 RichMenuContext。
 * - 建立單次 LINE 使用者互動的 policy evaluation context。
 * - 取得將傳入 assignment 與 unlink workflows 的 LINE userId。
 * - 取得 policy implementations 可使用的角色名稱。 預設 comparer 不分大小寫，避免應用程式角色大小寫差異影響 decisions。
 * - 取得應用程式提供給自訂 policy logic 使用的額外事實。
 * 資料／選項欄位包含 RichMenuContext、LineUserId、Roles、ReceivedText、CurrentMenuKey、Attributes，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs、
 * LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs、LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs、
 * LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs 追查目前工作樹中的合作／使用位置。
 */
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
