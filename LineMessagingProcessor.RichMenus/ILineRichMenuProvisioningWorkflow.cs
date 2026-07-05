namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將應用程式 RichMenu catalog 與 LINE provider 狀態同步。
/// </summary>
public interface ILineRichMenuProvisioningWorkflow
{
    /// <summary>
    /// 依目前 catalog 建立、重用、設定 alias、設定預設值，並快取 RichMenu 對照。
    /// </summary>
    /// <param name="cancellationToken">供 LINE API 與 catalog 操作使用的取消權杖。</param>
    Task<LineRichMenuSyncReport> SyncAsync(CancellationToken cancellationToken = default);
}
