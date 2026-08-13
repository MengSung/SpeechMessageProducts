// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/Package01DedicationBookingReadClientTests.cs
// 用途：以純記憶體 executor 驗證 P7.1 認獻單讀取 ProductClient 的封閉 capability
//       邊界。測試僅接觸具型別 request、response 與 DTO，不建立 CE、網路、connector、
//       ToolUtility 或 CRM SDK Entity。
//
// 隔離與生命週期契約：
// 1. 每次呼叫都必須以 deployment-owned profile/workload、typed contactId 與原始取消
//    token 建立獨立 request；替身不保存任何跨測試的 session、connection、lease、cache
//    或 principal。
// 2. 成功資料只能來自 operation ID、discriminator 與 dedicated-booking branch 都精確
//    符合的 OperationResponseData；任何錯配或缺 branch 都在公開 DTO 配置前 fail-closed。
// 3. wire collection 與 A/B 結果均須在 request 範圍建立新 DTO list，避免 profile、使用者或
//    回應資料透過可變集合、快取或前一次呼叫交叉洩漏。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.FeeReads;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P7.1 認獻單讀取用戶端只消費 dedicated booking 的封閉 response branch。
/// 此類別以有辨識度的 A/B 假資料守護跨 profile、workload 與 request 的資料隔離；所有替身
/// 都是測試個體私有且無外部資源 owner，故不會建立 CRM 連線、保留 session 或延長 connector lease。
/// </summary>
public sealed class Package01DedicationBookingReadClientTests
{
    /// <summary>
    /// 保護 client 只傳送已登錄的 payments.dedication.retrieve.by.contact，以及完整轉送 server-owned
    /// profile、workload、typed contactId、可選 contactName 與同一個 cancellation token。故障注入使用
    /// 純記憶體 dedicated branch；決定性斷言拒絕任何未登錄參數，並逐欄確認公開 DTO 不需要 CE、網路或
    /// CRM SDK Entity 才能形成。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_forwards_the_exact_closed_request_and_maps_the_dedicated_branch()
    {
        var contactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(request => CreateDedicatedBookingResult(
            request.CapabilityOperationId,
            CreateRecord("booking-A")));
        var client = CreateClient(executor);

        var rows = await client.RetrieveDedicationBookingsByContactAsync(
            "server-owned-profile-A",
            "church-report-payment-read",
            contactId,
            "contact-name-A",
            cancellationSource.Token);

        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.CapabilityOperationId.Should().Be(
            OperationIds.PaymentsDedicationRetrieveByContact);
        executor.LastRequest.ProfileAlias.Should().Be("server-owned-profile-A");
        executor.LastRequest.WorkloadSubjectId.Should().Be("church-report-payment-read");
        executor.LastRequest.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["contactId"] = contactId,
            ["contactName"] = "contact-name-A"
        });
        executor.LastCancellationToken.Should().Be(cancellationSource.Token);

        rows.Should().ContainSingle();
        rows[0].DedicationBookingId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        rows[0].DedicationCategoryOption.Should().Be(100000007);
        rows[0].DedicationCategoryLabel.Should().Be("category-A");
        rows[0].DedicationBookingStatusOption.Should().Be(100000001);
        rows[0].DedicationBookingStatusLabel.Should().Be("status-A");
        rows[0].AmountPerStage.Should().Be(500m);
        rows[0].TotalStages.Should().Be("12");
        rows[0].DedicationAmount.Should().Be(6000m);
        rows[0].PaidPeriod.Should().Be("2026-08");
        rows[0].RollupPaidFee.Should().Be(1500m);
        rows[0].StartDate.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        rows[0].EndDate.Should().Be(new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// 保護 optional contactName 在未提供時不會被補成前一位使用者的字串或未登錄預設值。故障注入是
    /// 不提供名稱的正常呼叫；決定性斷言只允許 contactId 留在 request，確保名稱不能成為查詢 authority、
    /// session cache key 或跨使用者 retained state。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_omits_the_optional_contact_name_when_it_is_not_supplied()
    {
        var contactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var executor = new RecordingExecutor(request => CreateDedicatedBookingResult(
            request.CapabilityOperationId,
            CreateRecord("booking-without-name")));
        var client = CreateClient(executor);

        await client.RetrieveDedicationBookingsByContactAsync(
            "server-owned-profile-A",
            "church-report-payment-read",
            contactId);

        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["contactId"] = contactId
        });
    }

    /// <summary>
    /// 保護回應 operation ID 是請求身分的一部分。故障注入讓 executor 回傳內容有效但屬於另一能力的
    /// dedicated booking branch；決定性斷言為 InvalidOperationException，證明 client 在建立任何 DTO
    /// 前 fail-closed，不能將錯路由、重試或其他 profile 的結果發布給本次呼叫者。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_dedicated_branch_with_a_mismatched_operation_id()
    {
        var executor = new RecordingExecutor(_ => CreateDedicatedBookingResult(
            OperationIds.FeeDedicationRetrieveByContact,
            CreateRecord("wrong-operation")));
        var client = CreateClient(executor);

        var act = () => client.RetrieveDedicationBookingsByContactAsync(
            "server-owned-profile-A",
            "church-report-payment-read",
            Guid.Parse("33333333-3333-3333-3333-333333333333"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 dedicated booking reader 不會把另一個合法 union discriminator 當成本能力資料。故障注入是
    /// 同一 operation ID 的 fee branch；決定性斷言要求在 DTO 配置前拒絕，避免金融 fee record 因 response
    /// 分支錯配被誤解為認獻 booking，且替身不涉及任何 CE 或 connector 資源。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_wrong_response_discriminator_before_mapping()
    {
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(
            OperationResponseData.ForPackage01FeeRecords(
                OperationIds.PaymentsDedicationRetrieveByContact,
                "9.1",
                Array.Empty<Package01FeeRecord>())));
        var client = CreateClient(executor);

        var act = () => client.RetrieveDedicationBookingsByContactAsync(
            "server-owned-profile-A",
            "church-report-payment-read",
            Guid.Parse("44444444-4444-4444-4444-444444444444"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護成功 envelope 若沒有 dedicated booking branch，仍不可被視為空資料成功。故障注入是 data 為 null
    /// 的成功 executor 結果；決定性斷言要求 fail-closed，避免前一次 list、快取或 partial response 被拿來
    /// 補成此次結果。這個 client 沒有 cache、lease 或 stream owner，故唯一安全行為是拒絕缺少 branch 的資料。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_a_success_result_without_the_dedicated_booking_branch()
    {
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(null));
        var client = CreateClient(executor);

        var act = () => client.RetrieveDedicationBookingsByContactAsync(
            "server-owned-profile-A",
            "church-report-payment-read",
            Guid.Parse("55555555-5555-5555-5555-555555555555"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 wire collection 在 response envelope 建立後即被 snapshot，且 client 再建立新的 DTO list。故障注入
    /// 會在 executor 使用 envelope 前清空並改寫原始來源集合；決定性斷言仍只看見初始 A marker，證明 source
    /// collection 的後續 mutation 無法污染公開 DTO，也沒有藉由 shared collection 保留到下一個 request。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_isolated_from_mutation_of_the_source_wire_collection_after_envelope_creation()
    {
        var sourceRecords = new List<Package01DedicationBookingRecord>
        {
            CreateRecord("booking-source-A")
        };
        var response = CreateDedicatedBookingResult(
            OperationIds.PaymentsDedicationRetrieveByContact,
            sourceRecords);
        sourceRecords.Clear();
        sourceRecords.Add(CreateRecord("booking-source-B"));

        var executor = new RecordingExecutor(_ => response);
        var client = CreateClient(executor);

        var rows = await client.RetrieveDedicationBookingsByContactAsync(
            "server-owned-profile-A",
            "church-report-payment-read",
            Guid.Parse("66666666-6666-6666-6666-666666666666"));

        rows.Should().ContainSingle();
        rows[0].DedicationCategoryLabel.Should().Be("category-source-A");
        rows[0].DedicationBookingStatusLabel.Should().Be("status-source-A");
        rows[0].Should().NotBeSameAs(sourceRecords[0]);
    }

    /// <summary>
    /// 保護交錯完成的 A/B executor 呼叫沒有共用 DTO list、wire row 或上一個回應的顯示字串。故障注入刻意先
    /// 完成 B 再完成 A；決定性斷言要求兩個結果各自保有 profile 對應 marker 並使用不同 list/row 實例。這模擬
    /// 非同步完成次序改變時的跨使用者隔離，且沒有連線、計時器、背景工作或可重用 connector。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_keeps_interleaved_a_and_b_results_in_separate_dto_collections()
    {
        var contactA = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var contactB = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var executor = new InterleavingExecutor(contactA, contactB);
        var client = CreateClient(executor);

        var aTask = client.RetrieveDedicationBookingsByContactAsync(
            "server-owned-profile-A",
            "workload-A",
            contactA,
            "contact-A");
        var bTask = client.RetrieveDedicationBookingsByContactAsync(
            "server-owned-profile-B",
            "workload-B",
            contactB,
            "contact-B");

        executor.CompleteB(CreateDedicatedBookingResult(
            OperationIds.PaymentsDedicationRetrieveByContact,
            CreateRecord("booking-B")));
        var bRows = await bTask;
        executor.CompleteA(CreateDedicatedBookingResult(
            OperationIds.PaymentsDedicationRetrieveByContact,
            CreateRecord("booking-A")));
        var aRows = await aTask;

        aRows.Should().ContainSingle();
        bRows.Should().ContainSingle();
        aRows[0].DedicationCategoryLabel.Should().Be("category-A");
        aRows[0].DedicationBookingStatusLabel.Should().Be("status-A");
        bRows[0].DedicationCategoryLabel.Should().Be("category-B");
        bRows[0].DedicationBookingStatusLabel.Should().Be("status-B");
        aRows.Should().NotBeSameAs(bRows);
        aRows[0].Should().NotBeSameAs(bRows[0]);
    }

    /// <summary>
    /// 建立受測 client；logger 是無狀態 NullLogger，executor 是單一測試私有替身。helper 不建立 HTTP、
    /// CRM、Data8 client、lease、stream 或 timer，因此沒有必須跨測試釋放的外部資源。
    /// </summary>
    private static Package01DedicationBookingReadClient CreateClient(IDynamicsOperationExecutor executor)
        => new(executor, NullLogger<Package01DedicationBookingReadClient>.Instance);

    /// <summary>
    /// 建立封閉 dedicated-booking response。operation ID 明確由呼叫端供給，讓錯 operation 的故障注入可
    /// 證明 client 先驗證身分再映射；來源列只包含 allowlisted scalar，不含 FetchXML、Entity、connector 或
    /// credential。
    /// </summary>
    private static OperationExecutionResult CreateDedicatedBookingResult(
        string operationId,
        IEnumerable<Package01DedicationBookingRecord> records)
        => OperationExecutionResult.Success(
            OperationResponseData.ForPackage01DedicationBookingRecords(operationId, "9.1", records));

    /// <summary>
    /// 保留單筆與多筆 row 的簡潔測試呼叫方式，同時委派至集合版本，避免測試資料在建立
    /// envelope 前後的集合快照行為出現兩套實作。此 helper 不保存 row、executor 或任何跨測試狀態。
    /// </summary>
    /// <param name="operationId">測試欲建立的封閉 capability operation 識別碼。</param>
    /// <param name="records">本次測試專屬、稍後會交由 response factory 防禦性複製的 wire rows。</param>
    /// <returns>不含 CRM、connector 或 lease 的成功 response envelope。</returns>
    private static OperationExecutionResult CreateDedicatedBookingResult(
        string operationId,
        params Package01DedicationBookingRecord[] records)
        => CreateDedicatedBookingResult(
            operationId,
            (IEnumerable<Package01DedicationBookingRecord>)records);

    /// <summary>
    /// 建立帶有可辨識 marker 的 immutable wire row。不同 marker 對應不同 category/status 顯示字串，供 A/B
    /// 隔離與來源集合 mutation 測試精確辨識；record 本身沒有外部資源、session 或可變集合所有權。
    /// </summary>
    private static Package01DedicationBookingRecord CreateRecord(string marker)
        => new()
        {
            DedicationBookingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DedicationCategoryOption = 100000007,
            DedicationCategoryLabel = marker.Replace("booking-", "category-", StringComparison.Ordinal),
            DedicationBookingStatusOption = 100000001,
            DedicationBookingStatusLabel = marker.Replace("booking-", "status-", StringComparison.Ordinal),
            AmountPerStage = 500m,
            TotalStages = "12",
            DedicationAmount = 6000m,
            PaidPeriod = "2026-08",
            RollupPaidFee = 1500m,
            StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)
        };

    /// <summary>
    /// 記錄單次呼叫的純記憶體 executor。它只持有本測試建立的 request 參考和 token 以供斷言，沒有 static
    /// state、網路、CE、connector、lease、timer 或 cancellation registration；測試結束後整個替身可被回收。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 建立測試案例專屬的結果委派。委派在當次同步 ExecuteAsync 使用，故不會將 profile、workload、
        /// contact 或 response 儲存在其他案例可觀察的共同集合。
        /// </summary>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// 取得最後一次 request，僅供同一個測試案例斷言固定 capability 和 allowlisted parameters。
        /// 它不是產品 cache，亦不會被注入到另一個 client 或跨 request 重用。
        /// </summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>
        /// 取得 executor 收到的 token，以驗證取消權沒有被替換、註冊或吞掉。替身不對 token 註冊 callback，
        /// 因此不需要 dispose cancellation registration，也不會延長 caller cancellation source 的生命週期。
        /// </summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// 同步回傳本測試指定的封閉結果。此替身不進行 I/O，讓 ProductClient 測試可專注於 request/response
        /// boundary；真正 executor 的 connector lease、fault eviction 與資源清理仍由 production owner 管理。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_handler(request));
        }
    }

    /// <summary>
    /// 控制 A/B 非同步完成順序的純記憶體 executor。每個 contact 有自己的 TaskCompletionSource，沒有 shared
    /// response list、cache 或 connector；Complete 方法只可完成相符 contact 的 request，避免替身本身掩蓋
    /// client 應守護的 profile/request 隔離缺陷。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly Guid _contactA;
        private readonly Guid _contactB;
        private readonly TaskCompletionSource<OperationExecutionResult> _aCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _bCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 建立以兩個已驗證 contact GUID 分流的替身。GUID 只用於測試內的 deterministic routing，不會成為
        /// 產品 profile、tenant、credential 或 CRM query authority，也不配置外部資源。
        /// </summary>
        public InterleavingExecutor(Guid contactA, Guid contactB)
        {
            _contactA = contactA;
            _contactB = contactB;
        }

        /// <summary>
        /// 依 request 的 typed contactId 傳回對應的 pending task。未知或缺少 GUID 立即 fail-closed，確保測試
        /// 不會意外將 A 的 completion 借給 B；取消 token 不會被註冊或保存，故沒有需釋放的 callback。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Parameters.TryGetValue("contactId", out var value) && value is Guid contactId)
            {
                if (contactId == _contactA)
                {
                    return _aCompletion.Task;
                }

                if (contactId == _contactB)
                {
                    return _bCompletion.Task;
                }
            }

            throw new InvalidOperationException("The test executor received an unknown contactId.");
        }

        /// <summary>
        /// 先或後完成 A 的指定 response；TrySetResult 保證同一 response 不會被重新發布到 B 或另一個測試。
        /// 完成後 TaskCompletionSource 不擁有外部 handle、stream、lease 或背景工作。
        /// </summary>
        public void CompleteA(OperationExecutionResult result)
            => _aCompletion.TrySetResult(result).Should().BeTrue();

        /// <summary>
        /// 先或後完成 B 的指定 response；此方法與 A 使用不同 completion，讓測試可證明映射結果不隨完成
        /// 次序交叉覆寫。它不保存 profile、principal、credential 或 DTO collection。
        /// </summary>
        public void CompleteB(OperationExecutionResult result)
            => _bCompletion.TrySetResult(result).Should().BeTrue();
    }
}
