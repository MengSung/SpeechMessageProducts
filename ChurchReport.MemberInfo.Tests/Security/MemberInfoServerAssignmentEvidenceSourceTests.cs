// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Security/MemberInfoServerAssignmentEvidenceSourceTests.cs
// 用途：保護 P7 request scope 到伺服器指派證據、再到 immutable target scope 的安全轉接。
//
// 所有替身都是單一測試執行個體、純記憶體且不建立 CRM/HTTP/connector/lease/session/cache/timer。測試藉由
// executor CallCount 證明 null scope 在任何 ProductClient I/O 前 fail closed；正式 transport 的取消、fault
// eviction 與資源釋放仍由既有 executor 及其 connector lease owner 處理。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using System.Reflection;
using Xunit;

namespace ChurchReport.Security;

/// <summary>
/// 驗證 MemberInfo server assignment source 只以 <see cref="P7GatewayRequestScope"/> 的 subject 作 identity input。
/// 故障注入涵蓋缺少 scope；成功路徑驗證 deployment routing、subject、mode 與 defensive list copy。這些測試
/// 防止 adapter 回讀 Session、ClaimsPrincipal、InMemoryContext、legacy ListManager 或 CRM entity，並避免 A/B
/// request 透過 singleton、cache 或可變 backing collection 交換 target authorization state。
/// </summary>
public sealed class MemberInfoServerAssignmentEvidenceSourceTests
{
    /// <summary>
    /// 保護有效的 assigned-list response 會精確映射為既有 request-local target scope。
    /// 故障注入為 response construction 後改寫 source list；決定性斷言是 published scope 只保留第一筆 list，
    /// executor request 使用固定 deployment routing 與 scope subject，沒有 browser locator、Owner 或 CRM SDK 欄位。
    /// </summary>
    [Fact]
    public async Task Resolve_async_maps_server_assignment_evidence_to_an_immutable_target_scope()
    {
        var subjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var allowedListId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var sourceListIds = new List<Guid> { allowedListId };
        var envelope = OperationResponseData.ForMemberInfoAuthorizationAssignmentEvidence(
            OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject,
            "9.1",
            new MemberInfoAuthorizationAssignmentEvidenceResponseData(
                subjectId,
                MemberInfoAuthorizationAssignmentAccessMode.AssignedLists,
                sourceListIds));
        sourceListIds.Clear();
        sourceListIds.Add(Guid.Parse("99999999-8888-7777-6666-555555555555"));
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(envelope));
        var source = new MemberInfoServerAssignmentEvidenceSource(
            new MemberInfoAuthorizationAssignmentReadClient(
                executor,
                NullLogger<MemberInfoAuthorizationAssignmentReadClient>.Instance),
            "profile-A",
            "churchreport-memberinfo");

        var resolution = await source.ResolveAsync(
            new P7GatewayRequestScope(subjectId, P7GatewayLoginKind.Account));

