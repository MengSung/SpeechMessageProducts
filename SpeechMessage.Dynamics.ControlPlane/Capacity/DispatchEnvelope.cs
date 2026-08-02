// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs
// 目的：進入 admission queue 的有界派送信封。
//
// 保母教學：
// - 只放非秘密、可審計的識別資訊。
// - 禁止放 HttpContext、JWT、cookie、密碼、token、LINE raw session。
// - WorkloadSubjectId 是服務工作負載 ID，不是終端使用者 CRM session key。
// ============================================================================

namespace SpeechMessage.Dynamics.ControlPlane.Capacity;

/// <summary>
/// 進入 admission queue 的有界派送信封。信封只保留非秘密路由與容量資訊，
/// 不得包含 request body、JsonElement、HttpContext、principal、session、token、credential、client 或 runtime。
/// </summary>
public sealed class DispatchEnvelope
{
    private int _canonicalEnvelopeBytes;

    /// <summary>取得已在不觸發 Secret/Token I/O 前正規化的 Profile Alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>取得 registry-owned canonical operation ID，不是 caller 提供的任意 route。</summary>
    public required string CapabilityOperationId { get; init; }

    /// <summary>取得伺服器推導的 bounded workload subject，不是 user/session cache key。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>取得 server-owned template ID，排隊期間不保留實體 template graph。</summary>
    public required string TemplateId { get; init; }

    /// <summary>取得 immutable template revision hash，用於防止 runtime 更換後靜默重綁定。</summary>
    public required string TemplateHash { get; init; }

    /// <summary>取得已驗證 1-128 個 URL-safe 字元的冪等鍵；read-only operation 可為 null。</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>取得本次 queued/outbound 工作不可超過的絕對期限。</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }

    /// <summary>
    /// 取得已實際寫入且由 prepared owner 持有的 canonical byte 數。這是 admission 強制上限的真值，
    /// 不是 UTF-16 字元估算或 CLR object size 猜測。
    /// </summary>
    public int CanonicalEnvelopeBytes
    {
        get => _canonicalEnvelopeBytes;
        init => _canonicalEnvelopeBytes = value;
    }

    /// <summary>
    /// 保留給既有 admission manager/test initializer 的相容別名。新 executor 只寫入
    /// <see cref="CanonicalEnvelopeBytes"/>；getter 回傳同一個 exact backing value，因此現有強制點不再使用粗估值。
    /// </summary>
    public int EstimatedEnvelopeBytes
    {
        get => _canonicalEnvelopeBytes;
        init => _canonicalEnvelopeBytes = value;
    }

    /// <summary>取得非秘密 correlation ID；它不是 user/session identity 且不參與 canonical hash。</summary>
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
}
