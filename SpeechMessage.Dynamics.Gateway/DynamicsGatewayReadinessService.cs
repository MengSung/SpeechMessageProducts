using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 將 Gateway readiness 與 durable coordinator schema、runtime-host slot 的安全狀態綁定。
/// 啟動必須先確認 control-plane schema，再取得能容納最大 outbound 工作生命週期的租約；任一步失敗都讓 Host 啟動失敗，
/// 避免在沒有跨主機容量 fencing 的情況下接收流量。停止時由 admission manager 排空工作並確定釋放續租 Task 與 lease。
/// </summary>
public sealed class DynamicsGatewayReadinessService : IHostedService
{
    private readonly SqlRuntimeHostSlotCoordinator _coordinator;
    private readonly IDynamicsProfileRuntimeManager _runtimeManager;

    /// <summary>
    /// 建立 Gateway readiness 啟動服務。SQL Coordinator 由 Host 唯一擁有，Runtime Manager 由 DI singleton 擁有；
    /// 本服務只排序啟動／停止，不直接 Dispose Client、Handler、Token Provider 或 Admission Manager。
    /// </summary>
    public DynamicsGatewayReadinessService(
        SqlRuntimeHostSlotCoordinator coordinator,
        IDynamicsProfileRuntimeManager runtimeManager)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
    }

    /// <summary>
    /// 先驗證 durable SQL schema，再初始化所有 Profile Generation 並取得各 Canonical Organization 的 Host Slot。
    /// 任一步失敗都讓 Host 啟動失敗，避免沒有 fencing／aggregate capacity 的 Gateway 被標記 Ready。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // VerifySchemaAsync 只驗證、不隱性建表；成功後才允許取得 host slot，避免錯誤環境被應用程式自行「修好」後上線。
        await _coordinator.VerifySchemaAsync(cancellationToken).ConfigureAwait(false);
        await _runtimeManager.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 停止接受新路由，等待所有 Active／Draining Generation 的 Execution Lease 排空，
    /// 再依 Runtime→Admission Registration→Host Slot 的 ownership 順序確定性清理；重複呼叫由 Manager 保證冪等。
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
        => _runtimeManager.DisposeAsync().AsTask();
}
