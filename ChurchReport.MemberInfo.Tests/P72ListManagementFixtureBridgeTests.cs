// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72ListManagementFixtureBridgeTests.cs
// 用途：以離線狀態機驗證 P7.2 Slice C static-list、小組固定欄位、owner 指派與
//       contact transfer fixture bridge 的單次 dispatch、reconciliation、復原與
//       去識別化結果契約。
//
// 安全與生命週期：
// 1. 測試替身只保留合成 GUID 與純值 projection，不建立 CRM/WCF/HTTP/credential/session。
// 2. 每個測試獨佔一個 store 和 client；bridge 不取得 Dispose ownership，using scope 結束
//    時由測試釋放，模擬真實 evidence runner 對 Data8 fixture store 的單一 owner 規則。
// 3. 每個 fault injection 都驗證一次 dispatch 後只做固定 read-back，絕不以相同 key 重送或
//    猜測 partial state；僅在完整 expected state 時允許執行一次 bounded cleanup。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.ListManagement;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 P7.2 Slice C fixture bridge 的安全狀態轉移。每個案例只使用 task-local 合成 identity，
/// 並以 client 呼叫次數、store restore 次數與固定分類作為決定性 assertion；若實作加入 automatic
/// retry、跨 fixture state 或未確認 cleanup，這些測試必須失敗。
/// </summary>
public sealed class P72ListManagementFixtureBridgeTests
{
    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SourceListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TargetListId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BaselineOwnerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TargetOwnerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TargetLeaderId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid DifferentLeaderId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ExpectedRelationshipListId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly DateTimeOffset SundayStart = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 保護 static-list add 的正常路徑：唯一的 typed capability dispatch 後必須以固定 membership
    /// read-back 證明 expected state，接著還原原 membership，且 restore read-back 完整相符才可回傳 go。
    /// </summary>
    [Fact]
    public async Task Adding_missing_member_restores_exact_baseline_and_returns_go()
    {
        using var store = new RecordingFixtureStore();
        store.SetMembership(TargetListId, []);
        var client = new RecordingListManagementClient
        {
            AddHandler = request =>
            {
                store.SetMembership(request.ListId, request.MemberIds);
                return Task.FromResult(ChangedMembership());
            }
        };

        var result = await P72ListManagementFixtureBridge.ExecuteAddMembersAsync(
            client,
            store,
            TargetListId,
            [ContactId],
            "p72-list-add-success");

        result.Outcome.Should().Be("go");
        result.Reason.Should().BeEmpty();
        result.OperationExecuted.Should().BeTrue();
        result.ReconciliationState.Should().Be("expected");
        result.CleanupState.Should().Be("restored");
        client.AddCallCount.Should().Be(1);
        store.MembershipRestoreCount.Should().Be(1);
        store.IsMember(TargetListId, ContactId).Should().BeFalse();
    }

    /// <summary>
    /// 注入「add 已在 server commit、caller 卻收到例外」；bridge 必須以 membership read-back 證明
    /// expected state、完成一次 baseline restore，並維持 no-go，不能把 timeout-after-commit 當 success。
    /// </summary>
    [Fact]
    public async Task Add_fault_after_commit_reconciles_and_restores_without_retry()
    {
        using var store = new RecordingFixtureStore();
        store.SetMembership(TargetListId, []);
        var client = new RecordingListManagementClient
        {
            AddHandler = request =>
            {
                store.SetMembership(request.ListId, request.MemberIds);
                throw new InvalidOperationException("injected-add-after-commit");
            }
        };

        var result = await P72ListManagementFixtureBridge.ExecuteAddMembersAsync(
            client,
            store,
            TargetListId,
            [ContactId],
            "p72-list-add-ambiguous");

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("write-ambiguous-reconciled");
        result.OperationExecuted.Should().BeTrue();
        result.ReconciliationState.Should().Be("expected-after-fault");
        result.CleanupState.Should().Be("restored");
        client.AddCallCount.Should().Be(1);
        store.MembershipRestoreCount.Should().Be(1);
        store.IsMember(TargetListId, ContactId).Should().BeFalse();
    }

