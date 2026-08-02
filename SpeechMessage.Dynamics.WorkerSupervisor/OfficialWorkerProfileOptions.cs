namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// Immutable deployment-owned bootstrap configuration for one worker profile generation.
/// It contains no CRM endpoint, credential, token, cookie, connection string, or product session.
/// </summary>
public sealed class OfficialWorkerProfileOptions
{
    public required string ProfileAlias { get; init; }

    public required string ProfileGenerationId { get; init; }

    public required OfficialWorkerVersion WorkerVersion { get; init; }

    public required string WorkerExecutablePath { get; init; }

    public required string WorkerExecutableSha256 { get; init; }

    public required string PackageLockId { get; init; }

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
