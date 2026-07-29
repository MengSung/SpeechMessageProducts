using SpeechMessage.Dynamics.WebApi.Capacity;

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
        await _coordinator.VerifySchemaAsync(cancellationToken).ConfigureAwait(false);
        await _admissionManager.EnsureHostSlotAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
