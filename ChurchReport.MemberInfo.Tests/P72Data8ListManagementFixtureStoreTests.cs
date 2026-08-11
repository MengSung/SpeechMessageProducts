// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStoreTests.cs
// 用途：驗證 P7.2 Slice C small-group expected-state store 只讀取 descriptor 指定的
//       task-owned relationship list，並在 missing 或錯誤 identity 時 fail closed。
//
// 安全與生命週期：
// 1. 每個測試只使用合成 GUID 與短生命期 IOrganizationService 替身；不建立 CRM、WCF、
//    credential、session、endpoint 或跨測試共享狀態。
// 2. 替身只允許固定 list relationship QueryExpression。任何 Create/Update/Delete/Execute
//    或其他 generic CRM API 都立即丟出，證明 expected-state proof 不會擴張成任意查詢或寫入。
// 3. using scope 是替身與 store 的唯一 Dispose owner；測試結束後 service 不可再被使用，避免
//    fixture identity 或 SDK 資源跨 test case 留存。
// ============================================================================

using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.ProductClient.ListManagement;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 保護 Slice C small-group expected relationship 的 descriptor-bound proof。測試以故障注入的
/// CRM row 驗證 store 必須同時限制 target leader 與專用 relationship list ID，並拒絕缺少或
/// 不相符的 relationship identity；這避免其他 list 的 area leader/name 進入 task-owned mutation
/// 的 expected projection。
/// </summary>
public sealed class P72Data8ListManagementFixtureStoreTests
{
    private static readonly Guid TargetSmallGroupListId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExpectedRelationshipListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WrongRelationshipListId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TargetLeaderContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AreaLeaderContactId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid MembershipListId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid MembershipContactId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid TransferSourceListId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid TransferTargetListId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid TransferContactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TransferWeeklyReportId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid WrongTransferWeeklyReportId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TransferPresentRecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TransferBaselineOwnerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateTimeOffset TransferWeekStartUtc = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 保護 listmember lookup 的兩種受支援 CRM/Data8 投影。真機 Dataverse 通常回傳帶正確 logical
    /// name 的 <see cref="EntityReference"/>；既有離線 Data8 fake 可能回傳裸 <see cref="Guid"/>。
    /// 兩者都必須代表同一個固定 list/contact identity，其他型別仍會由 store fail closed，避免本次
    /// expected-relationship hardening 意外破壞已核准的 membership read-back compatibility。
    /// </summary>
    /// <param name="useEntityReferences"><see langword="true"/> 模擬真機 lookup；否則模擬既有 raw GUID fake。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_membership_accepts_only_the_supported_guid_and_entity_reference_lookup_shapes(bool useEntityReferences)
    {
        using var service = new MembershipProjectionRecordingService(useEntityReferences);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var membership = store.ReadMembership(MembershipListId, [MembershipContactId]);

        membership.PresentMemberIds.Should().Equal(MembershipContactId);
        service.RetrieveMultipleCount.Should().Be(1);
        service.UnexpectedOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 保護使用者確認的 <c>zero-active</c> 正常分支。故障注入讓 exact target-list／UTC-Sunday
    /// 週報查詢完整回傳零列，並讓同一 contact/date 的 present-record 查詢也回傳零列；store 必須
    /// 產生「沒有既有出席紀錄、沒有週報 lookup」的安全 baseline，而不是因缺少週報拋例外。
    /// 決定性斷言同時確認 present-record query 沒有加入不存在的週報 filter，且所有 mutation API
    /// 維持零次，避免診斷讀取被誤擴張成建立或修補 weekly report。
    /// </summary>
    [Fact]
    public void Read_transfer_graph_accepts_zero_active_weekly_report_as_an_unlinked_absent_baseline()
    {
        using var service = new TransferGraphRecordingOrganizationService(
            WeeklyReportProjection.Zero,
            PresentRecordProjection.Absent,
            sourceMembershipPresent: true,
            targetMembershipPresent: false,
            primaryListId: TransferSourceListId);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.ReadTransferGraph(CreateTransferFixture());

        result.Should().Be(new P72TransferGraphSnapshot(
            SourceMembershipPresent: true,
            TargetMembershipPresent: false,
            PresentRecordId: null,
            PresentRecordMatches: false,
            PrimaryListId: TransferSourceListId,
            OwnerId: TransferBaselineOwnerId));
        service.WeeklyReportReadCount.Should().Be(1);
        service.PresentRecordReadCount.Should().Be(1);
        service.UnexpectedMutationCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 <c>exactly-one-active</c> 分支的精確 lookup read-back。替身只回傳 descriptor-bound
    /// 唯一週報，以及一筆 contact/list/date/weekly-report 全部相符的 present record；store 必須保留
    /// 該 record ID 並將 <see cref="P72TransferGraphSnapshot.PresentRecordMatches"/> 判定為 true。
    /// 決定性斷言要求 present-record query 含唯一週報 filter，且整個讀取過程不產生任何 mutation。
    /// </summary>
    [Fact]
    public void Read_transfer_graph_preserves_the_exactly_one_active_weekly_report_lookup()
    {
        using var service = new TransferGraphRecordingOrganizationService(
            WeeklyReportProjection.ExactlyOne,
            PresentRecordProjection.Matching,
            sourceMembershipPresent: false,
            targetMembershipPresent: true,
            primaryListId: TransferTargetListId);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.ReadTransferGraph(CreateTransferFixture());

        result.PresentRecordId.Should().Be(TransferPresentRecordId);
        result.PresentRecordMatches.Should().BeTrue();
        service.WeeklyReportReadCount.Should().Be(1);
        service.PresentRecordReadCount.Should().Be(1);
        service.UnexpectedMutationCount.Should().Be(0);
    }

    /// <summary>
    /// 注入 exact target-list／UTC-Sunday 週報的重複列或 paging continuation。兩種情況都代表
    /// bounded query 無法證明零或唯一週報，store 必須在 contact/present-record 讀取與任何 mutation
    /// 之前 fail closed；不得挑第一筆、掃描下一頁或退回無 lookup 分支。決定性斷言是 present-record
    /// 讀取與所有寫入計數維持零。
    /// </summary>
    /// <param name="projection">要注入的重複列或不完整 paging 投影。</param>
    [Theory]
    [InlineData(WeeklyReportProjection.Duplicate)]
    [InlineData(WeeklyReportProjection.Paging)]
    [InlineData(WeeklyReportProjection.Malformed)]
    public void Read_transfer_graph_rejects_an_ambiguous_weekly_report_before_later_graph_reads(
        WeeklyReportProjection projection)
    {
        using var service = new TransferGraphRecordingOrganizationService(
            projection,
            PresentRecordProjection.Absent,
            sourceMembershipPresent: true,
            targetMembershipPresent: false,
            primaryListId: TransferSourceListId);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var action = () => store.ReadTransferGraph(CreateTransferFixture());

        action.Should().Throw<InvalidOperationException>();
        service.PresentRecordReadCount.Should().Be(0);
        service.ContactRetrieveCount.Should().Be(0);
        service.UnexpectedMutationCount.Should().Be(0);
    }

    /// <summary>
    /// 在 <c>zero-active</c> 分支注入一筆已關聯到其他週報的既有 present record。因為零週報查詢
    /// 刻意不加 weekly lookup filter，store 必須看見這筆衝突資料，保留其短生命期 synthetic ID，並將
    /// matching 判定為 false；不得把它漏掉後宣告安全 baseline，也不得嘗試清除或改寫。決定性斷言是
    /// snapshot 明確為不相符，且 mutation 計數仍為零。
    /// </summary>
    [Fact]
    public void Read_transfer_graph_marks_an_existing_wrong_weekly_lookup_as_an_unsafe_baseline()
    {
        using var service = new TransferGraphRecordingOrganizationService(
            WeeklyReportProjection.Zero,
            PresentRecordProjection.WrongWeeklyLookup,
            sourceMembershipPresent: true,
            targetMembershipPresent: false,
            primaryListId: TransferSourceListId);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.ReadTransferGraph(CreateTransferFixture());

        result.PresentRecordId.Should().Be(TransferPresentRecordId);
        result.PresentRecordMatches.Should().BeFalse();
        service.UnexpectedMutationCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 <c>zero-active</c> expected graph 的 cleanup。替身先呈現 target membership、無週報 lookup 的
    /// matching present record 與 target primary-list；store 只能在重新證明同一 record 後依序刪除該筆
    /// record、還原 primary-list，最後移除 target membership。故障注入若仍使用 required weekly lookup
    /// 會在刪除前失敗；決定性斷言是三個固定 rollback 邊界各執行一次，沒有額外 mutation。
    /// </summary>
    [Fact]
    public void Restore_transfer_graph_cleans_the_zero_active_unlinked_present_record_branch()
    {
        using var service = new TransferGraphRecordingOrganizationService(
            WeeklyReportProjection.Zero,
            PresentRecordProjection.Matching,
            sourceMembershipPresent: false,
            targetMembershipPresent: true,
            primaryListId: TransferTargetListId,
            allowCleanup: true);
        using var store = new P72Data8ListManagementFixtureStore(service);
        var fixture = CreateTransferFixture(sourceListId: null, targetOwnerId: null);
        var baseline = new P72TransferGraphSnapshot(false, false, null, false, null, TransferBaselineOwnerId);
        var expected = new P72TransferGraphSnapshot(
            false,
            true,
            TransferPresentRecordId,
            true,
            TransferTargetListId,
            TransferBaselineOwnerId);

        store.RestoreTransferGraph(fixture, baseline, expected);

        service.DeleteCount.Should().Be(1);
        service.ContactUpdateCount.Should().Be(1);
        service.MembershipRemoveCount.Should().Be(1);
        service.UnexpectedMutationCount.Should().Be(0);
    }

    /// <summary>
    /// 建立固定 transfer fixture。identity 全為合成值且僅存在單一測試 scope；不包含 endpoint、credential、
    /// profile 或可跨測試重用的 CRM state。
    /// </summary>
    /// <param name="sourceListId">可選 source list；null 用於驗證沒有 source rollback 的 cleanup 分支。</param>
    /// <param name="targetOwnerId">可選 owner；null 用於隔離 present-record/list cleanup。</param>
    /// <returns>固定 UTC Sunday 的 bounded fixture。</returns>
    private static P72TransferFixture CreateTransferFixture()
        => CreateTransferFixture(TransferSourceListId, null);

    /// <summary>建立可指定 source/owner 分支的 transfer fixture，供 cleanup isolation 測試使用。</summary>
    private static P72TransferFixture CreateTransferFixture(Guid? sourceListId, Guid? targetOwnerId)
        => new(
            TransferContactId,
            sourceListId,
            TransferTargetListId,
            TransferWeekStartUtc,
            targetOwnerId);

    /// <summary>
    /// 保護 relationship-derived area leader 的遠端 task marker。relationship row 本身即使具有正確 list ID、
    /// race leader 與 area name，只要 area leader contact 不帶 <c>P7.2-SC-</c> marker，就不能把它當成
    /// Slice C fixture 的預期值。決定性 assertion 是 store 在唯讀 Retrieve 後拋出，沒有任何 CRUD、
    /// association 或跨測試保存的 CRM state。
    /// </summary>
    [Fact]
    public void Resolve_small_group_expected_rejects_a_relationship_area_leader_without_the_task_marker()
    {
        using var service = new RelationshipRecordingOrganizationService(
            ExpectedRelationshipListId,
            areaLeaderMarkerIsMissing: true);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var action = () => InvokeResolveSmallGroupExpected(
            store,
            TargetSmallGroupListId,
            SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
            TargetLeaderContactId,
            ExpectedRelationshipListId);

        action.Should().Throw<InvalidOperationException>();
        service.RetrieveMultipleCount.Should().Be(1);
        service.RetrieveCount.Should().Be(1);
        service.UnexpectedOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 保護正常 expected-state proof：store 必須接受 descriptor 指定的專用 relationship list ID，
    /// 並只對該 list 與目標 race leader 建立固定查詢。決定性 assertion 是回傳的 six-field
    /// projection 只使用該 relationship row，且沒有任何寫入或其他 CRM API。
    /// </summary>
    [Fact]
    public void Resolve_small_group_expected_uses_the_dedicated_relationship_list()
    {
        using var service = new RelationshipRecordingOrganizationService(ExpectedRelationshipListId);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = InvokeResolveSmallGroupExpected(
            store,
            TargetSmallGroupListId,
            SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
            TargetLeaderContactId,
            ExpectedRelationshipListId);

        result.Should().Be(new P72SmallGroupFixedFieldsSnapshot(
            AreaLeaderContactId,
            "task-owned-area",
            TargetLeaderContactId,
            null,
            null,
            null));
        service.RetrieveMultipleCount.Should().Be(1);
        service.RetrieveCount.Should().Be(1);
        service.UnexpectedOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 注入 missing dedicated relationship ID。fixture descriptor 無法指向一個非空、task-owned
    /// expected source 時，store 必須在取得 CRM service 或發出 QueryExpression 前停止，不能退回
    /// 到只依 leader 的廣泛 relationship 搜尋。
    /// </summary>
    [Fact]
    public void Resolve_small_group_expected_rejects_a_missing_dedicated_relationship_list_id_before_query()
    {
        using var service = new RelationshipRecordingOrganizationService(ExpectedRelationshipListId);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var action = () => InvokeResolveSmallGroupExpected(
            store,
            TargetSmallGroupListId,
            SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
            TargetLeaderContactId,
            Guid.Empty);

        action.Should().Throw<ArgumentException>();
        service.RetrieveMultipleCount.Should().Be(0);
        service.UnexpectedOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 注入回傳 identity 與 descriptor 不一致的 relationship row。即使欄位與 leader 看起來有效，
    /// store 也必須 fail closed，避免錯誤 list 的 area leader/name 成為 target small-group update
    /// 的 expected state 或後續 rollback 判定依據。
    /// </summary>
    [Fact]
    public void Resolve_small_group_expected_rejects_a_relationship_row_for_another_list()
    {
        using var service = new RelationshipRecordingOrganizationService(WrongRelationshipListId);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var action = () => InvokeResolveSmallGroupExpected(
            store,
            TargetSmallGroupListId,
            SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
            TargetLeaderContactId,
            ExpectedRelationshipListId);

        action.Should().Throw<InvalidOperationException>();
        service.RetrieveMultipleCount.Should().Be(1);
        service.UnexpectedOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 fixture repair 只會對 relationship list 送出兩個 allowlisted 欄位，並以 read-back
    /// 確認 deterministic expected state；任何其他欄位、通用 CRUD 或第二次 Update 都不允許。
    /// </summary>
    [Fact]
    public void Repair_expected_relationship_fields_updates_only_the_allowlisted_fields()
    {
        using var service = new RepairRecordingOrganizationService();
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.RepairTaskOwnedExpectedRelationshipFields(
            MembershipContactId,
            TargetSmallGroupListId,
            TargetLeaderContactId,
            ExpectedRelationshipListId);

        result.Should().Be(new P72SmallGroupFixtureRepairResult("go", string.Empty, true, true));
        service.UpdateCount.Should().Be(1);
        service.UpdatedAttributeNames.Should().BeEquivalentTo(
            "new_contact_list_arealeader",
            "new_area_name");
        service.UnexpectedOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 部分填寫的 relationship fixture 必須 fail closed，且在判斷出狀態不安全後完全不寫入 CRM。
    /// </summary>
    [Fact]
    public void Repair_expected_relationship_fields_rejects_partial_state_without_update()
    {
        using var service = new RepairRecordingOrganizationService(
            areaLeaderId: AreaLeaderContactId,
            areaName: null);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.RepairTaskOwnedExpectedRelationshipFields(
            MembershipContactId,
            TargetSmallGroupListId,
            TargetLeaderContactId,
            ExpectedRelationshipListId);

        result.Should().Be(new P72SmallGroupFixtureRepairResult(
            "no-go",
            "fixture-state-unexpected",
            false,
            false));
        service.UpdateCount.Should().Be(0);
        service.UnexpectedOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 repair probe 的唯讀契約：正常 task-owned graph 應逐項回報 true 與 blank 欄位狀態，
    /// 且不應送出任何 CRM mutation。這個測試的決定性斷言是 UpdateCount 與
    /// UnexpectedOperationCount 都維持零。
    /// </summary>
    [Fact]
    public void Probe_expected_relationship_repair_preconditions_is_read_only_and_bounded()
    {
        using var service = new RepairRecordingOrganizationService();
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.ProbeTaskOwnedExpectedRelationshipFields(
            MembershipContactId,
            TargetSmallGroupListId,
            TargetLeaderContactId,
            ExpectedRelationshipListId);

        result.Should().Be(new P72SmallGroupFixtureRepairProbe(
            SourceContactMarkerValid: true,
            SmallGroupListValid: true,
            ExpectedRelationshipListValid: true,
            TargetLeaderMarkerValid: true,
            ExpectedRelationshipRaceLeaderMatches: true,
            ExpectedRelationshipFieldsState: "blank",
            PreconditionState: "blank-repairable"));
        service.UpdateCount.Should().Be(0);
        service.UnexpectedOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 呼叫 descriptor-bound 四參數 contract。TDD 的 red phase 已確認舊 store 沒有這個 boundary；
    /// 現在以型別安全呼叫保護 query 的專用 relationship ID，不讓未來重構退回三參數、只依 leader
    /// 的廣泛查詢。
    /// </summary>
    private static P72SmallGroupFixedFieldsSnapshot InvokeResolveSmallGroupExpected(
        P72Data8ListManagementFixtureStore store,
        Guid targetSmallGroupListId,
        SmallGroupFixedFieldsUpdateMode mode,
        Guid targetLeaderContactId,
        Guid expectedRelationshipListId)
        => store.ResolveSmallGroupExpected(
            targetSmallGroupListId,
            mode,
            targetLeaderContactId,
            expectedRelationshipListId);

    /// <summary>
    /// repair lane 的 deterministic CRM double。它只暴露固定的 list/contact projection 與一次
    /// relationship-list Update，藉此讓測試能證明 provenance、欄位 allowlist 與 read-back 順序。
    /// </summary>
    private sealed class RepairRecordingOrganizationService : IOrganizationService, IDisposable
    {
        private readonly Entity _smallGroup;
        private readonly Entity _expectedRelationship;
        private readonly Entity _sourceContact;
        private readonly Entity _targetLeader;
        private bool _disposed;

        /// <summary>建立可選擇初始 area 欄位狀態的 repair double。</summary>
        internal RepairRecordingOrganizationService(Guid? areaLeaderId = null, string? areaName = null)
        {
            _smallGroup = CreateStaticList(TargetSmallGroupListId, TargetLeaderContactId);
            _expectedRelationship = CreateStaticList(ExpectedRelationshipListId, TargetLeaderContactId);
            _expectedRelationship["new_contact_list_arealeader"] = areaLeaderId is Guid id
                ? new EntityReference("contact", id)
                : null!;
            _expectedRelationship["new_area_name"] = areaName!;
            _sourceContact = new Entity("contact", MembershipContactId)
            {
                ["description"] = "p7.2-contact-basic-info"
            };
            _targetLeader = new Entity("contact", TargetLeaderContactId)
            {
                ["fullname"] = "P7.2-SC-LEADER"
            };
        }

        /// <summary>記錄唯一允許的 Update 次數。</summary>
        internal int UpdateCount { get; private set; }

        /// <summary>記錄 Update 實際接收的欄位名稱，避免 repair 擴張成 generic CRUD。</summary>
        internal IReadOnlyList<string> UpdatedAttributeNames { get; private set; } = Array.Empty<string>();

        /// <summary>記錄任何未列入 repair allowlist 的 CRM 呼叫。</summary>
        internal int UnexpectedOperationCount { get; private set; }

        /// <summary>只回傳固定的 list/contact projection；其餘查詢一律拒絕。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (entityName == "list" && id == TargetSmallGroupListId)
            {
                return _smallGroup;
            }

            if (entityName == "list" && id == ExpectedRelationshipListId)
            {
                return _expectedRelationship;
            }

            if (entityName == "contact" && id == MembershipContactId)
            {
                return _sourceContact;
            }

            if (entityName == "contact" && id == TargetLeaderContactId)
            {
                return _targetLeader;
            }

            throw Unexpected();
        }

        /// <summary>只接受 expected relationship list 的兩個 allowlisted 欄位。</summary>
        public void Update(Entity entity)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (entity.LogicalName != "list" || entity.Id != ExpectedRelationshipListId ||
                !entity.Attributes.Keys.OrderBy(static key => key, StringComparer.Ordinal)
                    .SequenceEqual(
                        ["new_area_name", "new_contact_list_arealeader"],
                        StringComparer.Ordinal))
            {
                throw Unexpected();
            }

            UpdateCount++;
            UpdatedAttributeNames = entity.Attributes.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();
            foreach (var pair in entity.Attributes)
            {
                _expectedRelationship[pair.Key] = pair.Value;
            }
        }

        /// <summary>repair lane 不允許 RetrieveMultiple，避免 caller query/discovery。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw Unexpected();

        /// <summary>repair lane 不允許建立 CRM row。</summary>
        public Guid Create(Entity entity) => throw Unexpected();

        /// <summary>repair lane 不允許刪除 CRM row。</summary>
        public void Delete(string entityName, Guid id) => throw Unexpected();

        /// <summary>repair lane 不允許 OrganizationRequest。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw Unexpected();

        /// <summary>repair lane 不允許 association。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw Unexpected();

        /// <summary>repair lane 不允許 disassociation。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw Unexpected();

        /// <summary>釋放 double；之後所有呼叫 fail closed。</summary>
        public void Dispose() => _disposed = true;

        private static Entity CreateStaticList(Guid listId, Guid raceLeaderId)
            => new("list", listId)
            {
                ["listname"] = "P7.2-SC-REPAIR-LIST",
                ["type"] = false,
                ["createdfromcode"] = new OptionSetValue(2),
                ["new_contact_race_leager_list"] = new EntityReference("contact", raceLeaderId),
                ["new_contact_list_arealeader"] = null!,
                ["new_area_name"] = null!,
                ["new_contact_list_co_arealeader"] = null!,
                ["new_contact_co_race_leager_list"] = null!,
                ["new_contact_list_vice_family_leader"] = null!
            };

        private InvalidOperationException Unexpected()
        {
            UnexpectedOperationCount++;
            return new InvalidOperationException("The repair fixture double received an unapproved CRM operation.");
        }
    }

    /// <summary>
    /// 最小且封閉的 CRM service 替身。它只允許 expected relationship 的讀取，並以 assertions
    /// 驗證 list ID 與 race leader filter 同時存在；所有未授權 CRUD/association path 都計數後失敗，
    /// 因此測試可以判定 fixture store 沒有擴張其讀取權限。
    /// </summary>
    private sealed class RelationshipRecordingOrganizationService : IOrganizationService, IDisposable
    {
        private readonly Guid _returnedRelationshipListId;
        private readonly bool _areaLeaderMarkerIsMissing;
        private bool _disposed;

        /// <summary>
        /// 建立回傳指定 relationship list ID 的離線替身。傳入錯誤 ID 可模擬 CRM 或 adapter 將其他
        /// list row 混入結果的故障；<paramref name="areaLeaderMarkerIsMissing"/> 則模擬 relationship row
        /// 指向未標記 contact。兩者都只能影響固定 read projection，不能建立可跨測試存活的 CRM state。
        /// </summary>
        /// <param name="returnedRelationshipListId">RetrieveMultiple 回傳 row 的 synthetic list ID。</param>
        /// <param name="areaLeaderMarkerIsMissing">是否將 area leader 的 fullname 改為非 task marker 值。</param>
        internal RelationshipRecordingOrganizationService(
            Guid returnedRelationshipListId,
            bool areaLeaderMarkerIsMissing = false)
        {
            _returnedRelationshipListId = returnedRelationshipListId;
            _areaLeaderMarkerIsMissing = areaLeaderMarkerIsMissing;
        }

        /// <summary>固定 relationship query 的呼叫次數；每個 proof 只能讀取一次。</summary>
        internal int RetrieveMultipleCount { get; private set; }

        /// <summary>固定 area leader 直接投影的讀取次數；每個 relationship proof 只能讀取一次。</summary>
        internal int RetrieveCount { get; private set; }

        /// <summary>未核准 CRM API 的嘗試次數；測試必須保持零。</summary>
        internal int UnexpectedOperationCount { get; private set; }

        /// <summary>
        /// 只接受 list 的固定 expected-relationship query。查詢必須有 TopCount=2、固定兩欄投影、
        /// descriptor 指定的 listid equality 與 target leader equality；這些條件共同阻止任意 list
        /// discovery 或跨 fixture 資料讀取。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RetrieveMultipleCount++;
            var expression = query.Should().BeOfType<QueryExpression>().Subject;
            expression.EntityName.Should().Be("list");
            expression.TopCount.Should().Be(2);
            expression.ColumnSet.Columns.Should().Equal(
                "new_contact_list_arealeader",
                "new_area_name");
            var listCondition = expression.Criteria.Conditions.Single(condition =>
                condition.AttributeName == "listid" && condition.Operator == ConditionOperator.Equal);
            listCondition.Values.Should().ContainSingle().Which.Should().Be(ExpectedRelationshipListId);
            var leaderCondition = expression.Criteria.Conditions.Single(condition =>
                condition.AttributeName == "new_contact_race_leager_list" && condition.Operator == ConditionOperator.Equal);
            leaderCondition.Values.Should().ContainSingle().Which.Should().Be(TargetLeaderContactId);

            return new EntityCollection(
            [
                new Entity("list", _returnedRelationshipListId)
                {
                    ["new_contact_list_arealeader"] = new EntityReference("contact", AreaLeaderContactId),
                    ["new_area_name"] = "task-owned-area"
                }
            ]);
        }

        /// <summary>此 fixture-store test 不允許建立 CRM entity；呼叫代表 boundary 被擴張。</summary>
        public Guid Create(Entity entity) => throw Unexpected();

        /// <summary>此 fixture-store test 不允許 update CRM entity；呼叫代表 boundary 被擴張。</summary>
        public void Update(Entity entity) => throw Unexpected();

        /// <summary>此 fixture-store test 不允許 delete CRM entity；呼叫代表 boundary 被擴張。</summary>
        public void Delete(string entityName, Guid id) => throw Unexpected();

        /// <summary>此 fixture-store test 不允許通用 OrganizationRequest；呼叫代表 boundary 被擴張。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw Unexpected();

        /// <summary>
        /// 只允許 relationship-derived area leader 的固定 fullname projection。這個 direct Retrieve 重新證明
        /// row 中的 lookup 不是任意 contact；除固定 ID/欄位外的一切讀取皆視為 boundary 擴張並 fail closed。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RetrieveCount++;
            if (entityName != "contact" || id != AreaLeaderContactId)
            {
                throw Unexpected();
            }

            columnSet.Columns.Should().Equal("fullname");
            return new Entity("contact", AreaLeaderContactId)
            {
                ["fullname"] = _areaLeaderMarkerIsMissing ? "unmarked-area-leader" : "P7.2-SC-AREA-LEADER"
            };
        }

        /// <summary>此 proof 不允許 association；呼叫代表 fixture graph 邊界被擴張。</summary>
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities)
            => throw Unexpected();

        /// <summary>此 proof 不允許 disassociation；呼叫代表 fixture graph 邊界被擴張。</summary>
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities)
            => throw Unexpected();

        /// <summary>
        /// 釋放單一測試替身並拒絕後續使用。替身不含外部 handle，但仍模擬 live store 的唯一
        /// resource owner，避免測試意外在 Dispose 後重用同一個可變 state。
        /// </summary>
        public void Dispose() => _disposed = true;

        /// <summary>記錄並建立固定例外，避免 assertion 或錯誤訊息攜帶任何 fixture identity。</summary>
        private InvalidOperationException Unexpected()
        {
            UnexpectedOperationCount++;
            return new InvalidOperationException("The fixture store called an unapproved CRM operation.");
        }
    }

    /// <summary>
    /// transfer weekly-report 查詢的受界限故障模式。每個值只控制一個 <c>TopCount=2</c> 回應，
    /// 不會建立跨測試共享資料或啟動外部 CRM I/O。
    /// </summary>
    public enum WeeklyReportProjection
    {
        /// <summary>完整零列，代表正常的 <c>zero-active</c> 分支。</summary>
        Zero,

        /// <summary>剛好一筆合法、非空 identity 的 active weekly report。</summary>
        ExactlyOne,

        /// <summary>同一 exact list/date 交集回傳兩筆，代表資料歧義。</summary>
        Duplicate,

        /// <summary>回應宣告還有下一頁，代表 bounded projection 不完整。</summary>
        Paging,

        /// <summary>唯一 row 的 logical name 不符，代表 SDK projection 無法被信任。</summary>
        Malformed
    }

    /// <summary>
    /// present-record 讀回的受界限故障模式。synthetic SDK row 只活在單次 RetrieveMultiple 呼叫，
    /// 不會被快取、寫入 evidence 或跨測試留存。
    /// </summary>
    public enum PresentRecordProjection
    {
        /// <summary>沒有 matching record，代表合法 baseline。</summary>
        Absent,

        /// <summary>所有 contact/list/date 與 optional weekly lookup 精確相符。</summary>
        Matching,

        /// <summary>record 關聯到另一筆週報，用於證明 zero-active 不會漏讀衝突資料。</summary>
        WrongWeeklyLookup
    }

    /// <summary>
    /// transfer graph 專用的封閉 OrganizationService 替身。它只允許兩個固定 listmember query、
    /// exact target-list/date weekly-report query、同 contact/date present-record query，以及 contact 的
    /// primary-list/owner direct Retrieve。一般讀取測試的所有 mutation 都會同步失敗；只有明確啟用
    /// cleanup 的案例可依序 Delete present record、Update primary-list、Remove target membership。
    /// 替身不建立 timer、背景 task、stream、credential、session 或 static state，並由單一 using scope
    /// 確定釋放，避免合成 identity 或可變 graph 跨測試留存。
    /// </summary>
    private sealed class TransferGraphRecordingOrganizationService : IOrganizationService, IDisposable
    {
        private readonly WeeklyReportProjection _weeklyReportProjection;
        private readonly PresentRecordProjection _presentRecordProjection;
        private readonly bool _sourceMembershipPresent;
        private readonly bool _allowCleanup;
        private bool _targetMembershipPresent;
        private bool _presentRecordExists;
        private Guid? _primaryListId;
        private bool _disposed;

        /// <summary>
        /// 建立單一 transfer graph 投影。所有輸入都由測試常數提供且有固定上限；<paramref name="allowCleanup"/>
        /// 只開放 store 已證明 expected record 後的三個 rollback 動作，不會成為通用 CRUD 開關。
        /// </summary>
        /// <param name="weeklyReportProjection">零、唯一、重複或 paging weekly-report 回應。</param>
        /// <param name="presentRecordProjection">缺席、精確相符或錯誤 weekly lookup 回應。</param>
        /// <param name="sourceMembershipPresent">source list 是否含 synthetic contact。</param>
        /// <param name="targetMembershipPresent">target list 是否含 synthetic contact。</param>
        /// <param name="primaryListId">contact 的 optional primary-list baseline。</param>
        /// <param name="allowCleanup">是否只允許預期的 exact-ID rollback sequence。</param>
        internal TransferGraphRecordingOrganizationService(
            WeeklyReportProjection weeklyReportProjection,
            PresentRecordProjection presentRecordProjection,
            bool sourceMembershipPresent,
            bool targetMembershipPresent,
            Guid? primaryListId,
            bool allowCleanup = false)
        {
            _weeklyReportProjection = weeklyReportProjection;
            _presentRecordProjection = presentRecordProjection;
            _sourceMembershipPresent = sourceMembershipPresent;
            _targetMembershipPresent = targetMembershipPresent;
            _primaryListId = primaryListId;
            _allowCleanup = allowCleanup;
            _presentRecordExists = presentRecordProjection != PresentRecordProjection.Absent;
        }

        /// <summary>weekly-report exact query 的呼叫次數；每次 graph read 只能為一。</summary>
        internal int WeeklyReportReadCount { get; private set; }

        /// <summary>present-record bounded query 的呼叫次數。</summary>
        internal int PresentRecordReadCount { get; private set; }

        /// <summary>contact primary-list/owner direct Retrieve 的總次數。</summary>
        internal int ContactRetrieveCount { get; private set; }

        /// <summary>已證明 record 的 exact-ID Delete 次數。</summary>
        internal int DeleteCount { get; private set; }

        /// <summary>contact primary-list rollback 的固定 Update 次數。</summary>
        internal int ContactUpdateCount { get; private set; }

        /// <summary>target membership rollback 的固定 RemoveMember 次數。</summary>
        internal int MembershipRemoveCount { get; private set; }

        /// <summary>任何不在 cleanup allowlist 的 mutation 嘗試次數；所有測試必須維持零。</summary>
        internal int UnexpectedMutationCount { get; private set; }

        /// <summary>
        /// 路由三種固定 QueryExpression。每條 query 都重驗 entity、欄位、TopCount 與 exact filter；
        /// 未知 query 立即失敗，避免 test double 不小心允許 generic discovery。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var expression = query.Should().BeOfType<QueryExpression>().Subject;
            return expression.EntityName switch
            {
                "listmember" => ReadMembership(expression),
                "new_group_present_weekly_report" => ReadWeeklyReport(expression),
                "new_present_record" => ReadPresentRecord(expression),
                _ => throw UnexpectedRead()
            };
        }

        /// <summary>
        /// 只允許 contact 的 primary-list 或 owner projection。兩者分開驗證 ColumnSet，避免 read-back
        /// 誤接受多餘 CRM 欄位或把 team owner 混入 systemuser cleanup contract。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ContactRetrieveCount++;
            if (entityName != "contact" || id != TransferContactId)
            {
                throw UnexpectedRead();
            }

            if (columnSet.Columns.SequenceEqual(["new_cell_list_contact"], StringComparer.Ordinal))
            {
                var contact = new Entity("contact", TransferContactId);
                if (_primaryListId is Guid primaryListId)
                {
                    contact["new_cell_list_contact"] = new EntityReference("list", primaryListId);
                }

                return contact;
            }

            if (columnSet.Columns.SequenceEqual(["ownerid"], StringComparer.Ordinal))
            {
                return new Entity("contact", TransferContactId)
                {
                    ["ownerid"] = new EntityReference("systemuser", TransferBaselineOwnerId)
                };
            }

            throw UnexpectedRead();
        }

        /// <summary>
        /// cleanup 案例只允許刪除已重新讀回證明的 synthetic present record；其他 logical name、ID、
        /// 重複刪除或一般讀取案例的 Delete 都是未核准 mutation。
        /// </summary>
        public void Delete(string entityName, Guid id)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_allowCleanup || !_presentRecordExists ||
                entityName != "new_present_record" || id != TransferPresentRecordId)
            {
                throw UnexpectedMutation();
            }

            DeleteCount++;
            _presentRecordExists = false;
        }

        /// <summary>
        /// cleanup 案例只允許 contact 的單一 primary-list 欄位 rollback。值可為 null，代表回復無 primary
        /// list baseline；其他 entity、欄位或多次 Update 都立即失敗。
        /// </summary>
        public void Update(Entity entity)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_allowCleanup || entity.LogicalName != "contact" || entity.Id != TransferContactId ||
                entity.Attributes.Count != 1 ||
                !entity.Attributes.TryGetValue("new_cell_list_contact", out var primaryListValue) ||
                primaryListValue is not null and not EntityReference)
            {
                throw UnexpectedMutation();
            }