    /// <summary>
    /// 保護 static-list remove：從已存在的 fixture membership 移除後，bridge 只能讀回 absence、
    /// 還原同一 member 的原 baseline，再回傳 go；不允許以 generic relationship API 擴張資料範圍。
    /// </summary>
    [Fact]
    public async Task Removing_existing_member_restores_exact_baseline_and_returns_go()
    {
        using var store = new RecordingFixtureStore();
        store.SetMembership(TargetListId, [ContactId]);
        var client = new RecordingListManagementClient
        {
            RemoveHandler = request =>
            {
                store.SetMembership(request.ListId, []);
                return Task.FromResult(ChangedMembership());
            }
        };

        var result = await P72ListManagementFixtureBridge.ExecuteRemoveMemberAsync(
            client,
            store,
            TargetListId,
            ContactId,
            "p72-list-remove-success");

        result.Outcome.Should().Be("go");
        result.Reason.Should().BeEmpty();
        result.OperationExecuted.Should().BeTrue();
        result.ReconciliationState.Should().Be("expected");
        result.CleanupState.Should().Be("restored");
        client.RemoveCallCount.Should().Be(1);
        store.MembershipRestoreCount.Should().Be(1);
        store.IsMember(TargetListId, ContactId).Should().BeTrue();
    }

