// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/SpecialResources/IPackage03SpecialResourceClient.cs
// 用途：定義 P7.3 image、metadata 與 weekly statistic 的產品可見 typed capability。
//
// 信任與生命週期邊界：
// 1. 呼叫端只可提供已由產品授權的 profile/workload scalar、固定 Guid/UTC Sunday、封閉 metadata target，及 image bytes。
// 2. 不接受 CRM SDK、Entity、QueryExpression、FetchXML、paging cookie、stream、IFormFile、endpoint、credential、
//    token、session、任意 schema 或 connector selection；那些只能在下游 server-owned capability 中存在。
// 3. 所有 image bytes 都在 request/result boundary defensive copy；DTO 不擁有 lease、client、timer、cache 或背景工作。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.SpecialResources;

/// <summary>
/// P7.3 特殊資源的 stateless typed ProductClient。實作只保存 DI-owned executor/logger；每次呼叫的 request、
/// image bytes、metadata/weekly result 都由當前呼叫端擁有，不能進入跨使用者、跨 profile 或跨 generation 的共享狀態。
/// </summary>
public interface IPackage03SpecialResourceClient
{
    /// <summary>讀取單一已授權 contact 的封閉 image projection；回傳 bytes 為 defensive copy，不含 stream 或 CRM Entity。</summary>
    Task<ContactImageResult> RetrieveContactImageAsync(
        ContactImageRetrieveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>更新 MemberInfo 固定 contact image；只有精確 read-back-confirmed 成功，未知結果不自動重試。</summary>
    Task<ContactImageUpdateResult> UpdateMemberInfoContactImageAsync(
        ContactImageUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>更新 NewPerson 固定 contact image；與 MemberInfo 保留不同 capability ID，不能合併成 generic CRUD。</summary>
    Task<ContactImageUpdateResult> UpdateNewPersonContactImageAsync(
        ContactImageUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>讀取唯一 server-owned metadata OptionSet target 的 immutable pure option projection。</summary>
    Task<OptionSetRetrieveResult> RetrieveOptionSetAsync(
        OptionSetRetrieveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>讀取唯一 UTC Sunday 的 bounded weekly meeting statistics；結果永不含 paging cookie 或 partial page。</summary>
    Task<MeetingStatisticsRetrieveResult> RetrieveMeetingStatisticsAsync(
        MeetingStatisticsRetrieveRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>image retrieve 的最小產品輸入；ContactId 必須已在產品授權 scope 中，profile/workload 不能由終端使用者任意指定。</summary>
public sealed record ContactImageRetrieveRequest
{
    /// <summary>部署組合根固定的 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>伺服器推導的產品 workload subject，不得用終端使用者 session 或 CRM identity 取代。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>已通過產品授權的非空 contact ID。</summary>
    public required Guid ContactId { get; init; }
}

/// <summary>image update 的最小產品輸入；ImageBytes 在 client 建立封閉 payload 前複製，寫入未知結果一律不盲目重送。</summary>
public sealed record ContactImageUpdateRequest
{
    /// <summary>部署組合根固定的 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>伺服器推導的產品 workload subject。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>已授權且非空的 contact ID。</summary>
    public required Guid ContactId { get; init; }

    /// <summary>caller-owned bounded image bytes；client 立刻複製，不能在 await 後保留 caller array。</summary>
    public required byte[] ImageBytes { get; init; }

    /// <summary>封閉 PNG/JPEG kind；executor/connector 仍會驗實際 decoder format。</summary>
    public required ContactImageMediaKind MediaKind { get; init; }

    /// <summary>1-128 個 URL-safe ASCII idempotency key；只用於單次 write contract，未知 outcome 不代表可重試。</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>metadata read 的最小產品輸入；Target 是唯一公開 enum，entity/attribute/locale 仍由 server private allowlist 決定。</summary>
public sealed record OptionSetRetrieveRequest
{
    /// <summary>部署組合根固定的 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>伺服器推導的產品 workload subject。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>唯一封閉 metadata target，未知 enum 在 executor 前拒絕。</summary>
    public required MetadataOptionSetTarget Target { get; init; }
}

/// <summary>weekly statistics 的最小產品輸入；SundayDate 必須為 UTC Sunday 00:00，不能改成 arbitrary date range 或 query。</summary>
public sealed record MeetingStatisticsRetrieveRequest
{
    /// <summary>部署組合根固定的 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>伺服器推導的產品 workload subject。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>唯一允許的 UTC Sunday key。</summary>
    public required DateTimeOffset SundayDate { get; init; }
}

/// <summary>
/// image retrieve 的產品安全結果。內部 bytes 不公開；每次 getter 都回傳新 copy，讓 response/cache/caller 的修改
/// 不會反向影響另一個 request。此 DTO 不擁有 stream、decoder、buffer、lease 或任何 connector 資源。
/// </summary>
public sealed class ContactImageResult
{
    private readonly byte[] _imageBytes;

    /// <summary>從已驗證的 connector response 建立新的 image copy。</summary>
    public ContactImageResult(byte[] imageBytes, ContactImageMediaKind mediaKind)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        _imageBytes = imageBytes.ToArray();
        MediaKind = mediaKind;
    }

    /// <summary>取得封閉 media kind；它不替代 gateway/connector 對真實 decoder format 的驗證。</summary>
    public ContactImageMediaKind MediaKind { get; }

    /// <summary>取得 caller-owned image copy；呼叫端不可假設其與其他 request/result 共用陣列。</summary>
    public byte[] GetImageBytes() => _imageBytes.ToArray();
}

/// <summary>image write 的最小成功結果；僅代表固定欄位的 exact read-back 已確認，沒有 correlation ID 或 CRM response。</summary>
public sealed record ContactImageUpdateResult
{
    /// <summary>唯一合法的 change disposition。</summary>
    public required ContactImageUpdateDisposition Disposition { get; init; }

    /// <summary>唯一合法的 bounded read-back category。</summary>
    public required ContactImageUpdateCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>metadata option 的產品 DTO；只保留 value/label/configured order，不保留 AttributeMetadata graph。</summary>
public sealed record OptionSetOptionDto
{
    /// <summary>固定 option integer value。</summary>
    public required int Value { get; init; }

    /// <summary>已投影的 bounded label。</summary>
    public required string Label { get; init; }

    /// <summary>固定 configured order。</summary>
    public required int ConfiguredOrder { get; init; }
}

/// <summary>metadata read 結果；Options 是新陣列，不能回寫 connector collection 或 shared cache entry。</summary>
public sealed record OptionSetRetrieveResult
{
    /// <summary>取得有界、已複製的 option DTO 集合。</summary>
    public required IReadOnlyList<OptionSetOptionDto> Options { get; init; }
}

/// <summary>weekly meeting statistic 的產品 DTO；不含 entity、page cookie、next link 或 session。</summary>
public sealed record MeetingStatisticDto
{
    /// <summary>固定 meeting statistic identity。</summary>
    public required Guid MeetingStatisticId { get; init; }

    /// <summary>可選、已限制的 display name。</summary>
    public string? Name { get; init; }

    /// <summary>可選 UTC creation timestamp。</summary>
    public DateTimeOffset? CreatedOn { get; init; }

    /// <summary>可選 UTC Sunday key。</summary>
    public DateTimeOffset? SundayDate { get; init; }
}

/// <summary>weekly statistics 結果；Statistics 是 request-local copied DTO collection，page failure 時不會產生此結果。</summary>
public sealed record MeetingStatisticsRetrieveResult
{
    /// <summary>取得 bounded、非 partial 的 meeting statistic DTO 集合。</summary>
    public required IReadOnlyList<MeetingStatisticDto> Statistics { get; init; }
}