            if (primaryListValue is EntityReference reference &&
                (reference.Id == Guid.Empty || !string.Equals(reference.LogicalName, "list", StringComparison.Ordinal)))
            {
                throw UnexpectedMutation();
            }

            ContactUpdateCount++;
            _primaryListId = (primaryListValue as EntityReference)?.Id;
        }

        /// <summary>
        /// cleanup 案例只允許一次 target-list <see cref="RemoveMemberListRequest"/>，並立刻更新替身的
        /// request-local membership state，讓後續測試無須建立背景 reconciliation 或共享 cache。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_allowCleanup || !_targetMembershipPresent ||
                request is not RemoveMemberListRequest remove ||
                remove.ListId != TransferTargetListId || remove.EntityId != TransferContactId)
            {
                throw UnexpectedMutation();
            }

            MembershipRemoveCount++;
            _targetMembershipPresent = false;
            return new OrganizationResponse();
        }

        /// <summary>transfer store 不允許建立任何 CRM row；present record 只由 product connector 建立。</summary>
        public Guid Create(Entity entity) => throw UnexpectedMutation();

        /// <summary>transfer store 不允許 generic association。</summary>
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities)
            => throw UnexpectedMutation();

        /// <summary>transfer store 不允許 generic disassociation。</summary>
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities)
            => throw UnexpectedMutation();

        /// <summary>結束唯一測試 scope；之後所有讀寫都 fail closed。</summary>
        public void Dispose() => _disposed = true;

        /// <summary>
        /// 驗證固定 listmember query 並投影 source/target 的目前狀態。lookup 使用既有離線 fake 支援的
        /// raw GUID 形狀；任何第三個 list 或未預期 filter 都不是本 transfer graph 的一部分。
        /// </summary>
        private EntityCollection ReadMembership(QueryExpression query)
        {
            query.ColumnSet.Columns.Should().Equal("listid", "entityid");
            query.TopCount.Should().Be(500);
            var listId = RequireEqualGuidCondition(query, "listid");
            RequireInGuidCondition(query, "entityid", TransferContactId);
            var isPresent = listId == TransferSourceListId
                ? _sourceMembershipPresent
                : listId == TransferTargetListId
                    ? _targetMembershipPresent
                    : throw UnexpectedRead();
            return isPresent
                ? new EntityCollection(
                [
                    new Entity("listmember")
                    {
                        ["listid"] = listId,
                        ["entityid"] = TransferContactId
                    }
                ])
                : new EntityCollection();
        }

        /// <summary>
        /// 驗證 exact target-list／active／UTC-Sunday／TopCount=2 weekly-report query，然後回傳測試指定的
        /// 零、唯一、重複或 paging 投影。此方法不計數或輸出真實資料規模。
        /// </summary>
        private EntityCollection ReadWeeklyReport(QueryExpression query)
        {
            WeeklyReportReadCount++;
            query.ColumnSet.Columns.Should().Equal("new_group_present_weekly_reportid");
            query.TopCount.Should().Be(2);
            RequireEqualGuidCondition(query, "new_list_group_present_weekly_report").Should().Be(TransferTargetListId);
            RequireEqualValueCondition(query, "statecode").Should().Be(0);
            RequireEqualValueCondition(query, "new_sunday_date").Should().Be(TransferWeekStartUtc.UtcDateTime);

            if (_weeklyReportProjection == WeeklyReportProjection.Zero)
            {
                return new EntityCollection();
            }

            if (_weeklyReportProjection == WeeklyReportProjection.Paging)
            {
                return new EntityCollection { MoreRecords = true };
            }

            if (_weeklyReportProjection == WeeklyReportProjection.Malformed)
            {
                return new EntityCollection([new Entity("contact", TransferWeeklyReportId)]);
            }

            var rows = new EntityCollection(
            [
                new Entity("new_group_present_weekly_report", TransferWeeklyReportId)
            ]);
            if (_weeklyReportProjection == WeeklyReportProjection.Duplicate)
            {
                rows.Entities.Add(new Entity(
                    "new_group_present_weekly_report",
                    WrongTransferWeeklyReportId));
            }

            return rows;
        }

        /// <summary>
        /// 驗證 present-record query 的 optional weekly filter。唯一週報必須精確帶入 lookup；zero-active
        /// 必須完全不帶該 filter，確保錯誤關聯的既有 record 不會被漏掉。回傳 row 仍須包含固定
        /// contact/list/date projection，且最多一筆。
        /// </summary>
        private EntityCollection ReadPresentRecord(QueryExpression query)
        {
            PresentRecordReadCount++;
            query.ColumnSet.Columns.Should().Equal(
                "new_group_present_weekly_report_prese",
                "new_contact_new_present_record",
                "new_list_new_present_record",
                "new_sunday_date");
            query.TopCount.Should().Be(2);
            RequireEqualGuidCondition(query, "new_contact_new_present_record").Should().Be(TransferContactId);
            RequireEqualValueCondition(query, "new_sunday_date").Should().Be(TransferWeekStartUtc.UtcDateTime);
            RequireEqualValueCondition(query, "statecode").Should().Be(0);

            var weeklyConditions = query.Criteria.Conditions
                .Where(static condition => condition.AttributeName == "new_group_present_weekly_report_prese")
                .ToArray();
            if (_weeklyReportProjection == WeeklyReportProjection.ExactlyOne)
            {
                weeklyConditions.Should().ContainSingle();
                weeklyConditions[0].Operator.Should().Be(ConditionOperator.Equal);
                weeklyConditions[0].Values.Should().ContainSingle().Which.Should().Be(TransferWeeklyReportId);
            }
            else
            {
                weeklyConditions.Should().BeEmpty();
            }

            if (!_presentRecordExists)
            {
                return new EntityCollection();
            }

            var row = new Entity("new_present_record", TransferPresentRecordId)
            {
                ["new_contact_new_present_record"] = new EntityReference("contact", TransferContactId),
                ["new_list_new_present_record"] = new EntityReference("list", TransferTargetListId),
                ["new_sunday_date"] = TransferWeekStartUtc.UtcDateTime
            };
            if (_presentRecordProjection == PresentRecordProjection.Matching &&
                _weeklyReportProjection == WeeklyReportProjection.ExactlyOne)
            {
                row["new_group_present_weekly_report_prese"] = new EntityReference(
                    "new_group_present_weekly_report",
                    TransferWeeklyReportId);
            }
            else if (_presentRecordProjection == PresentRecordProjection.WrongWeeklyLookup)
            {
                row["new_group_present_weekly_report_prese"] = new EntityReference(
                    "new_group_present_weekly_report",
                    WrongTransferWeeklyReportId);
            }

            return new EntityCollection([row]);
        }

        /// <summary>取得固定 equality GUID filter；缺失、多值、錯 operator 或錯型別都使替身 fail closed。</summary>
        private static Guid RequireEqualGuidCondition(QueryExpression query, string attributeName)
            => RequireEqualValueCondition(query, attributeName) is Guid value && value != Guid.Empty
                ? value
                : throw new InvalidOperationException("The transfer fake received an invalid GUID filter.");

        /// <summary>取得唯一固定 equality filter 的單一值，不接受隱含轉型或額外條件值。</summary>
        private static object RequireEqualValueCondition(QueryExpression query, string attributeName)
        {
            var condition = query.Criteria.Conditions.Single(item =>
                item.AttributeName == attributeName && item.Operator == ConditionOperator.Equal);
            return condition.Values.Should().ContainSingle().Which;
        }

        /// <summary>驗證 membership 的固定單值 IN filter。</summary>
        private static void RequireInGuidCondition(QueryExpression query, string attributeName, Guid expected)
        {
            var condition = query.Criteria.Conditions.Single(item =>
                item.AttributeName == attributeName && item.Operator == ConditionOperator.In);
            condition.Values.Should().ContainSingle().Which.Should().Be(expected);
        }

        /// <summary>建立固定讀取錯誤，不將 synthetic identity 或 query 內容放入例外。</summary>
        private static InvalidOperationException UnexpectedRead()
            => new("The transfer fixture double received an unapproved read operation.");

        /// <summary>記錄未核准 mutation 並建立固定錯誤，不回顯 Entity、request 或 identity。</summary>
        private InvalidOperationException UnexpectedMutation()
        {
            UnexpectedMutationCount++;
            return new InvalidOperationException("The transfer fixture double received an unapproved mutation.");
        }
    }

    /// <summary>
    /// 封閉的 membership projection 替身。它在單一同步 RetrieveMultiple scope 內回傳 raw GUID 或
    /// EntityReference，藉此測試 store 的明確型別 allowlist；不保留 SDK row、connection 或任何
    /// fixture state 到 test scope 之外。
    /// </summary>
    private sealed class MembershipProjectionRecordingService : IOrganizationService, IDisposable
    {
        private readonly bool _useEntityReferences;
        private bool _disposed;

        /// <summary>
        /// 建立單一可選 lookup shape 的替身。shape 只影響 listmember 兩個 lookup 欄位，不改變
        /// query filter 或 identity，確保 assertion 真正保護投影 compatibility 而不是不同資料集。
        /// </summary>
        /// <param name="useEntityReferences">是否以真機常見的 EntityReference 投影兩個 lookup。</param>
        internal MembershipProjectionRecordingService(bool useEntityReferences)
            => _useEntityReferences = useEntityReferences;

        /// <summary>固定 listmember query 的呼叫次數。</summary>
        internal int RetrieveMultipleCount { get; private set; }

        /// <summary>未核准 CRM API 的呼叫次數；測試必須保持零。</summary>
        internal int UnexpectedOperationCount { get; private set; }

        /// <summary>
        /// 只接受 store-owned listmember query，並回傳同一 identity 的 raw GUID 或 EntityReference。
        /// ColumnSet、list filter 與 member IN filter 都必須固定，避免此 regression test 成為任意
        /// CRM query 的寬鬆替身。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RetrieveMultipleCount++;
            var expression = query.Should().BeOfType<QueryExpression>().Subject;
            expression.EntityName.Should().Be("listmember");
            expression.ColumnSet.Columns.Should().Equal("listid", "entityid");
            var listCondition = expression.Criteria.Conditions.Single(condition =>
                condition.AttributeName == "listid" && condition.Operator == ConditionOperator.Equal);
            listCondition.Values.Should().ContainSingle().Which.Should().Be(MembershipListId);
            var memberCondition = expression.Criteria.Conditions.Single(condition =>
                condition.AttributeName == "entityid" && condition.Operator == ConditionOperator.In);
            memberCondition.Values.Should().ContainSingle().Which.Should().Be(MembershipContactId);

            object listLookup = _useEntityReferences
                ? new EntityReference("list", MembershipListId)
                : MembershipListId;
            object contactLookup = _useEntityReferences
                ? new EntityReference("contact", MembershipContactId)
                : MembershipContactId;
            return new EntityCollection(
            [
                new Entity("listmember")
                {
                    ["listid"] = listLookup,
                    ["entityid"] = contactLookup
                }
            ]);
        }

        /// <summary>此 regression test 不允許建立 CRM entity；呼叫代表讀取邊界被擴張。</summary>
        public Guid Create(Entity entity) => throw Unexpected();

        /// <summary>此 regression test 不允許 update CRM entity；呼叫代表讀取邊界被擴張。</summary>
        public void Update(Entity entity) => throw Unexpected();

        /// <summary>此 regression test 不允許 delete CRM entity；呼叫代表讀取邊界被擴張。</summary>
        public void Delete(string entityName, Guid id) => throw Unexpected();

        /// <summary>此 regression test 不允許通用 OrganizationRequest；呼叫代表讀取邊界被擴張。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw Unexpected();

        /// <summary>此 regression test 不需要 direct Retrieve；呼叫代表 fixed listmember query 被繞過。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw Unexpected();

        /// <summary>此 regression test 不允許 association；呼叫代表讀取邊界被擴張。</summary>
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities)
            => throw Unexpected();

        /// <summary>此 regression test 不允許 disassociation；呼叫代表讀取邊界被擴張。</summary>
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities)
            => throw Unexpected();

        /// <summary>結束唯一 test scope；後續 CRM 呼叫一律被拒絕。</summary>
        public void Dispose() => _disposed = true;

        /// <summary>記錄未核准 API 並產生固定例外，不暴露 synthetic identity。</summary>
        private InvalidOperationException Unexpected()
        {
            UnexpectedOperationCount++;
            return new InvalidOperationException("The fixture store called an unapproved CRM operation.");
        }
    }
}