    /// <summary>
    /// 注入 small-group write 後的 partial six-field projection；它既非完整 baseline 也非完整
    /// server-owned expected projection，因此 bridge 必須停止為 manual reconciliation，不得嘗試 restore。
    /// </summary>
    [Fact]
    public async Task Partial_small_group_state_stops_without_cleanup_guess()
    {
        using var store = new RecordingFixtureStore
        {
            SmallGroup = new P72SmallGroupFixedFieldsSnapshot(
                BaselineOwnerId,
                "baseline-area",
                DifferentLeaderId,
                TargetOwnerId,
                null,
                null),
            SmallGroupExpected = new P72SmallGroupFixedFieldsSnapshot(
                TargetOwnerId,
                "expected-area",
                TargetLeaderId,
                null,
                null,
                null)
        };
        var client = new RecordingListManagementClient
        {
            SmallGroupHandler = _ =>
            {
                store.SmallGroup = store.SmallGroup with { RaceLeaderId = TargetLeaderId };
                throw new InvalidOperationException("injected-small-group-partial");
            }
        };

        var result = await P72ListManagementFixtureBridge.ExecuteSmallGroupFieldsAsync(
            client,
            store,
            TargetListId,
            SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
            TargetLeaderId,
            ExpectedRelationshipListId,
            "p72-small-group-partial");

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("write-ambiguous");
        result.ReconciliationState.Should().Be("partial-or-unknown");
        result.CleanupState.Should().Be("manual-reconciliation-required");
        client.SmallGroupCallCount.Should().Be(1);
        store.SmallGroupRestoreCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 bridge test double 本身的 fail-closed 契約：故意省略 descriptor-bound relationship list
    /// identity 時，舊三參數 store 入口不得回傳 synthetic expected projection。這個故障注入可防止未來
    /// bridge 若意外退回廣泛 leader-only lookup，離線測試仍以錯誤的「可解析 expected state」掩蓋真機
    /// fixture isolation 缺陷；決定性 assertion 是立即擲出固定例外，且沒有 CRM、dispatch 或 cleanup
    /// 資源可被建立或保留。
    /// </summary>
    [Fact]
    public void Legacy_small_group_store_contract_fails_closed_without_descriptor_relationship_identity()
    {
        using var store = new RecordingFixtureStore();

        var action = () => store.ResolveSmallGroupExpected(
            TargetListId,
            SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
            TargetLeaderId);

        action.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 contact owner Assign：target owner read-back 成功後，fixture owner 必須以 baseline systemuser
    /// 執行一次固定 restore 並再次 read-back，才能把一次 live evidence 分類為 go。
    /// </summary>
    [Fact]
    public async Task Owner_assignment_restores_baseline_and_returns_go()
    {
        using var store = new RecordingFixtureStore { OwnerId = BaselineOwnerId };
        var client = new RecordingListManagementClient
        {
            OwnerHandler = request =>
            {
                store.OwnerId = request.OwnerSystemUserId;
                return Task.FromResult(ChangedOwner());
            }
        };

        var result = await P72ListManagementFixtureBridge.ExecuteOwnerAssignmentAsync(
            client,
            store,
            ContactId,
            TargetOwnerId,
            "p72-owner-success");

        result.Outcome.Should().Be("go");
        result.Reason.Should().BeEmpty();
        result.OperationExecuted.Should().BeTrue();
        result.ReconciliationState.Should().Be("expected");
        result.CleanupState.Should().Be("restored");
        client.OwnerCallCount.Should().Be(1);
        store.OwnerRestoreCount.Should().Be(1);
        store.OwnerId.Should().Be(BaselineOwnerId);
    }

    /// <summary>
    /// 保護完整 transfer composite：source/target membership、唯一 matching present record、primary list
    /// lookup 與 optional owner 都必須同時符合 expected graph，才可執行一次 store-owned bounded rollback。
    /// </summary>
    [Fact]
    public async Task Complete_transfer_graph_restores_baseline_and_returns_go()
    {
        var fixture = CreateTransferFixture();
        var baseline = new P72TransferGraphSnapshot(
            SourceMembershipPresent: true,
            TargetMembershipPresent: false,
            PresentRecordId: null,
            PresentRecordMatches: false,
            PrimaryListId: SourceListId,
            OwnerId: BaselineOwnerId);
        using var store = new RecordingFixtureStore { Transfer = baseline };
        var client = new RecordingListManagementClient
        {
            TransferHandler = request =>
            {
                store.Transfer = new P72TransferGraphSnapshot(
                    SourceMembershipPresent: false,
                    TargetMembershipPresent: true,
                    PresentRecordId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
                    PresentRecordMatches: true,
                    PrimaryListId: request.TargetListId,
                    OwnerId: request.OwnerSystemUserId);
                return Task.FromResult(ChangedTransfer());
            }
        };

        var result = await P72ListManagementFixtureBridge.ExecuteTransferAsync(
            client,
            store,
            fixture,
            "p72-transfer-success");

        result.Outcome.Should().Be("go");
        result.Reason.Should().BeEmpty();
        result.OperationExecuted.Should().BeTrue();
        result.ReconciliationState.Should().Be("expected");
        result.CleanupState.Should().Be("restored");
        client.TransferCallCount.Should().Be(1);
        store.TransferRestoreCount.Should().Be(1);
        store.Transfer.Should().Be(baseline);
    }

    /// <summary>
    /// 注入 transfer partial graph；target membership 已變而 present record、primary list 或 owner 未完整建立時，
    /// bridge 必須 retain cleanup owner、禁止重送 composite 與禁止猜測要刪除哪一筆 record。
    /// </summary>
    [Fact]
    public async Task Partial_transfer_graph_stops_without_cleanup_guess()
    {
        var fixture = CreateTransferFixture();
        var baseline = new P72TransferGraphSnapshot(
            SourceMembershipPresent: true,
            TargetMembershipPresent: false,
            PresentRecordId: null,
            PresentRecordMatches: false,
            PrimaryListId: SourceListId,
            OwnerId: BaselineOwnerId);
        using var store = new RecordingFixtureStore { Transfer = baseline };
        var client = new RecordingListManagementClient
        {
            TransferHandler = _ =>
            {
                store.Transfer = baseline with { TargetMembershipPresent = true };
                throw new InvalidOperationException("injected-transfer-partial");
            }
        };

        var result = await P72ListManagementFixtureBridge.ExecuteTransferAsync(
            client,
            store,
            fixture,
            "p72-transfer-partial");

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("write-ambiguous");
        result.ReconciliationState.Should().Be("partial-or-unknown");
        result.CleanupState.Should().Be("manual-reconciliation-required");
        client.TransferCallCount.Should().Be(1);
        store.TransferRestoreCount.Should().Be(0);
        store.Transfer.Should().NotBe(baseline, because: "partial state must not be overwritten by an unproven rollback");
    }

    /// <summary>建立固定 CE 9.1 Sunday-start transfer descriptor；它不含 endpoint、credential 或 CRM SDK input。</summary>
    private static P72TransferFixture CreateTransferFixture()
        => new(ContactId, SourceListId, TargetListId, SundayStart, TargetOwnerId);

    /// <summary>建立 typed add/remove 成功的固定 response；response 只表達已證實 mutation，不含 CRM identity。</summary>
    private static StaticListMembershipMutationResult ChangedMembership()
        => new()
        {
            Disposition = P72ControlledMutationDisposition.Changed,
            CorrelationCategory = P72ControlledMutationCorrelationCategory.ReadBackConfirmed
        };

    /// <summary>建立 typed owner Assign 成功的固定 response。</summary>
    private static ContactOwnerAssignmentResult ChangedOwner()
        => new()
        {
            Disposition = P72ControlledMutationDisposition.Changed,
            CorrelationCategory = P72ControlledMutationCorrelationCategory.ReadBackConfirmed
        };

    /// <summary>建立 typed transfer 成功的固定 response。</summary>
    private static ContactListTransferResult ChangedTransfer()
        => new()
        {
            Disposition = P72ControlledMutationDisposition.Changed,
            CorrelationCategory = P72ControlledMutationCorrelationCategory.ReadBackConfirmed
        };

    /// <summary>
    /// 離線 fixture store。它只保存 Slice C 五個 operation 的純值 state，並在 restore 計數上提供決定性
    /// assertion；不接受 Entity、QueryBase、FetchXML、endpoint、credential 或任何跨測試 static state。
    /// </summary>
    private sealed class RecordingFixtureStore : IP72ListManagementFixtureStore
    {
        private readonly Dictionary<Guid, HashSet<Guid>> _memberships = [];
        private bool _disposed;

        /// <summary>目前 small-group 六欄 projection；每個測試可用純值建立自己的 baseline。</summary>
        public P72SmallGroupFixedFieldsSnapshot SmallGroup { get; set; } = new(null, null, null, null, null, null);

        /// <summary>server-owned expected projection 的測試替身；bridge 不能由 caller 欄位 map 建構這個值。</summary>
        public P72SmallGroupFixedFieldsSnapshot SmallGroupExpected { get; set; } = new(null, null, null, null, null, null);

        /// <summary>目前 contact owner 純 GUID；沒有 CRM Entity 或 user profile graph。</summary>
        public Guid OwnerId { get; set; } = BaselineOwnerId;

        /// <summary>目前 transfer graph projection；只在單一測試 scope 存活。</summary>
        public P72TransferGraphSnapshot Transfer { get; set; } = new(false, false, null, false, null, null);

        /// <summary>membership cleanup 呼叫次數，證明 bridge 不 retry。</summary>
        public int MembershipRestoreCount { get; private set; }

        /// <summary>small-group cleanup 呼叫次數。</summary>
        public int SmallGroupRestoreCount { get; private set; }

        /// <summary>owner cleanup 呼叫次數。</summary>
        public int OwnerRestoreCount { get; private set; }

        /// <summary>transfer cleanup 呼叫次數。</summary>
        public int TransferRestoreCount { get; private set; }

        /// <summary>建立或覆寫某 task-local static-list membership set。</summary>
        public void SetMembership(Guid listId, IEnumerable<Guid> members)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _memberships[listId] = new HashSet<Guid>(members);
        }

        /// <summary>只供 assertion 查詢單一 synthetic membership。</summary>
        public bool IsMember(Guid listId, Guid contactId)
            => _memberships.TryGetValue(listId, out var members) && members.Contains(contactId);

        /// <summary>
        /// 讀取指定 synthetic list 的 requested membership subset。替身只投影呼叫端提供的 bounded IDs，
        /// 並在 Dispose 後拒絕存取，模擬 live store 不可跨 bridge scope 保存 mutable membership state。
        /// </summary>
        public P72MembershipSnapshot ReadMembership(Guid listId, IReadOnlyList<Guid> contactIds)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var members = _memberships.TryGetValue(listId, out var current)
                ? current
                : [];
            return new P72MembershipSnapshot(
                new HashSet<Guid>(contactIds.Where(members.Contains)));
        }

        /// <summary>
        /// 以 captured subset baseline 覆寫目前 synthetic membership。先移除 requested IDs 再加入 baseline，
        /// 使測試可精確判定 bridge 是否只做一次 bounded cleanup，而非碰觸其他 list/member state。
        /// </summary>
        public void RestoreMembership(Guid listId, IReadOnlyList<Guid> contactIds, P72MembershipSnapshot baseline)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            MembershipRestoreCount++;
            var members = _memberships.TryGetValue(listId, out var current)
                ? current
                : _memberships[listId] = [];
            foreach (var contactId in contactIds)
            {
                members.Remove(contactId);
            }

            members.UnionWith(baseline.PresentMemberIds);
        }

