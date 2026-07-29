// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionOptions.cs
// 目的：Organization 級 admission / capacity 設定。
//
// 保母教學：
// - LocalMaxInFlight 不是手動填，而是 floor(AggregateMaxInFlight / MaximumRuntimeHosts)。
// - 這裡管的是「對 CRM 的併發與排隊」，不是 per-user session pool。
// - 正式環境的 MaximumRuntimeHosts 必須涵蓋 Gateway + Embedded + blue/green 重疊。
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// Organization admission 設定。
/// </summary>
public sealed class OrganizationAdmissionOptions
{
    /// <summary>
    /// 預期 Organization GUID。容量以這個實體組織為準。
    /// </summary>
    [Required]
    public Guid ExpectedOrganizationId { get; set; }

    /// <summary>
    /// 整個實體組織可同時 in-flight 的安全上限。
    /// </summary>
    [Range(1, 10000)]
    public int AggregateMaxInFlight { get; set; } = 24;

    /// <summary>
    /// 最多同時存在的 runtime host 數（Gateway + Embedded + 重疊世代）。
    /// </summary>
    [Range(1, 1000)]
    public int MaximumRuntimeHosts { get; set; } = 6;

    /// <summary>
    /// 單一 host 上可排隊等待的請求數上限。
    /// </summary>
    [Range(0, 100000)]
    public int LocalQueueCapacity { get; set; } = 48;

    /// <summary>
    /// 派送 envelope 最大位元組數（防止超大 payload 佔住 queue）。
    /// </summary>
    [Range(256, 8_388_608)]
    public int MaxDispatchEnvelopeBytes { get; set; } = 65_536;

    /// <summary>
    /// 排隊等待取得 in-flight 名額的最長秒數。
    /// </summary>
    [Range(1, 300)]
    public int QueueAdmissionTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// 單一 WorkloadSubjectId 在本 host 最多可佔用的 queued+in-flight 名額。
    /// 用來避免一個產品塞滿整條 queue。
    /// </summary>
    [Range(1, 10000)]
    public int MaxInFlightAndQueuedPerWorkload { get; set; } = 8;

    /// <summary>
    /// admission namespace id（queue/permit key）。
    /// </summary>
    [Required]
    public string AdmissionNamespaceId { get; set; } = "default-admission";

    /// <summary>
    /// host slot lease namespace id。
    /// </summary>
    [Required]
    public string LeaseNamespaceId { get; set; } = "default-host-lease";

    /// <summary>
    /// Coordinator-issued runtime host slot lifetime.
    /// </summary>
    [Range(5, 3600)]
    public int RuntimeHostSlotLeaseTtlSeconds { get; set; } = 120;

    /// <summary>
    /// Background renewal cadence. This must leave enough lease time for a
    /// maximum outbound operation and the expiry fence.
    /// </summary>
    [Range(1, 600)]
    public int RuntimeHostSlotRenewalIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// No work may be admitted or remain active beyond this margin before the
    /// coordinator lease expiry.
    /// </summary>
    [Range(1, 300)]
    public int RuntimeHostSlotExpiryFenceSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum request plus cleanup/cancellation lifetime for one admitted CRM
    /// operation.
    /// </summary>
    [Range(1, 600)]
    public int MaximumOutboundWorkLifetimeSeconds { get; set; } = 35;

    /// <summary>
    /// Maximum graceful stop drain interval before active work is cancelled at
    /// the lease-loss boundary.
    /// </summary>
    [Range(1, 900)]
    public int ShutdownDrainTimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// 是否要求 durable host-slot coordinator。
    /// 開發/單機可 false；正式多 host 應 true，否則 readiness 應失敗。
    /// </summary>
    public bool RequireDurableHostCoordinator { get; set; }
}
