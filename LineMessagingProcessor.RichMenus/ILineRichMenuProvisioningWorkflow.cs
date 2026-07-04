namespace LineMessagingProcessor.RichMenus;

public interface ILineRichMenuProvisioningWorkflow
{
    Task<LineRichMenuSyncReport> SyncAsync(CancellationToken cancellationToken = default);
}
