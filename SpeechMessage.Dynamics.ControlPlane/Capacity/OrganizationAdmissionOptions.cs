// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Capacity/OrganizationAdmissionOptions.cs
// 目的：Organization 級 admission / capacity 設定。
//
// 保母教學：
// - LocalMaxInFlight 不是手動填，而是 floor(AggregateMaxInFlight / MaximumRuntimeHosts)。
// - 這裡管的是「對 CRM 的併發與排隊」，不是 per-user session pool。
// - 正式環境的 MaximumRuntimeHosts 必須涵蓋 Gateway + Embedded + blue/green 重疊。
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace SpeechMessage.Dynamics.ControlPlane.Capacity;

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
    /// admission namespace ID，作為 queue/permit 的容量隔離鍵；不同實體組織不得誤用相同鍵。
    /// </summary>
    [Required]
    public string AdmissionNamespaceId { get; set; } = "default-admission";

    /// <summary>
    /// host-slot lease namespace ID；Gateway、Embedded 與重疊世代若指向同一實體組織，必須共用此鍵。
    /// </summary>
    [Required]
    public string LeaseNamespaceId { get; set; } = "default-host-lease";

    /// <summary>
    /// 全域協調且不可變的容量/設定 epoch。Aggregate capacity、host 上限或 lease 政策改變時必須遞增，
    /// durable coordinator 會拒絕仍持有舊 epoch 或不同設定摘要的主機。
    /// </summary>
    [Range(1, long.MaxValue)]
    public long AdmissionEpoch { get; set; } = 1;

    /// <summary>
    /// Coordinator 核發的 runtime-host slot 租約生命週期；必須長於續租間隔、最大 outbound 工作與 expiry fence 總和。
    /// </summary>
    [Range(5, 3600)]
    public int RuntimeHostSlotLeaseTtlSeconds { get; set; } = 120;

    /// <summary>
    /// 背景續租週期。續租完成後仍須保留足夠時間容納最大 outbound 作業與 expiry fence，否則 readiness 失敗。
    /// </summary>
    [Range(1, 600)]
    public int RuntimeHostSlotRenewalIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Coordinator lease 到期前的安全邊界；新工作不得跨越此 margin，已執行工作在 lease lost 時也會收到取消訊號。
    /// </summary>
    [Range(1, 300)]
    public int RuntimeHostSlotExpiryFenceSeconds { get; set; } = 10;

    /// <summary>
    /// 單一已 admission CRM 作業的「要求 + 取消/清理」最大生命週期，用來判斷租約是否足以完整容納工作。
    /// </summary>
    [Range(1, 600)]
    public int MaximumOutboundWorkLifetimeSeconds { get; set; } = 35;

    /// <summary>
    /// 優雅停止時等待現有工作排空的最長時間；逾時後會進入 lease-loss 邊界並取消尚未完成的工作，絕不無限等待。
    /// </summary>
    [Range(1, 900)]
    public int ShutdownDrainTimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// 是否要求 durable host-slot coordinator。
    /// 開發/單機可 false；正式多 host 應 true，否則 readiness 應失敗。
    /// </summary>
    public bool RequireDurableHostCoordinator { get; set; }
}
