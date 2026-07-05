// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuTextTriggerPolicy
// 主要成員：DecideAsync
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將使用者輸入文字轉成 RichMenu 指派決策的共用 policy。
/// 文字對照表由產品在 options 中設定；此型別只負責解析文字並回傳 menu key 決策。
/// </summary>
public sealed class LineRichMenuTextTriggerPolicy : IRichMenuPolicy
{
    private readonly ILineRichMenuTextTriggerResolver _resolver;

    /// <summary>
    /// 建立文字觸發 policy。
    /// </summary>
    /// <param name="resolver">將收到的 LINE 文字解析成 menu key 的 resolver。</param>
    public LineRichMenuTextTriggerPolicy(ILineRichMenuTextTriggerResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>
    /// 若收到的文字命中設定表，回傳高優先權的 RichMenu 指派決策。
    /// </summary>
    /// <param name="context">包含 received text 的使用者互動上下文。</param>
    /// <param name="cancellationToken">此 in-memory policy 目前不使用，保留以符合 policy 介面。</param>
    public Task<RichMenuDecision> DecideAsync(RichMenuContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return Task.FromResult(_resolver.TryResolve(context.ReceivedText, out var menuKey)
            ? RichMenuDecision.Assign(menuKey, RichMenuDecisionPriority.TextTrigger, "text-trigger")
            : RichMenuDecision.None);
    }
}
