// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AppNamedMembershipReadClientTests.cs
// 用途：驗證 ORG-CALL-00057 app-named membership 唯讀 ProductClient 的封閉 request、回應與隔離契約。
//
// 所有 fake executor 都只存在於單一測試執行個體，沒有 CRM、HTTP、connector、lease、cache、timer、取消註冊或背景工作。
// 測試以不同 profile/workload/contact marker 與可控 completion 驗證 singleton client 不會保存或交叉發佈任何 request 資料。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.ListCatalog;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 app-named membership read 的固定 ProductClient 邊界。
/// 每個案例僅組合 deployment-owned profile、server-owned workload 與上層已授權的 contact locator；測試不建立
/// consumer、HTTP endpoint、Session、Entity、cache、fallback 或 retry，並以 request-local DTO 快照保護 A/B 隔離。
/// </summary>
public sealed class AppNamedMembershipReadClientTests
{
    private const string OperationId = OperationIds.ListMembershipRetrieveAppNamedByContact;

    /// <summary>
    /// 保護唯一 operation、contact-only parameter map、nullable list name 與純量 DTO 映射契約。
    /// 測試故意使用可辨識但非敏感的固定 marker；決定性斷言確認 contact 不能改選 profile/workload，也確認 ProductClient
    /// 不回傳 wire record、CRM Entity、query、cookie、credential 或另一個 request 的可變資料。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_forwards_the_exact_closed_request_and_maps_nullable_list_name()
    {
        using var cancellationSource = new CancellationTokenSource();
        var contactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var executor = new RecordingExecutor(request => CreateMembershipResult(
            request.CapabilityOperationId,
            CreateRecord("A")));
        var client = CreateClient(executor);

        var rows = await client.RetrieveAppNamedMembershipsByContactAsync(
            new AppNamedMembershipReadRequest
            {
                ProfileAlias = "server-owned-profile-A",
                WorkloadSubjectId = "server-owned-workload-A",
                ContactId = contactId
            },
            cancellationSource.Token);

        executor.CallCount.Should().Be(1);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.CapabilityOperationId.Should().Be(OperationId);
        executor.LastRequest.ProfileAlias.Should().Be("server-owned-profile-A");
        executor.LastRequest.WorkloadSubjectId.Should().Be("server-owned-workload-A");
        executor.LastRequest.Parameters.Should().ContainSingle();
        executor.LastRequest.Parameters["contactId"].Should().Be(contactId);
        executor.LastRequest.IdempotencyKey.Should().BeNull();
        executor.LastCancellationToken.Should().Be(cancellationSource.Token);

        rows.Should().ContainSingle();
        rows[0].ListId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        rows[0].ListName.Should().Be("membership-A");
    }

    /// <summary>
    /// 保護 nullable list name 必須保留缺值語意的契約。
    /// 對上游缺少名稱的回應，client 不得以 Entity 補查、metadata、cache、前次結果或 fallback 填值；唯一可接受的
    /// 發佈值是 null，以避免將另一 contact/profile 的顯示資料交給目前 request。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_preserves_a_null_list_name_without_rehydration()
    {
        var executor = new RecordingExecutor(request => CreateMembershipResult(
            request.CapabilityOperationId,
            new AppNamedMembershipRecord
            {
                ListId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
            }));
        var client = CreateClient(executor);

        var rows = await client.RetrieveAppNamedMembershipsByContactAsync(CreateRequest());

        rows.Should().ContainSingle();
        rows[0].ListId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        rows[0].ListName.Should().BeNull();
    }

    /// <summary>
    /// 保護 routing 與 contact locator 全部必須在 executor I/O 前 fail closed 的契約。
    /// 注入空白 deployment/server routing 或空 GUID 後，決定性斷言是 executor 呼叫數保持零；因此無效 request 不會
    /// 解析 profile、借用 lease、讀取 cache、啟動 retry、接觸 legacy CRM 或使用前一個 request 的任何 state。
    /// </summary>
    /// <param name="profileAlias">模擬失效 deployment profile 的值。</param>
    /// <param name="workloadSubjectId">模擬遺失 server workload 的值。</param>
    /// <param name="contactId">模擬未經授權或無效 locator 的值。</param>
    [Theory]
    [InlineData(null, "workload-A", "11111111-1111-1111-1111-111111111111")]
    [InlineData("", "workload-A", "11111111-1111-1111-1111-111111111111")]
    [InlineData("   ", "workload-A", "11111111-1111-1111-1111-111111111111")]
    [InlineData("profile-A", null, "11111111-1111-1111-1111-111111111111")]
    [InlineData("profile-A", "", "11111111-1111-1111-1111-111111111111")]
    [InlineData("profile-A", "   ", "11111111-1111-1111-1111-111111111111")]
    [InlineData("profile-A", "workload-A", "00000000-0000-0000-0000-000000000000")]
    public async Task Retrieve_async_rejects_invalid_routing_or_contact_before_executor_io(
        string? profileAlias,
        string? workloadSubjectId,
        string contactId)
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var client = CreateClient(executor);
        var request = new AppNamedMembershipReadRequest
        {
            ProfileAlias = profileAlias!,
            WorkloadSubjectId = workloadSubjectId!,
            ContactId = Guid.Parse(contactId)
        };

