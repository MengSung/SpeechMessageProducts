// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/MemberInfo/IMemberInfoAuthorizationAssignmentReadClient.cs
// 用途：定義 MemberInfo 伺服器指派證據的 ProductClient 邊界與 immutable DTO。
// ============================================================================

using System.Collections.ObjectModel;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.MemberInfo;

/// <summary>
/// 讀取目前已驗證 subject 的 MemberInfo 指派證據。
/// 呼叫端只可傳入 deployment-owned profile、server workload 與已由產品驗證的 subject GUID；介面不接受
/// list、角色、Owner、FetchXML、endpoint、credential 或任何 browser selector，避免將 CRM 授權範圍交還給呼叫端。
/// 實作為無狀態 singleton，不保存 request、session、principal、response、token 或 CRM 資源；transport/lease 的
/// 取消、fault eviction 與 deterministic disposal 仍由下層 executor owner 負責。
/// </summary>
public interface IMemberInfoAuthorizationAssignmentReadClient
{
    /// <summary>
    /// 以固定 operation 取得單一 subject 的 Church-wide 或 assigned-list evidence snapshot。
    /// 在任何 dispatch 前驗證 routing 與 subject，且 response discriminator、CE version、subject、mode、GUID 上限或
    /// 重複項目不符時一律 fail closed；不會重試、fallback 至 legacy ListManager，或保留可變結果供後續 request 使用。
    /// </summary>
    /// <param name="request">只含 server/deployment 擁有 routing 與 subject 的 request-local 標量。</param>
    /// <param name="cancellationToken">由外層 request 擁有並原樣傳遞給 executor 的取消 token。</param>
    /// <returns>防禦性複製且不可寫入的指派 evidence DTO。</returns>
    Task<MemberInfoAuthorizationAssignmentReadResult> ResolveBySubjectAsync(
        MemberInfoAuthorizationAssignmentReadRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 指派 evidence operation 的封閉輸入。
/// 三個值皆在 dispatch 前被驗證；ProfileAlias 與 WorkloadSubjectId 只能由 deployment/service composition 提供，
/// SubjectContactId 必須來自已驗證的 server request scope。此 record 不承載 session、claim、CRM entity、連線、
/// credential、Owner 或 query，因此不會延長外部資源或跨使用者狀態的生命週期。
/// </summary>
public sealed record MemberInfoAuthorizationAssignmentReadRequest
{
    /// <summary>
    /// 由 deployment composition 選定的 Dynamics profile alias；不得來自瀏覽器或呼叫端資料列。
    /// </summary>
    public required string ProfileAlias { get; init; }

    /// <summary>
    /// 由服務端固定的 workload isolation scalar；不得攜帶登入 token、cookie 或使用者可控制的 routing 值。
    /// </summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>
    /// 已由產品 request scope 驗證的目前 contact GUID；它是唯一可傳入 Data8 固定 query 的 subject。
    /// </summary>
    public required Guid SubjectContactId { get; init; }
}

/// <summary>
/// 從封閉 operation response 投影出的 immutable subject assignment result。
/// 建構時複製每個 list GUID 並發布 <see cref="ReadOnlyCollection{T}"/>，防止上游 wire collection、序列化器或
/// 呼叫端在 authorization 與 ChurchReport scope mapping 間加入另一位使用者的 target。結果不保存 CRM entity、
/// profile、connector、lease、session、cache 或 cancellation registration，資源釋放不在此 DTO 的責任範圍內。
/// </summary>
public sealed class MemberInfoAuthorizationAssignmentReadResult
{
    /// <summary>
    /// 建立 request-local evidence snapshot。
    /// </summary>
    /// <param name="subjectContactId">此 evidence 唯一所屬、不可為空的 subject GUID。</param>
    /// <param name="accessMode">封閉的 Church-wide 或 assigned-list mode。</param>
    /// <param name="assignedListIds">已驗證、將被防禦性複製的 list GUID 集合。</param>
    internal MemberInfoAuthorizationAssignmentReadResult(
        Guid subjectContactId,
        MemberInfoAuthorizationAssignmentAccessMode accessMode,
        IEnumerable<Guid> assignedListIds)
    {
        ArgumentNullException.ThrowIfNull(assignedListIds);
        SubjectContactId = subjectContactId;
        AccessMode = accessMode;
        AssignedListIds = new ReadOnlyCollection<Guid>(assignedListIds.ToList());
    }

    /// <summary>
    /// 取得 evidence 的 server-validated subject GUID；不作為 profile、credential 或 connector 選擇器。
    /// </summary>
    public Guid SubjectContactId { get; }

    /// <summary>
    /// 取得封閉 access mode；Church-wide 必須沒有 list，AssignedLists 可為空以表示該 subject 目前沒有有效指派。
    /// </summary>
    public MemberInfoAuthorizationAssignmentAccessMode AccessMode { get; }

    /// <summary>
    /// 取得防禦性複製的 target allowlist snapshot；collection 不可轉為陣列或寫入。
    /// </summary>
    public IReadOnlyList<Guid> AssignedListIds { get; }
}
