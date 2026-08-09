// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureProvenanceTests.cs
// 用途：鎖定 P7.2 Slice C 實機 evidence 在第一次 CRM mutation 前，必須以固定的唯讀
//       projection 證明 descriptor 中的 contact 與 list 都是 task-owned fixture。
//
// 信任與生命週期：
// 1. 測試替身只接受固定 list/contact Retrieve；它不建立連線、session、credential、cache 或背景工作。
// 2. 故障注入模擬 dynamic list 或遠端 marker 缺失；決定性 assertion 是 validation 回傳 false，且
//    沒有 Update、Delete、Assign、Associate 或其他 CRM mutation。
// 3. 替身由每個 test 的 using scope 唯一擁有；Dispose 後拒絕後續呼叫，避免測試間可變狀態重用。
// ============================================================================

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 Slice C descriptor 的遠端 provenance 防線。此測試保護的不是 descriptor JSON 格式，而是
/// descriptor 指向的 CE list/contact 必須帶 task marker 並且 list 是 static；否則 live bridge 在
/// 任何 Data8 dispatch 前都必須停止，不能把同一 Windows 使用者可讀取的任意 CRM 記錄當成 fixture。
/// </summary>
public sealed class P72Data8ListManagementFixtureProvenanceTests
{
    private static readonly Guid FixtureContactId = Guid.Parse("10101010-1010-1010-1010-101010101010");
    private static readonly Guid AddListId = Guid.Parse("20202020-2020-2020-2020-202020202020");
    private static readonly Guid RemoveListId = Guid.Parse("30303030-3030-3030-3030-303030303030");
    private static readonly Guid SmallGroupListId = Guid.Parse("40404040-4040-4040-4040-404040404040");
    private static readonly Guid TargetLeaderContactId = Guid.Parse("50505050-5050-5050-5050-505050505050");
    private static readonly Guid ExpectedRelationshipListId = Guid.Parse("60606060-6060-6060-6060-606060606060");
    private static readonly Guid TransferSourceListId = Guid.Parse("70707070-7070-7070-7070-707070707070");
    private static readonly Guid TransferTargetListId = Guid.Parse("80808080-8080-8080-8080-808080808080");

