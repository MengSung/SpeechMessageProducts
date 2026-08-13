// ============================================================================
// 檔案路徑：ChurchReport.MemberInfo.Tests/Services/DonationBookingReadServiceTests.cs
// 檔案責任：以無外部 I/O 的 typed client double 驗證 P7.4 認獻單讀取邊界的 gate 後服務契約、
//           cancellation、DTO 驗證、跨請求隔離與 model 原子發布規則。
// 生命週期：每個測試自行建立 service、options、fake client 與 model；不使用 static collection、
//           Session、HttpContext、CRM Entity、timer、cache、背景工作或可重用 lease。
// ============================================================================

using ChurchReport.Models;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證認獻單 read service 與 adapter 的最小安全契約。測試以不同 contact 的 synthetic marker
/// 交錯執行，證明 service 不會把上一個 request 的 DTO、profile、response 或 model 放進共享狀態。
/// 故障注入涵蓋取消與不完整 DTO；決定性斷言是：呼叫失敗時不發布 partial result，既有 request-local
/// model list 維持同一個 instance，成功時才以新 list 一次性替換。
/// </summary>
public sealed class DonationBookingReadServiceTests
{
    /// <summary>
    /// 驗證 service 以 deployment-owned ProfileAlias、固定 workload 與原始 cancellation token 呼叫
    /// typed ProductClient，並在 source DTO collection 後續被呼叫端覆寫時仍保留自己的 immutable result。
    /// 此契約禁止 client 把 session、caller supplied routing 或可變 collection 留在跨 request state。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_uses_server_owned_routing_and_publishes_a_defensive_immutable_result()
    {
        var contactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var source = new List<DedicationBookingRecordDto> { CreateValidRow("A") };
        var client = new RecordingDedicationBookingReadClient(
            (_, _) => Task.FromResult<IReadOnlyList<DedicationBookingRecordDto>>(source));
        var service = new DonationBookingReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "crm91" }));
        using var cancellation = new CancellationTokenSource();

        var result = await service.RetrieveAsync(contactId, cancellation.Token);
        source[0] = CreateValidRow("mutated");

        client.RequestCount.Should().Be(1);
        client.ObservedProfileAlias.Should().Be("crm91");
        client.ObservedWorkloadSubject.Should().Be("church-report-dedication-booking-read");
        client.ObservedContactId.Should().Be(contactId);
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
        result.Rows.Should().ContainSingle();
        result.Rows[0].DedicationCategory.Should().Be("category-A");
    }

    /// <summary>
    /// 驗證 service 在任何 outbound call 前拒絕空 contact ID 與空白 deployment ProfileAlias。這兩個值
    /// 是 server composition 的最小 isolation boundary；若允許缺漏，可能令不同使用者或 profile 共用
    /// 無法證明歸屬的 routing state。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_missing_contact_or_deployment_profile_before_publishing_any_result()
    {
        var client = new RecordingDedicationBookingReadClient(
            (_, _) => Task.FromResult<IReadOnlyList<DedicationBookingRecordDto>>(new[] { CreateValidRow("A") }));
        var profileService = new DonationBookingReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = " " }));
        var validProfileService = new DonationBookingReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "crm91" }));

        Func<Task> emptyContact = () => validProfileService.RetrieveAsync(Guid.Empty);
        Func<Task> emptyProfile = () => profileService.RetrieveAsync(Guid.NewGuid());

        await emptyContact.Should().ThrowAsync<ArgumentException>();
        await emptyProfile.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ProfileAlias*");
        client.RequestCount.Should().Be(0);
    }

    /// <summary>
    /// 注入 null row 與空 booking ID，驗證 service 拒絕整份 response 而不是回傳已驗證的前半段。
    /// 這防止上游 contract drift、部分 transport 回應或錯誤 mapping 讓使用者看到不完整認獻資料。
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidResponseRows))]
    public async Task Retrieve_async_fails_closed_when_any_row_is_null_or_has_no_booking_identity(
        IReadOnlyList<DedicationBookingRecordDto> source)
    {
        var client = new RecordingDedicationBookingReadClient(
            (_, _) => Task.FromResult(source));
        var service = new DonationBookingReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "crm91" }));

        Func<Task> action = () => service.RetrieveAsync(Guid.NewGuid());

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 注入已取消的 typed read，驗證 adapter 不會在取得完整 result 前清空、追加或替換既有 model list。
    /// cancellation 不會啟動 retry/fallback；transport 生命週期仍由既有 ProcessHost owner 處理，adapter
    /// 只負責 request-local projection，因此取消後沒有可被下一位使用者觀察到的 partial state。
    /// </summary>
    [Fact]
    public async Task Populate_async_leaves_the_existing_model_list_unchanged_when_the_typed_read_is_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new RecordingDedicationBookingReadClient(
            (_, token) => Task.FromCanceled<IReadOnlyList<DedicationBookingRecordDto>>(token));
        var service = new DonationBookingReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "crm91" }));
        var adapter = new DonationBookingReadModelAdapter(service);
        var model = new DonationPaymentFormModel();
        model.DedicationBookingList.Add(new DedicationBooking { EntityId = "legacy-marker" });
        var original = model.DedicationBookingList;

        Func<Task> action = () => adapter.PopulateAsync(model, Guid.NewGuid(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        model.DedicationBookingList.Should().BeSameAs(original);
        model.DedicationBookingList.Should().ContainSingle(x => x.EntityId == "legacy-marker");
    }

    /// <summary>
    /// 以兩個 contact marker 交錯執行，驗證每個 adapter 只發布自己的完整 result。成功時以新 list
    /// 取代 model 的舊 list，永遠不共用 DTO 或 `DedicationBooking` collection；此測試是 A/B request
    /// isolation 的最小 regression guard，不需要 CRM、Session 或 shared cache。
    /// </summary>
    [Fact]
    public async Task Populate_async_keeps_interleaved_contact_results_isolated_and_replaces_the_model_list_atomically()
    {
        var contactA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var contactB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var client = new RecordingDedicationBookingReadClient(
            (contactId, _) => Task.FromResult<IReadOnlyList<DedicationBookingRecordDto>>(
                new[] { CreateValidRow(contactId == contactA ? "A" : "B") }));
        var service = new DonationBookingReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "crm91" }));
        var adapter = new DonationBookingReadModelAdapter(service);
        var modelA = new DonationPaymentFormModel();
        var modelB = new DonationPaymentFormModel();
        modelA.DedicationBookingList.Add(new DedicationBooking { EntityId = "old-A" });
        modelB.DedicationBookingList.Add(new DedicationBooking { EntityId = "old-B" });
        var originalA = modelA.DedicationBookingList;
        var originalB = modelB.DedicationBookingList;

        await Task.WhenAll(
            adapter.PopulateAsync(modelA, contactA),
            adapter.PopulateAsync(modelB, contactB));

        modelA.DedicationBookingList.Should().NotBeSameAs(originalA);
        modelB.DedicationBookingList.Should().NotBeSameAs(originalB);
        modelA.DedicationBookingList.Should().ContainSingle(x => x.DedicationCategory == "category-A");
        modelB.DedicationBookingList.Should().ContainSingle(x => x.DedicationCategory == "category-B");
        modelA.DedicationBookingList.Should().NotContain(x => x.DedicationCategory == "category-B");
        modelB.DedicationBookingList.Should().NotContain(x => x.DedicationCategory == "category-A");
    }

    /// <summary>
    /// 提供兩種無法安全發布的 upstream response：第一種含 null row，第二種含空 GUID。資料保持在
    /// request-local test collection，不含 CRM Entity、endpoint、credential 或可跨測試重用的 mutable state。
    /// </summary>
    public static IEnumerable<object[]> InvalidResponseRows()
    {
        yield return new object[] { new List<DedicationBookingRecordDto> { CreateValidRow("valid"), null! } };
        yield return new object[]
        {
            new List<DedicationBookingRecordDto>
            {
                CreateValidRow("valid"),
                CreateValidRow("invalid") with { DedicationBookingId = Guid.Empty }
            }
        };
    }

    /// <summary>
    /// 建立完整、bounded 的 scalar DTO。每個 marker 只用於本測試 assertion，不會傳至 log、static state
    /// 或外部服務；日期採 UTC 以避免測試環境時區造成不穩定的 contract 判斷。
    /// </summary>
    private static DedicationBookingRecordDto CreateValidRow(string marker)
    {
        return new DedicationBookingRecordDto
        {
            DedicationBookingId = Guid.NewGuid(),
            DedicationCategoryOption = 100000000,
            DedicationCategoryLabel = "category-" + marker,
            DedicationBookingStatusOption = 100000001,
            DedicationBookingStatusLabel = "status-" + marker,
            AmountPerStage = 100.75m,
            TotalStages = "12",
            DedicationAmount = 1209.5m,
            PaidPeriod = "monthly",
            RollupPaidFee = 200.25m,
            StartDate = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2027, 7, 31, 0, 0, 0, TimeSpan.Zero)
        };
    }

    /// <summary>
    /// 無 transport/resource owner 的 in-memory typed client double。它只在單一測試生命週期內記錄
    /// request scalar，讓測試證明 routing 與 cancellation 正確傳遞；不保存 DTO response、session、
    /// profile cache 或任何會跨 request 留存的資源。
    /// </summary>
    private sealed class RecordingDedicationBookingReadClient : IPackage01DedicationBookingReadClient
    {
        private readonly Func<Guid, CancellationToken, Task<IReadOnlyList<DedicationBookingRecordDto>>> _retrieve;

        /// <summary>取得已觀測的呼叫次數；僅由目前 test instance 使用，沒有 static 或跨測試共享。</summary>
        public int RequestCount { get; private set; }

        /// <summary>取得 service 傳遞的 deployment-owned profile alias，不含 endpoint 或 credential。</summary>
        public string? ObservedProfileAlias { get; private set; }

        /// <summary>取得固定 server workload，驗證它不是瀏覽器、Session 或 caller input。</summary>
        public string? ObservedWorkloadSubject { get; private set; }

        /// <summary>取得本次 request 的 server-authorized contact ID，不會在 response 後保留模型資料。</summary>
        public Guid ObservedContactId { get; private set; }

        /// <summary>取得 forwarded cancellation token，證明 service 沒有忽略 request cancellation。</summary>
        public CancellationToken ObservedCancellationToken { get; private set; }

        /// <summary>
        /// 建立 test-local fake；delegate 的 owner 是個別 test，完成後與 test instance 一起被 GC，不產生
        /// background work、subscription、handle、timer 或 connection retention。
        /// </summary>
        public RecordingDedicationBookingReadClient(
            Func<Guid, CancellationToken, Task<IReadOnlyList<DedicationBookingRecordDto>>> retrieve)
        {
            _retrieve = retrieve ?? throw new ArgumentNullException(nameof(retrieve));
        }

        /// <summary>
        /// 記錄 typed request scalar 後立即委派給 test-local delegate。contactName 不作 routing authority，
        /// 因此不儲存或檢查它；production service 也不會由 caller 提供 profile、endpoint 或 credential。
        /// </summary>
        public Task<IReadOnlyList<DedicationBookingRecordDto>> RetrieveDedicationBookingsByContactAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            string? contactName = null,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            ObservedProfileAlias = profileAlias;
            ObservedWorkloadSubject = workloadSubjectId;
            ObservedContactId = contactId;
            ObservedCancellationToken = cancellationToken;
            return _retrieve(contactId, cancellationToken);
        }
    }
}
