namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// Bounded ownership counters used by lifecycle, soak, and no-leak verification.
/// </summary>
public sealed record OfficialWorkerLifecycleSnapshot(
    bool IsReady,
    int OwnedProcessCount,
    int OwnedPipeCount,
    int OwnedBackgroundTaskCount,
    int ActiveOperationCount);
