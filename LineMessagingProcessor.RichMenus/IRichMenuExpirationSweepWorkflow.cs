namespace LineMessagingProcessor.RichMenus;

public interface IRichMenuExpirationSweepWorkflow
{
    Task<RichMenuExpirationSweepReport> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
