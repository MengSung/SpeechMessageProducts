namespace LineMessagingProcessor.RichMenus;

public interface IRichMenuOrchestrator
{
    Task<LineRichMenuAssignmentResult> ApplyAsync(RichMenuContext context, CancellationToken cancellationToken = default);
}
