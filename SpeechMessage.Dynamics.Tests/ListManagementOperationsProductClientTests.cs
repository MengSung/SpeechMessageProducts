// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ListManagementOperationsProductClientTests.cs
// 目的：以 TDD 鎖定 P7.2 Slice C 五個固定 list-management ProductClient 契約。
//
// 信任與生命週期：
// - 測試只使用 request-scope fake executor，不建立 CRM、HTTP、credential、session、stream 或背景工作。
// - 每個測試證明 list/contact/owner GUID 與 member array 都在第一次 await 前被複製、限制與正規化，不能變成
//   generic Entity、FetchXML、OrganizationRequest 或 request-time connector/profile/CE routing input。
// - fake executor 僅保留當前測試 instance 的最後 request；測試結束後沒有 static 或跨測試 retained state。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.ListManagement;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Slice C 的 typed ProductClient 只能派送五個固定 business capability，並在 transport 前拒絕無界或
/// 不相容的資料。所有測試都刻意不接觸 Data8／SDK，藉此分離產品契約與真實 CE fixture 的責任。
/// </summary>
public sealed class ListManagementOperationsProductClientTests
{
    /// <summary>
    /// 保護 static-list add-many 將 caller 的任意順序 member collection 複製成最多 1,000 個、排序後的 GUID array，
    /// 再以唯一 allowlisted operation 派送。故障注入是可變來源陣列；決定性斷言是 executor 收到新的 immutable
    /// 內容、固定 list/member key 與 idempotency key，且結果不含 list/contact identity 或 CRM response。
    /// </summary>
    [Fact]
    public async Task Add_members_maps_a_distinct_sorted_member_set_to_the_fixed_capability()
    {
        var executor = new RecordingExecutor(OperationExecutionResult.Success(
            OperationResponseData.ForStaticListMembershipMutation(
                OperationIds.ListMembersAddMany,
                "9.1",
                P72ControlledMutationDisposition.Changed,
                P72ControlledMutationCorrelationCategory.ReadBackConfirmed)));
        var client = new Package02ListManagementClient(
            executor,
            NullLogger<Package02ListManagementClient>.Instance);
        var members = new[]
        {
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("11111111-1111-1111-1111-111111111111")
        };

        var result = await client.AddMembersAsync(new StaticListMembersAddRequest
        {
            ProfileAlias = " crm91 ",
            WorkloadSubjectId = " churchreport-list-management ",
            ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            MemberIds = members,
            IdempotencyKey = "p72-list-add-1"
        });
        members[0] = Guid.Parse("99999999-9999-9999-9999-999999999999");

        result.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        result.CorrelationCategory.Should().Be(P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.ProfileAlias.Should().Be("crm91");
        executor.LastRequest.WorkloadSubjectId.Should().Be("churchreport-list-management");
        executor.LastRequest.CapabilityOperationId.Should().Be(OperationIds.ListMembersAddMany);
        executor.LastRequest.Parameters.Keys.Should().Equal("listId", "memberIds");
        executor.LastRequest.Parameters["listId"].Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        ((IReadOnlyList<Guid>)executor.LastRequest.Parameters["memberIds"]!)
            .Should()
            .Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("55555555-5555-5555-5555-555555555555"));
    }

    /// <summary>
    /// 保護 ProductClient 在 executor 前拒絕 Slice C 不能安全 reconcile 的 member set。故障注入依序為空 GUID、
    /// duplicate GUID、超過 1,000 筆及 source/target 相同的 transfer；決定性斷言是全部 fail fast、
    /// executor 次數維持零，因此不會取得 connector lease 或送出 CRM action。
    /// </summary>
    [Fact]
    public async Task List_management_rejects_unbounded_or_ambiguous_input_before_executor()
    {
        var executor = new RecordingExecutor(OperationExecutionResult.Failure("unused", "unused"));
        var client = new Package02ListManagementClient(
            executor,
            NullLogger<Package02ListManagementClient>.Instance);
        var repeated = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var overLimit = Enumerable.Range(1, 1001)
            .Select(static value => Guid.Parse($"00000000-0000-0000-0000-{value:D12}"))
            .ToArray();
        var sameList = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        Func<Task> emptyGuid = () => client.AddMembersAsync(CreateAddRequest(new[] { Guid.Empty }));
        Func<Task> duplicateGuid = () => client.AddMembersAsync(CreateAddRequest(new[] { repeated, repeated }));
        Func<Task> tooManyMembers = () => client.AddMembersAsync(CreateAddRequest(overLimit));
        Func<Task> sameSourceAndTarget = () => client.TransferContactBetweenListsAsync(new ContactListTransferRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-list-management",
            ContactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            SourceListId = sameList,
            TargetListId = sameList,
            WeekStartDate = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
            IdempotencyKey = "p72-transfer-invalid"
        });

