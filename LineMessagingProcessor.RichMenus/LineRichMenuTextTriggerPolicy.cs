namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將使用者輸入文字轉成 RichMenu 指派決策的共用 policy。
/// 文字對照表由產品在 options 中設定；此型別只負責解析文字並回傳 menu key 決策。
/// </summary>
public sealed class LineRichMenuTextTriggerPolicy : IRichMenuPolicy
{
    private readonly ILineRichMenuTextTriggerResolver _resolver;

    public LineRichMenuTextTriggerPolicy(ILineRichMenuTextTriggerResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

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
