// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ListManagementFreshFixtureProvisionerTests.cs
// 用途：以完全離線的假 IOrganizationService 鎖定 P7.2 Slice C 新建 fixture 控制面，確保
//       不會把既有未知 CRM 資料當成測試資料，也不會在 owner baseline 無法隔離時送出寫入。
//
// 核心隔離與生命週期契約：
// 1. 每個測試都建立自己的服務替身、request、ledger 與 CRM Entity；沒有 static 可變狀態、
//    共用快取、背景工作、token、credential 或跨測試保留的 CRM session。
// 2. 替身會記錄所有 Create/Update/Delete/Execute/Associate/Disassociate 呼叫。測試的決定性
//    assertion 是 baseline owner 等於 Data8 WhoAmI user 時，所有 mutation 計數必須保持零。
// 3. 即使未來實作於 preflight 後改變讀取次序，假服務仍只允許固定 ID、固定投影與一個受界限
//    的 weekly-report 回應，避免測試不小心接受全組織探索或 caller-provided CRM query。
// ============================================================================

using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 Slice C 新建 fixture provisioner 的最小 fail-closed 前置條件。
/// 此測試只測試 test-only 控制面，不會載入 Data8 runtime、Credential Manager、瀏覽器 session
/// 或任何遠端 CE。它保護的合約是：新 source contact 的 baseline owner 必須是已由 task-marked
/// leader 精確導出的 active system user，且該 user 不得與本次 Data8 WhoAmI service user 相同；
/// 否則後續 owner-assignment evidence 沒有可觀察的變化，任何 remote mutation 都必須被拒絕。
/// </summary>
public sealed class P72Data8ListManagementFreshFixtureProvisionerTests
{
    private static readonly Guid AddListId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RemoveListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SmallGroupListId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ExistingLeaderContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TransferSourceListId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TransferTargetListId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Data8ServiceUserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid BaselineOwnerId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid FreshSourceContactId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid FreshLeaderContactId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid FreshRelationshipListId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
    private static readonly Guid WeeklyReportId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");

    /// <summary>
    /// 定義 offline service 可注入 timeout-after-dispatch 的固定 mutation boundary。它只存在於單元
    /// 測試，將每種遠端寫入的不確定性明確化；production provisioner 不接受此 enum 或任何 caller
    /// supplied fault selector，因此不可能把測試控制面暴露為產品 API。
    /// </summary>
    public enum FreshFixtureMutationFault
    {
        /// <summary>不注入 fault，讓完整 success graph 可被驗證。</summary>
        None,

        /// <summary>source contact Create 已開始但未回傳可確認 ID。</summary>
        SourceCreate,

        /// <summary>leader contact Create 已開始但未回傳可確認 ID。</summary>
        LeaderCreate,

        /// <summary>expected-relationship list Create 已開始但未回傳可確認 ID。</summary>
        RelationshipListCreate,

        /// <summary>remove-list AddListMembers request 已開始但 read-back 不可假設。</summary>
        RemoveMembership,

        /// <summary>transfer-source AddListMembers request 已開始但 read-back 不可假設。</summary>
        TransferSourceMembership,

        /// <summary>baseline owner Assign request 已開始但 read-back 不可假設。</summary>
        BaselineOwnerAssign,

        /// <summary>
        /// 已送出 transfer-source 成員移除要求後發生傳輸不確定性。清理擁有者必須保留
        /// ledger，且不得繼續送出下一個遠端變更，避免把不完整圖誤判為已清理。
        /// </summary>
        CleanupTransferSourceMembership,

        /// <summary>
        /// 已送出 remove-list 成員移除要求後發生不確定性；relationship list 與兩個
        /// fresh contact 必須繼續由 ledger 擁有，直到獨立的精確 ID 對帳完成。
        /// </summary>
        CleanupRemoveMembership,

        /// <summary>
        /// fresh relationship list 的刪除要求開始後結果不確定，後續 source 與 leader
        /// 不得被刪除，以維持可對帳的反向清理邊界。
        /// </summary>
        CleanupRelationshipListDelete,

        /// <summary>
        /// fresh source contact 的刪除要求開始後結果不確定，fresh leader 仍必須保留
        /// 由同一個 ledger 擁有，不可由本次流程猜測成功或重試。
        /// </summary>
        CleanupSourceContactDelete,

        /// <summary>
        /// fresh leader contact 的刪除要求開始後結果不確定；父控制面必須保留 ledger
        /// 並回報 no-go，不能因為這是最後一步就宣告成功。
        /// </summary>
        CleanupLeaderContactDelete
    }

    /// <summary>
    /// 當既有 task-marked leader 的 owner 與本次 Data8 WhoAmI service user 相同時，驗證
    /// provisioner 會在任何 Create、association、Assign、Update 或 Delete 前回傳固定 no-go。
    /// 故障注入是將唯一可證明的 baseline-owner candidate 設為 service user；決定性 assertion
    /// 是所有 CRM mutation 與 pending ledger 都保持空白，避免將無法產生 owner rollback
    /// evidence 的 source contact 建立到遠端。
    /// </summary>
    [Fact]
    public void Provision_rejects_service_user_as_the_only_baseline_owner_before_any_mutation()
    {
        using var service = new FreshFixtureProvisionPreconditionService(Data8ServiceUserId);
        var ledger = new RecordingFreshFixtureLedger();
        var request = new P72FreshSliceCFixtureProvisionRequest(
            AddListId,
            RemoveListId,
            SmallGroupListId,
            ExistingLeaderContactId,
            TransferSourceListId,
            TransferTargetListId,
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Data8ServiceUserId,
            Guid.Parse("88888888-8888-8888-8888-888888888888"));
        var provisioner = new P72FreshSliceCFixtureProvisioner(service);

        var result = provisioner.Provision(request, ledger);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("baseline-owner-unavailable");
        result.OperationExecuted.Should().BeFalse();
        service.MutationAttemptCount.Should().Be(0);
        ledger.States.Should().BeEmpty();
    }