        resolution.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.None);
        resolution.Scope.Should().NotBeNull();
        resolution.Scope!.SubjectContactId.Should().Be(subjectId);
        resolution.Scope.AccessMode.Should().Be(MemberInfoTargetAccessMode.AssignedLists);
        resolution.Scope.VisibleListIds.Should().Equal(allowedListId);
        resolution.Scope.VisibleListIds.Should().NotBeOfType<Guid[]>();
        executor.CallCount.Should().Be(1);
        executor.LastRequest!.ProfileAlias.Should().Be("profile-A");
        executor.LastRequest.WorkloadSubjectId.Should().Be("churchreport-memberinfo");
        executor.LastRequest.Parameters.Should().ContainSingle();
        executor.LastRequest.Parameters["subjectContactId"].Should().Be(subjectId);
    }

    /// <summary>
    /// 保護缺少 request scope 時不得嘗試建立 ProductClient request 或 dispatch。
    /// 故障注入為 null scope；決定性斷言是既有 <see cref="MemberInfoTargetAuthorizationFailure.MissingRequestScope"/>
    /// 與 executor 零呼叫，避免缺少驗證 subject 的情況下建立 profile、connector、lease 或 CRM session。
    /// </summary>
    [Fact]
    public async Task Resolve_async_rejects_a_missing_scope_before_client_io()
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var source = new MemberInfoServerAssignmentEvidenceSource(
            new MemberInfoAuthorizationAssignmentReadClient(
                executor,
                NullLogger<MemberInfoAuthorizationAssignmentReadClient>.Instance),
            "profile-A",
            "churchreport-memberinfo");

        var resolution = await source.ResolveAsync(null);

        resolution.Scope.Should().BeNull();
        resolution.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.MissingRequestScope);
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 typed ProductClient 的 fault 只能轉成既有去識別化 SourceUnavailable，不得將例外、CRM detail 或
    /// legacy fallback 發布給 MemberInfo consumer。故障注入是立即丟出例外的 client；決定性斷言是有效 scope
    /// 仍沒有 scope 結果，failure 固定為 SourceUnavailable，且 source 不嘗試 Session/ListManager 補救。
    /// </summary>
    [Fact]
    public async Task Resolve_async_maps_client_fault_to_a_deidentified_source_unavailable_failure()
    {
        var source = new MemberInfoServerAssignmentEvidenceSource(
            new ThrowingReadClient(),
            "profile-A",
            "churchreport-memberinfo");

        var resolution = await source.ResolveAsync(
            new P7GatewayRequestScope(
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                P7GatewayLoginKind.Account));

        resolution.Scope.Should().BeNull();
        resolution.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.SourceUnavailable);
    }

    /// <summary>
    /// 驗證 request cancellation token 不會被 security adapter 吞掉或改寫成 SourceUnavailable。
    /// 模擬的 typed client 在 dispatch 後回傳取消，決定性斷言是同一個 token 與
    /// <see cref="OperationCanceledException"/> 原樣傳回，且不走 legacy fallback。
    /// </summary>
    [Fact]
    public async Task Resolve_async_preserves_typed_client_cancellation()
    {
        var client = new CancelingReadClient();
        var source = new MemberInfoServerAssignmentEvidenceSource(
            client,
            "profile-A",
            "churchreport-memberinfo");
        using var cancellation = new CancellationTokenSource();

        var act = () => source.ResolveAsync(
            new P7GatewayRequestScope(
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                P7GatewayLoginKind.Account),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        client.LastCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 保護 source 的 public/internal instance surface 不保留 static request state 或禁用的 legacy/HTTP/CRM 類型。
    /// 故障注入是 reflection 掃描所有 declared fields；決定性斷言是沒有 static field，且 instance fields 只含
    /// typed ProductClient 與兩個 routing string。這是防止未來加入 Session、ClaimsPrincipal、HttpContext、
    /// Entity、IOrganizationService、connector、lease 或 cache 後跨使用者保留狀態的回歸護欄。
    /// </summary>
    [Fact]
    public void Source_declares_no_static_or_legacy_request_state()
    {
        var fields = typeof(MemberInfoServerAssignmentEvidenceSource).GetFields(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        fields.Should().NotContain(field => field.IsStatic);
        fields.Select(field => field.FieldType).Should().BeEquivalentTo(
        [
            typeof(IMemberInfoAuthorizationAssignmentReadClient),
            typeof(string),
            typeof(string)
        ]);
    }

    /// <summary>
    /// 保護同一 source 的 A/B 非同步完成順序不會交叉發布 target scope。
    /// 故障注入是 B 的 typed response 先完成、A 後完成；決定性斷言是每個 resolution 只含自己的 subject/list，
    /// 不會因 adapter、ProductClient 或 resolver 保存 mutable last-result、Session、cache 或 shared collection 而洩漏。
    /// </summary>
    [Fact]
    public async Task Resolve_async_keeps_interleaved_subject_scopes_isolated()
    {
        var subjectA = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var subjectB = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var listA = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var listB = Guid.Parse("99999999-aaaa-bbbb-cccc-dddddddddddd");
        var executor = new InterleavingExecutor(subjectA, subjectB);
        var source = new MemberInfoServerAssignmentEvidenceSource(
            new MemberInfoAuthorizationAssignmentReadClient(
                executor,
                NullLogger<MemberInfoAuthorizationAssignmentReadClient>.Instance),
            "profile-A",
            "churchreport-memberinfo");

        var aTask = source.ResolveAsync(new P7GatewayRequestScope(subjectA, P7GatewayLoginKind.Account));
        var bTask = source.ResolveAsync(new P7GatewayRequestScope(subjectB, P7GatewayLoginKind.Line));

        executor.CompleteB(CreateAssignmentResult(subjectB, listB));
        var resultB = await bTask;
        executor.CompleteA(CreateAssignmentResult(subjectA, listA));
        var resultA = await aTask;

        resultA.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.None);
        resultB.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.None);
        resultA.Scope!.SubjectContactId.Should().Be(subjectA);
        resultA.Scope.VisibleListIds.Should().Equal(listA);
        resultB.Scope!.SubjectContactId.Should().Be(subjectB);
        resultB.Scope.VisibleListIds.Should().Equal(listB);
        resultA.Scope.VisibleListIds.Should().NotBeSameAs(resultB.Scope.VisibleListIds);
        resultA.Scope.VisibleListIds.Should().NotContain(listB);
        resultB.Scope.VisibleListIds.Should().NotContain(listA);
    }

    /// <summary>
    /// 建立指定 subject/list 的封閉 typed result，僅包含 operation discriminator、CE version、mode 與 GUID。
    /// </summary>
    /// <param name="subjectContactId">A 或 B scope 的 subject GUID。</param>
    /// <param name="listId">只屬於該 subject 的 allowlist GUID。</param>
    /// <returns>供 interleaving executor 完成的 immutable operation result。</returns>
    private static OperationExecutionResult CreateAssignmentResult(Guid subjectContactId, Guid listId)
        => OperationExecutionResult.Success(
            OperationResponseData.ForMemberInfoAuthorizationAssignmentEvidence(
                OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject,
                "9.1",
                new MemberInfoAuthorizationAssignmentEvidenceResponseData(
                    subjectContactId,
                    MemberInfoAuthorizationAssignmentAccessMode.AssignedLists,
                    new[] { listId })));

    /// <summary>
    /// 每個測試私有的 executor fake，僅觀測 ProductClient dispatch 形狀。
    /// 此替身沒有 shared static state、外部資源或 retry，不能掩蓋正式 connector 的 fault/dispose 行為；其生命週期
    /// 隨測試結束而終止，故不會保留 subject、profile 或 list evidence 給另一個測試或 request。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 建立目前測試獨有的封閉 response handler。
        /// </summary>
        /// <param name="handler">接收 operation request 並回傳已完成結果的函式。</param>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
            => _handler = handler ?? throw new ArgumentNullException(nameof(handler));

        /// <summary>
        /// 取得 ProductClient 嘗試 dispatch 的次數；用於驗證 fail-closed admission 順序。
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 取得最後一次 request 的測試範圍快照；不會跨測試保存。
        /// </summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>
        /// 同步回傳 handler 結果，並刻意不保存 cancellation token 或註冊 callback。
        /// </summary>
        /// <param name="request">ProductClient 建立的 request-local operation request。</param>
        /// <param name="cancellationToken">由正式呼叫鏈擁有的取消 token。</param>
        /// <returns>封閉的 completed operation result。</returns>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }

    /// <summary>
    /// 模擬 transport/connector failure 的無資源 typed client。
    /// 它不保存 scope、subject、token 或 exception detail；只用於驗證 source 的 fail-closed error mapping，避免測試
    /// 需要 CRM SDK、HTTP、connector pool 或 legacy Session。
    /// </summary>
    private sealed class ThrowingReadClient : IMemberInfoAuthorizationAssignmentReadClient
    {
        /// <summary>
        /// 立即產生非取消 fault；source 應把它轉為 SourceUnavailable。
        /// </summary>
        /// <param name="request">不會被保存或使用的 request。</param>
        /// <param name="cancellationToken">不會被保存或註冊的 token。</param>
        /// <returns>此方法不會回傳正常 result。</returns>
        public Task<MemberInfoAuthorizationAssignmentReadResult> ResolveBySubjectAsync(
            MemberInfoAuthorizationAssignmentReadRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated typed-client fault.");
    }

    /// <summary>
    /// 模擬 typed ProductClient 在已接收 request token 後取消。替身不建立連線、註冊 callback
    /// 或保存 request／subject；只記錄 token 的值供本測試確認 adapter 沒有替換它。
    /// </summary>
    private sealed class CancelingReadClient : IMemberInfoAuthorizationAssignmentReadClient
    {
        /// <summary>目前測試呼叫傳入、未被替身註冊或保留的取消 token 值。</summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// 固定模擬 transport cancellation，保證正式 adapter 必須保留取消語意而不是映射為來源不可用。
        /// </summary>
        /// <param name="request">僅為符合介面而接收的 request；替身不讀取或保存其資料。</param>
        /// <param name="cancellationToken">必須由 adapter 原樣轉送的呼叫端 token。</param>
        /// <returns>永遠以取消完成的工作。</returns>
        public Task<MemberInfoAuthorizationAssignmentReadResult> ResolveBySubjectAsync(
            MemberInfoAuthorizationAssignmentReadRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromException<MemberInfoAuthorizationAssignmentReadResult>(
                new OperationCanceledException(cancellationToken));
        }
    }

    /// <summary>
    /// 以兩個 request-local gate 製造 A/B response 交錯的 executor 替身。
    /// 它不建立外部連線、lease、timer、cache 或 cancellation registration；每個 gate 只接受建構時指定的 subject，
    /// 因而可偵測 adapter 是否錯把某一 request 的 operation result 或 allowlist 套用到另一個 request。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly Guid _subjectA;
        private readonly Guid _subjectB;
        private readonly TaskCompletionSource<OperationExecutionResult> _aCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _bCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 建立只允許兩個固定 subject 的交錯 executor。
        /// </summary>
        /// <param name="subjectA">第一個 request 的 server scope subject。</param>
        /// <param name="subjectB">第二個 request 的 server scope subject。</param>
        public InterleavingExecutor(Guid subjectA, Guid subjectB)
        {
            _subjectA = subjectA;
            _subjectB = subjectB;
        }

        /// <summary>
        /// 依固定 subject parameter 選擇其專屬 gate；未知或缺少 subject 不會借用另一個 pending response。
        /// </summary>
        /// <param name="request">ProductClient 建立的固定 operation request。</param>
        /// <param name="cancellationToken">呼叫端 token；替身不保存或註冊它。</param>
        /// <returns>對應 subject 的 pending result task。</returns>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!request.Parameters.TryGetValue("subjectContactId", out var value) || value is not Guid subjectId)
            {
                throw new InvalidOperationException("The test executor requires a subjectContactId.");
            }

            return subjectId switch
            {
                var valueA when valueA == _subjectA => _aCompletion.Task,
                var valueB when valueB == _subjectB => _bCompletion.Task,
                _ => throw new InvalidOperationException("The test executor received an unknown subject.")
            };
        }

        /// <summary>
        /// 只完成 A 的 request-local response gate。
        /// </summary>
        /// <param name="result">A 的封閉 result。</param>
        public void CompleteA(OperationExecutionResult result)
            => _aCompletion.TrySetResult(result).Should().BeTrue();

        /// <summary>
        /// 只完成 B 的 request-local response gate。
        /// </summary>
        /// <param name="result">B 的封閉 result。</param>
        public void CompleteB(OperationExecutionResult result)
            => _bCompletion.TrySetResult(result).Should().BeTrue();
    }
}
