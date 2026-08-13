// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AppNamedListCatalogReadClientTests.cs
// 用途：驗證 ORG-CALL-00014 的 ProductClient 邊界只發送固定 operation、純量 DTO 與目前要求專屬快照。
//
// 本測試刻意以可控制 executor 模擬 Gateway/Embedded 邊界，而不建立 CRM、連線、租用、計時器或快取。
// 每個案例保護的契約都在 XML 註解中說明，讓未來維護者能判斷是路由隔離、取消傳遞、union 驗證或
// collection 不可變性失效；fake executor 的暫存資料也只存活於單一測試執行個體。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.ListCatalog;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 app-named list catalog ProductClient 的封閉讀取契約。
/// 此類別以 A/B 可辨識 marker 與受控完成順序證明 singleton client 不會保留前一個 profile、workload、
/// wire collection 或 DTO；executor 是 transport/lease 的唯一 owner，因此測試不模擬或保留任何外部資源。
/// </summary>
public sealed class AppNamedListCatalogReadClientTests
{
    /// <summary>
    /// 保護「唯一固定 operation、空白參數 map 與逐一純量投影」契約。
    /// fake executor 回傳正確 branch，斷言 request 中沒有 caller selector、list ID、profile 以外的路由資料，並且
    /// 取消權杖以同一個 instance 傳遞；若 mapper 遺漏欄位或將 wire/Entity 型別外洩，斷言會立即失敗。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_forwards_the_exact_closed_request_and_maps_the_catalog_branch()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(request => CreateCatalogResult(
            request.CapabilityOperationId,
            CreateRecord("A")));
        var client = CreateClient(executor);

        var rows = await client.RetrieveAppNamedListCatalogAsync(
            "server-owned-profile-A",
            "server-owned-workload-A",
            cancellationSource.Token);

        executor.CallCount.Should().Be(1);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.CapabilityOperationId.Should().Be(OperationIds.ListCatalogRetrieveAppNamed);
        executor.LastRequest.ProfileAlias.Should().Be("server-owned-profile-A");
        executor.LastRequest.WorkloadSubjectId.Should().Be("server-owned-workload-A");
        executor.LastRequest.Parameters.Should().BeEmpty();
        executor.LastRequest.IdempotencyKey.Should().BeNull();
        executor.LastCancellationToken.Should().Be(cancellationSource.Token);

        rows.Should().ContainSingle();
        rows[0].ListId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        rows[0].ListName.Should().Be("list-A");
        rows[0].CreatedFromCodeOption.Should().Be(100000007);
        rows[0].LastUsedOn.Should().Be(new DateTimeOffset(2026, 8, 13, 12, 30, 0, TimeSpan.Zero));
        rows[0].Purpose.Should().Be("purpose-A");
    }

    /// <summary>
    /// 保護「profile/workload 在 executor I/O 前 fail closed」契約。
    /// 對空白與缺失路由值注入失敗，唯一決定性斷言是 executor 呼叫數仍為零，因此不能因無效輸入建立
    /// connector、借用 lease、觸發 fallback 或碰觸另一個 profile 的可變狀態。
    /// </summary>
    /// <param name="profileAlias">模擬 deployment 組態失效的 profile 值。</param>
    /// <param name="workloadSubjectId">模擬 server-derived workload 缺失的值。</param>
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

        var act = () => client.RetrieveAppNamedListCatalogAsync(profileAlias!, workloadSubjectId!);

        await act.Should().ThrowAsync<ArgumentException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 response operation ID 必須等於 request operation 的 fail-closed 契約。
    /// 故意以另一個已存在 operation 建立同一 catalog branch；client 不得把 branch 名稱當成充分證據，
    /// 也不得重試、fallback 或發佈部分 DTO。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_catalog_branch_with_a_mismatched_operation_id()
    {
        var executor = new RecordingExecutor(_ => CreateCatalogResult(
            OperationIds.PaymentsDedicationRetrieveByContact,
            CreateRecord("wrong-operation")));
        var client = CreateClient(executor);

        var act = () => client.RetrieveAppNamedListCatalogAsync("profile-A", "workload-A");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 response discriminator 必須為 AppNamedListCatalogRecords 的 union 契約。
    /// 故障注入回傳另一個成功 branch；client 必須在任何 mapper 執行前拒絕，避免把其他 capability 的資料
    /// 誤當成 catalog 結果而跨 capability 發佈。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_wrong_response_discriminator_before_mapping()
    {
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(
            OperationResponseData.ForPackage01DedicationBookingRecords(
                OperationIds.ListCatalogRetrieveAppNamed,
                "9.1",
                Array.Empty<Package01DedicationBookingRecord>())));
        var client = CreateClient(executor);

        var act = () => client.RetrieveAppNamedListCatalogAsync("profile-A", "workload-A");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 null envelope、null wire row 與空白 list ID 都不得被發佈的 fail-closed 契約。
    /// 三種故障注入分別模擬 executor 沒有安全 response、上游集合包含不存在的資料列，以及 connector 漏驗證 GUID；
    /// 決定性斷言是每次都拋出而非回傳 partial list、重試或改走另一個 CRM/legacy 路徑。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_null_or_invalid_catalog_response_records()
    {
        var noDataClient = CreateClient(new RecordingExecutor(_ => OperationExecutionResult.Success(null)));
        var nullRowClient = CreateClient(new RecordingExecutor(_ => CreateCatalogResult(
            OperationIds.ListCatalogRetrieveAppNamed,
            new AppNamedListCatalogRecord[] { null! })));
        var invalidIdClient = CreateClient(new RecordingExecutor(_ => CreateCatalogResult(
            OperationIds.ListCatalogRetrieveAppNamed,
            new AppNamedListCatalogRecord
            {
                ListId = Guid.Empty,
                ListName = "invalid"
            })));

        var noData = () => noDataClient.RetrieveAppNamedListCatalogAsync("profile-A", "workload-A");
        var nullRow = () => nullRowClient.RetrieveAppNamedListCatalogAsync("profile-A", "workload-A");
        var invalidId = () => invalidIdClient.RetrieveAppNamedListCatalogAsync("profile-A", "workload-A");

        await noData.Should().ThrowAsync<InvalidOperationException>();
        await nullRow.Should().ThrowAsync<ArgumentNullException>();
        await invalidId.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護取消權杖的透明傳遞契約。
    /// fake executor 記錄收到的 token，但不註冊 callback 或建立背景工作；決定性斷言確認 client 沒有替換、
    /// linked、吞掉或延後取消訊號，實際 transport/lease 的清理責任仍留給 executor owner。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_forwards_the_supplied_cancellation_token_unchanged()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(request => CreateCatalogResult(
            request.CapabilityOperationId,
            CreateRecord("cancellation")));
        var client = CreateClient(executor);

        await client.RetrieveAppNamedListCatalogAsync(
            "profile-A",
            "workload-A",
            cancellationSource.Token);

        executor.LastCancellationToken.Should().Be(cancellationSource.Token);
    }

    /// <summary>
    /// 保護 envelope 來源 collection 在建立後遭修改時，已發佈 DTO 不受影響的快照契約。
    /// 測試先建立成功 envelope，再清除並替換測試來源；決定性斷言要求 client 只看見建立時的 A marker，
    /// 使產品 singleton 不可能藉由共享 collection 保留 B 或前一個 request 的資料。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_isolated_from_source_collection_mutation_after_envelope_creation()
    {
        var sourceRecords = new List<AppNamedListCatalogRecord>
        {
            CreateRecord("source-A")
        };
        var response = CreateCatalogResult(OperationIds.ListCatalogRetrieveAppNamed, sourceRecords);
        sourceRecords.Clear();
        sourceRecords.Add(CreateRecord("source-B"));

        var executor = new RecordingExecutor(_ => response);
        var client = CreateClient(executor);

        var rows = await client.RetrieveAppNamedListCatalogAsync("profile-A", "workload-A");

        rows.Should().ContainSingle();
        rows[0].ListName.Should().Be("list-source-A");
        rows[0].Should().NotBeSameAs(sourceRecords[0]);
    }

    /// <summary>
    /// 保護公開 collection 不可由呼叫端改寫的契約。
    /// 成功結果必須不是可向下轉型的陣列，且任何 <see cref="IList{T}"/> 寫入嘗試都要失敗；這避免 controller
    /// 在序列化前替換資料列，造成同一 request 或後續 request 可觀察到可變 backing collection。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_publishes_a_non_array_read_only_collection()
    {
        var executor = new RecordingExecutor(request => CreateCatalogResult(
            request.CapabilityOperationId,
            CreateRecord("immutable")));
        var client = CreateClient(executor);

        var rows = await client.RetrieveAppNamedListCatalogAsync("profile-A", "workload-A");

        rows.Should().NotBeOfType<AppNamedListCatalogRecordDto[]>();
        var writableView = rows.Should().BeAssignableTo<IList<AppNamedListCatalogRecordDto>>().Subject;
        var act = () => writableView.Add(new AppNamedListCatalogRecordDto
        {
            ListId = Guid.NewGuid(),
            ListName = "must-not-publish"
        });

        act.Should().Throw<NotSupportedException>();
        rows.Should().ContainSingle(row => row.ListName == "list-immutable");
    }

    /// <summary>
    /// 保護 A/B 交錯完成時的 request-local 結果隔離契約。
    /// fake executor 同時保留兩個未完成 operation，故意先完成 B 再完成 A；斷言兩個 collection 與 DTO 都是不同
    /// instance 且 marker 不交叉，藉此偵測 singleton 的 last-result cache、static collection 或共享可變 response 漏洩。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_keeps_interleaved_a_and_b_results_request_local_and_immutable()
    {
        var executor = new InterleavingExecutor();
        var client = CreateClient(executor);

        var aTask = client.RetrieveAppNamedListCatalogAsync("profile-A", "workload-A");
        var bTask = client.RetrieveAppNamedListCatalogAsync("profile-B", "workload-B");

        executor.CompleteB(CreateCatalogResult(OperationIds.ListCatalogRetrieveAppNamed, CreateRecord("B")));
        var bRows = await bTask;
        executor.CompleteA(CreateCatalogResult(OperationIds.ListCatalogRetrieveAppNamed, CreateRecord("A")));
        var aRows = await aTask;

        aRows.Should().ContainSingle();
        bRows.Should().ContainSingle();
        aRows[0].ListName.Should().Be("list-A");
        bRows[0].ListName.Should().Be("list-B");
        aRows.Should().NotBeSameAs(bRows);
        aRows[0].Should().NotBeSameAs(bRows[0]);
        aRows.Should().NotContain(row => row.ListName == "list-B");
        bRows.Should().NotContain(row => row.ListName == "list-A");
    }

    /// <summary>
    /// 保護 composition root 以 singleton 註冊 stateless catalog client 的契約。
    /// 此測試不建立 provider 或 executor，僅檢查 descriptor；因為 client 不保留 request/profile/DTO，singleton 不會造成
    /// 跨使用者 retained state，而任何真實 transport/lease 的建立與釋放仍留在既有 executor registration。
    /// </summary>
    [Fact]
    public void Catalog_read_registration_uses_the_stateless_singleton_client()
    {
        var services = new ServiceCollection();

        services.AddSpeechMessageDynamicsAppNamedListCatalogReads();

        var descriptor = services.Should().ContainSingle(service =>
            service.ServiceType == typeof(IAppNamedListCatalogReadClient)).Subject;
        descriptor.ImplementationType.Should().Be(typeof(AppNamedListCatalogReadClient));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    /// <summary>
    /// 建立不持有 HTTP、CRM、lease、快取或跨測試狀態的 client。
    /// NullLogger 與測試 executor 都是此測試的唯一 owner；production 由 DI 擁有長生命週期 executor，client 本身
    /// 只保留該 stateless boundary 參考，不保留 request DTO、profile 或 workload。
    /// </summary>
    /// <param name="executor">本測試要觀察的 operation executor。</param>
    /// <returns>可供單一測試使用的 catalog client。</returns>
    private static AppNamedListCatalogReadClient CreateClient(IDynamicsOperationExecutor executor)
        => new(executor, NullLogger<AppNamedListCatalogReadClient>.Instance);

    /// <summary>
    /// 建立只含 catalog union branch 的成功 operation envelope。
    /// factory 是 envelope 的 defensive-copy owner；此 helper 不接觸 connector、CRM Entity 或傳輸資源，因此來源
    /// collection mutation 案例能精準檢查跨 boundary 的快照語意。
    /// </summary>
    /// <param name="operationId">要寫入 envelope 的 capability operation ID。</param>
    /// <param name="records">要被 factory materialize 的 wire scalar 資料列。</param>
    /// <returns>沒有 upstream stream、例外或 credential 的成功結果。</returns>
    private static OperationExecutionResult CreateCatalogResult(
        string operationId,
        IEnumerable<AppNamedListCatalogRecord> records)
        => OperationExecutionResult.Success(
            OperationResponseData.ForAppNamedListCatalogRecords(operationId, "9.1", records));

    /// <summary>
    /// 以一或多筆 wire row 建立 catalog 成功 envelope，避免每個案例手動轉換成 <see cref="IEnumerable{T}"/>。
    /// 這個 overload 仍交由 collection overload 與 response factory 立即 materialize，因此不引入共享 backing
    /// collection、cache 或跨 request state；它只縮短測試中的 happy-path 與 fault-injection 建構語法。
    /// </summary>
    /// <param name="operationId">要寫入 envelope 的 capability operation ID。</param>
    /// <param name="records">要納入 response branch 的純量 wire rows。</param>
    /// <returns>只具有 catalog branch 的 immutable response envelope。</returns>
    private static OperationExecutionResult CreateCatalogResult(
        string operationId,
        params AppNamedListCatalogRecord[] records)
        => CreateCatalogResult(operationId, (IEnumerable<AppNamedListCatalogRecord>)records);

    /// <summary>
    /// 以可辨識 marker 建立合法的純量 wire record。
    /// 每一筆資料不帶 Entity、OData annotation、profile、credential 或 collection；marker 僅存活於測試輸入，
    /// 用來判斷 A/B 結果是否因共享 state 而交叉。
    /// </summary>
    /// <param name="marker">區分測試資料來源與交錯呼叫的非敏感文字。</param>
    /// <returns>可由 catalog branch 接受的有效 wire record。</returns>
    private static AppNamedListCatalogRecord CreateRecord(string marker)
        => new()
        {
            ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ListName = $"list-{marker}",
            CreatedFromCodeOption = 100000007,
            LastUsedOn = new DateTimeOffset(2026, 8, 13, 12, 30, 0, TimeSpan.Zero),
            Purpose = $"purpose-{marker}"
        };

    /// <summary>
    /// 記錄單一同步完成 dispatch 的 test double。
    /// 它只保存此測試的最後 request/token 與呼叫數，不建立取消註冊、網路、租用或背景工作；任何無效路由若
    /// 意外進入這裡，就會增加 <see cref="CallCount"/> 並使 zero-I/O 契約失敗。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 初始化可驗證 request 的同步完成 handler。
        /// handler 的生命週期受單一測試控制，不會捕捉 production request、身分或 profile 資料。
        /// </summary>
        /// <param name="handler">依目前 operation request 建立測試結果的函式。</param>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// 取得 executor 已接收的 dispatch 次數；用於證明驗證在任何 I/O 前完成。
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 取得此測試最後一次 request 的快照參考；僅供斷言，絕不跨測試或跨 request 快取。
        /// </summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>
        /// 取得 executor 原樣收到的取消權杖；test double 不為它建立 registration。
        /// </summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// 記錄 dispatch 後立即回傳 handler 結果。
        /// 此方法不模擬 transport cleanup，因為 production executor 才是 connector、HTTP 與 lease 的單一 owner；
        /// 測試只驗證 ProductClient 不新增 retry、fallback 或可變共享狀態。
        /// </summary>
        /// <param name="request">client 準備的封閉 operation request。</param>
        /// <param name="cancellationToken">client 應原樣傳入的取消權杖。</param>
        /// <returns>測試 handler 所建立的已完成 operation 結果。</returns>
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
    /// 用兩個獨立 completion 模擬 A/B 非同步交錯的 test double。
    /// TaskCompletionSource 採用非同步 continuation，避免 completion thread 直接執行 consumer；它不建立 timer、
    /// cache、lease 或取消註冊，且所有 pending state 在測試結束時由 test instance 一起釋放。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly TaskCompletionSource<OperationExecutionResult> _aCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _bCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 依 deployment-owned profile marker 路由到該測試專屬 completion。
        /// profile 在這裡只作為 test scheduling key，不能代表 production caller authority；未知 profile 立即失敗，
        /// 防止測試誤把 A 或 B 回應交給其他 request。
        /// </summary>
        /// <param name="request">client 已驗證並送出的封閉 request。</param>
        /// <param name="cancellationToken">未由 fake 擁有或註冊的取消權杖。</param>
        /// <returns>對應 A 或 B 的獨立 pending task。</returns>
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
        /// 完成 A 的獨立 response，不會改寫 B 的 completion 或資料列。
        /// </summary>
        /// <param name="result">要交付給 A request 的成功結果。</param>
        public void CompleteA(OperationExecutionResult result)
            => _aCompletion.TrySetResult(result).Should().BeTrue();

        /// <summary>
        /// 完成 B 的獨立 response，不會改寫 A 的 completion 或資料列。
        /// </summary>
        /// <param name="result">要交付給 B request 的成功結果。</param>
        public void CompleteB(OperationExecutionResult result)
            => _bCompletion.TrySetResult(result).Should().BeTrue();
    }
}