    /// <summary>
    /// 驗證 fresh source contact 的建立模板只能包含固定的 task marker 與 required source marker。
    /// 此測試刻意不執行 CRM Create：它先鎖定未來寫入所能送出的最小 Entity shape，避免呼叫端將
    /// 任意欄位、owner、endpoint 或既有 CRM ID 注入 control plane。決定性 assertion 是 logical
    /// name 固定為 contact，且 attributes 恰好只有 nonce-derived lastname 與 source description。
    /// </summary>
    [Fact]
    public void Create_source_contact_template_uses_only_the_fixed_task_marker_and_source_marker()
    {
        var nonce = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var entity = P72FreshSliceCFixtureRequestTemplates.CreateSourceContact(nonce);

        entity.LogicalName.Should().Be("contact");
        entity.Id.Should().Be(Guid.Empty);
        entity.Attributes.Keys.Should().BeEquivalentTo(["lastname", "description"]);
        entity.GetAttributeValue<string>("lastname").Should().Be("P7.2-SC-SOURCE-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        entity.GetAttributeValue<string>("description").Should().Be("p7.2-contact-basic-info");
    }

    /// <summary>
    /// 驗證 fresh leader contact 的建立模板只建立可由 CRM fullname 投影驗證的 task marker。此測試
    /// 防止來源 contact、既有 leader、或 caller-provided 名稱被拿來充當 relationship list 的 leader；
    /// 新 leader 必須在本次 nonce scope 內生成，後續才能精確 read-back、寫入 ledger 並在 cleanup 時
    /// 依 exact ID 刪除。決定性 assertion 是 contact 沒有任何 owner、relationship 或任意欄位。
    /// </summary>
    [Fact]
    public void Create_leader_contact_template_uses_only_the_fixed_task_marker()
    {
        var nonce = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var entity = P72FreshSliceCFixtureRequestTemplates.CreateLeaderContact(nonce);

        entity.LogicalName.Should().Be("contact");
        entity.Id.Should().Be(Guid.Empty);
        entity.Attributes.Keys.Should().BeEquivalentTo(["lastname"]);
        entity.GetAttributeValue<string>("lastname").Should().Be("P7.2-SC-LEADER-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    }

    /// <summary>
    /// 驗證 fresh expected-relationship list 的模板完全封閉：它只能建立 static contact list，並將
    /// area leader 與 race leader 同時指向本次 fresh leader，area name 也必須是既有 bridge 所需的
    /// deterministic 值。此離線測試防止重用 stale list、只填半組欄位，或讓 caller 注入任意 CRM
    /// attribute；後續 Create 成功後仍必須以 exact-ID read-back 重做相同 proof。
    /// </summary>
    [Fact]
    public void Create_relationship_list_template_uses_only_the_fixed_static_contact_list_projection()
    {
        var nonce = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var leaderId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var entity = P72FreshSliceCFixtureRequestTemplates.CreateRelationshipList(nonce, leaderId);

        entity.LogicalName.Should().Be("list");
        entity.Id.Should().Be(Guid.Empty);
        entity.Attributes.Keys.Should().BeEquivalentTo(
        [
            "listname",
            "type",
            "createdfromcode",
            "new_contact_list_arealeader",
            "new_area_name",
            "new_contact_race_leager_list"
        ]);
        entity.GetAttributeValue<string>("listname").Should().Be("P7.2-SC-REL-cccccccccccccccccccccccccccccccc");
        entity.GetAttributeValue<bool>("type").Should().BeFalse();
        entity.GetAttributeValue<OptionSetValue>("createdfromcode").Value.Should().Be(2);
        entity.GetAttributeValue<EntityReference>("new_contact_list_arealeader").Should().Be(new EntityReference("contact", leaderId));
        entity.GetAttributeValue<string>("new_area_name").Should().Be("P7.2-SC-AREA-EXPECTED");
        entity.GetAttributeValue<EntityReference>("new_contact_race_leager_list").Should().Be(new EntityReference("contact", leaderId));
    }

    /// <summary>
    /// 驗證所有 remote preflight 都成立時，provisioner 僅以已測試的固定 allowlist 建立新的 source、
    /// leader 與 relationship list，並在每個寫入後讀回、持久化 pending ledger，再執行剛好兩次
    /// membership 與一次 baseline-owner Assign。此測試使用離線 service，不會觸碰 CE；它的故障模型
    /// 是任何額外或順序錯誤的 CRM 操作都立即拋出。決定性 assertion 同時涵蓋 final graph、零 generic
    /// Update/Delete/Associate/Disassociate 與每一個可清理 stage 的 ledger snapshot。
    /// </summary>
    [Fact]
    public void Provision_creates_and_proves_only_the_fixed_fresh_fixture_graph()
    {
        using var service = new FreshFixtureProvisionSuccessService(BaselineOwnerId, Data8ServiceUserId);
        var ledger = new RecordingFreshFixtureLedger();
        var request = new P72FreshSliceCFixtureProvisionRequest(
            AddListId,
            RemoveListId,
            SmallGroupListId,
            ExistingLeaderContactId,
            TransferSourceListId,
            TransferTargetListId,
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Data8ServiceUserId,
            Guid.Parse("dddddddd-4444-4444-4444-444444444444"));
        var provisioner = new P72FreshSliceCFixtureProvisioner(service);

        var result = provisioner.Provision(request, ledger);

        result.Outcome.Should().Be("go");
        result.Reason.Should().Be("fresh-fixture-provisioned");
        result.OperationExecuted.Should().BeTrue();
        service.MutationTrace.Should().Equal(
            "create:source",
            "create:leader",
            "create:relationship-list",
            "add:remove",
            "add:transfer-source",
            "assign:baseline-owner");
        service.SourceContactOwnerId.Should().Be(BaselineOwnerId);
        service.IsMember(RemoveListId, FreshSourceContactId).Should().BeTrue();
        service.IsMember(TransferSourceListId, FreshSourceContactId).Should().BeTrue();
        service.IsMember(AddListId, FreshSourceContactId).Should().BeFalse();
        service.IsMember(TransferTargetListId, FreshSourceContactId).Should().BeFalse();
        service.FreshRelationshipLeaderId.Should().Be(FreshLeaderContactId);
        ledger.States.Select(static state => state.Stage).Should().Equal(
            "preflight-proven",
            "source-contact-created",
            "leader-contact-created",
            "relationship-list-created",
            "remove-membership-added",
            "transfer-source-membership-added",
            "baseline-owner-assigned",
            "fresh-graph-proven");
    }

    /// <summary>
    /// 注入第一個 CRM Create 在送出後逾時的情境，驗證 provisioner 不會把同一個 source Create 重送，
    /// 不會猜測新 ID、發出後續 mutation 或發布 descriptor。這保護 timeout-after-dispatch 的核心生命
    /// 週期契約：唯一可留下的恢復資料是已在 Create 前寫入的 current-user pending ledger stage；結果必須
    /// 是 non-retryable 的 sanitized no-go，而不是可視為安全的前置條件失敗。
    /// </summary>
    [Fact]
    public void Provision_preserves_only_the_pending_ledger_when_source_create_is_ambiguous()
    {
        using var service = new FreshFixtureProvisionSuccessService(BaselineOwnerId, Data8ServiceUserId, throwAfterSourceCreateBegins: true);
        var ledger = new RecordingFreshFixtureLedger();
        var request = new P72FreshSliceCFixtureProvisionRequest(
            AddListId,
            RemoveListId,
            SmallGroupListId,
            ExistingLeaderContactId,
            TransferSourceListId,
            TransferTargetListId,
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Data8ServiceUserId,
            Guid.Parse("eeeeeeee-5555-5555-5555-555555555555"));
        var provisioner = new P72FreshSliceCFixtureProvisioner(service);

        var result = provisioner.Provision(request, ledger);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("provisioning-ambiguous");
        result.OperationExecuted.Should().BeTrue();
        service.SourceCreateAttemptCount.Should().Be(1);
        service.MutationTrace.Should().BeEmpty();
        ledger.States.Select(static state => state.Stage).Should().Equal("preflight-proven");
    }

    /// <summary>
    /// 注入 transfer target 沒有唯一 active weekly report 的 preflight 缺口，驗證控制面會在任何
    /// ledger、Create、membership 或 Assign 前停止。這防止先留下無法執行 transfer composite 的 fresh
    /// source，再事後嘗試猜測或掃描其他 weekly report；決定性 assertion 是 operationExecuted、所有
    /// mutation trace 與 pending ledger 都維持空白。
    /// </summary>
    [Fact]
    public void Provision_rejects_missing_weekly_report_before_ledger_or_mutation()
    {
        using var service = new FreshFixtureProvisionSuccessService(BaselineOwnerId, Data8ServiceUserId, weeklyReportCount: 0);
        var ledger = new RecordingFreshFixtureLedger();
        var request = new P72FreshSliceCFixtureProvisionRequest(
            AddListId,
            RemoveListId,
            SmallGroupListId,
            ExistingLeaderContactId,
            TransferSourceListId,
            TransferTargetListId,
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Data8ServiceUserId,
            Guid.Parse("ffffffff-6666-6666-6666-666666666666"));
        var provisioner = new P72FreshSliceCFixtureProvisioner(service);

        var result = provisioner.Provision(request, ledger);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("fixture-precondition-failed");
        result.OperationExecuted.Should().BeFalse();
        service.SourceCreateAttemptCount.Should().Be(0);
        service.MutationTrace.Should().BeEmpty();
        ledger.States.Should().BeEmpty();
    }

    /// <summary>
    /// 對 source Create 之後的每一個 fixed remote mutation 注入 timeout-after-dispatch，驗證同一條
    /// non-retryable policy 一致套用於 leader、relationship list、兩次 membership 與 baseline Assign。
    /// 每個案例只能保留已 read-back 的前段 ledger states；失敗中的 request 不得重送，也不得猜測 ID、
    /// 發出後續 mutation 或執行自動 cleanup。這是確保任何 partial graph 不會跨 user/profile reused 的
    /// 決定性 fault-injection proof。
    /// </summary>
    /// <param name="faultAfterMutation">本次要模擬 transport 不確定性的唯一 fixed mutation boundary。</param>
    /// <param name="expectedAttempts">包含 faulted request 在內的總 remote mutation 嘗試數。</param>
    /// <param name="expectedCompletedMutations">在 fault 前已被 read-back 的成功 mutation 數。</param>
    /// <param name="expectedLedgerStates">preflight 加上每個成功 mutation 的 pending ledger stage 數。</param>
    [Theory]
    [InlineData(FreshFixtureMutationFault.LeaderCreate, 2, 1, 2)]
    [InlineData(FreshFixtureMutationFault.RelationshipListCreate, 3, 2, 3)]
    [InlineData(FreshFixtureMutationFault.RemoveMembership, 4, 3, 4)]
    [InlineData(FreshFixtureMutationFault.TransferSourceMembership, 5, 4, 5)]
    [InlineData(FreshFixtureMutationFault.BaselineOwnerAssign, 6, 5, 6)]
    public void Provision_does_not_retry_or_advance_past_any_ambiguous_mutation(
        FreshFixtureMutationFault faultAfterMutation,
        int expectedAttempts,
        int expectedCompletedMutations,
        int expectedLedgerStates)
    {
        using var service = new FreshFixtureProvisionSuccessService(
            BaselineOwnerId,
            Data8ServiceUserId,
            faultAfterMutation: faultAfterMutation);
        var ledger = new RecordingFreshFixtureLedger();
        var request = new P72FreshSliceCFixtureProvisionRequest(
            AddListId,
            RemoveListId,
            SmallGroupListId,
            ExistingLeaderContactId,
            TransferSourceListId,
            TransferTargetListId,
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Data8ServiceUserId,
            Guid.Parse("12121212-7777-7777-7777-777777777777"));
        var provisioner = new P72FreshSliceCFixtureProvisioner(service);

        var result = provisioner.Provision(request, ledger);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("provisioning-ambiguous");
        result.OperationExecuted.Should().BeTrue();
        service.MutationAttemptCount.Should().Be(expectedAttempts);
        service.MutationTrace.Should().HaveCount(expectedCompletedMutations);
        ledger.States.Should().HaveCount(expectedLedgerStates);
    }

    /// <summary>
    /// 驗證 explicit cleanup 只接受 final graph ledger state，並以與建立相反的順序移除 transfer-source、
    /// remove membership，再刪除 fresh relationship list、source contact、leader contact。測試先透過
    /// 同一離線 service 完成 provision，確保 cleanup 取得的是實際 read-back 的 IDs，而非 caller 自填
    /// GUID；決定性 assertion 是沒有 stale entity 被讀取或修改，且每個 remote delete/remove 都有 exact
    /// read-back absence proof 與下一個 ledger stage。
    /// </summary>
    [Fact]
    public void Cleanup_removes_only_the_ledger_proven_fresh_graph_in_reverse_order()
    {
        using var service = new FreshFixtureProvisionSuccessService(BaselineOwnerId, Data8ServiceUserId);
        var ledger = new RecordingFreshFixtureLedger();
        var request = new P72FreshSliceCFixtureProvisionRequest(
            AddListId,
            RemoveListId,
            SmallGroupListId,
            ExistingLeaderContactId,
            TransferSourceListId,
            TransferTargetListId,
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Data8ServiceUserId,
            Guid.Parse("13131313-8888-8888-8888-888888888888"));
        var provisioner = new P72FreshSliceCFixtureProvisioner(service);
        provisioner.Provision(request, ledger).Outcome.Should().Be("go");
        var provenLedgerState = ledger.States.Single(static state => state.Stage == "fresh-graph-proven");

        var result = provisioner.Cleanup(request, provenLedgerState, ledger);

        result.Outcome.Should().Be("go");
        result.Reason.Should().Be("fresh-fixture-cleaned");
        result.OperationExecuted.Should().BeTrue();
        service.MutationTrace.Skip(6).Should().Equal(
            "remove:transfer-source",
            "remove:remove",
            "delete:relationship-list",
            "delete:source",
            "delete:leader");
        service.IsMember(RemoveListId, FreshSourceContactId).Should().BeFalse();
        service.IsMember(TransferSourceListId, FreshSourceContactId).Should().BeFalse();
        service.EntityExists("list", FreshRelationshipListId).Should().BeFalse();
        service.EntityExists("contact", FreshSourceContactId).Should().BeFalse();
        service.EntityExists("contact", FreshLeaderContactId).Should().BeFalse();
        ledger.States.Select(static state => state.Stage).TakeLast(6).Should().Equal(
            "cleanup-preflight-proven",
            "cleanup-transfer-source-membership-removed",
            "cleanup-remove-membership-removed",
            "cleanup-relationship-list-deleted",
            "cleanup-source-contact-deleted",
            "cleanup-leader-contact-deleted");
    }

    /// <summary>
    /// 驗證 fresh fixture 發布後，活動 descriptor 已指向由 Data8 服務帳號建立的 fresh leader 時，
    /// cleanup 仍只能使用 provision 時已證明的原始 non-service baseline leader。此測試模擬真正
    /// parent 發布 descriptor 後的跨程序資料流：cleanup request 內的 leader 是 fresh leader，而
    /// recovery ledger 必須保存原始 leader。若 cleanup 改讀活動 descriptor，會拒絕正確的 fixture
    /// 或更糟地將服務帳號當作資料擁有者；兩者都會破壞可恢復性與 profile 隔離。測試 service、
    /// ledger 與所有 Entity 都是單一測試方法的 request-local 物件，結束時由 using Dispose，
    /// 因此不會保留跨測試、跨使用者或跨 profile 的 CRM/session 狀態。
    /// </summary>
    [Fact]
    public void Cleanup_uses_the_ledger_original_baseline_leader_after_descriptor_publication()
    {
        using var service = new FreshFixtureProvisionSuccessService(BaselineOwnerId, Data8ServiceUserId);
        var ledger = new RecordingFreshFixtureLedger();
        var provisionRequest = new P72FreshSliceCFixtureProvisionRequest(
            AddListId,
            RemoveListId,
            SmallGroupListId,
            ExistingLeaderContactId,
            TransferSourceListId,
            TransferTargetListId,
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Data8ServiceUserId,
            Guid.Parse("15151515-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var provisioner = new P72FreshSliceCFixtureProvisioner(service);

        provisioner.Provision(provisionRequest, ledger).Outcome.Should().Be("go");
        var provenLedgerState = ledger.States.Single(static state => state.Stage == "fresh-graph-proven");
        var publishedDescriptorRequest = provisionRequest with
        {
            ExistingTargetLeaderContactId = FreshLeaderContactId
        };

        var result = provisioner.Cleanup(publishedDescriptorRequest, provenLedgerState, ledger);

        result.Outcome.Should().Be("go");
        result.Reason.Should().Be("fresh-fixture-cleaned");
        service.MutationTrace.Skip(6).Should().Equal(
            "remove:transfer-source",
            "remove:remove",
            "delete:relationship-list",
            "delete:source",
            "delete:leader");
        ledger.States.Should().OnlyContain(
            state => state.OriginalTargetLeaderContactId == ExistingLeaderContactId,
            because: "cleanup stages must retain the provision-proven baseline leader even when the published descriptor points at the fresh leader");
    }

    /// <summary>
    /// 驗證 cleanup 的每一個遠端 mutation boundary 都採用相同的保守生命週期契約：
    /// dispatch 後 timeout 一律回報 <c>cleanup-ambiguous</c>、不自動重試、不執行邊界
    /// 之後的任何 mutation，並保留 current-user ledger 供獨立 exact-ID reconciliation
    /// lane 使用。這個 fault-injection 測試不接觸 CE，且以固定 fake graph 證明清理順序、
    /// mutation 嘗試次數與 ledger stage 均不會跨使用者或跨 fixture 重用。
    /// </summary>
    /// <param name="faultAfterMutation">要注入不確定性的 cleanup mutation boundary。</param>
    /// <param name="expectedCompletedCleanupMutations">timeout 前已完成並 read-back 的 cleanup mutation 數量。</param>
    /// <param name="expectedMutationAttempts">包含 provision 六次與本次 cleanup 嘗試的總數。</param>
    [Theory]
    [InlineData(FreshFixtureMutationFault.CleanupTransferSourceMembership, 0, 7)]
    [InlineData(FreshFixtureMutationFault.CleanupRemoveMembership, 1, 8)]
    [InlineData(FreshFixtureMutationFault.CleanupRelationshipListDelete, 2, 9)]
    [InlineData(FreshFixtureMutationFault.CleanupSourceContactDelete, 3, 10)]
    [InlineData(FreshFixtureMutationFault.CleanupLeaderContactDelete, 4, 11)]
    public void Cleanup_does_not_retry_or_mutate_past_an_ambiguous_boundary(
        FreshFixtureMutationFault faultAfterMutation,
        int expectedCompletedCleanupMutations,
        int expectedMutationAttempts)
    {
        using var service = new FreshFixtureProvisionSuccessService(
            BaselineOwnerId,
            Data8ServiceUserId,
            faultAfterMutation: faultAfterMutation);
        var ledger = new RecordingFreshFixtureLedger();
        var request = new P72FreshSliceCFixtureProvisionRequest(
            AddListId,
            RemoveListId,
            SmallGroupListId,
            ExistingLeaderContactId,
            TransferSourceListId,
            TransferTargetListId,
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Data8ServiceUserId,
            Guid.Parse("14141414-9999-9999-9999-999999999999"));
        var provisioner = new P72FreshSliceCFixtureProvisioner(service);

        provisioner.Provision(request, ledger).Outcome.Should().Be("go");
        var provenLedgerState = ledger.States.Single(static state => state.Stage == "fresh-graph-proven");

        var result = provisioner.Cleanup(request, provenLedgerState, ledger);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("cleanup-ambiguous");
        result.OperationExecuted.Should().BeTrue();
        result.LedgerEligibleForRemoval.Should().BeFalse();
        service.MutationAttemptCount.Should().Be(expectedMutationAttempts);
        service.MutationTrace.Should().HaveCount(6 + expectedCompletedCleanupMutations);
        service.MutationTrace.Skip(6).Should().Equal(
            new[]
            {
                "remove:transfer-source",
                "remove:remove",
                "delete:relationship-list",
                "delete:source"
            }.Take(expectedCompletedCleanupMutations));
        ledger.States.Last().Stage.Should().Be(
            expectedCompletedCleanupMutations == 0
                ? "cleanup-preflight-proven"
                : expectedCompletedCleanupMutations switch
                {
                    1 => "cleanup-transfer-source-membership-removed",
                    2 => "cleanup-remove-membership-removed",
                    3 => "cleanup-relationship-list-deleted",
                    4 => "cleanup-source-contact-deleted",
                    _ => throw new InvalidOperationException("Unexpected cleanup test case.")
                });
    }

    /// <summary>
    /// 模擬 provision child 所見的最小、固定且已由 descriptor 綁定的 CRM 投影。
    /// 這個替身只允許五個 static contact list、task-marked leader、其 active owner 與一個
    /// bounded weekly report；任何寫入 API 都會同步遞增計數。服務的生命週期只屬於本測試 using
    /// scope，<see cref="Dispose"/> 後再讀取會 fail closed，避免替身掩蓋真實 service disposal
    /// 後仍可被重用的資源或 session leakage。
    /// </summary>
    private sealed class FreshFixtureProvisionPreconditionService : IOrganizationService, IDisposable
    {
        private readonly Guid _leaderOwnerId;
        private bool _disposed;

        /// <summary>
        /// 建立固定 owner candidate 的唯讀 CRM 替身。
        /// <paramref name="leaderOwnerId"/> 是測試唯一可配置的身份差異；它不會來自使用者輸入、
        /// 環境變數或共用狀態，因此可精確注入「owner 等於 service user」的失敗條件。
        /// </summary>
        /// <param name="leaderOwnerId">由 task-marked leader 投影出的 system user ID。</param>
        internal FreshFixtureProvisionPreconditionService(Guid leaderOwnerId)
            => _leaderOwnerId = leaderOwnerId;

        /// <summary>
        /// 取得任何禁止的 CRM mutation 次數。此計數僅存在於 test instance，Dispose 後不會被
        /// 下一個測試或其他 profile 看見，作為 A/B isolation 的離線斷言。
        /// </summary>
        internal int MutationAttemptCount { get; private set; }

        /// <summary>
        /// 回傳固定 direct-Retrieve projection，拒絕不屬於新建 fixture preflight 的 entity、ID
        /// 或欄位。這迫使實作維持 exact-ID proof，而不是改成不受界限的 CRM discovery。
        /// </summary>
        /// <param name="entityName">只允許 list、contact 與 systemuser。</param>
        /// <param name="id">只允許本測試宣告的 task-bound ID。</param>
        /// <param name="columnSet">實作要求的 bounded 欄位投影。</param>
        /// <returns>與 CE 9.1 static-list、leader owner 與 active user 相符的合成 Entity。</returns>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(columnSet);

            if (string.Equals(entityName, "list", StringComparison.Ordinal) && IsOperationalList(id))
            {
                return new Entity("list", id)
                {
                    ["listname"] = "P7.2-SC-OPERATIONS",
                    ["type"] = false,
                    ["createdfromcode"] = new OptionSetValue(2)
                };
            }

            if (string.Equals(entityName, "contact", StringComparison.Ordinal) && id == ExistingLeaderContactId)
            {
                return new Entity("contact", id)
                {
                    ["fullname"] = "P7.2-SC-EXISTING-LEADER",
                    ["ownerid"] = new EntityReference("systemuser", _leaderOwnerId)
                };
            }

            if (string.Equals(entityName, "systemuser", StringComparison.Ordinal) && id == _leaderOwnerId)
            {
                return new Entity("systemuser", id)
                {
                    ["isdisabled"] = false
                };
            }

            throw new InvalidOperationException("The provisioner requested an unapproved direct CRM projection.");
        }

        /// <summary>
        /// 回傳唯一、active、目標 list/date 相符的 weekly report，讓本測試的唯一拒絕原因保持為
        /// baseline owner 與 service user 相同。若實作改用非 QueryExpression 或未受界限的查詢，
        /// 替身會 fail closed，避免測試錯誤接受昂貴或跨 fixture 的探索。
        /// </summary>
        /// <param name="query">僅接受受界限的 QueryExpression。</param>
        /// <returns>剛好一筆 synthetic weekly report。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ThrowIfDisposed();
            query.Should().BeOfType<QueryExpression>();
            var expression = (QueryExpression)query;
            expression.EntityName.Should().Be("new_group_present_weekly_report");
            expression.TopCount.Should().Be(2);

            var rows = new EntityCollection();
            rows.Entities.Add(new Entity("new_group_present_weekly_report", Guid.Parse("99999999-9999-9999-9999-999999999999")));
            return rows;
        }

        /// <summary>此失敗案例不可建立任何 CRM Entity；若被呼叫即記錄違約並拋出例外。</summary>
        public Guid Create(Entity entity) => ThrowUnexpectedMutation<Guid>();

        /// <summary>此失敗案例不可更新任何 CRM Entity；若被呼叫即記錄違約並拋出例外。</summary>
        public void Update(Entity entity) => ThrowUnexpectedMutation<object?>();

        /// <summary>此失敗案例不可刪除任何 CRM Entity；若被呼叫即記錄違約並拋出例外。</summary>
        public void Delete(string entityName, Guid id) => ThrowUnexpectedMutation<object?>();

        /// <summary>此失敗案例不可執行 association 或 Assign；若被呼叫即記錄違約並拋出例外。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => ThrowUnexpectedMutation<OrganizationResponse>();

        /// <summary>此失敗案例不可新增關聯；若被呼叫即記錄違約並拋出例外。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => ThrowUnexpectedMutation<object?>();

        /// <summary>此失敗案例不可移除關聯；若被呼叫即記錄違約並拋出例外。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => ThrowUnexpectedMutation<object?>();

        /// <summary>
        /// 結束此假服務的唯一資源 owner。它不持有連線、timer、subscription 或 background work；
        /// 僅將 disposed 狀態設為真，讓後續存取直接失敗，模擬真實 fixture store 不可跨 run 重用。
        /// </summary>
        public void Dispose() => _disposed = true;

        /// <summary>
        /// 驗證固定 operational list identity；不含 stale expected-relationship list，因為 fresh
        /// provision lane 不可讀取或改寫它。
        /// </summary>
        /// <param name="id">待驗證的 CRM list ID。</param>
        /// <returns>若 ID 是五個重用的 operational lists 之一則為 <see langword="true"/>。</returns>
        private static bool IsOperationalList(Guid id)
            => id == AddListId ||
               id == RemoveListId ||
               id == SmallGroupListId ||
               id == TransferSourceListId ||
               id == TransferTargetListId;

        /// <summary>
        /// 將任何不應存在的 mutation 轉成測試可觀察的失敗。計數在拋出前遞增，確保即使上層吞掉
        /// 例外，最後 assertion 仍能偵測到違反「preflight before write」的 contract。
        /// </summary>
        /// <typeparam name="T">呼叫端所需的回傳型別。</typeparam>
        /// <returns>此方法一定拋出，沒有可重用或暫存的結果。</returns>
        private T ThrowUnexpectedMutation<T>()
        {
            MutationAttemptCount++;
            throw new InvalidOperationException("The fresh-fixture precondition path attempted a CRM mutation.");
        }

        /// <summary>
        /// 讓 disposed 假服務與真實 request-scoped Data8 service 具有相同的 fail-closed 特性，防止
        /// 未來測試在已釋放的 client 上繼續投影資料而掩蓋資源或 session reuse 缺陷。
        /// </summary>
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 模擬 complete fresh-fixture graph 的嚴格離線 CRM service。它只保存本測試的 private entity
    /// snapshots 與 membership tuple；每次 Retrieve 都回傳深複本，避免 provisioner 修改後的 Entity
    /// 回寫進假服務或跨測試保留。所有未知 query、entity、mutation 與順序錯誤皆 fail closed，讓測試
    /// 能證明控制面沒有悄悄退回 generic CRM API、全組織掃描或 stale fixture reuse。
    /// </summary>
    private sealed class FreshFixtureProvisionSuccessService : IOrganizationService, IDisposable
    {
        private const string ContactEntityName = "contact";
        private const string ListEntityName = "list";
        private const string SystemUserEntityName = "systemuser";
        private const string WeeklyReportEntityName = "new_group_present_weekly_report";
        private const string ListMemberEntityName = "listmember";
        private const string PresentRecordEntityName = "new_present_record";
        private readonly Dictionary<(string EntityName, Guid Id), Entity> _entities = [];
        private readonly HashSet<(Guid ListId, Guid ContactId)> _memberships = [];
        private readonly Guid _baselineOwnerId;
        private readonly Guid _data8ServiceUserId;
        private readonly bool _throwAfterSourceCreateBegins;
        private readonly FreshFixtureMutationFault _faultAfterMutation;
        private readonly int _weeklyReportCount;
        private bool _disposed;

        /// <summary>
        /// 建立一個完整且只屬於單一測試 invocation 的 CRM graph。兩個 owner 值由測試明確傳入，
        /// 用以驗證 baseline owner 與 Data8 service user 的區隔；它們沒有來自環境、瀏覽器、credential
        /// 或共用 static state，因此不可能形成 A/B profile 的 session leakage。
        /// </summary>
        /// <param name="baselineOwnerId">task-marked existing leader 導出的 active non-service owner。</param>
        /// <param name="data8ServiceUserId">本次 child WhoAmI 已證明的 service user。</param>
        /// <param name="throwAfterSourceCreateBegins">為 true 時模擬 source Create 已送出但 transport 未回傳 ID。</param>
        /// <param name="weeklyReportCount">受界限 weekly-report response 的筆數；只允許 0、1 或 2 用於 preflight fault injection。</param>
        /// <param name="faultAfterMutation">除 source Create 外，要模擬 transport 不確定性的唯一固定 mutation boundary。</param>
        internal FreshFixtureProvisionSuccessService(
            Guid baselineOwnerId,
            Guid data8ServiceUserId,
            bool throwAfterSourceCreateBegins = false,
            int weeklyReportCount = 1,
            FreshFixtureMutationFault faultAfterMutation = FreshFixtureMutationFault.None)
        {
            if (weeklyReportCount is < 0 or > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(weeklyReportCount));
            }

            _baselineOwnerId = baselineOwnerId;
            _data8ServiceUserId = data8ServiceUserId;
            _throwAfterSourceCreateBegins = throwAfterSourceCreateBegins;
            _faultAfterMutation = throwAfterSourceCreateBegins
                ? FreshFixtureMutationFault.SourceCreate
                : faultAfterMutation;
            _weeklyReportCount = weeklyReportCount;
            foreach (var listId in new[]
                     {
                         AddListId,
                         RemoveListId,
                         SmallGroupListId,
                         TransferSourceListId,
                         TransferTargetListId
                     })
            {
                _entities[(ListEntityName, listId)] = new Entity(ListEntityName, listId)
                {
                    ["listname"] = "P7.2-SC-OPERATIONS",
                    ["type"] = false,
                    ["createdfromcode"] = new OptionSetValue(2)
                };
            }

            _entities[(ContactEntityName, ExistingLeaderContactId)] = new Entity(ContactEntityName, ExistingLeaderContactId)
            {
                ["fullname"] = "P7.2-SC-EXISTING-LEADER",
                ["ownerid"] = new EntityReference(SystemUserEntityName, baselineOwnerId)
            };
            _entities[(SystemUserEntityName, baselineOwnerId)] = new Entity(SystemUserEntityName, baselineOwnerId)
            {
                ["isdisabled"] = false
            };
        }

        /// <summary>
        /// 取得成功 lane 已實際接受的遠端 mutation 順序。這是 test-instance list，Dispose 時會清除，
        /// 不存在於產品 runtime、log、TRX 或另一個 profile 的測試 scope。
        /// </summary>
        internal List<string> MutationTrace { get; } = [];

        /// <summary>
        /// 取得 source Create 已開始的次數。timeout injection 會在此計數遞增後拋出，使測試可證明
        /// provisioner 沒有重送 uncertain request；這個值只存活於單一 test instance，Dispose 時歸零。
        /// </summary>
        internal int SourceCreateAttemptCount { get; private set; }

        /// <summary>
        /// 取得所有固定 remote mutation 已開始的總次數。每個 fault case 會在這個計數遞增後中斷，
        /// 因而能證明 production provisioner 沒有重送 request 或繞過一個未知的 partial graph。
        /// </summary>
        internal int MutationAttemptCount { get; private set; }

        /// <summary>取得 fresh source 的目前 owner；用於確認唯一 Assign 已把 owner 改為 baseline。</summary>
        internal Guid SourceContactOwnerId
            => GetRequiredReference(ContactEntityName, FreshSourceContactId, "ownerid").Id;

        /// <summary>取得 fresh relationship list 的 race leader；用於確認兩個 relationship lookup 沒有分岔。</summary>
        internal Guid FreshRelationshipLeaderId
            => GetRequiredReference(ListEntityName, FreshRelationshipListId, "new_contact_race_leager_list").Id;

        /// <summary>
        /// 判斷本測試私有 graph 中是否存在指定 membership。這是純記憶體 assertion helper，並不模擬或
        /// 洩漏真實 CE session；它在 disposed 後拒絕讀取，與 request-scoped service 的生命周期一致。
        /// </summary>
        /// <param name="listId">已被 control plane 驗證的固定 list ID。</param>
        /// <param name="contactId">已被 control plane 建立且 read-back 的 fresh contact ID。</param>
        /// <returns>該 exact list/contact pair 是否目前存在。</returns>
        internal bool IsMember(Guid listId, Guid contactId)
        {
            ThrowIfDisposed();
            return _memberships.Contains((listId, contactId));
        }

        /// <summary>
        /// 只供測試驗證 cleanup read-back 的精確實體存在性。此方法僅查詢本測試執行個體
        /// 私有的 entity 字典，不暴露任何真實 CE、使用者、profile 或跨測試可重用狀態；
        /// <see cref="Dispose"/> 會在測試結束時清除所有資料，避免測試本身保留 fixture。
        /// </summary>
        /// <param name="entityName">固定 allowlist 中的 <c>contact</c> 或 <c>list</c> logical name。</param>
        /// <param name="id">由 fake CRM Create 產生且由 ledger 證明的 exact ID。</param>
        /// <returns>若私有圖仍包含該 exact entity，則傳回 <see langword="true"/>。</returns>
        internal bool EntityExists(string entityName, Guid id)
        {
            ThrowIfDisposed();
            return _entities.ContainsKey((entityName, id));
        }

        /// <summary>
        /// 以 exact entity/ID 讀取 deep-cloned CRM projection。columnSet 不會改變本替身的安全 shape：
        /// production code 仍必須只要求它自己的 bounded columns；這裡回傳複本以防 SDK Entity 可變
        /// attribute dictionary 造成後續 assertion 被呼叫端污染。
        /// </summary>
        /// <param name="entityName">只允許 fixed contact、list 或 systemuser logical name。</param>
        /// <param name="id">只允許 existing descriptor 或本次 fresh graph 的 exact ID。</param>
        /// <param name="columnSet">呼叫端的 bounded projection 宣告。</param>
        /// <returns>與 private test graph 相符的獨立 Entity copy。</returns>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(columnSet);
            if (!_entities.TryGetValue((entityName, id), out var entity))
            {
                throw new InvalidOperationException("The provisioner requested an entity outside the fixed fresh-fixture graph.");
            }

            return CloneEntity(entity);
        }

        /// <summary>
        /// 回傳三種受界限的 query 結果：唯一 weekly report、單一 exact membership、或空的 matching
        /// present-record 集合。任何其他 entity/query 都立即拒絕，確保實作不以 caller-controlled
        /// FetchXML、paging 或 Organization scan 取代 fixed proof。
        /// </summary>
        /// <param name="query">只接受 <see cref="QueryExpression"/> 與固定 TopCount=2 projection。</param>
        /// <returns>不包含另一個 fixture、profile 或使用者資料的 bounded EntityCollection。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ThrowIfDisposed();
            if (query is not QueryExpression expression || expression.TopCount != 2)
            {
                throw new InvalidOperationException("The provisioner issued an unbounded or unsupported CRM query.");
            }

            return expression.EntityName switch
            {
                WeeklyReportEntityName => CreateWeeklyReportRows(expression),
                ListMemberEntityName => CreateMembershipRows(expression),
                PresentRecordEntityName => new EntityCollection(),
                ContactEntityName => CreateExactEntityRows(expression, "contactid"),
                ListEntityName => CreateExactEntityRows(expression, "listid"),
                _ => throw new InvalidOperationException("The provisioner queried an entity outside the fixed fresh-fixture graph.")
            };
        }

        /// <summary>
        /// 接受剛好三個 fixed Create templates，並立即將持久化結果保存在本測試私有 graph。source
        /// 由 service user 初始擁有，讓後續 Assign 的 observable owner transition 可被驗證；leader
        /// 的 fullname 則模擬 CRM 的 server-derived projection。任何第 4 次 Create 或欄位形狀漂移都拒絕。
        /// </summary>
        /// <param name="entity">只允許 source contact、leader contact 或 expected relationship list 模板。</param>
        /// <returns>由 server 指派的固定 fresh ID。</returns>
        public Guid Create(Entity entity)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(entity);

            if (string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
                entity.GetAttributeValue<string>("lastname")?.StartsWith("P7.2-SC-SOURCE-", StringComparison.Ordinal) == true)
            {
                EnsureMutationOrder("create:source");
                SourceCreateAttemptCount++;
                MutationAttemptCount++;
                if (_throwAfterSourceCreateBegins)
                {
                    throw new TimeoutException("Synthetic timeout after the source Create request began.");
                }

                ThrowIfFault(FreshFixtureMutationFault.SourceCreate);

                var source = CloneEntity(entity);
                source.Id = FreshSourceContactId;
                source["ownerid"] = new EntityReference(SystemUserEntityName, _data8ServiceUserId);
                _entities[(ContactEntityName, FreshSourceContactId)] = source;
                MutationTrace.Add("create:source");
                return FreshSourceContactId;
            }

            if (string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
                entity.GetAttributeValue<string>("lastname")?.StartsWith("P7.2-SC-LEADER-", StringComparison.Ordinal) == true)
            {
                EnsureMutationOrder("create:leader");
                MutationAttemptCount++;
                ThrowIfFault(FreshFixtureMutationFault.LeaderCreate);
                var leader = CloneEntity(entity);
                leader.Id = FreshLeaderContactId;
                leader["fullname"] = leader.GetAttributeValue<string>("lastname")!;
                // 真正 CE Create 會讓 fresh leader 先屬於 Data8 service user。此測試資料必須保留
                // 這個 owner 差異，才能證明 cleanup 不會錯把已發布 descriptor 的 fresh leader
                // 當成原始 baseline owner；字典完全是 test-instance 私有狀態，Dispose 後不會跨測試保留。
                leader["ownerid"] = new EntityReference(SystemUserEntityName, _data8ServiceUserId);
                _entities[(ContactEntityName, FreshLeaderContactId)] = leader;
                MutationTrace.Add("create:leader");
                return FreshLeaderContactId;
            }

            if (string.Equals(entity.LogicalName, ListEntityName, StringComparison.Ordinal) &&
                entity.GetAttributeValue<string>("listname")?.StartsWith("P7.2-SC-REL-", StringComparison.Ordinal) == true)
            {
                EnsureMutationOrder("create:relationship-list");
                MutationAttemptCount++;
                ThrowIfFault(FreshFixtureMutationFault.RelationshipListCreate);
                var relationshipList = CloneEntity(entity);
                relationshipList.Id = FreshRelationshipListId;
                _entities[(ListEntityName, FreshRelationshipListId)] = relationshipList;
                MutationTrace.Add("create:relationship-list");
                return FreshRelationshipListId;
            }

            throw new InvalidOperationException("The provisioner attempted an unapproved CRM Create.");
        }

        /// <summary>此 lane 不允許 generic Update；relationship fields 必須在 fixed Create template 中完整建立。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Fresh-fixture provisioning must not issue Update.");

        /// <summary>此 lane 不允許 delete；cleanup 必須經由另行 test-first 的 exact-ID cleanup control plane。</summary>
        /// <summary>
        /// 模擬 cleanup lane 唯一允許的三個 exact-ID Delete：fresh relationship list、fresh
        /// source contact 與 fresh leader contact。任何其他實體或 ID 都會 fail closed，避免
        /// 測試替實作意外放寬成泛用 CRM 刪除 API；逾時注入在送出後立刻停止後續變更。
        /// </summary>
        /// <param name="entityName">固定的 <c>list</c> 或 <c>contact</c> logical name。</param>
        /// <param name="id">僅可為 ledger 證明的 fresh graph exact ID。</param>
        public void Delete(string entityName, Guid id)
        {
            ThrowIfDisposed();

            var (trace, fault) = (entityName, id) switch
            {
                (ListEntityName, var relationshipId) when relationshipId == FreshRelationshipListId
                    => ("delete:relationship-list", FreshFixtureMutationFault.CleanupRelationshipListDelete),
                (ContactEntityName, var sourceId) when sourceId == FreshSourceContactId
                    => ("delete:source", FreshFixtureMutationFault.CleanupSourceContactDelete),
                (ContactEntityName, var leaderId) when leaderId == FreshLeaderContactId
                    => ("delete:leader", FreshFixtureMutationFault.CleanupLeaderContactDelete),
                _ => throw new InvalidOperationException("The cleanup lane attempted an unapproved CRM Delete.")
            };

            EnsureMutationOrder(trace);
            MutationAttemptCount++;
            ThrowIfFault(fault);
            if (!_entities.Remove((entityName, id)))
            {
                throw new InvalidOperationException("The cleanup lane attempted to delete an absent fresh-fixture entity.");
            }

            MutationTrace.Add(trace);
        }

        /// <summary>
        /// 接受剛好兩個 AddListMembersListRequest 與一個 AssignRequest。它不接受 Associate 或 arbitrary
        /// OrganizationRequest，讓 membership/owner mutation 保留可讀回、可 ledger 化的固定 sequence。
        /// </summary>
        /// <param name="request">只允許 remove membership、transfer-source membership 或 baseline owner Assign。</param>
        /// <returns>空白成功 response；所有可觀察狀態皆由後續 Retrieve/Query 讀回。</returns>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(request);

            if (request is AddListMembersListRequest addRequest)
            {
                if (addRequest.MemberIds is not { Length: 1 } memberIds || memberIds[0] != FreshSourceContactId)
                {
                    throw new InvalidOperationException("The provisioner attempted an unapproved membership payload.");
                }

                var trace = addRequest.ListId switch
                {
                    var id when id == RemoveListId => "add:remove",
                    var id when id == TransferSourceListId => "add:transfer-source",
                    _ => throw new InvalidOperationException("The provisioner selected an unapproved membership list.")
                };
                EnsureMutationOrder(trace);
                MutationAttemptCount++;
                ThrowIfFault(trace == "add:remove"
                    ? FreshFixtureMutationFault.RemoveMembership
                    : FreshFixtureMutationFault.TransferSourceMembership);
                _memberships.Add((addRequest.ListId, FreshSourceContactId));
                MutationTrace.Add(trace);
                return new OrganizationResponse();
            }

            if (request is RemoveMemberListRequest removeRequest)
            {
                if (removeRequest.EntityId != FreshSourceContactId)
                {
                    throw new InvalidOperationException("The cleanup lane attempted an unapproved membership payload.");
                }

                var (trace, fault) = removeRequest.ListId switch
                {
                    var id when id == TransferSourceListId
                        => ("remove:transfer-source", FreshFixtureMutationFault.CleanupTransferSourceMembership),
                    var id when id == RemoveListId
                        => ("remove:remove", FreshFixtureMutationFault.CleanupRemoveMembership),
                    _ => throw new InvalidOperationException("The cleanup lane selected an unapproved membership list.")
                };
                EnsureMutationOrder(trace);
                MutationAttemptCount++;
                ThrowIfFault(fault);
                if (!_memberships.Remove((removeRequest.ListId, FreshSourceContactId)))
                {
                    throw new InvalidOperationException("The cleanup lane attempted to remove an absent membership.");
                }

                MutationTrace.Add(trace);
                return new OrganizationResponse();
            }

            if (request is AssignRequest assignRequest)
            {
                EnsureMutationOrder("assign:baseline-owner");
                MutationAttemptCount++;
                ThrowIfFault(FreshFixtureMutationFault.BaselineOwnerAssign);
                if (assignRequest.Target is not { LogicalName: ContactEntityName } target ||
                    target.Id != FreshSourceContactId ||
                    assignRequest.Assignee is not { LogicalName: SystemUserEntityName, Id: var assigneeId } ||
                    assigneeId != _baselineOwnerId)
                {
                    throw new InvalidOperationException("The provisioner attempted an unapproved owner assignment.");
                }

                _entities[(ContactEntityName, FreshSourceContactId)]["ownerid"] = new EntityReference(SystemUserEntityName, _baselineOwnerId);
                MutationTrace.Add("assign:baseline-owner");
                return new OrganizationResponse();
            }

            throw new InvalidOperationException("The provisioner attempted an unapproved CRM Execute request.");
        }

