// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MemberInfoAuthorizationAssignmentData8Tests.cs
// 用途：以 RED 契約驗證 MemberInfo assignment evidence 的 subject 參數必須在 Data8 router／lease 之前失敗關閉。
//
// 測試替身不建立 CRM、HTTP、connector pool、lease、Session、cache、timer 或背景工作。它只記錄 router Resolve
// 次數，故可精確證明空白 subject 不能藉由 profile 或前一個 request 的 client 進入 Data8 資源生命週期。
// ============================================================================

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 守護 MemberInfo 指派證據 operation 的 Data8 pre-admission 邊界。
/// 此類別只覆蓋固定 subject GUID 的零 I/O 拒絕；它不執行 CE 查詢、不接線 ChurchReport consumer，也不會把
/// profile、workload 或失敗輸入保存在 static state。後續 query contract 仍必須以獨立測試驗證 TopCount 513、
/// duplicate、paging、日期與資源釋放語意。
/// </summary>
public sealed class MemberInfoAuthorizationAssignmentData8Tests
{
    /// <summary>
    /// 保護 Church-wide job title 在 direct contact retrieve 後立即停止，絕不查詢 Shepherd list。
    /// 故障注入是 service 若收到 RetrieveMultiple 即丟出例外；決定性斷言是 ChurchWide mode、空 allowlist
    /// 與零 list-query 呼叫。這確保較寬的伺服器職務不會混入較窄或另一位使用者的 list evidence。
    /// </summary>
    [Theory]
    [InlineData("牧師傳道")]
    [InlineData("牧養主任")]
    [InlineData("檢視全教會照片資訊")]
    public void Execute_churchwide_subject_does_not_query_assigned_lists(string churchWideJobTitle)
    {
        var subjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var service = new RecordingOrganizationService(
            new Entity("contact")
            {
                Id = subjectId,
                ["contactid"] = subjectId,
                ["new_church_jobtitle"] = churchWideJobTitle
            },
            page: null);

        var response = Package02Data8MemberInfoAuthorizationAssignmentOperations.Execute(
            service,
            CreateAssignmentOperation(subjectId),
            "9.1",
            CancellationToken.None);

        response.MemberInfoAuthorizationAssignmentEvidence.Should().NotBeNull();
        response.MemberInfoAuthorizationAssignmentEvidence!.SubjectContactId.Should().Be(subjectId);
        response.MemberInfoAuthorizationAssignmentEvidence.AccessMode
            .Should().Be(MemberInfoAuthorizationAssignmentAccessMode.ChurchWide);
        response.MemberInfoAuthorizationAssignmentEvidence.AssignedListIds.Should().BeEmpty();
        service.RetrieveCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 Shepherd subject 只會使用固定六欄位 OR query，且以 TopCount 513 偵測 overflow。
    /// 故障注入為 service 只回傳一筆完整 row；決定性斷言是 exactly-one assigned evidence、固定 list projection、
    /// active/purpose/app-named filters、六個 OR condition 與升冪主鍵排序。替身不建立連線、lease 或 session，
    /// 因此可隔離驗證 CRM query shape 而不接觸 CE。
    /// </summary>
    [Fact]
    public void Execute_assigned_subject_uses_the_fixed_bounded_six_lookup_query()
    {
        var subjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var listId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var list = new Entity("list")
        {
            Id = listId,
            ["listid"] = listId,
            ["statecode"] = new OptionSetValue(0),
            ["purpose"] = "小組名單",
            ["new_app_named"] = true,
            ["new_contact_list_vice_family_leader"] = new EntityReference("contact", subjectId)
        };
        var service = new RecordingOrganizationService(
            new Entity("contact")
            {
                Id = subjectId,
                ["contactid"] = subjectId,
                ["new_church_jobtitle"] = "一般成員"
            },
            new EntityCollection(new List<Entity> { list }));

        var response = Package02Data8MemberInfoAuthorizationAssignmentOperations.Execute(
            service,
            CreateAssignmentOperation(subjectId),
            "9.1",
            CancellationToken.None);

        response.MemberInfoAuthorizationAssignmentEvidence!.AccessMode
            .Should().Be(MemberInfoAuthorizationAssignmentAccessMode.AssignedLists);
        response.MemberInfoAuthorizationAssignmentEvidence.AssignedListIds.Should().Equal(listId);
        service.RetrieveMultipleCount.Should().Be(1);
        var query = service.LastQuery.Should().BeOfType<QueryExpression>().Subject;
        query.EntityName.Should().Be("list");
        query.TopCount.Should().Be(513);
        query.PageInfo.Count.Should().Be(0);
        query.PageInfo.PageNumber.Should().Be(0);
        query.PageInfo.PagingCookie.Should().BeNull();
        query.ColumnSet.Columns.Should().Contain(new[]
        {
            "listid",
            "statecode",
            "purpose",
            "new_app_named",
            "new_happy_start_date",
            "new_happy_end_date",
            "new_contact_list_vice_family_leader",
            "new_contact_family_leader_list",
            "new_contact_co_race_leager_list",
            "new_contact_race_leager_list",
            "new_contact_list_arealeader",
            "new_contact_list_co_arealeader"
        });
        query.Criteria.Conditions.Should().HaveCount(3);
        var purposeCondition = query.Criteria.Conditions
            .Single(condition => string.Equals(condition.AttributeName, "purpose", StringComparison.Ordinal));
        purposeCondition.Operator.Should().Be(ConditionOperator.Equal);
        purposeCondition.Values.Should().ContainSingle().Which.Should().Be("小組名單");
        query.Criteria.Filters.Should().ContainSingle().Which.Conditions.Should().HaveCount(6);
        query.Criteria.Filters.Single().FilterOperator.Should().Be(LogicalOperator.Or);
        query.Orders.Should().ContainSingle();
        query.Orders[0].AttributeName.Should().Be("listid");
        query.Orders[0].OrderType.Should().Be(OrderType.Ascending);
    }

    /// <summary>
    /// 驗證查詢只允許完整的一頁且不得超過 512 筆；只要 CRM 回報尚有下一頁或帶有
    /// paging cookie，就必須拒絕整個 evidence，不能發布目前已讀到的部分結果。
    /// </summary>
    [Fact]
    public void Execute_assigned_subject_rejects_an_incomplete_page_without_partial_evidence()
    {
        var subjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var page = new EntityCollection(new List<Entity>
        {
            CreateAssignedList(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), subjectId)
        })
        {
            MoreRecords = true,
            PagingCookie = "opaque-cookie"
        };
        var service = CreateShepherdService(subjectId, page);

        var act = () => Package02Data8MemberInfoAuthorizationAssignmentOperations.Execute(
            service,
            CreateAssignmentOperation(subjectId),
            "9.1",
            CancellationToken.None);

        act.Should().Throw<InvalidOperationException>();
        service.RetrieveMultipleCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證相同 list GUID 出現兩次時 fail closed。重複資料不能被去重後悄悄發布，
    /// 否則上游的 query／schema 漂移會被誤當成已證明的授權範圍。
    /// </summary>
    [Fact]
    public void Execute_assigned_subject_rejects_duplicate_list_ids()
    {
        var subjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var listId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var page = new EntityCollection(new List<Entity>
        {
            CreateAssignedList(listId, subjectId),
            CreateAssignedList(listId, subjectId)
        });
        var service = CreateShepherdService(subjectId, page);

        var act = () => Package02Data8MemberInfoAuthorizationAssignmentOperations.Execute(
            service,
            CreateAssignmentOperation(subjectId),
            "9.1",
            CancellationToken.None);

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 驗證 lookup logical name 或型別不正確時不會把任意 EntityReference 當成成員授權。
    /// 此故障注入模擬 CRM 欄位投影漂移，決定性結果是拒絕而不是猜測或 fallback。
    /// </summary>
    [Fact]
    public void Execute_assigned_subject_rejects_a_malformed_assignment_lookup()
    {
        var subjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var malformed = CreateAssignedList(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            subjectId);
        malformed["new_contact_list_vice_family_leader"] =
            new EntityReference("systemuser", Guid.Parse("99999999-8888-7777-6666-555555555555"));
        var service = CreateShepherdService(
            subjectId,
            new EntityCollection(new List<Entity> { malformed }));

        var act = () => Package02Data8MemberInfoAuthorizationAssignmentOperations.Execute(
            service,
            CreateAssignmentOperation(subjectId),
            "9.1",
            CancellationToken.None);

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 驗證取消發生在 direct Retrieve 完成後、list query 前時，仍會停止後續 CRM I/O，
    /// 並將取消原樣交給呼叫端；不得以空 evidence 或 legacy fallback 取代取消。
    /// </summary>
    [Fact]
    public void Execute_stops_before_assigned_list_query_when_cancelled_after_subject_retrieve()
    {
        var subjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        using var cancellation = new CancellationTokenSource();
        var service = new RecordingOrganizationService(
            new Entity("contact")
            {
                Id = subjectId,
                ["contactid"] = subjectId,
                ["new_church_jobtitle"] = "一般會友"
            },
            new EntityCollection(),
            cancellation.Cancel);

        var act = () => Package02Data8MemberInfoAuthorizationAssignmentOperations.Execute(
            service,
            CreateAssignmentOperation(subjectId),
            "9.1",
            cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        service.RetrieveCount.Should().Be(1);
        service.RetrieveMultipleCount.Should().Be(0);
    }

    /// <summary>
    /// 保護空白 subject GUID 必須在 router I/O 前回傳固定 invalid-parameters 分類。
    /// 故障注入為 <see cref="Guid.Empty"/>；決定性斷言要求 router 的 Resolve 次數維持零，避免無效 subject
    /// 配置、污染或保留 Data8 client/session，亦不得退回 generic CRM query 或 legacy ListManager。
    /// </summary>
    [Fact]
    public async Task Empty_subject_is_rejected_before_router_io()
    {
        var router = new RecordingRouter();
        var executor = new Data8ProfileOperationExecutor(new FixedProfileResolver(CreateProfile()), router);
        var request = new OperationExecutionRequest
        {
            ProfileAlias = "memberinfo-assignment-profile",
            CapabilityOperationId = OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject,
            WorkloadSubjectId = "memberinfo-assignment-workload",
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["subjectContactId"] = Guid.Empty
            }
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("operation.invalid-parameters");
        router.ResolveCount.Should().Be(0);
    }

    /// <summary>
    /// 建立僅供本測試使用的 deployment-resolved Data8 profile snapshot。它不含 credential 實值、client、cookie、
    /// Session 或可變設定；executor 僅能用此不可變值比對目前 request，無效 input 路徑不得把它交給 router。
    /// </summary>
    /// <returns>具有 CE 9.1、單一 generation 與有限 timeout 的固定 profile。</returns>
    private static ResolvedProfile CreateProfile()
        => new(
            "memberinfo-assignment-profile",
            "memberinfo-assignment-organization",
            Guid.Parse("3a2b1c0d-1111-2222-3333-444444444444"),
            CeVersion.Ce91,
            ConnectorKind.Data8,
            "memberinfo-assignment-credential-reference",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.Zero),
            GenerationId: 1);

    /// <summary>
    /// 建立唯一接受 subjectContactId 的固定 connector operation；測試不提供 caller query、list、Owner、profile
    /// selector 或 credential，確保所有 CRM query authority 仍由 production helper/registry 擁有。
    /// </summary>
    /// <param name="subjectContactId">模擬已驗證 request scope 傳入的 subject GUID。</param>
    /// <returns>供純記憶體 IOrganizationService 替身執行的封閉 operation。</returns>
    private static ConnectorOperation CreateAssignmentOperation(Guid subjectContactId)
        => new()
        {
            OperationId = OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject,
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5),
            WorkloadSubjectId = "memberinfo-assignment-test",
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["subjectContactId"] = subjectContactId
            }
        };

    /// <summary>
    /// 建立一筆符合固定 query projection 的測試 list。資料只存在於目前測試 instance，
    /// 不會連線 CRM 或保存跨測試的 authorization state。
    /// </summary>
    private static Entity CreateAssignedList(Guid listId, Guid subjectContactId)
        => new("list")
        {
            Id = listId,
            ["listid"] = listId,
            ["statecode"] = new OptionSetValue(0),
            ["purpose"] = "撠??",
            ["new_app_named"] = true,
            ["new_contact_list_vice_family_leader"] = new EntityReference("contact", subjectContactId)
        };

    /// <summary>
    /// 建立 Shepherd 測試 service，讓固定 subject retrieve 與單頁 list query 的 I/O
    /// 計數保持 request-local，並可注入取消時點。
    /// </summary>
    private static RecordingOrganizationService CreateShepherdService(
        Guid subjectId,
        EntityCollection page,
        Action? afterRetrieve = null)
        => new(
            new Entity("contact")
            {
                Id = subjectId,
                ["contactid"] = subjectId,
                ["new_church_jobtitle"] = "一般會友"
            },
            page,
            afterRetrieve);

    /// <summary>
    /// 將 profile alias 解析為單一 immutable snapshot 的 request-safe 替身。它不保留 caller parameter、
    /// credential、Session 或上一個 request 的 routing state；alias mismatch 只回傳固定分類。
    /// </summary>
    private sealed class FixedProfileResolver : IProfileResolver
    {
        private readonly ResolvedProfile _profile;

        /// <summary>
        /// 初始化唯一不可變 profile，避免測試透過共享環境或可變組態取得跨 profile 路由權限。
        /// </summary>
        /// <param name="profile">測試擁有且不可為 null 的 profile snapshot。</param>
        public FixedProfileResolver(ResolvedProfile profile)
            => _profile = profile ?? throw new ArgumentNullException(nameof(profile));

        /// <summary>
        /// 只解析精確 alias；失敗時不輸出 endpoint、credential 或其他 profile 的資料。
        /// </summary>
        public bool TryResolve(string profileAlias, out ResolvedProfile? profile, out string error)
        {
            if (string.Equals(profileAlias, _profile.ProfileAlias, StringComparison.Ordinal))
            {
                profile = _profile;
                error = string.Empty;
                return true;
            }

            profile = null;
            error = "profile-not-found";
            return false;
        }
    }

    /// <summary>
    /// 純記憶體 CRM service 替身，只允許本測試需要的 Retrieve/RetrieveMultiple。
    /// 所有 mutation API 都立即丟出例外；這確保 read-boundary 測試無法意外建立、修改、指派、刪除或關聯任何
    /// CRM 資料。instance-local counter/query 會在測試結束後自然釋放，不保存跨使用者、跨 profile 的 session 或資源。
    /// </summary>
    private sealed class RecordingOrganizationService : IOrganizationService
    {
        private readonly Entity _subject;
        private readonly EntityCollection? _page;

        /// <summary>
        /// 建立可回傳固定 subject/page 的 test-local service。
        /// </summary>
        /// <param name="subject">direct retrieve 應取得的完整 subject entity。</param>
        /// <param name="page">assigned-list query 應取得的單一 page；null 表示該測試禁止 list query。</param>
        public RecordingOrganizationService(
            Entity subject,
            EntityCollection? page,
            Action? afterRetrieve = null)
        {
            _subject = subject ?? throw new ArgumentNullException(nameof(subject));
            _page = page;
            _afterRetrieve = afterRetrieve;
        }

        private readonly Action? _afterRetrieve;

        /// <summary>取得 direct retrieve 次數。</summary>
        public int RetrieveCount { get; private set; }

        /// <summary>取得 list query 次數。</summary>
        public int RetrieveMultipleCount { get; private set; }

        /// <summary>取得最後一個固定 list query，供欄位/條件/上限 assertion 使用。</summary>
        public QueryBase? LastQuery { get; private set; }

        /// <summary>回傳預先配置的 subject；不執行網路 I/O。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            RetrieveCount++;
            entityName.Should().Be("contact");
            id.Should().Be(_subject.Id);
            _afterRetrieve?.Invoke();
            return _subject;
        }

        /// <summary>回傳預先配置的一頁 list data；Church-wide 測試若誤呼叫會立刻失敗。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            RetrieveMultipleCount++;
            LastQuery = query;
            return _page ?? throw new InvalidOperationException("Church-wide evidence must not query assigned lists.");
        }

