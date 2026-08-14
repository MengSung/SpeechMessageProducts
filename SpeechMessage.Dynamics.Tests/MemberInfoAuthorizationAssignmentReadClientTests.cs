// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MemberInfoAuthorizationAssignmentReadClientTests.cs
// 用途：保護 MemberInfo 指派證據 ProductClient 的固定 request、封閉 response 與跨使用者隔離契約。
//
// 測試不建立 CRM、HTTP、connector、lease、快取、計時器或背景工作。RecordingExecutor 僅保存目前測試
// 呼叫的 request/token，讓測試能決定性驗證 ProductClient 不接受呼叫端 query、Owner、憑證或其他
// 路由權限；正式 transport 與資源釋放仍由 IDynamicsOperationExecutor 的既有 owner 負責。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 MemberInfo 指派證據讀取 client 只發布 immutable、subject-bound 的 DTO。
/// 故障注入涵蓋空白 subject 與 response collection 可變來源；決定性斷言是無效輸入零 executor I/O，
/// 成功路徑只使用固定 operation/單一 subject parameter，且呼叫端無法取得可下轉為陣列的 allowlist。
/// 這可防止 singleton client 以跨請求快取、上一位使用者的 profile 或可變集合污染下一個 request。
/// </summary>
public sealed class MemberInfoAuthorizationAssignmentReadClientTests
{
    /// <summary>
    /// 保護正常 Shepherd 指派 evidence 只能以 deployment-owned routing 與 server subject dispatch。
    /// 故障注入是外部仍可變的 source list；決定性斷言是 ProductClient 發出的 request 僅有
    /// <c>subjectContactId</c>，並發布獨立、不可寫入且不可轉為陣列的 list snapshot。
    /// </summary>
    [Fact]
    public async Task Resolve_async_sends_only_the_fixed_subject_parameter_and_publishes_an_immutable_snapshot()
    {
        var subjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var firstListId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var sourceIds = new List<Guid> { firstListId };
        var response = OperationResponseData.ForMemberInfoAuthorizationAssignmentEvidence(
            OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject,
            "9.1",
            new MemberInfoAuthorizationAssignmentEvidenceResponseData(
                subjectId,
                MemberInfoAuthorizationAssignmentAccessMode.AssignedLists,
                sourceIds));
        sourceIds.Clear();
        sourceIds.Add(Guid.Parse("99999999-8888-7777-6666-555555555555"));
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(response));
        var client = CreateClient(executor);

        var result = await client.ResolveBySubjectAsync(new MemberInfoAuthorizationAssignmentReadRequest
        {
            ProfileAlias = "profile-A",
            WorkloadSubjectId = "churchreport-memberinfo",
            SubjectContactId = subjectId
        });

        result.SubjectContactId.Should().Be(subjectId);
        result.AccessMode.Should().Be(MemberInfoAuthorizationAssignmentAccessMode.AssignedLists);
        result.AssignedListIds.Should().Equal(firstListId);
        result.AssignedListIds.Should().NotBeOfType<Guid[]>();
        var writableIds = result.AssignedListIds.Should().BeAssignableTo<IList<Guid>>().Subject;
        var mutate = () => writableIds.Add(Guid.NewGuid());
        mutate.Should().Throw<NotSupportedException>();