        var act = () => client.RetrieveAppNamedMembershipsByContactAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護成功 envelope 的 operation ID 必須精確相符。
    /// 此故障注入讓正確 membership branch 偽裝成另一 capability；client 必須在 mapping 前拒絕，且不得重試、fallback、
    /// 發佈 partial collection 或把錯誤 capability 的資料保存到 singleton。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_membership_branch_with_a_mismatched_operation_id()
    {
        var executor = new RecordingExecutor(_ => CreateMembershipResult(
            OperationIds.ListCatalogRetrieveAppNamed,
            CreateRecord("wrong-operation")));
        var client = CreateClient(executor);

        var act = () => client.RetrieveAppNamedMembershipsByContactAsync(CreateRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 response discriminator 與唯一非 null branch 都必須精確匹配的契約。
    /// fake executor 回傳另一個合法 catalog union branch；client 不可依資料列形狀猜測成功，否則不同 capability 的資料
    /// 可能在 profile 或 contact 邊界外流。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_wrong_response_discriminator_before_mapping()
    {
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(
            OperationResponseData.ForAppNamedListCatalogRecords(
                OperationId,
                "9.1",
                Array.Empty<AppNamedListCatalogRecord>())));
        var client = CreateClient(executor);

        var act = () => client.RetrieveAppNamedMembershipsByContactAsync(CreateRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護成功 executor 結果不能缺少封閉 response envelope 的契約。
    /// 此 fault injection 模擬 executor 在成功旗標下沒有可安全發布的資料；client 必須在建立 DTO collection 前失敗，
    /// 不能改走 legacy/CRM、retry、fallback 或把前一個 request 的 collection 當成本次結果。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_success_result_without_the_required_response_envelope()
    {
        var noDataClient = CreateClient(new RecordingExecutor(_ => OperationExecutionResult.Success(null)));

        var noData = () => noDataClient.RetrieveAppNamedMembershipsByContactAsync(CreateRequest());

        await noData.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護取消權杖要不經 linked token、registration、catch 或替換而透明傳遞的契約。
    /// fake executor 只記錄 token；決定性斷言確認 ProductClient 不會攔截取消，而 transport/lease 對取消、fault 和
    /// uncertain state 的淘汰與釋放仍由 executor 的唯一 owner 管理。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_forwards_the_supplied_cancellation_token_unchanged()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(request => CreateMembershipResult(
            request.CapabilityOperationId,
            CreateRecord("cancellation")));
        var client = CreateClient(executor);

        await client.RetrieveAppNamedMembershipsByContactAsync(CreateRequest(), cancellationSource.Token);

        executor.LastCancellationToken.Should().Be(cancellationSource.Token);
    }

    /// <summary>
    /// 保護 envelope 來源集合後續遭變更時，已發佈結果仍是本次呼叫的獨立 snapshot。
    /// 測試先要求 response factory materialize A，再將來源集合改成 B；決定性斷言確認 ProductClient 沒有保存來源
    /// collection，也沒有從後續可變內容、cache 或另一 request 重取資料。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_isolated_from_source_collection_mutation_and_publishes_a_non_array_read_only_collection()
    {
        var sourceRecords = new List<AppNamedMembershipRecord> { CreateRecord("source-A") };
        var response = CreateMembershipResult(OperationId, sourceRecords);
        sourceRecords.Clear();
        sourceRecords.Add(CreateRecord("source-B"));
        var client = CreateClient(new RecordingExecutor(_ => response));

        var rows = await client.RetrieveAppNamedMembershipsByContactAsync(CreateRequest());

        rows.Should().ContainSingle();
        rows[0].ListName.Should().Be("membership-source-A");
        rows[0].Should().NotBeSameAs(sourceRecords[0]);
        rows.Should().NotBeOfType<AppNamedMembershipRecordDto[]>();
        var writableView = rows.Should().BeAssignableTo<IList<AppNamedMembershipRecordDto>>().Subject;
        var mutation = () => writableView.Add(new AppNamedMembershipRecordDto
        {
            ListId = Guid.NewGuid(),
            ListName = "must-not-publish"
        });
        mutation.Should().Throw<NotSupportedException>();
        rows.Should().ContainSingle(row => row.ListName == "membership-source-A");
    }

    /// <summary>
    /// 保護 A/B 非同步交錯時 client 不可保留 last request、last response、profile、contact 或可變 DTO collection。
    /// fake executor 先完成 B 再完成 A；決定性斷言確認兩組 DTO/collection 均為不同 instance，marker 也完全不交叉，
    /// 因而能偵測 singleton field、static cache、closure 或 shared response collection 的 session leakage。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_keeps_interleaved_a_and_b_results_request_local_and_immutable()
    {
        var executor = new InterleavingExecutor();
        var client = CreateClient(executor);

        var aTask = client.RetrieveAppNamedMembershipsByContactAsync(CreateRequest(
            "profile-A",
            "workload-A",
            Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111")));
        var bTask = client.RetrieveAppNamedMembershipsByContactAsync(CreateRequest(
            "profile-B",
            "workload-B",
            Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222")));

        executor.CompleteB(CreateMembershipResult(OperationId, CreateRecord("B")));
        var bRows = await bTask;
        executor.CompleteA(CreateMembershipResult(OperationId, CreateRecord("A")));
        var aRows = await aTask;

        aRows.Should().ContainSingle();
        bRows.Should().ContainSingle();
        aRows[0].ListName.Should().Be("membership-A");
        bRows[0].ListName.Should().Be("membership-B");
        aRows.Should().NotBeSameAs(bRows);
        aRows[0].Should().NotBeSameAs(bRows[0]);
        aRows.Should().NotContain(row => row.ListName == "membership-B");
        bRows.Should().NotContain(row => row.ListName == "membership-A");
    }

    /// <summary>
    /// 保護 composition root 以 singleton 註冊 stateless read client 的契約。
    /// 此測試只檢查 descriptor，不建立 service provider、executor、connector 或 transport；singleton 安全依賴 client
    /// 僅持有 DI-owned stateless dependencies，並讓 request DTO/response collection 在每次呼叫完成後釋放。
    /// </summary>
    [Fact]
    public void Membership_read_registration_uses_the_stateless_singleton_client()
    {
        var services = new ServiceCollection();

        services.AddSpeechMessageDynamicsAppNamedMembershipReads();

        var descriptor = services.Should().ContainSingle(service =>
            service.ServiceType == typeof(IAppNamedMembershipReadClient)).Subject;
        descriptor.ImplementationType.Should().Be(typeof(AppNamedMembershipReadClient));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    /// <summary>
    /// 建立不含 CRM、HTTP、lease、cache 或跨測試狀態的 client。
    /// NullLogger 和 fake executor 均由測試執行個體擁有；production client 只保有 DI-managed executor/logger，實際
    /// transport 與 deterministic cleanup 一律由 executor owner 處理。
    /// </summary>
    /// <param name="executor">要觀察 closed operation request 的測試 executor。</param>
    /// <returns>可供單一測試使用的 stateless membership client。</returns>
    private static AppNamedMembershipReadClient CreateClient(IDynamicsOperationExecutor executor)
        => new(executor, NullLogger<AppNamedMembershipReadClient>.Instance);

    /// <summary>
    /// 建立固定且已授權的純量 read request。
    /// helper 每次配置新的 request instance，避免測試自身共享可變 request；profile/workload 僅是 fake scheduling
    /// marker，不能代表瀏覽器、Session、token 或 connector routing authority。
    /// </summary>
    /// <param name="profileAlias">測試專屬 deployment profile marker。</param>
    /// <param name="workloadSubjectId">測試專屬 server workload marker。</param>
    /// <param name="contactId">已授權 contact locator 的測試值。</param>
    /// <returns>只含 allowed scalar 的 request。</returns>
    private static AppNamedMembershipReadRequest CreateRequest(
        string profileAlias = "profile-A",
        string workloadSubjectId = "workload-A",
        Guid? contactId = null)
        => new()
        {
            ProfileAlias = profileAlias,
            WorkloadSubjectId = workloadSubjectId,
            ContactId = contactId ?? Guid.Parse("11111111-1111-1111-1111-111111111111")
        };

    /// <summary>
    /// 建立只含 membership response branch 的成功 envelope。
    /// response factory 必須在目前測試呼叫中 materialize records；此 helper 不接觸 CRM Entity、connector 或 stream，
    /// 因此來源 mutation 測試可精確驗證跨 boundary defensive-copy 語意。
    /// </summary>
    /// <param name="operationId">要寫入 envelope 的 operation ID。</param>
    /// <param name="records">要被 branch materialize 的純量 wire rows。</param>
    /// <returns>不帶 upstream exception、profile、credential 或外部資源的成功結果。</returns>
    private static OperationExecutionResult CreateMembershipResult(
        string operationId,
        IEnumerable<AppNamedMembershipRecord> records)
        => OperationExecutionResult.Success(
            OperationResponseData.ForAppNamedMembershipRecords(operationId, "9.1", records));

    /// <summary>
    /// 為常見 happy-path 與 fault injection 縮短 membership envelope 建立語法。
    /// params 陣列在 factory 內立即 materialize；helper 不把它儲存在 static field、cache 或未完成 task，避免測試
    /// 支援碼本身遮蔽 production client 的 collection-retention defect。
    /// </summary>
    /// <param name="operationId">要寫入 response branch 的 operation ID。</param>
    /// <param name="records">本次 response 的純量 wire rows。</param>
    /// <returns>只有 membership branch 的成功 executor 結果。</returns>
    private static OperationExecutionResult CreateMembershipResult(
        string operationId,
        params AppNamedMembershipRecord[] records)
        => CreateMembershipResult(operationId, (IEnumerable<AppNamedMembershipRecord>)records);

    /// <summary>
    /// 以可辨識 marker 建立合法 membership wire row。
    /// marker 只存在測試資料，用於驗證交錯 A/B 結果不交叉；wire record 沒有 profile、credential、Entity、query、
    /// session、cache 或任何可釋放資源。
    /// </summary>
    /// <param name="marker">區分資料來源的非敏感測試文字。</param>
    /// <returns>符合 fixed membership branch 的有效純量資料列。</returns>
    private static AppNamedMembershipRecord CreateRecord(string marker)
        => new()
        {
            ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ListName = $"membership-{marker}"
        };

    /// <summary>
    /// 記錄同步完成 dispatch 的 test double。
    /// 它只保留本測試的 request/token/count，沒有網路、lease、cancel registration 或 background work；無效輸入若
    /// 錯誤進入 executor，<see cref="CallCount"/> 就會揭露 I/O 前驗證缺陷。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 建立由單一測試擁有的同步結果 handler。
        /// handler 不捕捉 production request、身分、profile 或 response，也不建立非決定性清理責任。
        /// </summary>
        /// <param name="handler">依已組合 operation request 建立測試結果的函式。</param>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// 取得已收到的 dispatch 數，作為無效 input 不能觸發 executor I/O 的決定性證據。
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 取得目前測試最後一次 request；它不跨測試、Session 或 process 保存。
        /// </summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>
        /// 取得 client 原樣傳入的 cancellation token；test double 不會為它註冊 callback。
        /// </summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// 記錄 request 後立即傳回 handler 的已完成結果。
        /// production executor 才是 connector/HTTP/lease 的 cleanup owner；此 fake 只驗證 ProductClient 不自行建立
        /// retry、fallback、transport 或共享 request state。
        /// </summary>
        /// <param name="request">client 準備的固定 operation request。</param>
        /// <param name="cancellationToken">client 應透明轉送的 request cancellation token。</param>
        /// <returns>handler 建立的已完成結果。</returns>
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
    /// 以兩個獨立 completion 模擬交錯 A/B dispatch 的 test double。
    /// TaskCompletionSource 使用非同步 continuation，避免 completion thread 內同步跑 consumer；pending task 僅由此
    /// 測試 instance 擁有，沒有 timer、cache、lease 或 cancellation registration，並在測試結束後可被回收。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly TaskCompletionSource<OperationExecutionResult> _aCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _bCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 以測試專屬 profile marker 取回獨立 completion。
        /// marker 僅安排測試執行順序，不能代表 production caller authority；未知 profile 立即失敗，防止錯將 A/B
        /// response 交付到其他 request 而掩蓋資料隔離缺陷。
        /// </summary>
        /// <param name="request">client 已驗證並發出的固定 operation request。</param>
        /// <param name="cancellationToken">未由 fake 擁有或註冊的 cancellation token。</param>
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
        /// 完成 A 專屬 response，不能影響 B 的 pending task 或 wire rows。
        /// </summary>
        /// <param name="result">要傳給 A request 的成功 executor 結果。</param>
        public void CompleteA(OperationExecutionResult result)
            => _aCompletion.TrySetResult(result).Should().BeTrue();

        /// <summary>
        /// 完成 B 專屬 response，不能影響 A 的 pending task 或 wire rows。
        /// </summary>
        /// <param name="result">要傳給 B request 的成功 executor 結果。</param>
        public void CompleteB(OperationExecutionResult result)
            => _bCompletion.TrySetResult(result).Should().BeTrue();
    }
}
