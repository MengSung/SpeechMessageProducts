// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72ListManagementFixtureBridge.cs
// 用途：提供 P7.2 Slice C list-membership、small-group 固定欄位、contact owner 與
//       transfer composite 的 live evidence 專用、可離線驗證之 bounded state machine。
//       它不屬於 ChurchReport production runtime，也不啟用任何產品流量。
//
// 信任、隔離與生命週期：
// 1. Bridge 只接受固定 typed ProductClient、task-owned fixture identity、固定 UTC Sunday 與
//    bounded idempotency key；它不接受 endpoint、OrganizationId、ConnectorKind、CE version、
//    credential、Entity、FetchXML、QueryBase、OrganizationRequest 或 caller field map。
// 2. Fixture store 的唯一資源 owner 是 evidence runner。Bridge 不 Dispose、不快取、不跨 await
//    保存 CRM service、lease、profile、session、token、timer、stream、buffer 或 background task。
// 3. 每個 mutation 最多 dispatch 一次。dispatch 後的 exception/cancellation 只觸發固定 read-back；
//    既非完整 baseline 也非完整 expected 的狀態一律 no-go，不重送、不猜測、不覆寫。
// 4. Result 只回傳固定分類與布林值，絕不含 contact/list/owner/record GUID、baseline、sentinel、
//    endpoint、帳密、token、cookie 或原始例外，可安全寫入 sanitized live evidence。
// ============================================================================

using System.Collections.Frozen;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.ListManagement;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 單一 static-list 與 bounded contact 集合的 membership projection。建構時會 defensive-copy 成 frozen set，
/// 所以 bridge 的 baseline 不會因 caller 或 CRM test double 在 await 期間改動集合而漂移；identity 僅存在
/// task-local memory，絕不可放入 evidence、log、cache、Session 或跨測試 static state。
/// </summary>
/// <param name="presentMemberIds">固定 list 中目前存在的、已要求 contact subset。</param>
internal sealed class P72MembershipSnapshot(IEnumerable<Guid> presentMemberIds)
{
    /// <summary>取得唯一、非空且 frozen 的 present member IDs；僅供 bridge 與 fixture store 在同一 process 內比較。</summary>
    public IReadOnlySet<Guid> PresentMemberIds { get; } = CreateFrozenSet(presentMemberIds);

    /// <summary>將外部集合轉成 immutable-like frozen set，拒絕 null 或 empty GUID，避免 baseline 邊界失真。</summary>
    private static FrozenSet<Guid> CreateFrozenSet(IEnumerable<Guid> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Any(static value => value == Guid.Empty))
        {
            throw new ArgumentException("Membership snapshots cannot contain an empty GUID.", nameof(values));
        }

        return copy.ToFrozenSet();
    }
}

/// <summary>
/// small-group 六個固定欄位的 SDK-free snapshot。六個欄位的順序本身是 contract：area leader、area name、
/// race leader 與三個 deputy lookup；不得以自由 dictionary、Entity 或 caller-selected attribute 取代。
/// </summary>
internal sealed record P72SmallGroupFixedFieldsSnapshot(
    Guid? AreaLeaderId,
    string? AreaName,
    Guid? RaceLeaderId,
    Guid? CoAreaLeaderId,
    Guid? CoRaceLeaderId,
    Guid? ViceFamilyLeaderId);

/// <summary>
/// transfer composite 的固定 graph projection。PresentRecordId 只在 task-local store 內用於確認「本次
/// reconciliation 所觀察到」的單筆 record；它不會進入 bridge result，cleanup 也只能刪除這個已確認 ID。
/// </summary>
internal sealed record P72TransferGraphSnapshot(
    bool SourceMembershipPresent,
    bool TargetMembershipPresent,
    Guid? PresentRecordId,
    bool PresentRecordMatches,
    Guid? PrimaryListId,
    Guid? OwnerId);

/// <summary>
/// transfer fixture 的固定 input。它只描述 task-owned contact/list graph 及 UTC Sunday week start；connection
/// routing、credential、CE version、endpoint 與任何 raw SDK request 都由 deployment/store 擁有而非本型別。
/// </summary>
internal sealed record P72TransferFixture(
    Guid ContactId,
    Guid? SourceListId,
    Guid TargetListId,
    DateTimeOffset WeekStartDate,
    Guid? TargetOwnerId);

/// <summary>
/// Slice C evidence 需要的窄 fixture store。每個方法都是 operation-specific；實作不得將這個介面擴張為
/// generic CRM repository。store 的 caller 是底層 Data8 OnPremiseClient／WCF 資源唯一 Dispose owner，bridge
/// 僅在同步 method scope 使用 snapshot，避免跨 profile、tenant、request 或 test session 留存 mutable state。
/// </summary>
internal interface IP72ListManagementFixtureStore : IDisposable
{
    /// <summary>以固定 listmember filter 讀取 requested subset 的 membership；結果不得含其他 list/contact。</summary>
    P72MembershipSnapshot ReadMembership(Guid listId, IReadOnlyList<Guid> contactIds);