    /// <summary>
    /// 保護正常的 task-owned graph proof。故障注入不存在；替身只提供六筆名稱帶有 P7.2-SC marker、
    /// type=static 的 list，以及 source contact marker 和 target leader marker。決定性 assertion 是
    /// validation 通過且沒有任何 mutation，讓後續 bridge 才有資格讀取 baseline。
    /// </summary>
    [Fact]
    public void Try_validate_task_owned_slice_c_fixture_graph_accepts_only_the_fixed_static_marker_bound_graph()
    {
        using var service = new FixtureProvenanceRecordingOrganizationService();
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.TryValidateTaskOwnedSliceCFixtureGraph(
            FixtureContactId,
            AddListId,
            RemoveListId,
            SmallGroupListId,
            TargetLeaderContactId,
            ExpectedRelationshipListId,
            TransferSourceListId,
            TransferTargetListId);

        result.Should().BeTrue();
        service.RetrieveCount.Should().Be(8);
        service.MutationAttemptCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 static-list 邊界。故障注入為一筆名稱正確但 type=dynamic 的 list；這可模擬惡意或錯誤
    /// descriptor 指向可讀取的 marketing dynamic list。決定性 assertion 是 fail closed，且 validation
    /// 不得以任何 mutation 嘗試補救、轉換或清理該 list。
    /// </summary>
    [Fact]
    public void Try_validate_task_owned_slice_c_fixture_graph_rejects_a_dynamic_list_without_mutation()
    {
        using var service = new FixtureProvenanceRecordingOrganizationService(dynamicListId: RemoveListId);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.TryValidateTaskOwnedSliceCFixtureGraph(
            FixtureContactId,
            AddListId,
            RemoveListId,
            SmallGroupListId,
            TargetLeaderContactId,
            ExpectedRelationshipListId,
            TransferSourceListId,
            TransferTargetListId);

        result.Should().BeFalse();
        service.MutationAttemptCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 source contact 的遠端 marker。故障注入為正確 GUID 但 description 沒有 Slice A fixture marker
    /// 的 contact；這證明本機 descriptor 和目前 Windows identity 不能單獨授權任意 CRM contact。決定性
    /// assertion 是 validation false 與零 mutation。
    /// </summary>
    [Fact]
    public void Try_validate_task_owned_slice_c_fixture_graph_rejects_a_contact_without_the_source_fixture_marker()
    {
        using var service = new FixtureProvenanceRecordingOrganizationService(sourceContactMarkerIsMissing: true);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.TryValidateTaskOwnedSliceCFixtureGraph(
            FixtureContactId,
            AddListId,
            RemoveListId,
            SmallGroupListId,
            TargetLeaderContactId,
            ExpectedRelationshipListId,
            TransferSourceListId,
            TransferTargetListId);

        result.Should().BeFalse();
        service.MutationAttemptCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 contact-only marketing-list 邊界。名稱 marker 與 static 型別本身不足以證明成員種類；若
    /// <c>createdfromcode</c> 指向 Account 或 Lead，listmember 的固定 contact membership 操作可能
    /// 在第一個 dispatch 才失敗。此故障注入保證 store 在純 Retrieve 階段即拒絕，且不產生任何 CRM
    /// mutation、連線快取或可跨測試重用的 mutable state。
    /// </summary>
    [Fact]
    public void Try_validate_task_owned_slice_c_fixture_graph_rejects_a_non_contact_list_without_mutation()
    {
        using var service = new FixtureProvenanceRecordingOrganizationService(
            createdFromCode: 1,
            useCrmBooleanListType: false);
        using var store = new P72Data8ListManagementFixtureStore(service);

        var result = store.TryValidateTaskOwnedSliceCFixtureGraph(
            FixtureContactId,
            AddListId,
            RemoveListId,
            SmallGroupListId,
            TargetLeaderContactId,
            ExpectedRelationshipListId,
            TransferSourceListId,
            TransferTargetListId);

        result.Should().BeFalse();
        service.MutationAttemptCount.Should().Be(0);
    }

    /// <summary>
    /// 封閉的唯讀 CRM 替身。它只投影 production store 會讀取的兩種 entity 與固定欄位，並記錄任何
    /// 非 Retrieve API 作為 mutation attempt；這使 regression test 能證明 provenance 失敗時不會產生
    /// 外部資料副作用，也不需要真正 CE、Data8 credential 或不受控 SDK graph。
    /// </summary>
    private sealed class FixtureProvenanceRecordingOrganizationService : IOrganizationService, IDisposable
    {
        private readonly Guid? _dynamicListId;
        private readonly bool _sourceContactMarkerIsMissing;
        private readonly int _createdFromCode;
        private readonly bool _useCrmBooleanListType;
        private bool _disposed;

        /// <summary>
    /// 建立可選故障注入的 source。dynamicListId 只改變固定 list type，sourceContactMarkerIsMissing
    /// 只移除 source contact 的 description marker，createdFromCode 則模擬 list 的固定 member-kind；
    /// useCrmBooleanListType 預設模擬真機 CE 9.1 的 Boolean <c>type</c>，false 時才保留過時選項集
    /// 投影以驗證 store 拒絕它。其餘 identity 維持正確，確保每個測試只驗證一個 fail-closed 分支。
        /// </summary>
        internal FixtureProvenanceRecordingOrganizationService(
            Guid? dynamicListId = null,
            bool sourceContactMarkerIsMissing = false,
            int createdFromCode = 2,
            bool useCrmBooleanListType = true)
        {
            _dynamicListId = dynamicListId;
            _sourceContactMarkerIsMissing = sourceContactMarkerIsMissing;
            _createdFromCode = createdFromCode;
            _useCrmBooleanListType = useCrmBooleanListType;
        }

        /// <summary>取得固定 provenance projection 的讀取次數；正常 graph 必須剛好讀取六 list 與兩 contact。</summary>
        internal int RetrieveCount { get; private set; }

        /// <summary>取得所有未核准 mutation API 的嘗試次數；每個 test 都必須為零。</summary>
        internal int MutationAttemptCount { get; private set; }

        /// <summary>
    /// 只接受 store-owned list/contact direct Retrieve。list 必須讀取 listname/type/createdfromcode，source contact
        /// 必須讀取 description，而 task-owned leader 必須讀取 fullname；其他 projection 代表 contract
        /// 被擴張，立即以固定例外停止。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RetrieveCount++;
            ArgumentNullException.ThrowIfNull(columnSet);

            if (entityName == "list" && IsListId(id))
            {
                columnSet.Columns.Should().Equal("listname", "type", "createdfromcode");
                return new Entity("list", id)
                {
                    ["listname"] = "P7.2-SC-fixture",
                    // CE 9.1 list.type 是 Boolean：true=dynamic、false=static。選項集分支僅用於
                    // 故障注入，證明 production store 不會依過時 SDK 假設誤判真機 static list。
                    ["type"] = _useCrmBooleanListType
                        ? id == _dynamicListId
                        : new OptionSetValue(id == _dynamicListId ? 1 : 0),
                    ["createdfromcode"] = new OptionSetValue(_createdFromCode)
                };
            }

            if (entityName == "contact" && id == FixtureContactId)
            {
                columnSet.Columns.Should().Equal("description");
                return new Entity("contact", id)
                {
                    ["description"] = _sourceContactMarkerIsMissing ? "other-fixture" : "p7.2-contact-basic-info"
                };
            }

            if (entityName == "contact" && id == TargetLeaderContactId)
            {
                columnSet.Columns.Should().Equal("fullname");
                return new Entity("contact", id)
                {
                    ["fullname"] = "P7.2-SC-LEADER"
                };
            }

            throw new InvalidOperationException("The provenance store requested an unapproved CRM projection.");
        }

        /// <summary>所有 generic query 都是未核准的 discovery；provenance proof 只能 direct Retrieve 已知 identity。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw UnexpectedMutationOrDiscovery();

        /// <summary>provenance proof 不得建立 CRM entity。</summary>
        public Guid Create(Entity entity) => throw UnexpectedMutationOrDiscovery();

        /// <summary>provenance proof 不得更新 CRM entity。</summary>
        public void Update(Entity entity) => throw UnexpectedMutationOrDiscovery();

        /// <summary>provenance proof 不得刪除 CRM entity。</summary>
        public void Delete(string entityName, Guid id) => throw UnexpectedMutationOrDiscovery();

        /// <summary>provenance proof 不得執行 Action、Assign 或其他 OrganizationRequest。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw UnexpectedMutationOrDiscovery();

        /// <summary>provenance proof 不得建立 association。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw UnexpectedMutationOrDiscovery();

        /// <summary>provenance proof 不得移除 association。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw UnexpectedMutationOrDiscovery();

        /// <summary>釋放 test-local state；其後所有讀取都 fail closed。</summary>
        public void Dispose() => _disposed = true;

        /// <summary>
        /// 以固定 exception 記錄不被允許的 API。無論是 mutation 或 generic discovery 都算作違反唯一的
        /// read-only provenance owner，不能攜帶 fixture GUID 或 entity payload 到 test output。
        /// </summary>
        private InvalidOperationException UnexpectedMutationOrDiscovery()
        {
            MutationAttemptCount++;
            return new InvalidOperationException("The provenance store called an unapproved CRM operation.");
        }

        /// <summary>判斷輸入是否是這個封閉 fixture graph 的六筆 list identity。</summary>
        private static bool IsListId(Guid id)
            => id == AddListId ||
               id == RemoveListId ||
               id == SmallGroupListId ||
               id == ExpectedRelationshipListId ||
               id == TransferSourceListId ||
               id == TransferTargetListId;
    }
}
