// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MemberInfoPresentRecordReadClientTests.cs
// 目的：驗證 ORG-CALL-00026 的 ProductClient 只發布已授權 contact 的出席紀錄純量快照。
//
// 安全與生命週期邊界：
// - 測試只使用本機 fake executor；不建立 HTTP、CRM SDK、connector、lease、計時器或背景工作。
// - 每個測試 instance 擁有自己的可變測試資料，並在測試完成後自然釋放；不使用 static request/response 狀態。
// - A/B 隔離、取消傳遞與 immutable collection 測試將防止 singleton client 保留跨 profile 或跨 request 資料。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 保護 MemberInfo 個人出席紀錄唯讀 ProductClient 的 server-authorized、DTO-only 與 fail-closed 邊界。
/// 故障注入一律在封閉 executor response 完成，不模擬或擁有 transport；決定性斷言聚焦在精確 operation、
/// response discriminator、不可變結果與不跨 profile/workload 保留資料，避免測試本身掩蓋產品資源生命週期。
/// </summary>
public sealed class MemberInfoPresentRecordReadClientTests
{
    /// <summary>
    /// 保護成功讀取只能以唯一 ORG-CALL-00026 operation、唯一 <c>contactId</c> 字典參數與新 DTO 快照發布。
    /// 故障模型是呼叫端可能企圖藉由未受控參數、既有 response 或上游可變列滲入 boundary；決定性斷言驗證
    /// request 不帶 idempotency key、輸出列不是 wire row 參考、nullable 日期保持原值，且不公開 CRM/transport 資料。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_sends_only_authorized_contact_and_returns_a_fresh_dto_snapshot()
    {
        var contactId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var presentRecordId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        OperationExecutionRequest? observedRequest = null;
        var wireRecord = new MemberInfoPresentRecordReadRecord
        {
            PresentRecordId = presentRecordId,
            ContactFullName = "member-A",
            SundayDate = new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Unspecified),
            Sunday = true,
            SmallGroup = false,
            PrayItem = "pray-A"
        };
        var executor = new RecordingExecutor(request =>
        {
            observedRequest = request;
            return OperationExecutionResult.Success(
                OperationResponseData.ForMemberInfoPresentRecordReadRecords(
                    request.CapabilityOperationId,
                    "9.1",
                    new[] { wireRecord }));
        });
        var client = CreateClient(executor);

        var records = await client.RetrievePresentRecordsByContactAsync(
            new MemberInfoPresentRecordReadRequest
            {
                ProfileAlias = " deployment-profile-A ",
                WorkloadSubjectId = " church-report-member-info-present-read ",
                ContactId = contactId
            });

