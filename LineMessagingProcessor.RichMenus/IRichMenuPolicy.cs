namespace LineMessagingProcessor.RichMenus;

public interface IRichMenuPolicy
{
    Task<RichMenuDecision> DecideAsync(RichMenuContext context, CancellationToken cancellationToken = default);
}