        /// <summary>
        /// 回傳目前 six-field synthetic projection，並斷言 bridge 只讀取指定 task-owned target list；
        /// 不建立 CRM SDK object、retry 或跨測試 cache。
        /// </summary>
        public P72SmallGroupFixedFieldsSnapshot ReadSmallGroupFields(Guid listId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            listId.Should().Be(TargetListId);
            return SmallGroup;
        }

        /// <summary>
        /// 舊三參數入口刻意拒絕提供 expected projection。它缺少 descriptor-bound relationship list ID，
        /// 若讓它從 synthetic state 或 CRM 廣泛搜尋 area leader/name，會讓 bridge test 錯誤接受跨 fixture
        /// identity；新 bridge path 必須只使用四參數 overload，並由該 overload 驗證專用 relationship ID。
        /// </summary>
        public P72SmallGroupFixedFieldsSnapshot ResolveSmallGroupExpected(
            Guid listId,
            SmallGroupFixedFieldsUpdateMode mode,
            Guid targetLeaderContactId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            throw new InvalidOperationException("The fixture expected relationship list identity is required.");
        }

        /// <summary>
        /// 驗證 bridge 會把 descriptor-bound relationship list ID 傳給 store。替身只接受固定 synthetic
        /// identity，避免這個 unit test 因舊三參數 API 悄悄退回到只依 leader 的廣泛查詢而仍然通過。
        /// </summary>
        public P72SmallGroupFixedFieldsSnapshot ResolveSmallGroupExpected(
            Guid listId,
            SmallGroupFixedFieldsUpdateMode mode,
            Guid targetLeaderContactId,
            Guid expectedRelationshipListId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            listId.Should().Be(TargetListId);
            targetLeaderContactId.Should().Be(TargetLeaderId);
            mode.Should().Be(SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader);
            expectedRelationshipListId.Should().Be(ExpectedRelationshipListId);
            return SmallGroupExpected;
        }

