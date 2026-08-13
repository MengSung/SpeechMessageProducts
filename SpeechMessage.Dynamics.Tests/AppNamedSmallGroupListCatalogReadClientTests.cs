// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AppNamedSmallGroupListCatalogReadClientTests.cs
// 用途：以受控 executor 驗證 ORG-CALL-00065 ProductClient 的封閉讀取、A/B 隔離與不可變快照契約。
//
// fake executor 僅存在於單一測試執行個體，不會建立 CRM、HTTP、connector、lease、計時器、快取或背景工作；
// 因此每個案例可精確斷言 client 的 routing、union 驗證、取消傳遞與 collection 發佈行為，而不接觸 CE。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.ListCatalog;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 app-named small-group list catalog ProductClient 的固定 operation 邊界。
/// 這些測試以不同 profile/workload 與明確的 A/B marker 證明 singleton client 不會保留 request、回應、DTO 或
/// routing state；executor 保有 transport/lease 的生命週期，client 只建立短命 request-local DTO 快照。
/// </summary>
public sealed class AppNamedSmallGroupListCatalogReadClientTests
{
    private const string OperationId = OperationIds.ListCatalogRetrieveAppNamedSmallGroups;

    /// <summary>
    /// 保護固定 operation、空白參數 map、nullable scalar 與 leader GUID 純量映射契約。
    /// executor 回傳唯一正確 branch；決定性斷言確認呼叫端無法夾帶 list selector、leader selector、query 或 profile
    /// 以外的 routing authority，並且所有允許的資料都由新的 DTO 承接，而非外洩 wire record 或 CRM SDK graph。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_forwards_the_exact_closed_request_and_maps_nullable_scalars_and_leader_ids()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(request => CreateCatalogResult(
            request.CapabilityOperationId,
            CreateRecord("A")));
        var client = CreateClient(executor);

        var rows = await client.RetrieveSmallGroupAppNamedListCatalogAsync(
            "server-owned-profile-A",
            "server-owned-workload-A",
            cancellationSource.Token);

        executor.CallCount.Should().Be(1);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.CapabilityOperationId.Should().Be(OperationId);
        executor.LastRequest.ProfileAlias.Should().Be("server-owned-profile-A");
        executor.LastRequest.WorkloadSubjectId.Should().Be("server-owned-workload-A");
        executor.LastRequest.Parameters.Should().BeEmpty();
        executor.LastRequest.IdempotencyKey.Should().BeNull();
        executor.LastCancellationToken.Should().Be(cancellationSource.Token);

        rows.Should().ContainSingle();
        rows[0].ListId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        rows[0].ListName.Should().Be("small-group-A");
        rows[0].CreatedFromCodeOption.Should().Be(100000007);
        rows[0].LastUsedOn.Should().Be(new DateTimeOffset(2026, 8, 13, 12, 30, 0, TimeSpan.Zero));
        rows[0].Purpose.Should().Be("purpose-A");
        rows[0].RaceLeaderContactId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        rows[0].FamilyLeaderContactId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    /// <summary>
    /// 保護 nullable scalar 與 nullable leader GUID 必須以原值映射的契約。
    /// 故障/缺值注入刻意移除名稱、option、時間、purpose 與兩個 leader；client 只能保留 null，不能用 CRM metadata、
    /// Entity rehydration、另一筆回應、快取或前一個 request 的值補齊，否則可能造成跨 profile 或跨使用者資料洩漏。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_preserves_nullable_scalars_and_leader_guids_without_rehydration()
    {
        var executor = new RecordingExecutor(request => CreateCatalogResult(
            request.CapabilityOperationId,
            new SmallGroupAppNamedListCatalogRecord
            {
                ListId = Guid.Parse("33333333-3333-3333-3333-333333333333")
            }));
        var client = CreateClient(executor);

        var rows = await client.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");

        rows.Should().ContainSingle();
        rows[0].ListId.Should().Be(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        rows[0].ListName.Should().BeNull();
        rows[0].CreatedFromCodeOption.Should().BeNull();
        rows[0].LastUsedOn.Should().BeNull();
        rows[0].Purpose.Should().BeNull();
        rows[0].RaceLeaderContactId.Should().BeNull();
        rows[0].FamilyLeaderContactId.Should().BeNull();
    }

    /// <summary>
    /// 保護 profile/workload 必須在 executor I/O 前驗證的 fail-closed 契約。
    /// 對空白或缺失 routing 故障注入後，唯一決定性斷言是 fake executor 呼叫數保持零；因此無效 deployment 值
    /// 不會建立 connector、借用 lease、讀取 cache、走 retry/fallback 或意外使用前一個 profile 的狀態。
    /// </summary>
    /// <param name="profileAlias">模擬失效的 deployment-owned profile 值。</param>
    /// <param name="workloadSubjectId">模擬缺失的 server-derived workload 值。</param>
    [Theory]
    [InlineData(null, "workload-A")]
    [InlineData("", "workload-A")]
    [InlineData("   ", "workload-A")]
    [InlineData("profile-A", null)]
    [InlineData("profile-A", "")]
    [InlineData("profile-A", "   ")]
    public async Task Retrieve_async_rejects_invalid_routing_before_executor_io(
        string? profileAlias,
        string? workloadSubjectId)
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var client = CreateClient(executor);

        var act = () => client.RetrieveSmallGroupAppNamedListCatalogAsync(profileAlias!, workloadSubjectId!);

        await act.Should().ThrowAsync<ArgumentException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護成功 response 必須攜帶相同 capability operation ID 的契約。
    /// 測試將一個有效 small-group branch 故意標成另一 operation；client 必須在 mapping 前拒絕它，且不得發布
    /// partial rows、重試或改用 legacy/CRM 讀取，避免跨 capability 的資料意外傳遞。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_branch_with_a_mismatched_operation_id()
    {
        var executor = new RecordingExecutor(_ => CreateCatalogResult(
            OperationIds.ListCatalogRetrieveAppNamed,
            CreateRecord("wrong-operation")));
        var client = CreateClient(executor);

        var act = () => client.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 exact response kind/branch 契約。
    /// 故障注入回傳另一個 catalog capability 的成功 union branch；client 只能接受 small-group branch，否則必須在
    /// mapper 前 fail closed，防止不同 catalog 的資料或可變 collection 被錯誤發佈給目前 request。
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

        var act = () => client.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 null envelope、null wire row 與 empty list ID 均不得發佈部分結果的 fail-closed 契約。
    /// 三種故障注入分別代表 executor 沒有安全 response、上游 collection 出現不存在列與 connector 漏驗證名單 ID；
    /// 每次都必須失敗而非回傳空/partial list、重試、fallback 或讀取另一個 catalog，避免錯誤回應洩漏任何既有資料。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_null_or_invalid_small_group_catalog_response_records()
    {
        var noDataClient = CreateClient(new RecordingExecutor(_ => OperationExecutionResult.Success(null)));
        var nullRowClient = CreateClient(new RecordingExecutor(_ => CreateCatalogResult(
            OperationId,
            new SmallGroupAppNamedListCatalogRecord[] { null! })));
        var invalidIdClient = CreateClient(new RecordingExecutor(_ => CreateCatalogResult(
            OperationId,
            new SmallGroupAppNamedListCatalogRecord
            {
                ListId = Guid.Empty,
                ListName = "invalid"
            })));

        var noData = () => noDataClient.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");
        var nullRow = () => nullRowClient.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");
        var invalidId = () => invalidIdClient.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");

        await noData.Should().ThrowAsync<InvalidOperationException>();
        await nullRow.Should().ThrowAsync<ArgumentNullException>();
        await invalidId.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護取消權杖必須未經替換地傳至 executor 的生命週期契約。
    /// fake executor 不註冊 callback，僅記錄 token；此斷言可防止 client 建立 linked token、吞掉取消或延後傳遞，
    /// 而實際 transport/lease 在取消時的 fault eviction 與釋放仍由 production executor 的單一 owner 負責。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_forwards_the_supplied_cancellation_token_unchanged()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(request => CreateCatalogResult(
            request.CapabilityOperationId,
            CreateRecord("cancellation")));
        var client = CreateClient(executor);

        await client.RetrieveSmallGroupAppNamedListCatalogAsync(
            "profile-A",
            "workload-A",
            cancellationSource.Token);

        executor.LastCancellationToken.Should().Be(cancellationSource.Token);
    }

    /// <summary>
    /// 保護來源 wire collection 在 envelope 建立後被修改時，已發佈小組目錄不會跟著變動的快照契約。
    /// 測試清空 A source 並插入 B source；成功結果只能含 A marker，證明 client 不會保存或再使用上游可變 collection。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_isolated_from_source_collection_mutation_after_envelope_creation()
    {
        var sourceRecords = new List<SmallGroupAppNamedListCatalogRecord>
        {
            CreateRecord("source-A")
        };
        var response = CreateCatalogResult(OperationId, sourceRecords);
        sourceRecords.Clear();
        sourceRecords.Add(CreateRecord("source-B"));

        var executor = new RecordingExecutor(_ => response);
        var client = CreateClient(executor);

        var rows = await client.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");

        rows.Should().ContainSingle();
        rows[0].ListName.Should().Be("small-group-source-A");
        rows[0].Should().NotBeSameAs(sourceRecords[0]);
    }

    /// <summary>
    /// 保護公開結果 collection 無法由呼叫端替換、加入或轉型為陣列的契約。
    /// 即使 DTO 為 immutable record，仍必須阻止 backing array/downcast；這會防止序列化前或下一個 consumer 將另一個
    /// request 的列寫入已發佈結果，造成 session/profile 資料外洩。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_publishes_a_non_array_read_only_collection()
    {
        var executor = new RecordingExecutor(request => CreateCatalogResult(
            request.CapabilityOperationId,
            CreateRecord("immutable")));
        var client = CreateClient(executor);

        var rows = await client.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");

        rows.Should().NotBeOfType<SmallGroupAppNamedListCatalogRecordDto[]>();
        var writableView = rows.Should().BeAssignableTo<IList<SmallGroupAppNamedListCatalogRecordDto>>().Subject;
        var act = () => writableView.Add(new SmallGroupAppNamedListCatalogRecordDto
        {
            ListId = Guid.NewGuid(),
            ListName = "must-not-publish"
        });

        act.Should().Throw<NotSupportedException>();
        rows.Should().ContainSingle(row => row.ListName == "small-group-immutable");
    }

    /// <summary>
    /// 保護 A/B 交錯完成時每個 profile/workload 擁有不同 request-local DTO/collection 的隔離契約。
    /// fake executor 先完成 B 再完成 A；雙向 marker 與 reference identity 斷言可偵測 singleton last-result、static
    /// collection、shared cache 或 closure 保留的跨使用者/跨 profile 汙染。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_keeps_interleaved_a_and_b_results_request_local_and_immutable()
    {
        var executor = new InterleavingExecutor();
        var client = CreateClient(executor);

        var aTask = client.RetrieveSmallGroupAppNamedListCatalogAsync("profile-A", "workload-A");
        var bTask = client.RetrieveSmallGroupAppNamedListCatalogAsync("profile-B", "workload-B");

        executor.CompleteB(CreateCatalogResult(OperationId, CreateRecord("B")));
        var bRows = await bTask;
        executor.CompleteA(CreateCatalogResult(OperationId, CreateRecord("A")));
        var aRows = await aTask;

        aRows.Should().ContainSingle();
        bRows.Should().ContainSingle();
        aRows[0].ListName.Should().Be("small-group-A");
        bRows[0].ListName.Should().Be("small-group-B");
        aRows.Should().NotBeSameAs(bRows);
        aRows[0].Should().NotBeSameAs(bRows[0]);
        aRows.Should().NotContain(row => row.ListName == "small-group-B");
        bRows.Should().NotContain(row => row.ListName == "small-group-A");
    }

    /// <summary>
    /// 保護 composition root 將無 request-state 的 client 註冊為 singleton 的契約。
    /// 這只檢查 descriptor，沒有建立 service provider、executor 或 transport；因為 client 每次呼叫都建立自己的 request
    /// 和 DTO collection，singleton 不會保留使用者資料，而 connector/lease 的清理仍由 executor owner 管理。
    /// </summary>
    [Fact]
    public void Small_group_catalog_read_registration_uses_the_stateless_singleton_client()
    {
        var services = new ServiceCollection();

        services.AddSpeechMessageDynamicsSmallGroupAppNamedListCatalogReads();

        var descriptor = services.Should().ContainSingle(service =>
            service.ServiceType == typeof(ISmallGroupAppNamedListCatalogReadClient)).Subject;
        descriptor.ImplementationType.Should().Be(typeof(SmallGroupAppNamedListCatalogReadClient));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    /// <summary>
    /// 建立不持有 HTTP、CRM、connector、lease、cache 或跨測試狀態的待測 client。
    /// NullLogger 與 executor 都由目前測試擁有；production DI 的 executor 才是可重用 transport/lease 的唯一 owner，
    /// 因此本 helper 不需要也不應實作 Dispose、timer 或取消註冊。
    /// </summary>
    /// <param name="executor">本測試要觀察的封閉 operation executor。</param>
    /// <returns>不帶 request/profile state 的 small-group catalog client。</returns>
    private static SmallGroupAppNamedListCatalogReadClient CreateClient(IDynamicsOperationExecutor executor)
        => new(executor, NullLogger<SmallGroupAppNamedListCatalogReadClient>.Instance);

    /// <summary>
    /// 建立唯一 small-group catalog branch 的成功 envelope。
    /// response factory 預期會立即 materialize source rows；此 helper 不攜帶 Entity、paging cookie、exception、endpoint、
    /// credential 或 stream，讓來源 mutation 案例只驗證 immutable wire/DTO boundary，而非模擬 connector 行為。
    /// </summary>
    /// <param name="operationId">要放入 response envelope 的 capability operation ID。</param>
    /// <param name="records">要由 factory defensive-copy 的 small-group catalog wire rows。</param>
    /// <returns>只有預期 branch 的成功 operation result。</returns>
    private static OperationExecutionResult CreateCatalogResult(
        string operationId,
        IEnumerable<SmallGroupAppNamedListCatalogRecord> records)
        => OperationExecutionResult.Success(
            OperationResponseData.ForSmallGroupAppNamedListCatalogRecords(operationId, "9.1", records));

    /// <summary>
    /// 以一或多筆 wire rows 建立結果，讓案例能清楚描述輸入 marker。
    /// 此 overload 仍委派給 <see cref="IEnumerable{T}"/> factory，沒有 static backing collection 或快取，所有陣列只在
    /// 目前測試與 envelope defensive-copy 前短暫存活。
    /// </summary>
    /// <param name="operationId">要放入 response envelope 的 capability operation ID。</param>
    /// <param name="records">要納入 response branch 的純量 wire rows。</param>
    /// <returns>只有預期 branch 的成功 operation result。</returns>
    private static OperationExecutionResult CreateCatalogResult(
        string operationId,
        params SmallGroupAppNamedListCatalogRecord[] records)
        => CreateCatalogResult(operationId, (IEnumerable<SmallGroupAppNamedListCatalogRecord>)records);

    /// <summary>
    /// 建立含 distinct A/B marker 的合法 wire record。
    /// leader 欄位只放 nullable GUID，不含 EntityReference.Name、CRM Entity、profile 或使用者/租戶資料；marker 只用於
    /// 測試結果隔離判定，避免為了測試引入任何可被 production cache 或 session 保留的 state。
    /// </summary>
    /// <param name="marker">區分來源或 A/B request 的測試文字。</param>
    /// <returns>符合 fixed small-group catalog branch 的純量 wire row。</returns>
    private static SmallGroupAppNamedListCatalogRecord CreateRecord(string marker)
        => new()
        {
            ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ListName = $"small-group-{marker}",
            CreatedFromCodeOption = 100000007,
            LastUsedOn = new DateTimeOffset(2026, 8, 13, 12, 30, 0, TimeSpan.Zero),
            Purpose = $"purpose-{marker}",
            RaceLeaderContactId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FamilyLeaderContactId = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };

    /// <summary>
    /// 記錄同步完成 dispatch 的測試 executor。
    /// 它只保存單一測試的 request/token/call-count，沒有共享 static state、取消註冊、背景工作、連線或 lease；無效 routing
    /// 若錯誤進入這個 fake，<see cref="CallCount"/> 便會讓 zero-I/O 測試立即失敗。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 初始化由目前測試控制生命週期的 response handler。
        /// handler 不捕捉 production request/session，也不執行遠端 I/O，讓每個測試可安全驗證 client boundary。
        /// </summary>
        /// <param name="handler">依收到的封閉 request 建立結果的函式。</param>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// 取得目前測試收到的 dispatch 次數；驗證失敗時應維持零。
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 取得目前測試最後一次 request；絕不跨測試、跨 profile 或跨 request 快取。
        /// </summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>
        /// 取得 executor 原樣收到的取消權杖；fake 不為它建立 registration。
        /// </summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// 記錄 request/token 後立即回傳受控結果。
        /// 真實 executor 才擁有 connector、transport、lease、permit 與其取消/fault cleanup；本 fake 不複製該生命週期，
        /// 以避免測試自身保留不受控資源或掩蓋 client 新增 retry/fallback 的問題。
        /// </summary>
        /// <param name="request">client 建立的固定 operation request。</param>
        /// <param name="cancellationToken">client 應原樣傳遞的取消權杖。</param>
        /// <returns>由目前測試 handler 建立的完成結果。</returns>
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
    /// 以兩個獨立 completion 模擬 A/B 非同步交錯的測試 executor。
    /// TaskCompletionSource 使用非同步 continuation，避免 SetResult 執行 consumer continuation；它沒有計時器、租用、快取或
    /// cancellation registration，所有 pending task 都由此單一測試 instance 擁有並隨測試結束釋放。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly TaskCompletionSource<OperationExecutionResult> _aCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _bCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 依測試 profile marker 回傳獨立 pending response。
        /// profile 在這裡只作為測試排程鍵，而不是 production authority；未知 marker 立即失敗，防止測試誤把 A/B
        /// response 交叉傳遞而掩蓋 singleton 的跨 profile 狀態洩漏。
        /// </summary>
        /// <param name="request">client 已驗證後送出的封閉 request。</param>
        /// <param name="cancellationToken">不由 fake 持有或註冊的取消權杖。</param>
        /// <returns>A 或 B 專屬的未完成 response task。</returns>
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
        /// 完成 A response，不能改寫或完成 B 的 state。
        /// </summary>
        /// <param name="result">要交付給 A request 的 operation result。</param>
        public void CompleteA(OperationExecutionResult result)
            => _aCompletion.TrySetResult(result).Should().BeTrue();

        /// <summary>
        /// 完成 B response，不能改寫或完成 A 的 state。
        /// </summary>
        /// <param name="result">要交付給 B request 的 operation result。</param>
        public void CompleteB(OperationExecutionResult result)
            => _bCompletion.TrySetResult(result).Should().BeTrue();
    }
}
