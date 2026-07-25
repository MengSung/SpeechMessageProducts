// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs
// 目的：進入 admission queue 的有界派送信封。
//
// 保母教學：
// - 只放非秘密、可審計的識別資訊。
// - 禁止放 HttpContext、JWT、cookie、密碼、token、LINE raw session。
// - WorkloadSubjectId 是服務工作負載 ID，不是終端使用者 CRM session key。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// 有界派送信封。
/// </summary>
public sealed class DispatchEnvelope
{
    public required string ProfileAlias { get; init; }
    public required string CapabilityOperationId { get; init; }
    public required string WorkloadSubjectId { get; init; }
    public required string TemplateId { get; init; }
    public required string TemplateHash { get; init; }
    public string? IdempotencyKey { get; init; }
    public required DateTimeOffset DeadlineUtc { get; init; }
    public required int EstimatedEnvelopeBytes { get; init; }
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
}