        /// <summary>
        /// 將目前 six-field state 還原為 caller 已擷取的 baseline，並記錄一次 cleanup。list identity 不符
        /// 或已 Dispose 都立即失敗，避免測試替身掩蓋 bridge 越界 rollback。
        /// </summary>
        public void RestoreSmallGroupFields(Guid listId, P72SmallGroupFixedFieldsSnapshot baseline)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            listId.Should().Be(TargetListId);
            SmallGroupRestoreCount++;
            SmallGroup = baseline;
        }

        /// <summary>
        /// 讀取指定 synthetic contact 的 owner GUID。此替身沒有 team 或任意 entity owner，確保測試只涵蓋
        /// bridge 的 fixed systemuser rollback contract。
        /// </summary>
        public Guid ReadOwnerId(Guid contactId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            contactId.Should().Be(ContactId);
            return OwnerId;
        }

        /// <summary>
        /// 將 synthetic contact owner 還原為 baseline 並記錄 cleanup 次數；橋接器若未先取得完整 baseline
        /// 或嘗試其他 contact，assertion 會立即失敗。
        /// </summary>
        public void RestoreOwner(Guid contactId, Guid baselineOwnerId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            contactId.Should().Be(ContactId);
            OwnerRestoreCount++;
            OwnerId = baselineOwnerId;
        }

        /// <summary>
        /// 回傳單一 fixed transfer graph projection，並確認 bridge 使用完整 task-owned fixture；結果僅包含
        /// synthetic pure values，因此不會在 test case 外保留 CRM record 或 owner identity。
        /// </summary>
        public P72TransferGraphSnapshot ReadTransferGraph(P72TransferFixture fixture)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            fixture.Should().Be(CreateTransferFixture());
            return Transfer;
        }

        /// <summary>
        /// 只在 expected graph 已含可證實 present-record ID 時還原 synthetic transfer baseline。此 assertion
        /// 保護 bridge 不得對 partial graph 猜測 delete target，並記錄唯一的 rollback 呼叫。
        /// </summary>
        public void RestoreTransferGraph(
            P72TransferFixture fixture,
            P72TransferGraphSnapshot baseline,
            P72TransferGraphSnapshot expected)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            fixture.Should().Be(CreateTransferFixture());
            expected.PresentRecordId.Should().NotBeNull("cleanup may only target the record confirmed by reconciliation");
            TransferRestoreCount++;
            Transfer = baseline;
        }

        /// <summary>結束單一 fixture scope；此替身無外部資源，仍拒絕後續存取以模擬 live store ownership。</summary>
        public void Dispose() => _disposed = true;
    }

    /// <summary>
    /// 離線 typed ProductClient。委派只注入單次 capability 回應或 fault，不能建立 retry queue、timer、
    /// background task 或 CRM connection；未使用的方法保持 fail closed。
    /// </summary>
    private sealed class RecordingListManagementClient : IPackage02ListManagementClient
    {
        /// <summary>add action 的單次注入行為。</summary>
        public Func<StaticListMembersAddRequest, Task<StaticListMembershipMutationResult>>? AddHandler { get; init; }

        /// <summary>remove action 的單次注入行為。</summary>
        public Func<StaticListMemberRemoveRequest, Task<StaticListMembershipMutationResult>>? RemoveHandler { get; init; }

        /// <summary>small-group action 的單次注入行為。</summary>
        public Func<SmallGroupFixedFieldsUpdateRequest, Task<SmallGroupFixedFieldsMutationResult>>? SmallGroupHandler { get; init; }

        /// <summary>owner action 的單次注入行為。</summary>
        public Func<ContactOwnerAssignmentRequest, Task<ContactOwnerAssignmentResult>>? OwnerHandler { get; init; }

        /// <summary>transfer composite 的單次注入行為。</summary>
        public Func<ContactListTransferRequest, Task<ContactListTransferResult>>? TransferHandler { get; init; }

        /// <summary>add dispatch 次數；任何 ambiguous path 必須維持一。</summary>
        public int AddCallCount { get; private set; }

        /// <summary>remove dispatch 次數。</summary>
        public int RemoveCallCount { get; private set; }

        /// <summary>small-group dispatch 次數。</summary>
        public int SmallGroupCallCount { get; private set; }

        /// <summary>owner dispatch 次數。</summary>
        public int OwnerCallCount { get; private set; }

        /// <summary>transfer dispatch 次數。</summary>
        public int TransferCallCount { get; private set; }

        /// <summary>
        /// 執行一次 add handler 並觀察取消。handler 缺失時 fail closed；計數器讓測試確認 timeout 或
        /// reconciliation 路徑不會以相同 key 重送 membership action。
        /// </summary>
        public Task<StaticListMembershipMutationResult> AddMembersAsync(
            StaticListMembersAddRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddCallCount++;
            return (AddHandler ?? throw new NotSupportedException()).Invoke(request);
        }

        /// <summary>
        /// 執行一次 remove handler 並觀察取消。此替身不建立 queue、timer 或 CRM connection，僅以計數器
        /// 驗證 bridge 的 single-dispatch contract。
        /// </summary>
        public Task<StaticListMembershipMutationResult> RemoveMemberAsync(
            StaticListMemberRemoveRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveCallCount++;
            return (RemoveHandler ?? throw new NotSupportedException()).Invoke(request);
        }

        /// <summary>
        /// 執行一次 fixed-field handler 並觀察取消。輸入仍是 typed request，讓測試可驗證 bridge 將
        /// descriptor-bound identity 交給 ProductClient，而不是改用 raw CRM field map。
        /// </summary>
        public Task<SmallGroupFixedFieldsMutationResult> UpdateSmallGroupFieldsAsync(
            SmallGroupFixedFieldsUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SmallGroupCallCount++;
            return (SmallGroupHandler ?? throw new NotSupportedException()).Invoke(request);
        }

        /// <summary>
        /// 執行一次 typed owner assignment handler 並觀察取消；handler fault 直接交給 bridge 的 read-back
        /// path，替身本身不 retry 或保存 owner state 到測試 scope 之外。
        /// </summary>
        public Task<ContactOwnerAssignmentResult> AssignContactOwnerAsync(
            ContactOwnerAssignmentRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OwnerCallCount++;
            return (OwnerHandler ?? throw new NotSupportedException()).Invoke(request);
        }

        /// <summary>
        /// 執行一次 typed transfer handler 並觀察取消。計數器保護 composite fault 時不會自動重開 source/
        /// target/present-record graph，避免測試掩蓋多次 mutation。
        /// </summary>
        public Task<ContactListTransferResult> TransferContactBetweenListsAsync(
            ContactListTransferRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TransferCallCount++;
            return (TransferHandler ?? throw new NotSupportedException()).Invoke(request);
        }
    }
}
