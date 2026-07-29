using SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// 將 Gateway readiness 與 durable coordinator schema、runtime-host slot 的安全狀態綁定。
/// 啟動必須先確認 control-plane schema，再取得能容納最大 outbound 工作生命週期的租約；任一步失敗都讓 Host 啟動失敗，
/// 避免在沒有跨主機容量 fencing 的情況下接收流量。停止時由 admission manager 排空工作並確定釋放續租 Task 與 lease。
/// </summary>
public sealed class DynamicsGatewayReadinessService : IHostedService
{
    private readonly SqlRuntimeHostSlotCoordinator _coordinator;
    private readonly IOrganizationAdmissionManager _admissionManager;

    public DynamicsGatewayReadinessService(
        SqlRuntimeHostSlotCoordinator coordinator,
        IOrganizationAdmissionManager admissionManager)
    {
        _coordinator = coordinator;
        _admissionManager = admissionManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // VerifySchemaAsync 只驗證、不隱性建表；成功後才允許取得 host slot，避免錯誤環境被應用程式自行「修好」後上線。
        await _coordinator.VerifySchemaAsync(cancellationToken).ConfigureAwait(false);
        await _admissionManager.EnsureHostSlotAsync(cancellationToken).ConfigureAwait(false);
    }

    // DI 容器可能稍後再次 Dispose singleton；OrganizationAdmissionManager 內部以單一 shutdown Task 保證重複關閉仍為冪等。
    public Task StopAsync(CancellationToken cancellationToken)
        => _admissionManager.DisposeAsync().AsTask();
}