    /// <summary>將 requested subset 還原到 captured baseline；實作只能增減這個 subset，不能碰其他 member。</summary>
    void RestoreMembership(Guid listId, IReadOnlyList<Guid> contactIds, P72MembershipSnapshot baseline);

    /// <summary>讀取固定 six-field small-group projection。</summary>
    P72SmallGroupFixedFieldsSnapshot ReadSmallGroupFields(Guid listId);

    /// <summary>
    /// 舊入口缺少 descriptor-bound relationship list ID，實作必須 fail closed；保留此 signature 僅為
    /// 讓舊 live runner 在完成 descriptor migration 前安全編譯，絕不可退回成只依 leader 的全域查詢。
    /// </summary>
    P72SmallGroupFixedFieldsSnapshot ResolveSmallGroupExpected(
        Guid listId,
        SmallGroupFixedFieldsUpdateMode mode,
        Guid targetLeaderContactId);

    /// <summary>
    /// 以固定 server-owned relationship list 解析唯一 expected six-field projection。relationship list 由
    /// task-owned descriptor 提供且必須不同於欲變更 list；不接受 caller field map、generic discovery 或
    /// 任意 CRM query，因此 expected area leader/name 不會從其他 fixture 或使用者 list 混入。
    /// </summary>
    P72SmallGroupFixedFieldsSnapshot ResolveSmallGroupExpected(
        Guid listId,
        SmallGroupFixedFieldsUpdateMode mode,
        Guid targetLeaderContactId,
        Guid expectedRelationshipListId);

    /// <summary>只還原同一 list 的 six-field baseline；不得更改其他 list 欄位。</summary>
    void RestoreSmallGroupFields(Guid listId, P72SmallGroupFixedFieldsSnapshot baseline);

    /// <summary>讀取單一 task-owned contact 的 owner systemuser ID。</summary>
    Guid ReadOwnerId(Guid contactId);

    /// <summary>將同一 contact owner 還原為 captured active systemuser baseline。</summary>
    void RestoreOwner(Guid contactId, Guid baselineOwnerId);

    /// <summary>讀取 transfer 的 membership、present record、primary list 與 owner 之完整 bounded graph。</summary>
    P72TransferGraphSnapshot ReadTransferGraph(P72TransferFixture fixture);

    /// <summary>
    /// 還原已確認 expected graph。實作只能刪除 <paramref name="expected"/> 中已確認且符合 fixture 的 present
    /// record，再復原 lookup/owner/source membership 並移除 target membership；partial graph 不得呼叫此方法。
    /// </summary>
    void RestoreTransferGraph(
        P72TransferFixture fixture,
        P72TransferGraphSnapshot baseline,
        P72TransferGraphSnapshot expected);
}

/// <summary>
/// 去識別化 Slice C bridge 結果。所有字串均為程式固定分類；不得填入 caller input、GUID、欄位值、endpoint、
/// credential、CRM response 或 exception message，因此可在 store/runtime disposal 完成後序列化為 evidence。
/// </summary>
internal sealed record P72ListManagementFixtureBridgeResult
{
    /// <summary><c>go</c> 或 <c>no-go</c>；go 表示單次 dispatch、read-back、restore 與 restore read-back 都證實。</summary>
    public required string Outcome { get; init; }

    /// <summary>成功為空；失敗時為固定、不可識別的分類。</summary>
    public required string Reason { get; init; }

    /// <summary>表示 typed ProductClient 已進入唯一一次 dispatch；不代表 CE 一定 commit。</summary>
    public required bool OperationExecuted { get; init; }

    /// <summary>baseline、expected、expected-after-fault、partial-or-unknown 或 unknown 的 reconciliation 分類。</summary>
    public required string ReconciliationState { get; init; }

    /// <summary>restored、not-required、restored-after-fault 或 manual-reconciliation-required 的 cleanup 分類。</summary>
    public required string CleanupState { get; init; }
}

/// <summary>
/// P7.2 Slice C evidence 的單次 state machine。它將每個 capability 固定為「baseline → one typed dispatch →
/// exact read-back → conditional baseline restore → restore read-back」；任何 ambiguous timeout 或 partial graph
/// 都 fail closed。這個類別不替代 connector 的 server-owned validation，而是負責確保 live evidence 不會遺留
/// mutation、session 或 resource ownership ambiguity。
/// </summary>
internal static class P72ListManagementFixtureBridge
{
    private const string ProfileAlias = "sunnyvalechback";
    private const string WorkloadSubjectId = "p7.2-list-management-fixture";
    private const int MaximumMemberIds = 1000;
    private const int MaximumIdempotencyKeyCharacters = 128;