        observedRequest.Should().NotBeNull();
        observedRequest!.ProfileAlias.Should().Be("deployment-profile-A");
        observedRequest.WorkloadSubjectId.Should().Be("church-report-member-info-present-read");
        observedRequest.CapabilityOperationId.Should().Be(OperationIds.MemberInfoPresentRetrieveByContact);
        observedRequest.IdempotencyKey.Should().BeNull();
        observedRequest.Parameters.Keys.Should().BeEquivalentTo(new[] { "contactId" });
        observedRequest.Parameters["contactId"].Should().Be(contactId);
        records.Should().ContainSingle();
        records[0].PresentRecordId.Should().Be(presentRecordId);
        records[0].ContactFullName.Should().Be("member-A");
        records[0].SundayDate.Should().Be(wireRecord.SundayDate);
        records[0].Sunday.Should().BeTrue();
        records[0].SmallGroup.Should().BeFalse();
        records[0].PrayItem.Should().Be("pray-A");
        records[0].Should().NotBeSameAs(wireRecord);
    }

    /// <summary>
    /// 保護 profile、workload 與 contactId 在 executor、host、connector 或 outbound I/O 前必須完整驗證。
    /// 故障注入為空白 routing 值和空 GUID；決定性斷言是 fake executor 的呼叫數始終為零，避免 singleton 從
    /// 先前 A/B request 借用 profile、token、session 或結果，也避免無效輸入建立後續資源。
    /// </summary>
    [Theory]
    [InlineData(null, "workload-A")]
    [InlineData("", "workload-A")]
    [InlineData("   ", "workload-A")]
    [InlineData("profile-A", null)]
    [InlineData("profile-A", "")]
    [InlineData("profile-A", "   ")]
    public async Task Retrieve_async_rejects_invalid_deployment_routing_before_executor_io(
        string? profileAlias,
        string? workloadSubjectId)
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var client = CreateClient(executor);

        var act = () => client.RetrievePresentRecordsByContactAsync(new MemberInfoPresentRecordReadRequest
        {
            ProfileAlias = profileAlias!,
            WorkloadSubjectId = workloadSubjectId!,
            ContactId = Guid.NewGuid()
        });

        await act.Should().ThrowAsync<ArgumentException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 controller 已授權的 contact locator 仍不得為空 GUID。故障注入將預設 GUID 送入 request；決定性斷言是
    /// ProductClient 在任何 dispatch 前拒絕它，不能以空值改查全部資料、猜選前一筆資料或交由下游 fallback。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_an_empty_authorized_contact_id_before_executor_io()
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var client = CreateClient(executor);

        var act = () => client.RetrievePresentRecordsByContactAsync(new MemberInfoPresentRecordReadRequest
        {
            ProfileAlias = "profile-A",
            WorkloadSubjectId = "workload-A",
            ContactId = Guid.Empty
        });

        await act.Should().ThrowAsync<ArgumentException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 failed execution、錯誤 operation、錯誤 CE version、錯誤 discriminator 與缺失 branch 都 fail closed。
    /// 每種故障只使用純記憶體 envelope，決定性斷言是方法拋出而非回傳 partial collection、重試、改走 legacy SDK
    /// 或保存另一個 request 的資料，讓 enabled typed path 保持單一路徑與可預測清理責任。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_invalid_execution_envelopes_before_mapping()
    {
        var record = CreateWireRecord("valid");
        var invalidExecutors = new IDynamicsOperationExecutor[]
        {
            new RecordingExecutor(_ => OperationExecutionResult.Failure("failed", "safe failure")),
            new RecordingExecutor(_ => OperationExecutionResult.Success(
                OperationResponseData.ForMemberInfoPresentRecordReadRecords(
                    "wrong.operation", "9.1", new[] { record }))),
            new RecordingExecutor(_ => OperationExecutionResult.Success(
                OperationResponseData.ForMemberInfoPresentRecordReadRecords(
                    OperationIds.MemberInfoPresentRetrieveByContact, "8.2", new[] { record }))),
            new RecordingExecutor(_ => OperationExecutionResult.Success(
                OperationResponseData.ForPackage01FeeRecords(
                    OperationIds.MemberInfoPresentRetrieveByContact, "9.1", Array.Empty<Package01FeeRecord>()))),
            new RecordingExecutor(_ => OperationExecutionResult.Success(null))
        };

        foreach (var executor in invalidExecutors)
        {
            var client = CreateClient(executor);
            var act = () => client.RetrievePresentRecordsByContactAsync(CreateRequest("profile-A", "workload-A"));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    /// <summary>
    /// 保護 envelope 來源集合在建立後被另一段程式修改時，ProductClient 發布的 DTO collection 仍是當次 response
    /// 快照，且呼叫端不能向其中加入 B 的列。故障注入先建立 A envelope 再清空來源並放入 B；決定性斷言同時檢查
    /// A marker 與 writable view 拋出例外，防止結果 collection 成為跨 session/profile 的可變共享狀態。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_defensively_copies_source_rows_and_publishes_a_non_array_read_only_collection()
    {
        var sourceRows = new List<MemberInfoPresentRecordReadRecord> { CreateWireRecord("A") };
        var response = OperationExecutionResult.Success(OperationResponseData.ForMemberInfoPresentRecordReadRecords(
            OperationIds.MemberInfoPresentRetrieveByContact,
            "9.1",
            sourceRows));
        sourceRows.Clear();
        sourceRows.Add(CreateWireRecord("B"));
        var client = CreateClient(new RecordingExecutor(_ => response));

        var records = await client.RetrievePresentRecordsByContactAsync(CreateRequest("profile-A", "workload-A"));

        records.Should().ContainSingle();
        records[0].ContactFullName.Should().Be("member-A");
        records.Should().NotBeOfType<MemberInfoPresentRecordReadDto[]>();
        var writableView = records.Should().BeAssignableTo<IList<MemberInfoPresentRecordReadDto>>().Subject;
        var mutate = () => writableView.Add(new MemberInfoPresentRecordReadDto
        {
            PresentRecordId = Guid.NewGuid(),
            Sunday = false,
            SmallGroup = false
        });
        mutate.Should().Throw<NotSupportedException>();
        records.Should().ContainSingle(record => record.ContactFullName == "member-A");
    }

    /// <summary>
    /// 保護傳入的取消 token 是 executor 唯一應接收的取消訊號。fake 不註冊 callback，故不延長 CTS 壽命；
    /// 決定性斷言是 reference-equivalent token 原樣送達，避免 client 偷建 linked token、吞掉取消或用 retry
    /// 延長已取消 request，真實 transport/lease 的 fault eviction 和 cleanup 仍由 executor owner 完成。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_forwards_the_supplied_cancellation_token_unchanged()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(request => CreateSuccessfulResult(request.CapabilityOperationId, "token"));
        var client = CreateClient(executor);

        await client.RetrievePresentRecordsByContactAsync(
            CreateRequest("profile-A", "workload-A"),
            cancellationSource.Token);

        executor.LastCancellationToken.Should().Be(cancellationSource.Token);
    }

    /// <summary>
    /// 保護 singleton client 在 A/B 非同步交錯完成時只使用各自的 profile/workload response。故障注入刻意先完成 B
    /// 再完成 A；決定性斷言比較 marker、DTO instance 與 collection instance，能偵測 last-result/static cache/
    /// captured token 等跨 request retained state，且不必建立 connector 或 session。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_keeps_interleaved_a_and_b_profiles_tokens_and_results_isolated()
    {
        using var aCancellationSource = new CancellationTokenSource();
        using var bCancellationSource = new CancellationTokenSource();
        var executor = new InterleavingExecutor();
        var client = CreateClient(executor);

        var aTask = client.RetrievePresentRecordsByContactAsync(
            CreateRequest("profile-A", "workload-A"),
            aCancellationSource.Token);
        var bTask = client.RetrievePresentRecordsByContactAsync(
            CreateRequest("profile-B", "workload-B"),
            bCancellationSource.Token);

        executor.CompleteB(CreateSuccessfulResult(OperationIds.MemberInfoPresentRetrieveByContact, "B"));
        var bRecords = await bTask;
        executor.CompleteA(CreateSuccessfulResult(OperationIds.MemberInfoPresentRetrieveByContact, "A"));
        var aRecords = await aTask;

        executor.ACancellationToken.Should().Be(aCancellationSource.Token);
        executor.BCancellationToken.Should().Be(bCancellationSource.Token);
        aRecords.Should().ContainSingle().Which.ContactFullName.Should().Be("member-A");
        bRecords.Should().ContainSingle().Which.ContactFullName.Should().Be("member-B");
        aRecords.Should().NotBeSameAs(bRecords);
        aRecords[0].Should().NotBeSameAs(bRecords[0]);
        aRecords.Should().NotContain(record => record.ContactFullName == "member-B");
        bRecords.Should().NotContain(record => record.ContactFullName == "member-A");
    }

    /// <summary>
    /// 保護 composition root 為無 request state 的獨立 present-record capability 註冊 singleton，而不是重用
    /// contact-profile write aggregate。此測試不建 service provider/transport；決定性 descriptor 斷言確保
    /// disabled-by-default rollout 可以只取得此 read boundary，沒有意外 mutation surface 或資源 owner。
    /// </summary>
    [Fact]
    public void Present_record_read_registration_uses_an_independent_stateless_singleton_client()
    {
        var services = new ServiceCollection();

        services.AddSpeechMessageDynamicsMemberInfoPresentRecordReads();

        var descriptor = services.Should().ContainSingle(service =>
            service.ServiceType == typeof(IMemberInfoPresentRecordReadClient)).Subject;
        descriptor.ImplementationType.Should().Be(typeof(MemberInfoPresentRecordReadClient));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Should().NotContain(service =>
            service.ServiceType == typeof(IPackage02ContactProfileClient));
    }

    /// <summary>
    /// 建立只由目前測試擁有的 stateless ProductClient。logger 與 fake executor 不保存其他測試的 request、
    /// profile、token 或 response；真實 executor 的 transport、lease 與取消清理由其 DI owner 負責。
    /// </summary>
    /// <param name="executor">要觀察固定 operation dispatch 的本機封閉 executor。</param>
    /// <returns>不建立或持有任何外部資源的待測 client。</returns>
    private static MemberInfoPresentRecordReadClient CreateClient(IDynamicsOperationExecutor executor)
        => new(executor, NullLogger<MemberInfoPresentRecordReadClient>.Instance);

    /// <summary>
    /// 建立 deployment-owned routing 與 controller 已授權 contact 的 request-local 測試輸入。此 helper 不從 HTTP、
    /// Session、query 或前一筆資料導出 profile/workload，讓每個案例明確描述自身 isolation boundary。
    /// </summary>
    /// <param name="profileAlias">目前測試的 deployment profile marker。</param>
    /// <param name="workloadSubjectId">目前測試的 server-owned workload marker。</param>
    /// <returns>不含 endpoint、credential、owner、query 或 CRM SDK 型別的純量 request。</returns>
    private static MemberInfoPresentRecordReadRequest CreateRequest(string profileAlias, string workloadSubjectId)
        => new()
        {
            ProfileAlias = profileAlias,
            WorkloadSubjectId = workloadSubjectId,
            ContactId = Guid.Parse("11111111-2222-3333-4444-555555555555")
        };

    /// <summary>
    /// 建立單筆合法 wire row，讓 A/B marker 和 identity 故障可在目前測試 scope 內精確控制。日期保留 legacy
    /// <see cref="DateTime"/> 值而不做 UTC 或時區推測；row 不含 CRM Entity、lookup graph、token、profile 或資源 owner。
    /// </summary>
    /// <param name="marker">區分目前測試 A/B 或來源快照的純文字 marker。</param>
    /// <param name="presentRecordId">可選的合法或故障注入用出席紀錄 GUID。</param>
    /// <returns>封閉 response branch 可接收的純量 wire row。</returns>
    private static MemberInfoPresentRecordReadRecord CreateWireRecord(string marker, Guid? presentRecordId = null)
        => new()
        {
            PresentRecordId = presentRecordId ?? Guid.NewGuid(),
            ContactFullName = $"member-{marker}",
            SundayDate = new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Unspecified),
            Sunday = true,
            SmallGroup = false,
            PrayItem = $"pray-{marker}"
        };

    /// <summary>
    /// 建立唯一合法的 present-record response envelope。factory 應在目前 request 立即 snapshot collection；
    /// 此 helper 不接受或建立 connector、CE fixture、session、stream、credential 或 background resource。
    /// </summary>
    /// <param name="operationId">要驗證 exact correlation 的 capability ID。</param>
    /// <param name="marker">建立可辨識 request-local row 的 marker。</param>
    /// <returns>只有預期 discriminator 的成功 operation result。</returns>
    private static OperationExecutionResult CreateSuccessfulResult(string operationId, string marker)
        => OperationExecutionResult.Success(OperationResponseData.ForMemberInfoPresentRecordReadRecords(
            operationId,
            "9.1",
            new[] { CreateWireRecord(marker) }));

    /// <summary>
    /// 將目前測試建立的 response handler 包成同步完成 executor。
    /// 它只記錄目前呼叫的 request，沒有 static 集合、cache、取消註冊、計時器或外部連線，因此可用來判斷
    /// ProductClient 是否在真正 I/O 前拒絕無效輸入，而不會引入跨測試或跨使用者的生命週期干擾。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 初始化由單一測試提供的封閉結果產生器。handler 不捕捉 production session、credential 或 response stream，
        /// 只依目前 request 生成 immutable envelope，讓測試可準確驗證 ProductClient 邊界。
        /// </summary>
        /// <param name="handler">根據封閉 operation request 回傳預期結果的函式。</param>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// 取得目前測試的 dispatch 次數。無效 request 時必須維持零，不能因 fallback、retry 或 prefetch 增加。
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 取得 executor 原樣收到的取消權杖。fake 不註冊 callback，故不取得 CTS 或 transport cleanup 所有權。
        /// </summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// 執行本機封閉 response handler。取消權杖在後續測試會原樣記錄；此初始 RED 測試不建立 linked token 或
        /// callback，避免 fake 取得實際 executor 專屬的資源清理責任。
        /// </summary>
        /// <param name="request">由 ProductClient 建立的 deployment-owned operation request。</param>
        /// <param name="cancellationToken">由目前 request 傳入且不由 fake 保存的取消權杖。</param>
        /// <returns>已完成且沒有外部資源所有權的 operation result。</returns>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_handler(request));
        }
    }

    /// <summary>
    /// 將 A 與 B request 分流到不同 pending completion 的測試 executor。每個 completion 只屬於本測試 instance，
    /// 使用 asynchronous continuation 避免 SetResult 在 completion 呼叫端重入；不建立 cancellation registration、
    /// timer、cache、connector 或其他需 Dispose 資源。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly TaskCompletionSource<OperationExecutionResult> _aCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _bCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>取得 A request 原樣傳入的取消權杖，不建立 registration 或延長 CTS 存活時間。</summary>
        public CancellationToken ACancellationToken { get; private set; }

        /// <summary>取得 B request 原樣傳入的取消權杖，不建立 registration 或延長 CTS 存活時間。</summary>
        public CancellationToken BCancellationToken { get; private set; }

        /// <summary>
        /// 只依已驗證的測試 profile marker 分派 completion。未知 profile 立即失敗而非借用 A/B response，避免
        /// 假替身掩蓋 production client 的 profile isolation 缺陷；token 只短暫記錄作目前測試斷言。
        /// </summary>
        /// <param name="request">ProductClient 建立的固定 operation request。</param>
        /// <param name="cancellationToken">應原樣傳達的目前 request token。</param>
        /// <returns>對應 A 或 B 的 pending operation result。</returns>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => request.ProfileAlias switch
            {
                "profile-A" => CaptureA(cancellationToken),
                "profile-B" => CaptureB(cancellationToken),
                _ => throw new InvalidOperationException("Unexpected profile for the isolation test.")
            };

        /// <summary>完成 A 的私有 response，不會寫入或完成 B completion。</summary>
        /// <param name="result">要交付給 A request 的封閉 operation result。</param>
        public void CompleteA(OperationExecutionResult result) => _aCompletion.SetResult(result);

        /// <summary>完成 B 的私有 response，不會寫入或完成 A completion。</summary>
        /// <param name="result">要交付給 B request 的封閉 operation result。</param>
        public void CompleteB(OperationExecutionResult result) => _bCompletion.SetResult(result);

        /// <summary>記錄 A token 並回傳 A 專屬 pending task。</summary>
        private Task<OperationExecutionResult> CaptureA(CancellationToken cancellationToken)
        {
            ACancellationToken = cancellationToken;
            return _aCompletion.Task;
        }

        /// <summary>記錄 B token 並回傳 B 專屬 pending task。</summary>
        private Task<OperationExecutionResult> CaptureB(CancellationToken cancellationToken)
        {
            BCancellationToken = cancellationToken;
            return _bCompletion.Task;
        }
    }
}
