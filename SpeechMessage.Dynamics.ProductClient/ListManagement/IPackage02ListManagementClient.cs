// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListManagement/IPackage02ListManagementClient.cs
// 目的：定義 P7.2 Slice C static-list、小組固定欄位、owner 與 transfer composite 的 typed 產品契約。
//
// 信任與生命週期邊界：
// 1. DTO 只允許已命名、有限的 GUID、UTC week start、mode 與 idempotency key；禁止 Entity、listmember、
//    FetchXML、OrganizationRequest、欄位 map、endpoint、credential、connector、CE version 或 profile 選擇器。
// 2. 每次呼叫的集合與 scalar 由 ProductClient 在第一次 await 前複製／正規化；DTO 不擁有 CRM lease、HTTP client、
//    stream、buffer、timer、session、token、background task 或跨 request cache。
// 3. response 只描述已證實的 bounded mutation outcome，不能外洩 list/contact/owner/weekly-report identity、
//    fixture baseline、CRM SDK response、credential 或 transport detail。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.ListManagement;

/// <summary>
/// P7.2 Slice C 的 stateless typed ProductClient 入口。每個方法對應一個固定 business capability；介面故意不
/// 提供 generic CRUD、任意 relationship、任意 owner assign 或可組合 CRM transaction，避免產品回到 ToolUtility/SDK 路徑。
/// </summary>
public interface IPackage02ListManagementClient
{
    /// <summary>將最多 1,000 位 distinct contact 加入單一 static list；unknown write outcome 不自動重送。</summary>
    Task<StaticListMembershipMutationResult> AddMembersAsync(
        StaticListMembersAddRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>從單一 static list 移除一位 contact；已不存在時只可由 pre-read 證實 no-change。</summary>
    Task<StaticListMembershipMutationResult> RemoveMemberAsync(
        StaticListMemberRemoveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>以固定 mode 更新小組六欄；area/race 關聯與欄位 set/clear 順序由 connector 決定。</summary>
    Task<SmallGroupFixedFieldsMutationResult> UpdateSmallGroupFieldsAsync(
        SmallGroupFixedFieldsUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>將 contact 指派給指定 systemuser；owner read-back 不符時必須 fail closed。</summary>
    Task<ContactOwnerAssignmentResult> AssignContactOwnerAsync(
        ContactOwnerAssignmentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>執行固定 contact list-transfer composite；所有 membership／weekly-record／lookup/owner state 都由 server 端依序 reconcile。</summary>
    Task<ContactListTransferResult> TransferContactBetweenListsAsync(
        ContactListTransferRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>small-group 固定欄位操作的唯一允許 mode；任意欄位 map 或新 mode 都必須在產品邊界拒絕。</summary>
public enum SmallGroupFixedFieldsUpdateMode
{
    /// <summary>只將 <c>new_contact_race_leager_list</c> 更新為指定 target leader。</summary>
    ChangeRaceLeader = 0,

    /// <summary>依 server-owned target-leader 關聯解析 area leader/name，並固定清除三個 deputy lookup。</summary>
    ChangeAreaLeader = 1
}

/// <summary>static-list add-many 的有限產品輸入。</summary>
public sealed record StaticListMembersAddRequest
{
    /// <summary>部署端決定的 profile alias；終端使用者不可從 UI／HTTP body 選取 connector 或 CE version。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>由已驗證工作負載推導的 subject；不能用 session、contact、LINE 或 CRM 身分取代。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>已授權且 task fixture-owned 的 static list identity。</summary>
    public required Guid ListId { get; init; }

    /// <summary>最多 1,000 位 distinct non-empty contact；client 會在第一個 await 前建立排序 copy。</summary>
    public required IReadOnlyList<Guid> MemberIds { get; init; }

    /// <summary>1-128 個 URL-safe 字元的寫入 idempotency key；它不是 CRM token，也不跨 request 保存。</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>static-list remove-one 的有限產品輸入。</summary>
public sealed record StaticListMemberRemoveRequest
{
    /// <summary>部署端決定的 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>由已驗證工作負載推導的 subject。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>已授權且 fixture-owned 的 static list identity。</summary>
    public required Guid ListId { get; init; }

    /// <summary>欲移除的單一 contact identity。</summary>
    public required Guid MemberId { get; init; }

    /// <summary>寫入 idempotency key；timeout 後仍必須靠 read-back reconcile，而非盲目重送。</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>small-group six-field fixed-mode update 的有限產品輸入。</summary>
public sealed record SmallGroupFixedFieldsUpdateRequest
{
    /// <summary>部署端決定的 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>由已驗證工作負載推導的 subject。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>要更新的 task-owned 小組 list identity。</summary>
    public required Guid ListId { get; init; }

    /// <summary>唯一允許的 race/area leader change mode。</summary>
    public required SmallGroupFixedFieldsUpdateMode Mode { get; init; }

    /// <summary>目標 race leader contact；area leader 及區名仍由 connector 以固定關聯解析。</summary>
    public required Guid TargetLeaderContactId { get; init; }

    /// <summary>寫入 idempotency key；partial/unknown state 不會被此 key 當成重送許可。</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>contact owner assignment 的有限產品輸入。</summary>
public sealed record ContactOwnerAssignmentRequest
{
    /// <summary>部署端決定的 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>由已驗證工作負載推導的 subject。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>要指派的 task-owned contact identity。</summary>
    public required Guid ContactId { get; init; }

    /// <summary>allowlisted active systemuser identity；不能換成 team 或任意 entity。</summary>
    public required Guid OwnerSystemUserId { get; init; }

    /// <summary>寫入 idempotency key。</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>contact list-transfer composite 的有限產品輸入。</summary>
public sealed record ContactListTransferRequest
{
    /// <summary>部署端決定的 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>由已驗證工作負載推導的 subject。</summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>要移轉的 task-owned contact identity。</summary>
    public required Guid ContactId { get; init; }

    /// <summary>可選來源 static list；提供時必須與 target list 不同。</summary>
    public Guid? SourceListId { get; init; }

    /// <summary>必填目標 static list identity。</summary>
    public required Guid TargetListId { get; init; }

    /// <summary>以明確 offset 提供的 weekly-report Sunday start；client 正規化為 UTC，不讀取主機時區。</summary>
    public required DateTimeOffset WeekStartDate { get; init; }

    /// <summary>可選、allowlisted active systemuser owner；省略時 composite 不作 owner mutation。</summary>
    public Guid? OwnerSystemUserId { get; init; }

    /// <summary>整個 composite 共用的寫入 idempotency key。</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>static-list action 的不識別結果。</summary>
public sealed record StaticListMembershipMutationResult
{
    /// <summary>取得 bounded changed/no-change outcome。</summary>
    public required P72ControlledMutationDisposition Disposition { get; init; }

    /// <summary>取得 bounded no-dispatch/read-back-confirmed category。</summary>
    public required P72ControlledMutationCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>small-group fixed-field write 的不識別結果。</summary>
public sealed record SmallGroupFixedFieldsMutationResult
{
    /// <summary>取得 bounded changed/no-change outcome。</summary>
    public required P72ControlledMutationDisposition Disposition { get; init; }

    /// <summary>取得 bounded no-dispatch/read-back-confirmed category。</summary>
    public required P72ControlledMutationCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>contact owner assignment 的不識別結果。</summary>
public sealed record ContactOwnerAssignmentResult
{
    /// <summary>取得 bounded changed/no-change outcome。</summary>
    public required P72ControlledMutationDisposition Disposition { get; init; }

    /// <summary>取得 bounded no-dispatch/read-back-confirmed category。</summary>
    public required P72ControlledMutationCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>contact transfer composite 的不識別結果。</summary>
public sealed record ContactListTransferResult
{
    /// <summary>取得 bounded changed/no-change outcome。</summary>
    public required P72ControlledMutationDisposition Disposition { get; init; }

    /// <summary>取得 bounded no-dispatch/read-back-confirmed category。</summary>
    public required P72ControlledMutationCorrelationCategory CorrelationCategory { get; init; }
}
