// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/MemberInfo/IPackage02ContactProfileClient.cs
// 目的：提供 P7.2 LINE profile write 與 ungrouped commitment aggregate 的 typed 產品契約。
//
// 邊界：
// - 不接受 LINE token、LINE user ID、FetchXML、QueryExpression、Entity、欄位 map、endpoint、credential 或 grouped GUID graph。
// - ProfileAlias／WorkloadSubjectId 由產品組合根與授權流程提供；ConnectorKind、CE version、Organization 由部署固定。
// - DTO 只持有 bounded 純值，不擁有 stream、buffer、lease、timer、session 或 background task。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.MemberInfo;

/// <summary>P7.2 contact profile 與 aggregate function 的 stateless typed ProductClient 入口。</summary>
public interface IPackage02ContactProfileClient
{
    /// <summary>更新單一 contact 的三個固定 LINE profile 欄位；未知結果不自動重試。</summary>
    /// <param name="request">只含 deployment/workload scalar、contact ID、固定 mutation mode/value 與冪等鍵。</param>
    /// <param name="cancellationToken">目前 request scope 擁有且不被 client 保存的取消訊號。</param>
    /// <returns>只含已確認 mutation 與 read-back category 的安全結果。</returns>
    Task<ContactLineProfileUpdateResult> UpdateLineProfileAsync(
        ContactLineProfileUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>執行 server-owned ungrouped commitment count function；不接受任意 query 或 grouped identities。</summary>
    /// <param name="request">只含 deployment/workload scalar 與 optional bounded search。</param>
    /// <param name="cancellationToken">目前 request scope 擁有且不被 client 保存的取消訊號。</param>
    /// <returns>依 connector 固定 query 投影的 bounded raw value/count records。</returns>
    Task<UngroupedCommitmentCountResult> CountUngroupedCommitmentAsync(
        UngroupedCommitmentCountRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>picture/status 欄位的封閉 mutation mode；Set 需要值，Clear 禁止附值。</summary>
public enum ContactLineProfileNullableTextMode
{
    /// <summary>寫入經驗證的 bounded 文字。</summary>
    Set = 0,

    /// <summary>將固定 CRM 欄位寫為 null。</summary>
    Clear = 1
}

/// <summary>display name 的封閉 mutation mode；沿用 legacy「空白不清除」語意。</summary>
public enum ContactLineProfileDisplayNameMode
{
    /// <summary>寫入經驗證的 bounded display name。</summary>
    Set = 0,

    /// <summary>不修改既有 display name，且 request 不得附帶值。</summary>
    Preserve = 1
}

/// <summary>
/// LINE profile write 的產品輸入。它不含 LINE credential／user ID 或第三方 response；所有字串在 executor 前被複製與限制。
/// </summary>
public sealed record ContactLineProfileUpdateRequest
{
    /// <summary>部署端固定的 profile alias；不能由終端使用者自由選擇。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>由已驗證服務身分推導的 workload subject，不得用 LINE user ID 或 session ID 取代。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>已通過產品層授權的唯一 contact identity。</summary>
    public required Guid ContactId { get; init; }

    /// <summary>指定 picture URL 要 set 或 clear。</summary>
    public required ContactLineProfileNullableTextMode PictureMode { get; init; }

    /// <summary>PictureMode=Set 時的 absolute HTTPS URL；Clear 時必須為 null。</summary>
    public string? PictureUrl { get; init; }

    /// <summary>指定 status message 要 set 或 clear。</summary>
    public required ContactLineProfileNullableTextMode StatusMode { get; init; }

    /// <summary>StatusMode=Set 時的 bounded 文字；Clear 時必須為 null。</summary>
    public string? StatusMessage { get; init; }

    /// <summary>指定 display name 要 set 或 preserve。</summary>
    public required ContactLineProfileDisplayNameMode DisplayNameMode { get; init; }

    /// <summary>DisplayNameMode=Set 時的 bounded 文字；Preserve 時必須為 null。</summary>
    public string? DisplayName { get; init; }

    /// <summary>caller 產生的 1-128 URL-safe 冪等鍵；unknown write outcome 仍不得盲目重送。</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>LINE profile write 的產品安全結果，不保存 contact、LINE profile 或 CRM response。</summary>
public sealed record ContactLineProfileUpdateResult
{
    /// <summary>表示已完成並確認的固定 mutation。</summary>
    public required ContactLineProfileUpdateDisposition Disposition { get; init; }

    /// <summary>表示固定欄位 read-back 已確認，不是可識別使用者的 correlation ID。</summary>
    public required ContactLineProfileUpdateCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>Ungrouped commitment count 的最小產品輸入。</summary>
public sealed record UngroupedCommitmentCountRequest
{
    /// <summary>部署端固定的 profile alias；不能由終端使用者自由選擇。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>由已驗證服務身分推導的 workload subject。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>可選搜尋文字；trim 後最多 256 UTF-8 bytes，空白表示不套用搜尋條件。</summary>
    public string? Search { get; init; }
}

/// <summary>產品可見的 raw OptionSet value/count DTO；排序仍由產品 metadata sequence 決定。</summary>
public sealed record UngroupedCommitmentCountDto
{
    /// <summary>取得 raw OptionSet value。</summary>
    public required int Value { get; init; }

    /// <summary>取得非負筆數。</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate function 的 immutable 結果。Counts 由 client 建立新陣列，沒有 CRM graph、stream、session 或 mutable upstream owner。
/// </summary>
public sealed record UngroupedCommitmentCountResult
{
    /// <summary>取得 bounded value/count records；每個 raw value 在同一結果中唯一。</summary>
    public required IReadOnlyList<UngroupedCommitmentCountDto> Counts { get; init; }
}
