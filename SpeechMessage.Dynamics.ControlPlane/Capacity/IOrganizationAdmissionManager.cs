// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Capacity/IOrganizationAdmissionManager.cs
// 目的：Organization 級 admission 管理介面。
//
// 保母教學：
// - 所有外呼 CRM 前都要先拿到 permit。
// - permit 必須在成功/失敗/取消時釋放，避免 in-flight 洩漏。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ControlPlane.Capacity;

/// <summary>
/// 已取得的 admission permit。
/// </summary>
public interface IAdmissionPermit : IAsyncDisposable, IDisposable
{
    Guid CorrelationId { get; }
    long HostFencingToken { get; }
    CancellationToken LeaseLostToken { get; }
}

/// <summary>
/// admission 結果。
/// </summary>
public sealed class AdmissionAcquireResult
{
    public bool Succeeded { get; init; }
    public IAdmissionPermit? Permit { get; init; }
    public OperationExecutionResult? Error { get; init; }

    public static AdmissionAcquireResult Success(IAdmissionPermit permit)
        => new() { Succeeded = true, Permit = permit };

    public static AdmissionAcquireResult Failure(OperationExecutionResult error)
        => new() { Succeeded = false, Error = error };
}

/// <summary>
/// admission 即時快照（供觀測/測試，不含秘密）。
/// </summary>
public sealed class AdmissionMetricsSnapshot
{
    public required int LocalMaxInFlight { get; init; }
    public required int InFlight { get; init; }
    public required int Queued { get; init; }
    public required int LocalQueueCapacity { get; init; }
    public required long AcceptedCount { get; init; }
    public required long RejectedCount { get; init; }
    public required long TimeoutCount { get; init; }
    public required bool HostSlotReady { get; init; }
    public required long HostFencingToken { get; init; }
    public required DateTimeOffset? HostLeaseExpiresAtUtc { get; init; }
    public required int ActivePermits { get; init; }
    public required bool RenewalLoopActive { get; init; }
    public int TrackedWorkloadCount { get; init; }
}

/// <summary>
/// 組織層級 admission manager；以有界 queue、workload 配額與 host-slot lease 控制 outbound Dynamics 併發。
/// </summary>
public interface IOrganizationAdmissionManager : IAsyncDisposable, IDisposable
{
    OrganizationAdmissionPlan Plan { get; }

    Task EnsureHostSlotAsync(CancellationToken cancellationToken);

    Task<AdmissionAcquireResult> AcquireAsync(DispatchEnvelope envelope, CancellationToken cancellationToken);

    AdmissionMetricsSnapshot GetSnapshot();
}