    /// <summary>
    /// 執行 bounded static-list add evidence。只在 baseline 至少缺少一個 requested member 時 dispatch；dispatch
    /// 後 exception 只讀回全部 requested membership，完整 expected state 才允許一次 exact-baseline restore。
    /// </summary>
    internal static async Task<P72ListManagementFixtureBridgeResult> ExecuteAddMembersAsync(
        IPackage02ListManagementClient client,
        IP72ListManagementFixtureStore store,
        Guid listId,
        IReadOnlyList<Guid> memberIds,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        RequireNonEmptyGuid(listId, nameof(listId));
        var members = CopyDistinctMembers(memberIds, nameof(memberIds));
        var key = RequireIdempotencyKey(idempotencyKey, nameof(idempotencyKey));

        cancellationToken.ThrowIfCancellationRequested();
        var baseline = store.ReadMembership(listId, members);
        if (ContainsAll(baseline, members))
        {
            return NoGo("write-not-required", false, "baseline", "not-required");
        }

        var operationExecuted = false;
        var writeReportedSuccess = false;
        var writeFaulted = false;
        try
        {
            operationExecuted = true;
            var response = await client.AddMembersAsync(new StaticListMembersAddRequest
            {
                ProfileAlias = ProfileAlias,
                WorkloadSubjectId = WorkloadSubjectId,
                ListId = listId,
                MemberIds = members,
                IdempotencyKey = key
            }, cancellationToken).ConfigureAwait(false);
            writeReportedSuccess = IsChangedAndConfirmed(response);
        }
        catch (Exception) when (operationExecuted)
        {
            // 可能是 timeout-after-commit；不得重送 AddListMembersListRequest。
            writeFaulted = true;
        }

        P72MembershipSnapshot current;
        try
        {
            current = store.ReadMembership(listId, members);
        }
        catch (Exception)
        {
            return NoGo("reconciliation-failed", operationExecuted, "unknown", "manual-reconciliation-required");
        }

        if (MembershipMatches(current, baseline))
        {
            return NoGo(
                writeReportedSuccess ? "write-response-state-mismatch" : "write-not-committed",
                operationExecuted,
                "baseline",
                "not-required");
        }

        if (!ContainsAll(current, members))
        {
            return NoGo("write-ambiguous", operationExecuted, "partial-or-unknown", "manual-reconciliation-required");
        }

        return CompleteMembershipCleanup(
            store,
            listId,
            members,
            baseline,
            current,
            operationExecuted,
            writeReportedSuccess,
            writeFaulted);
    }

    /// <summary>
    /// 執行 bounded static-list remove evidence。baseline 必須確實含有 member，否則不 dispatch；後續只有 fixed
    /// membership absence 才是 expected，避免「已不存在」的 no-change 被誤當成真實 action evidence。
    /// </summary>
    internal static async Task<P72ListManagementFixtureBridgeResult> ExecuteRemoveMemberAsync(
        IPackage02ListManagementClient client,
        IP72ListManagementFixtureStore store,
        Guid listId,
        Guid memberId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        RequireNonEmptyGuid(listId, nameof(listId));
        RequireNonEmptyGuid(memberId, nameof(memberId));
        var members = new[] { memberId };
        var key = RequireIdempotencyKey(idempotencyKey, nameof(idempotencyKey));

        cancellationToken.ThrowIfCancellationRequested();
        var baseline = store.ReadMembership(listId, members);
        if (!ContainsAll(baseline, members))
        {
            return NoGo("write-not-required", false, "baseline", "not-required");
        }

        var operationExecuted = false;
        var writeReportedSuccess = false;
        var writeFaulted = false;
        try
        {
            operationExecuted = true;
            var response = await client.RemoveMemberAsync(new StaticListMemberRemoveRequest
            {
                ProfileAlias = ProfileAlias,
                WorkloadSubjectId = WorkloadSubjectId,
                ListId = listId,
                MemberId = memberId,
                IdempotencyKey = key
            }, cancellationToken).ConfigureAwait(false);
            writeReportedSuccess = IsChangedAndConfirmed(response);
        }
        catch (Exception) when (operationExecuted)
        {
            // RemoveMemberListRequest 沒有可由本 bridge 證明的 server idempotency；只可 read-back。
            writeFaulted = true;
        }

        P72MembershipSnapshot current;
        try
        {
            current = store.ReadMembership(listId, members);
        }
        catch (Exception)
        {
            return NoGo("reconciliation-failed", operationExecuted, "unknown", "manual-reconciliation-required");
        }

        if (MembershipMatches(current, baseline))
        {
            return NoGo(
                writeReportedSuccess ? "write-response-state-mismatch" : "write-not-committed",
                operationExecuted,
                "baseline",
                "not-required");
        }

        if (ContainsAll(current, members))
        {
            return NoGo("write-ambiguous", operationExecuted, "partial-or-unknown", "manual-reconciliation-required");
        }

        return CompleteMembershipCleanup(
            store,
            listId,
            members,
            baseline,
            current,
            operationExecuted,
            writeReportedSuccess,
            writeFaulted);
    }

