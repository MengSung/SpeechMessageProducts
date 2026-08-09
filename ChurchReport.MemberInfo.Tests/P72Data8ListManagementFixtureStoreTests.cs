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