        /// <summary>禁止 mutation，避免測試越過 read-only 邊界。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException("Read-boundary test does not permit Create.");

        /// <summary>禁止 mutation，避免測試越過 read-only 邊界。</summary>
        public void Update(Entity entity) => throw new NotSupportedException("Read-boundary test does not permit Update.");

        /// <summary>禁止 mutation，避免測試越過 read-only 邊界。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException("Read-boundary test does not permit Delete.");

        /// <summary>禁止 generic SDK command，避免測試新增未審核的副作用。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) =>
            throw new NotSupportedException("Read-boundary test does not permit Execute.");

        /// <summary>禁止關聯 mutation，避免測試越過 read-only 邊界。</summary>
        public void Associate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException("Read-boundary test does not permit Associate.");

        /// <summary>禁止解除關聯 mutation，避免測試越過 read-only 邊界。</summary>
        public void Disassociate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException("Read-boundary test does not permit Disassociate.");
    }

    /// <summary>
    /// 僅記錄不應發生的 router Resolve 呼叫。計數器是 instance-local 並以 <see cref="Interlocked"/> 保護，
    /// 不保存 profile、pool、lease 或 connector；任何被呼叫都立即失敗，以保護本測試的 zero-I/O 契約。
    /// </summary>
    private sealed class RecordingRouter : IConnectorRouter
    {
        private int _resolveCount;

        /// <summary>取得目前替身 instance 的 router Resolve 次數。</summary>
        public int ResolveCount => Volatile.Read(ref _resolveCount);

        /// <summary>
        /// 記錄不合法輸入意外進入 router 的事件並立即拋出。這條路徑不得建立或歸還 client，避免測試掩蓋
        /// 真實 pool/session cleanup 缺口。
        /// </summary>
        public IConnectorPool Resolve(ResolvedProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            Interlocked.Increment(ref _resolveCount);
            throw new InvalidOperationException("Empty assignment evidence subject must not resolve a connector pool.");
        }
    }
}