    /// <summary>
    /// 相容舊 runner 的 small-group bridge 入口。舊 descriptor 沒有專用 expected relationship list ID 時，
    /// 此 overload 一律在 dispatch 前回傳 no-go；它不會以 target leader 進行廣泛 query，也不會變更 CRM。
    /// 新 runner 必須呼叫含 <paramref name="expectedRelationshipListId"/> 的 overload。
    /// </summary>
    /// <param name="client">固定 typed Package02 list-management client。</param>
    /// <param name="store">由 evidence runner 擁有的 task-scoped fixture store。</param>
    /// <param name="listId">欲變更的 task-owned small-group list ID。</param>
    /// <param name="mode">固定 small-group mode。</param>
    /// <param name="targetLeaderContactId">已驗證的 target leader contact ID。</param>
    /// <param name="idempotencyKey">單次 dispatch 的 bounded idempotency key。</param>
    /// <param name="cancellationToken">只控制本次 bridge scope，取消不會觸發 retry。</param>
    /// <returns>缺少 dedicated relationship identity 時的 sanitized no-go 結果。</returns>
    internal static Task<P72ListManagementFixtureBridgeResult> ExecuteSmallGroupFieldsAsync(
        IPackage02ListManagementClient client,
        IP72ListManagementFixtureStore store,
        Guid listId,
        SmallGroupFixedFieldsUpdateMode mode,
        Guid targetLeaderContactId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        => ExecuteSmallGroupFieldsAsync(
            client,
            store,
            listId,
            mode,
            targetLeaderContactId,
            Guid.Empty,
            idempotencyKey,
            cancellationToken);

    /// <summary>
    /// 執行 descriptor-bound small-group fixed-fields evidence。store 在 dispatch 前以專用 task-owned
    /// relationship list 算出 expected six-field projection；bridge 不自行編造 area/name/deputy 欄位，任何
    /// 非 baseline/expected 的 projection 都保留給人工 reconciliation，避免 cleanup 覆寫真正不明狀態。
    /// </summary>
    /// <param name="client">固定 typed Package02 list-management client。</param>
    /// <param name="store">由 evidence runner 擁有的 task-scoped fixture store。</param>
    /// <param name="listId">欲變更的 task-owned small-group list ID。</param>
    /// <param name="mode">固定 small-group mode。</param>
    /// <param name="targetLeaderContactId">已驗證的 target leader contact ID。</param>
    /// <param name="expectedRelationshipListId">提供 expected area leader/name 的專用 task-owned list ID。</param>
    /// <param name="idempotencyKey">單次 dispatch 的 bounded idempotency key。</param>
    /// <param name="cancellationToken">只控制本次 bridge scope，取消不會觸發 retry。</param>
    /// <returns>完成或 fail-closed 的去識別化 reconciliation/cleanup 結果。</returns>
    internal static async Task<P72ListManagementFixtureBridgeResult> ExecuteSmallGroupFieldsAsync(
        IPackage02ListManagementClient client,
        IP72ListManagementFixtureStore store,
        Guid listId,
        SmallGroupFixedFieldsUpdateMode mode,
        Guid targetLeaderContactId,
        Guid expectedRelationshipListId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        RequireNonEmptyGuid(listId, nameof(listId));
        RequireNonEmptyGuid(targetLeaderContactId, nameof(targetLeaderContactId));
        if (expectedRelationshipListId == Guid.Empty || expectedRelationshipListId == listId)
        {
            return NoGo("fixture-precondition-failed", false, "baseline-invalid", "not-required");
        }

        ValidateSmallGroupMode(mode);
        var key = RequireIdempotencyKey(idempotencyKey, nameof(idempotencyKey));

        cancellationToken.ThrowIfCancellationRequested();
        var baseline = store.ReadSmallGroupFields(listId);
        var expected = store.ResolveSmallGroupExpected(
            listId,
            mode,
            targetLeaderContactId,
            expectedRelationshipListId);
        if (baseline == expected)
        {
            return NoGo("write-not-required", false, "baseline", "not-required");
        }

        var operationExecuted = false;
        var writeReportedSuccess = false;
        var writeFaulted = false;
        try
        {
            operationExecuted = true;
            var response = await client.UpdateSmallGroupFieldsAsync(new SmallGroupFixedFieldsUpdateRequest
            {
                ProfileAlias = ProfileAlias,
                WorkloadSubjectId = WorkloadSubjectId,
                ListId = listId,
                Mode = mode,
                TargetLeaderContactId = targetLeaderContactId,
                IdempotencyKey = key
            }, cancellationToken).ConfigureAwait(false);
            writeReportedSuccess = IsChangedAndConfirmed(response);
        }
        catch (Exception) when (operationExecuted)
        {
            writeFaulted = true;
        }

        P72SmallGroupFixedFieldsSnapshot current;
        try
        {
            current = store.ReadSmallGroupFields(listId);
        }
        catch (Exception)
        {
            return NoGo("reconciliation-failed", operationExecuted, "unknown", "manual-reconciliation-required");
        }

        if (current == baseline)
        {
            return NoGo(
                writeReportedSuccess ? "write-response-state-mismatch" : "write-not-committed",
                operationExecuted,
                "baseline",
                "not-required");
        }

        if (current != expected)
        {
            return NoGo("write-ambiguous", operationExecuted, "partial-or-unknown", "manual-reconciliation-required");
        }

        var cleanupFaulted = false;
        try
        {
            store.RestoreSmallGroupFields(listId, baseline);
        }
        catch (Exception)
        {
            cleanupFaulted = true;
        }

        P72SmallGroupFixedFieldsSnapshot afterCleanup;
        try
        {
            afterCleanup = store.ReadSmallGroupFields(listId);
        }
        catch (Exception)
        {
            return NoGo("cleanup-reconciliation-failed", operationExecuted, ReconciliationState(writeFaulted, writeReportedSuccess), "manual-reconciliation-required");
        }

        if (afterCleanup != baseline)
        {
            return NoGo("cleanup-failed", operationExecuted, ReconciliationState(writeFaulted, writeReportedSuccess), "manual-reconciliation-required");
        }

        return FinalizeConfirmedCleanup(operationExecuted, writeReportedSuccess, writeFaulted, cleanupFaulted);
    }

    /// <summary>
    /// 執行 contact owner assignment evidence。baseline 與 target owner 必須不同，否則不 dispatch；connector
    /// 已負責 target active-systemuser validation，bridge 則負責 timeout 後只以 ownerid read-back 判定，再以
    /// captured baseline owner 執行一次 cleanup。它不接受 team 或任意 entity owner。
    /// </summary>
    internal static async Task<P72ListManagementFixtureBridgeResult> ExecuteOwnerAssignmentAsync(
        IPackage02ListManagementClient client,
        IP72ListManagementFixtureStore store,
        Guid contactId,
        Guid targetOwnerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        RequireNonEmptyGuid(contactId, nameof(contactId));
        RequireNonEmptyGuid(targetOwnerId, nameof(targetOwnerId));
        var key = RequireIdempotencyKey(idempotencyKey, nameof(idempotencyKey));

        cancellationToken.ThrowIfCancellationRequested();
        var baselineOwnerId = store.ReadOwnerId(contactId);
        RequireNonEmptyGuid(baselineOwnerId, "baselineOwnerId");
        if (baselineOwnerId == targetOwnerId)
        {
            return NoGo("write-not-required", false, "baseline", "not-required");
        }

        var operationExecuted = false;
        var writeReportedSuccess = false;
        var writeFaulted = false;
        try
        {
            operationExecuted = true;
            var response = await client.AssignContactOwnerAsync(new ContactOwnerAssignmentRequest
            {
                ProfileAlias = ProfileAlias,
                WorkloadSubjectId = WorkloadSubjectId,
                ContactId = contactId,
                OwnerSystemUserId = targetOwnerId,
                IdempotencyKey = key
            }, cancellationToken).ConfigureAwait(false);
            writeReportedSuccess = IsChangedAndConfirmed(response);
        }
        catch (Exception) when (operationExecuted)
        {
            writeFaulted = true;
        }

        Guid currentOwnerId;
        try
        {
            currentOwnerId = store.ReadOwnerId(contactId);
        }
        catch (Exception)
        {
            return NoGo("reconciliation-failed", operationExecuted, "unknown", "manual-reconciliation-required");
        }

        if (currentOwnerId == baselineOwnerId)
        {
            return NoGo(
                writeReportedSuccess ? "write-response-state-mismatch" : "write-not-committed",
                operationExecuted,
                "baseline",
                "not-required");
        }

        if (currentOwnerId != targetOwnerId)
        {
            return NoGo("write-ambiguous", operationExecuted, "partial-or-unknown", "manual-reconciliation-required");
        }

        var cleanupFaulted = false;
        try
        {
            store.RestoreOwner(contactId, baselineOwnerId);
        }
        catch (Exception)
        {
            cleanupFaulted = true;
        }

        Guid afterCleanupOwnerId;
        try
        {
            afterCleanupOwnerId = store.ReadOwnerId(contactId);
        }
        catch (Exception)
        {
            return NoGo("cleanup-reconciliation-failed", operationExecuted, ReconciliationState(writeFaulted, writeReportedSuccess), "manual-reconciliation-required");
        }

        if (afterCleanupOwnerId != baselineOwnerId)
        {
            return NoGo("cleanup-failed", operationExecuted, ReconciliationState(writeFaulted, writeReportedSuccess), "manual-reconciliation-required");
        }

        return FinalizeConfirmedCleanup(operationExecuted, writeReportedSuccess, writeFaulted, cleanupFaulted);
    }

    /// <summary>
    /// 執行 contact list-transfer composite evidence。baseline 必須是完整可證實的 source→target 前狀態；dispatch
    /// 後只有 source/target membership、matching single present record、primary-list lookup 與 optional owner 全部
    /// 同時為 expected，才可讓 store 使用 observed record ID 做一次 bounded cleanup。任何 partial graph 不重開
    /// composite，也不刪除無法證明屬於本 fixture 的 record。
    /// </summary>
    internal static async Task<P72ListManagementFixtureBridgeResult> ExecuteTransferAsync(
        IPackage02ListManagementClient client,
        IP72ListManagementFixtureStore store,
        P72TransferFixture fixture,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateTransferFixture(fixture);
        var key = RequireIdempotencyKey(idempotencyKey, nameof(idempotencyKey));

        cancellationToken.ThrowIfCancellationRequested();
        var baseline = store.ReadTransferGraph(fixture);
        if (!IsTransferBaseline(fixture, baseline))
        {
            return NoGo("fixture-precondition-failed", false, "baseline-invalid", "not-required");
        }

        var operationExecuted = false;
        var writeReportedSuccess = false;
        var writeFaulted = false;
        try
        {
            operationExecuted = true;
            var response = await client.TransferContactBetweenListsAsync(new ContactListTransferRequest
            {
                ProfileAlias = ProfileAlias,
                WorkloadSubjectId = WorkloadSubjectId,
                ContactId = fixture.ContactId,
                SourceListId = fixture.SourceListId,
                TargetListId = fixture.TargetListId,
                WeekStartDate = fixture.WeekStartDate,
                OwnerSystemUserId = fixture.TargetOwnerId,
                IdempotencyKey = key
            }, cancellationToken).ConfigureAwait(false);
            writeReportedSuccess = IsChangedAndConfirmed(response);
        }
        catch (Exception) when (operationExecuted)
        {
            writeFaulted = true;
        }

        P72TransferGraphSnapshot current;
        try
        {
            current = store.ReadTransferGraph(fixture);
        }
        catch (Exception)
        {
            return NoGo("reconciliation-failed", operationExecuted, "unknown", "manual-reconciliation-required");
        }

        if (current == baseline)
        {
            return NoGo(
                writeReportedSuccess ? "write-response-state-mismatch" : "write-not-committed",
                operationExecuted,
                "baseline",
                "not-required");
        }

        if (!IsTransferExpected(fixture, current))
        {
            return NoGo("write-ambiguous", operationExecuted, "partial-or-unknown", "manual-reconciliation-required");
        }

        var cleanupFaulted = false;
        try
        {
            store.RestoreTransferGraph(fixture, baseline, current);
        }
        catch (Exception)
        {
            cleanupFaulted = true;
        }

        P72TransferGraphSnapshot afterCleanup;
        try
        {
            afterCleanup = store.ReadTransferGraph(fixture);
        }
        catch (Exception)
        {
            return NoGo("cleanup-reconciliation-failed", operationExecuted, ReconciliationState(writeFaulted, writeReportedSuccess), "manual-reconciliation-required");
        }

        if (afterCleanup != baseline)
        {
            return NoGo("cleanup-failed", operationExecuted, ReconciliationState(writeFaulted, writeReportedSuccess), "manual-reconciliation-required");
        }

        return FinalizeConfirmedCleanup(operationExecuted, writeReportedSuccess, writeFaulted, cleanupFaulted);
    }

    /// <summary>將 expected membership 還原 baseline，並用完整 subset read-back 判定資料與 transport outcome。</summary>
    private static P72ListManagementFixtureBridgeResult CompleteMembershipCleanup(
        IP72ListManagementFixtureStore store,
        Guid listId,
        IReadOnlyList<Guid> memberIds,
        P72MembershipSnapshot baseline,
        P72MembershipSnapshot expected,
        bool operationExecuted,
        bool writeReportedSuccess,
        bool writeFaulted)
    {
        var cleanupFaulted = false;
        try
        {
            store.RestoreMembership(listId, memberIds, baseline);
        }
        catch (Exception)
        {
            cleanupFaulted = true;
        }

        P72MembershipSnapshot afterCleanup;
        try
        {
            afterCleanup = store.ReadMembership(listId, memberIds);
        }
        catch (Exception)
        {
            return NoGo("cleanup-reconciliation-failed", operationExecuted, ReconciliationState(writeFaulted, writeReportedSuccess), "manual-reconciliation-required");
        }

        if (!MembershipMatches(afterCleanup, baseline))
        {
            return NoGo("cleanup-failed", operationExecuted, ReconciliationState(writeFaulted, writeReportedSuccess), "manual-reconciliation-required");
        }

        return FinalizeConfirmedCleanup(operationExecuted, writeReportedSuccess, writeFaulted, cleanupFaulted);
    }

    /// <summary>
    /// 將已經由 read-back 證明的 cleanup 結果投影成固定 evidence category。即使資料已恢復，write/cleanup
    /// transport 曾 fault 或 typed response 不符時仍保持 no-go，避免將未知 delivery 升格為可靠成功。
    /// </summary>
    private static P72ListManagementFixtureBridgeResult FinalizeConfirmedCleanup(
        bool operationExecuted,
        bool writeReportedSuccess,
        bool writeFaulted,
        bool cleanupFaulted)
    {
        var reconciliationState = ReconciliationState(writeFaulted, writeReportedSuccess);
        if (cleanupFaulted)
        {
            return NoGo("cleanup-ambiguous-reconciled", operationExecuted, reconciliationState, "restored-after-fault");
        }

        if (writeFaulted)
        {
            return NoGo("write-ambiguous-reconciled", operationExecuted, reconciliationState, "restored");
        }

        if (!writeReportedSuccess)
        {
            return NoGo("write-result-invalid", operationExecuted, reconciliationState, "restored");
        }

        return new P72ListManagementFixtureBridgeResult
        {
            Outcome = "go",
            Reason = string.Empty,
            OperationExecuted = operationExecuted,
            ReconciliationState = reconciliationState,
            CleanupState = "restored"
        };
    }

    /// <summary>判斷 static-list response 是否表示 single mutation 且 connector 已以 read-back 確認。</summary>
    private static bool IsChangedAndConfirmed(StaticListMembershipMutationResult response)
        => response is not null &&
           response.Disposition == P72ControlledMutationDisposition.Changed &&
           response.CorrelationCategory == P72ControlledMutationCorrelationCategory.ReadBackConfirmed;

    /// <summary>判斷 small-group response 是否表示 single fixed-field mutation 且 connector 已 read-back 確認。</summary>
    private static bool IsChangedAndConfirmed(SmallGroupFixedFieldsMutationResult response)
        => response is not null &&
           response.Disposition == P72ControlledMutationDisposition.Changed &&
           response.CorrelationCategory == P72ControlledMutationCorrelationCategory.ReadBackConfirmed;

    /// <summary>判斷 contact owner response 是否表示 single Assign 且 connector 已 ownerid read-back 確認。</summary>
    private static bool IsChangedAndConfirmed(ContactOwnerAssignmentResult response)
        => response is not null &&
           response.Disposition == P72ControlledMutationDisposition.Changed &&
           response.CorrelationCategory == P72ControlledMutationCorrelationCategory.ReadBackConfirmed;

    /// <summary>判斷 transfer response 是否表示完整 fixed graph 已由 connector read-back 確認。</summary>
    private static bool IsChangedAndConfirmed(ContactListTransferResult response)
        => response is not null &&
           response.Disposition == P72ControlledMutationDisposition.Changed &&
           response.CorrelationCategory == P72ControlledMutationCorrelationCategory.ReadBackConfirmed;

    /// <summary>比較 requested subset 的 present set；iteration 只在最多一千個 task-owned IDs 內進行。</summary>
    private static bool MembershipMatches(P72MembershipSnapshot left, P72MembershipSnapshot right)
        => left.PresentMemberIds.SetEquals(right.PresentMemberIds);

    /// <summary>確認每個 requested member 都存在；memberIds 已先 bounded、distinct 且 defensive-copy。</summary>
    private static bool ContainsAll(P72MembershipSnapshot snapshot, IReadOnlyList<Guid> memberIds)
        => memberIds.All(snapshot.PresentMemberIds.Contains);

    /// <summary>驗證 transfer baseline 是完整 source→target 前狀態；不完整既有資料不可拿來做 live evidence。</summary>
    private static bool IsTransferBaseline(P72TransferFixture fixture, P72TransferGraphSnapshot state)
        => (!fixture.SourceListId.HasValue || state.SourceMembershipPresent) &&
           !state.TargetMembershipPresent &&
           state.PresentRecordId is null &&
           !state.PresentRecordMatches &&
           state.PrimaryListId != fixture.TargetListId &&
           (!fixture.TargetOwnerId.HasValue || state.OwnerId != fixture.TargetOwnerId);

    /// <summary>驗證 transfer expected 是所有 component 同時完成的 graph；單一 component partial 不可 cleanup。</summary>
    private static bool IsTransferExpected(P72TransferFixture fixture, P72TransferGraphSnapshot state)
        => (!fixture.SourceListId.HasValue || !state.SourceMembershipPresent) &&
           state.TargetMembershipPresent &&
           state.PresentRecordId is not null &&
           state.PresentRecordMatches &&
           state.PrimaryListId == fixture.TargetListId &&
           (!fixture.TargetOwnerId.HasValue || state.OwnerId == fixture.TargetOwnerId);

    /// <summary>將 dispatch response/fault 組合成不含敏感資料的 expected category。</summary>
    private static string ReconciliationState(bool writeFaulted, bool writeReportedSuccess)
        => writeFaulted
            ? "expected-after-fault"
            : writeReportedSuccess
                ? "expected"
                : "expected-after-invalid-response";

    /// <summary>建立固定 no-go 結果，避免每條 return path 手寫並意外帶入 caller input 或 exception。</summary>
    private static P72ListManagementFixtureBridgeResult NoGo(
        string reason,
        bool operationExecuted,
        string reconciliationState,
        string cleanupState)
        => new()
        {
            Outcome = "no-go",
            Reason = reason,
            OperationExecuted = operationExecuted,
            ReconciliationState = reconciliationState,
            CleanupState = cleanupState
        };

    /// <summary>在 CRM call 前檢查 list/contact/owner identity，不讓 empty GUID 擴大成無邊界 query 或 update。</summary>
    private static void RequireNonEmptyGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty fixture identity is required.", parameterName);
        }
    }

    /// <summary>複製並排序最多一千個 member IDs，阻斷外部 mutable list 在 await 期間被竄改或保留。</summary>
    private static Guid[] CopyDistinctMembers(IReadOnlyList<Guid>? source, string parameterName)
    {
        if (source is null || source.Count is < 1 or > MaximumMemberIds)
        {
            throw new ArgumentException("The fixture member set must contain between one and one thousand entries.", parameterName);
        }

        var copy = new Guid[source.Count];
        var distinct = new HashSet<Guid>();
        for (var index = 0; index < source.Count; index++)
        {
            var memberId = source[index];
            if (memberId == Guid.Empty || !distinct.Add(memberId))
            {
                throw new ArgumentException("The fixture member set must contain distinct non-empty GUIDs.", parameterName);
            }

            copy[index] = memberId;
        }

        Array.Sort(copy);
        return copy;
    }

    /// <summary>驗證 1–128 個 RFC 3986 unreserved 字元；key 不寫入 log、不跨 request 儲存，也不是 retry 許可。</summary>
    private static string RequireIdempotencyKey(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > MaximumIdempotencyKeyCharacters ||
            value.Any(static character =>
                !((character >= 'a' && character <= 'z') ||
                  (character >= 'A' && character <= 'Z') ||
                  (character >= '0' && character <= '9') ||
                  character is '-' or '.' or '_' or '~')))
        {
            throw new ArgumentException("A bounded URL-safe idempotency key is required.", parameterName);
        }

        return value;
    }

    /// <summary>拒絕未知 enum，避免未來 mode 無意間取得 connector lease 或任意欄位更新能力。</summary>
    private static void ValidateSmallGroupMode(SmallGroupFixedFieldsUpdateMode mode)
    {
        if (mode is not SmallGroupFixedFieldsUpdateMode.ChangeRaceLeader and not SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The fixture small-group mode is unsupported.");
        }
    }

    /// <summary>驗證 composite identity 與 UTC Sunday，避免本機時區、DST 或相同 source/target list 導致不受控 rollback。</summary>
    private static void ValidateTransferFixture(P72TransferFixture fixture)
    {
        RequireNonEmptyGuid(fixture.ContactId, nameof(fixture.ContactId));
        RequireNonEmptyGuid(fixture.TargetListId, nameof(fixture.TargetListId));
        if (fixture.SourceListId is Guid sourceListId)
        {
            RequireNonEmptyGuid(sourceListId, nameof(fixture.SourceListId));
            if (sourceListId == fixture.TargetListId)
            {
                throw new ArgumentException("The fixture source and target lists must differ.", nameof(fixture));
            }
        }

        if (fixture.TargetOwnerId is Guid targetOwnerId)
        {
            RequireNonEmptyGuid(targetOwnerId, nameof(fixture.TargetOwnerId));
        }

        var utc = fixture.WeekStartDate.ToUniversalTime();
        if (fixture.WeekStartDate.Offset != TimeSpan.Zero ||
            utc.TimeOfDay != TimeSpan.Zero ||
            utc.DayOfWeek != DayOfWeek.Sunday)
        {
            throw new ArgumentException("The fixture week start must be UTC Sunday midnight.", nameof(fixture));
        }
    }
}
