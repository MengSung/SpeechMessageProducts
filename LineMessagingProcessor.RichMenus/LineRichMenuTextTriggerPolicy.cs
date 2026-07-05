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