        executor.CallCount.Should().Be(1);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.CapabilityOperationId.Should().Be(OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject);
        executor.LastRequest.Parameters.Should().ContainSingle();
        executor.LastRequest.Parameters["subjectContactId"].Should().Be(subjectId);
        executor.LastRequest.IdempotencyKey.Should().BeNull();
    }

    /// <summary>
    /// 保護空白 subject 在組合任何 executor request 前即被拒絕。
    /// 故障注入為 <see cref="Guid.Empty"/>；決定性斷言為固定引數錯誤與零 executor 呼叫，
    /// 因而不會在無有效 request identity 時配置 profile、connector、lease 或 CRM session。
    /// </summary>
    [Fact]
    public async Task Resolve_async_rejects_an_empty_subject_before_executor_io()
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var client = CreateClient(executor);

        var act = () => client.ResolveBySubjectAsync(new MemberInfoAuthorizationAssignmentReadRequest
        {
            ProfileAlias = "profile-A",
            WorkloadSubjectId = "churchreport-memberinfo",
            SubjectContactId = Guid.Empty
        });

        await act.Should().ThrowAsync<ArgumentException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 operation discriminator 或 CE 版本不正確時，ProductClient 不會把其他能力的
    /// response 當成 MemberInfo 授權證據發布。
    /// </summary>
    [Fact]
    public async Task Resolve_async_rejects_a_wrong_operation_response_branch()
    {
        var executor = new RecordingExecutor(_ =>
            OperationExecutionResult.Success(
                OperationResponseData.Unsupported("some.other.operation", "9.1")));
        var client = CreateClient(executor);

        var act = () => client.ResolveBySubjectAsync(new MemberInfoAuthorizationAssignmentReadRequest
        {
            ProfileAlias = "profile-A",
            WorkloadSubjectId = "churchreport-memberinfo",
            SubjectContactId = Guid.Parse("11111111-2222-3333-4444-555555555555")
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        executor.CallCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 response subject 必須與 request subject 精確相等；不允許 caller 以合法 GUID
    /// 取得另一個 subject 的 assignment evidence。
    /// </summary>
    [Fact]
    public async Task Resolve_async_rejects_a_response_subject_mismatch()
    {
        var requestSubject = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var responseSubject = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var executor = new RecordingExecutor(_ => CreateResult(
            responseSubject,
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));
        var client = CreateClient(executor);

        var act = () => client.ResolveBySubjectAsync(new MemberInfoAuthorizationAssignmentReadRequest
        {
            ProfileAlias = "profile-A",
            WorkloadSubjectId = "churchreport-memberinfo",
            SubjectContactId = requestSubject
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 驗證 request cancellation token 原樣傳給 executor；ProductClient 不註冊、保存或
    /// 替換 token，也不在 typed dispatch 後自行 retry/fallback。
    /// </summary>
    [Fact]
    public async Task Resolve_async_forwards_the_request_cancellation_token_exactly()
    {
        var executor = new RecordingExecutor(_ => CreateResult(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));
        var client = CreateClient(executor);
        using var cancellation = new CancellationTokenSource();

        await client.ResolveBySubjectAsync(new MemberInfoAuthorizationAssignmentReadRequest
        {
            ProfileAlias = "profile-A",
            WorkloadSubjectId = "churchreport-memberinfo",
            SubjectContactId = Guid.Parse("11111111-2222-3333-4444-555555555555")
        }, cancellation.Token);

        executor.LastCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 保護 composition root 將 client 註冊為真正無 request state 的 singleton。
    /// 故障注入是空白 service collection；決定性斷言是只有介面到具體 stateless client 的 singleton descriptor，
    /// 不會註冊 Session、HttpContext、CRM connector、lease 或需要 request disposal 的中介包裝。
    /// </summary>
    [Fact]
    public void Registration_uses_the_stateless_singleton_assignment_read_client()
    {
        var services = new ServiceCollection();

        services.AddSpeechMessageDynamicsMemberInfoAuthorizationAssignmentReads();

        var descriptor = services.Should().ContainSingle(service =>
            service.ServiceType == typeof(IMemberInfoAuthorizationAssignmentReadClient)).Subject;
        descriptor.ImplementationType.Should().Be(typeof(MemberInfoAuthorizationAssignmentReadClient));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    /// <summary>
    /// 保護同一 singleton client 的 A/B 非同步交錯不會交換 profile、subject、list 或 result reference。
    /// 故障注入是先完成 B、再完成 A 的 TCS 排程；決定性斷言是兩個結果保留各自的 subject/list，沒有 static last
    /// response、shared mutable collection、cache 或 closure 將一方 evidence 發布給另一方。
    /// </summary>
    [Fact]
    public async Task Resolve_async_keeps_interleaved_a_and_b_assignment_evidence_request_local()
    {
        var subjectA = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var subjectB = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var listA = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var listB = Guid.Parse("99999999-aaaa-bbbb-cccc-dddddddddddd");
        var executor = new InterleavingExecutor();
        var client = CreateClient(executor);

        var aTask = client.ResolveBySubjectAsync(new MemberInfoAuthorizationAssignmentReadRequest
        {
            ProfileAlias = "profile-A",
            WorkloadSubjectId = "workload-A",
            SubjectContactId = subjectA
        });
        var bTask = client.ResolveBySubjectAsync(new MemberInfoAuthorizationAssignmentReadRequest
        {
            ProfileAlias = "profile-B",
            WorkloadSubjectId = "workload-B",
            SubjectContactId = subjectB
        });

        executor.CompleteB(CreateResult(subjectB, listB));
        var resultB = await bTask;
        executor.CompleteA(CreateResult(subjectA, listA));
        var resultA = await aTask;

        resultA.SubjectContactId.Should().Be(subjectA);
        resultA.AssignedListIds.Should().Equal(listA);
        resultB.SubjectContactId.Should().Be(subjectB);
        resultB.AssignedListIds.Should().Equal(listB);
        resultA.AssignedListIds.Should().NotBeSameAs(resultB.AssignedListIds);
        resultA.AssignedListIds.Should().NotContain(listB);
        resultB.AssignedListIds.Should().NotContain(listA);
    }

    /// <summary>
    /// 建立僅有 executor/logger 相依的無狀態 client。
    /// NullLogger 不記錄 subject、profile、list ID 或 upstream payload；測試替身也不擁有需 Dispose 的資源。
    /// </summary>
    private static MemberInfoAuthorizationAssignmentReadClient CreateClient(IDynamicsOperationExecutor executor)
        => new(executor, NullLogger<MemberInfoAuthorizationAssignmentReadClient>.Instance);

    /// <summary>
    /// 建立與指定 subject/list 精確相符的封閉 assignment envelope；不含 CRM entity、profile、endpoint、憑證或 token。
    /// </summary>
    /// <param name="subjectContactId">A 或 B request 的唯一 subject GUID。</param>
    /// <param name="listId">該 subject 的唯一 allowlist GUID。</param>
    /// <returns>可安全供 interleaving fake 完成的 operation result。</returns>
    private static OperationExecutionResult CreateResult(Guid subjectContactId, Guid listId)
        => OperationExecutionResult.Success(
            OperationResponseData.ForMemberInfoAuthorizationAssignmentEvidence(
                OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject,
                "9.1",
                new MemberInfoAuthorizationAssignmentEvidenceResponseData(
                    subjectContactId,
                    MemberInfoAuthorizationAssignmentAccessMode.AssignedLists,
                    new[] { listId })));

    /// <summary>
    /// 以每個測試實例私有的 handler 模擬 executor 回應。
    /// 此替身不重試、不保存跨測試／跨使用者的 request，且不建立任何外部連線；CallCount 是零 I/O admission
    /// contract 的唯一觀測值，正式 connector/lease 的 disposal 仍不會由此替身模擬或遮蔽。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 建立只屬於目前測試的同步回應投影。
        /// </summary>
        /// <param name="handler">接收 request 後回傳封閉 operation result 的函式。</param>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
            => _handler = handler ?? throw new ArgumentNullException(nameof(handler));

        /// <summary>
        /// 取得此替身被呼叫的次數；不代表真實 CRM I/O，僅用於驗證 ProductClient admission 順序。
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 取得最後一次 request 的測試快照；此值只在單一 test instance 生命週期內存在。
        /// </summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>記錄本次 request 傳入的取消 token；測試替身不取得其註冊或所有權。</summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// 以傳入 request 立即產生回應，保留 cancellation token 的所有權在呼叫端／正式 executor。
        /// </summary>
        /// <param name="request">ProductClient 建立的 immutable operation request。</param>
        /// <param name="cancellationToken">由測試呼叫端傳遞、不得由替身註冊或保存的 token。</param>
        /// <returns>已完成的封閉 operation result。</returns>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_handler(request));
        }
    }

    /// <summary>
    /// 用兩個 request-local completion gate 製造可重現的 A/B 交錯。
    /// gate 僅在單一 test instance 存活，沒有 static state、timer、cancellation registration 或外部資源；它讓
    /// B 的 continuation 先於 A 執行，以偵測 client 若錯誤保存「最後 response」或可變集合時的跨使用者洩漏。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly TaskCompletionSource<OperationExecutionResult> _aCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _bCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 依固定 profile 將 request 導向其專屬 gate；未知 profile 一律拒絕，不猜測或共用另一方 response。
        /// </summary>
        /// <param name="request">ProductClient 建立的 immutable request。</param>
        /// <param name="cancellationToken">呼叫端 token；替身不保存或註冊它。</param>
        /// <returns>對應 A 或 B 的 pending result task。</returns>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => request.ProfileAlias switch
            {
                "profile-A" => _aCompletion.Task,
                "profile-B" => _bCompletion.Task,
                _ => throw new InvalidOperationException("The test executor received an unknown profile.")
            };

        /// <summary>
        /// 只完成 A 的 request-local gate，不能影響 B。
        /// </summary>
        /// <param name="result">A 的封閉 operation result。</param>
        public void CompleteA(OperationExecutionResult result)
            => _aCompletion.TrySetResult(result).Should().BeTrue();

        /// <summary>
        /// 只完成 B 的 request-local gate，不能影響 A。
        /// </summary>
        /// <param name="result">B 的封閉 operation result。</param>
        public void CompleteB(OperationExecutionResult result)
            => _bCompletion.TrySetResult(result).Should().BeTrue();
    }
}