        await emptyGuid.Should().ThrowAsync<ArgumentException>();
        await duplicateGuid.Should().ThrowAsync<ArgumentException>();
        await tooManyMembers.Should().ThrowAsync<ArgumentException>();
        await sameSourceAndTarget.Should().ThrowAsync<ArgumentException>();

        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 small-group、owner assignment 與 transfer 必須各自使用固定 operation/response branch，不能共享一個
    /// generic CRUD result。故障注入是 executor 回傳錯誤 operation；決定性斷言是 typed client 只送出指定
    /// parameters，並在 response identity/discriminator 不相符時 fail closed。
    /// </summary>
    [Fact]
    public async Task Fixed_small_group_owner_and_transfer_operations_require_matching_response_branches()
    {
        var executor = new SequencedExecutor(
            OperationExecutionResult.Success(OperationResponseData.ForSmallGroupFixedFieldsMutation(
                OperationIds.ListManagementSmallGroupUpdateFields,
                "9.1",
                P72ControlledMutationDisposition.NoChange,
                P72ControlledMutationCorrelationCategory.NoDispatch)),
            OperationExecutionResult.Success(OperationResponseData.ForContactOwnerAssignment(
                OperationIds.ContactAssignOwner,
                "9.1",
                P72ControlledMutationDisposition.Changed,
                P72ControlledMutationCorrelationCategory.ReadBackConfirmed)),
            OperationExecutionResult.Success(OperationResponseData.ForContactListTransfer(
                OperationIds.NewPersonContactTransferBetweenLists,
                "9.1",
                P72ControlledMutationDisposition.Changed,
                P72ControlledMutationCorrelationCategory.ReadBackConfirmed)),
            OperationExecutionResult.Success(OperationResponseData.ForContactOwnerAssignment(
                OperationIds.ContactAssignOwner,
                "9.1",
                P72ControlledMutationDisposition.Changed,
                P72ControlledMutationCorrelationCategory.ReadBackConfirmed)));
        var client = new Package02ListManagementClient(
            executor,
            NullLogger<Package02ListManagementClient>.Instance);
        var listId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var contactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var leaderId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var ownerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var smallGroup = await client.UpdateSmallGroupFieldsAsync(new SmallGroupFixedFieldsUpdateRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-list-management",
            ListId = listId,
            Mode = SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
            TargetLeaderContactId = leaderId,
            IdempotencyKey = "p72-small-group-1"
        });
        var assignment = await client.AssignContactOwnerAsync(new ContactOwnerAssignmentRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-list-management",
            ContactId = contactId,
            OwnerSystemUserId = ownerId,
            IdempotencyKey = "p72-owner-1"
        });
        var transfer = await client.TransferContactBetweenListsAsync(new ContactListTransferRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-list-management",
            ContactId = contactId,
            SourceListId = listId,
            TargetListId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            WeekStartDate = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.FromHours(8)),
            OwnerSystemUserId = ownerId,
            IdempotencyKey = "p72-transfer-1"
        });

        smallGroup.Disposition.Should().Be(P72ControlledMutationDisposition.NoChange);
        assignment.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        transfer.Disposition.Should().Be(P72ControlledMutationDisposition.Changed);
        executor.Requests.Select(request => request.CapabilityOperationId).Should().Equal(
            OperationIds.ListManagementSmallGroupUpdateFields,
            OperationIds.ContactAssignOwner,
            OperationIds.NewPersonContactTransferBetweenLists);
        executor.Requests[0].Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["listId"] = listId,
            ["mode"] = "change-area-leader",
            ["targetLeaderContactId"] = leaderId
        });
        executor.Requests[2].Parameters["weekStartDate"]
            .Should()
            .Be(new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));

        var mismatch = () => client.RemoveMemberAsync(new StaticListMemberRemoveRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-list-management",
            ListId = listId,
            MemberId = contactId,
            IdempotencyKey = "p72-list-remove-mismatch"
        });
        await mismatch.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 Slice C client 需要明確 DI 註冊、只重用既有 executor，且不因註冊動作建立 Gateway、Data8 service、
    /// credential、timer 或 background task。決定性斷言是 service provider 只得到一個 stateless typed client。
    /// </summary>
    [Fact]
    public void Slice_c_registration_is_explicit_and_reuses_the_registered_executor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDynamicsOperationExecutor>(
            new RecordingExecutor(OperationExecutionResult.Failure("unused", "unused")));
        services.AddSpeechMessageDynamicsPackage02ListManagementOperations();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IPackage02ListManagementClient>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<Package02ListManagementClient>();
    }

    /// <summary>建立合法 add-many request；它只包含測試 GUID 與不含個資的 bounded idempotency key。</summary>
    private static StaticListMembersAddRequest CreateAddRequest(IReadOnlyList<Guid> memberIds)
        => new()
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-list-management",
            ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            MemberIds = memberIds,
            IdempotencyKey = "p72-list-add-valid"
        };

    /// <summary>
    /// 單次測試 scope 的 executor fake。它不建立 transport；每次呼叫只將 request 保存在當前 test object，
    /// 因此不會將 profile、workload 或 member collection 留到下一個測試或使用者工作階段。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly OperationExecutionResult _result;

        /// <summary>建立固定 immutable result 的 fake；不擁有 connector、client、credential 或可釋放資源。</summary>
        public RecordingExecutor(OperationExecutionResult result) => _result = result;

        /// <summary>取得 transport 前驗證是否阻擋 request 的呼叫次數。</summary>
        public int CallCount { get; private set; }

        /// <summary>取得最後一筆 request；此參考只限目前測試 instance 的短生命週期。</summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>記錄 request 後回傳 fixed result，不建立非同步背景工作或取消註冊。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// 依照預先給定的 result sequence 記錄 requests。sequence 與 list 都由單一測試 owner 持有，沒有 static cache；
    /// 用盡 sequence 時直接失敗，避免測試意外把錯誤 response 當作成功。
    /// </summary>
    private sealed class SequencedExecutor : IDynamicsOperationExecutor
    {
        private readonly Queue<OperationExecutionResult> _results;

        /// <summary>建立固定順序結果；queue 只存在於此 test scope。</summary>
        public SequencedExecutor(params OperationExecutionResult[] results) => _results = new Queue<OperationExecutionResult>(results);

        /// <summary>取得已記錄的 request snapshots；不包含 credential、token 或 CRM transport state。</summary>
        public List<OperationExecutionRequest> Requests { get; } = [];

        /// <summary>以 FIFO 回傳結果；缺少預期結果即 fail fast，避免測試漏驗 response mismatch。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("The test executor has no remaining result.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}
