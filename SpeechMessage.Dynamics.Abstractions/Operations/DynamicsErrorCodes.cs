// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/DynamicsErrorCodes.cs
// 目的：統一操作錯誤代碼，方便 Gateway 與產品端一致處理。
//
// 保母教學：
// - 成功時不要塞錯誤碼。
// - 失敗時用穩定字串代碼，不要把上游原始錯誤全文直接當代碼。
// - 絕對不要把密碼、token、cookie、連線字串寫進錯誤訊息。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// Dynamics 受控操作錯誤代碼。
/// </summary>
public static class DynamicsErrorCodes
{
    public const string UnknownOperation = "dynamics.operation.unknown";
    public const string InvalidParameter = "dynamics.operation.invalid_parameter";
    public const string NotReady = "dynamics.runtime.not_ready";
    public const string NotImplemented = "dynamics.operation.not_implemented";
    public const string InvalidConfiguration = "dynamics.config.invalid";
    public const string UpstreamFailure = "dynamics.upstream.failure";
    public const string UpstreamTimeout = "dynamics.upstream.timeout";
    public const string Unauthorized = "dynamics.upstream.unauthorized";
    public const string SecretResolutionFailed = "dynamics.secret.resolution_failed";
    public const string CapacityRejected = "dynamics.admission.capacity_rejected";
    public const string QueueFull = "dynamics.admission.queue_full";
    public const string AdmissionTimeout = "dynamics.admission.timeout";
    public const string WorkloadCapExceeded = "dynamics.admission.workload_cap_exceeded";
    public const string HostSlotUnavailable = "dynamics.admission.host_slot_unavailable";
    public const string EnvelopeTooLarge = "dynamics.admission.envelope_too_large";
}