        /// <summary>此 lane 不使用 generic Associate；membership 必須透過 fixed AddListMembers request。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new InvalidOperationException("Fresh-fixture provisioning must not issue Associate.");

        /// <summary>此 lane 不使用 generic Disassociate；cleanup 是獨立且 reverse-order 的 control plane。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new InvalidOperationException("Fresh-fixture provisioning must not issue Disassociate.");

        /// <summary>
        /// 釋放本 test service 擁有的所有私有 mutable state。真實 runtime 的 WCF client/lease 不屬於
        /// 此替身；本測試仍以 clear + disposed guard 模擬 deterministic drain，避免下一個測試或 profile
        /// 看見任何 source、leader、membership 或 trace。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entities.Clear();
            _memberships.Clear();
            MutationTrace.Clear();
            SourceCreateAttemptCount = 0;
            MutationAttemptCount = 0;
        }

        /// <summary>建立唯一 weekly report 的 bounded query response，拒絕遺失的 target-list/date filters。</summary>
        private EntityCollection CreateWeeklyReportRows(QueryExpression query)
        {
            RequireEqualCondition(query, "new_list_group_present_weekly_report", TransferTargetListId);
            RequireEqualCondition(query, "statecode", 0);
            if (!query.Criteria.Conditions.Any(condition => string.Equals(condition.AttributeName, "new_sunday_date", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("The provisioner omitted the bounded weekly-report date filter.");
            }

            var rows = new EntityCollection();
            if (_weeklyReportCount >= 1)
            {
                rows.Entities.Add(new Entity(WeeklyReportEntityName, WeeklyReportId));
            }

            if (_weeklyReportCount == 2)
            {
                rows.Entities.Add(new Entity(WeeklyReportEntityName, Guid.Parse("dddddddd-3333-3333-3333-333333333333")));
            }

            return rows;
        }

        /// <summary>建立單一 exact membership read-back，禁止 list/contact 以外的 membership discovery。</summary>
        private EntityCollection CreateMembershipRows(QueryExpression query)
        {
            var listId = ReadGuidCondition(query, "listid");
            var contactId = ReadGuidCondition(query, "entityid");
            var rows = new EntityCollection();
            if (_memberships.Contains((listId, contactId)))
            {
                rows.Entities.Add(new Entity(ListMemberEntityName, Guid.NewGuid())
                {
                    ["listid"] = new EntityReference(ListEntityName, listId),
                    ["entityid"] = new EntityReference(ContactEntityName, contactId)
                });
            }

            return rows;
        }

        /// <summary>
        /// 模擬 cleanup 的 bounded exact-ID absence read-back。只接受 <c>TopCount=2</c>、
        /// 單一主鍵 Equal 條件與單欄位投影；這使刪除成功不能由例外、快取或泛用掃描推論，
        /// 並且不會讓測試 fake 成為可供其他路徑使用的 CRM 查詢器。
        /// </summary>
        /// <param name="query">由 provisioner 建構的固定 <see cref="QueryExpression"/>。</param>
        /// <param name="idAttribute">對應 entity 的固定 primary-key attribute。</param>
        /// <returns>存在時只回傳一筆 private graph 投影；不存在時回傳空集合。</returns>
        private EntityCollection CreateExactEntityRows(QueryExpression query, string idAttribute)
        {
            if (query.ColumnSet.AllColumns ||
                query.ColumnSet.Columns.Count != 1 ||
                !string.Equals(query.ColumnSet.Columns[0], idAttribute, StringComparison.Ordinal) ||
                query.Criteria.Conditions.Count != 1)
            {
                throw new InvalidOperationException("The cleanup lane issued an invalid exact-ID absence query.");
            }

            var id = ReadGuidCondition(query, idAttribute);
            var rows = new EntityCollection();
            if (_entities.ContainsKey((query.EntityName, id)))
            {
                rows.Entities.Add(new Entity(query.EntityName, id));
            }

            return rows;
        }

        private void EnsureMutationOrder(string expectedTrace)
        {
            var expectedIndex = MutationTrace.Count;
            var expected = new[]
            {
                "create:source",
                "create:leader",
                "create:relationship-list",
                "add:remove",
                "add:transfer-source",
                "assign:baseline-owner",
                "remove:transfer-source",
                "remove:remove",
                "delete:relationship-list",
                "delete:source",
                "delete:leader"
            };
            if (expectedIndex >= expected.Length || !string.Equals(expected[expectedIndex], expectedTrace, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The provisioner issued a CRM mutation outside the fixed sequence.");
            }
        }

        /// <summary>
        /// 在指定 request 已開始、尚未取得可安全 read-back 的結果時注入 timeout。此方法不改寫
        /// private graph、ledger 或 trace，模擬真實 transport ambiguity：production code 必須停止，
        /// 而不是依測試內的記憶體狀態猜測 server 是否已提交。
        /// </summary>
        /// <param name="boundary">目前正要開始的 fixed mutation boundary。</param>
        private void ThrowIfFault(FreshFixtureMutationFault boundary)
        {
            if (_faultAfterMutation == boundary)
            {
                throw new TimeoutException("Synthetic timeout after a fixed fresh-fixture mutation began.");
            }
        }

        /// <summary>取得 private fresh graph 上某個必填 reference，不讓測試讀取或保留共用 Entity。</summary>
        private EntityReference GetRequiredReference(string entityName, Guid entityId, string attributeName)
        {
            ThrowIfDisposed();
            var entity = _entities[(entityName, entityId)];
            return entity.GetAttributeValue<EntityReference>(attributeName)
                ?? throw new InvalidOperationException("The fixed relationship projection is missing.");
        }

        /// <summary>要求 query 存在固定 Equal GUID/int condition，避免不受界限的資料探索。</summary>
        private static void RequireEqualCondition(QueryExpression query, string attributeName, object expectedValue)
        {
            var condition = query.Criteria.Conditions.SingleOrDefault(candidate => string.Equals(candidate.AttributeName, attributeName, StringComparison.Ordinal));
            if (condition is null || condition.Operator != ConditionOperator.Equal || condition.Values.Count != 1 || !Equals(condition.Values[0], expectedValue))
            {
                throw new InvalidOperationException("The provisioner issued an invalid fixed query condition.");
            }
        }

        /// <summary>從固定 Equal condition 讀取 exact GUID，不接受 IN、EntityReference 或多值 selector。</summary>
        private static Guid ReadGuidCondition(QueryExpression query, string attributeName)
        {
            var condition = query.Criteria.Conditions.SingleOrDefault(candidate => string.Equals(candidate.AttributeName, attributeName, StringComparison.Ordinal));
            if (condition is null || condition.Operator != ConditionOperator.Equal || condition.Values.Count != 1 || condition.Values[0] is not Guid id || id == Guid.Empty)
            {
                throw new InvalidOperationException("The provisioner issued an invalid exact-ID query condition.");
            }

            return id;
        }

        /// <summary>建立 Entity/lookup/optionset 的隔離複本，避免可變 SDK payload 在呼叫端與 store 間共用。</summary>
        private static Entity CloneEntity(Entity source)
        {
            var clone = new Entity(source.LogicalName, source.Id);
            foreach (var pair in source.Attributes)
            {
                clone[pair.Key] = pair.Value switch
                {
                    EntityReference reference => new EntityReference(reference.LogicalName, reference.Id),
                    OptionSetValue optionSet => new OptionSetValue(optionSet.Value),
                    _ => pair.Value
                };
            }

            return clone;
        }

        /// <summary>讓 disposed test service 與 request-scoped Data8 service 同樣拒絕任何後續存取。</summary>
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 以 test-instance collection 取代本機 ledger 檔案，驗證 provisioner 在前置條件失敗時連
    /// 任何 pending state 都不會發布。collection 不會離開測試範圍，因此不可能保留別的使用者、
    /// profile 或 fixture run 的 identifier。
    /// </summary>
    private sealed class RecordingFreshFixtureLedger : IP72FreshSliceCFixtureLedger
    {
        /// <summary>取得已寫入的 immutable ledger state；預設為空集合。</summary>
        internal List<P72FreshSliceCFixtureLedgerState> States { get; } = [];

        /// <summary>
        /// 記錄 provisioner 想持久化的 state。真實實作將以 current-user local atomic file 取代此
        /// collection；此替身只保存 test-local reference，沒有 I/O、credential 或跨測試保留。
        /// </summary>
        /// <param name="state">已由 provisioner 建立的 immutable stage snapshot。</param>
        public void Persist(P72FreshSliceCFixtureLedgerState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            States.Add(state);
        }
    }
}
