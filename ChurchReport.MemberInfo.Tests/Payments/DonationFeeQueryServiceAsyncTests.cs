using ChurchReport.Models;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Models;
using ToolUtilityNameSpace;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 Package 01 費用查詢的真正非同步與取消隔離契約。
/// 測試使用可控制完成時點的 fake client，確保要求執行緒不會 sync-over-async，且取消時不會把半成品寫回共用表單模型。
/// </summary>
public sealed class DonationFeeQueryServiceAsyncTests
{
    /// <summary>
    /// I/O 尚未完成時服務方法也必須維持未完成狀態，證明沒有 GetAwaiter().GetResult() 或其他同步阻塞。
    /// </summary>
    [Fact]
    public async Task Package01_fee_read_does_not_block_the_request_thread()
    {
        var source = new TaskCompletionSource<IReadOnlyList<FeeRecordDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new DeferredFeeReadClient(source.Task);
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new DonationFeeQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);
        var model = new DonationPaymentFormModel
        {
            FullName = "isolated-test-user",
            QueryStartDate = new DateTime(2026, 1, 1),
            QueryEndDate = new DateTime(2026, 1, 31)
        };
        var contact = new Entity("contact", Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var operation = service.FillFeeListAsync(model, contact, CancellationToken.None);

        operation.IsCompleted.Should().BeFalse(
            "the Package 01 path must await I/O instead of using sync-over-async");

        source.SetResult(new[]
        {
            new FeeRecordDto
            {
                Amount = 1250m,
                CategoryLabel = "十一奉獻",
                PayWayLabel = "信用卡",
                CreatedOn = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                PayDate = new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero)
            }
        });

        await operation;

        model.TotalAmount.Should().Be(1250);
        model.DedicationFeeList.Should().ContainSingle();
        client.ObservedCancellationToken.CanBeCanceled.Should().BeFalse();
    }

    /// <summary>
    /// 呼叫端 CancellationToken 必須原封不動傳到產品 client；取消例外向上傳播，原模型內容保持原子不變。
    /// </summary>
    [Fact]
    public async Task Package01_fee_read_propagates_cancellation_without_mutating_the_model()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new DeferredFeeReadClient(Task.FromCanceled<IReadOnlyList<FeeRecordDto>>(
            new CancellationToken(canceled: true)));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new DonationFeeQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);
        var originalFee = new DedicationFee { Amount = 99 };
        var model = new DonationPaymentFormModel
        {
            FullName = "isolated-test-user",
            TotalAmount = 99,
            DedicationFeeList = new List<DedicationFee> { originalFee }
        };
        var contact = new Entity("contact", Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Func<Task> act = () => service.FillFeeListAsync(model, contact, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        model.TotalAmount.Should().Be(99);
        model.DedicationFeeList.Should().ContainSingle().Which.Should().BeSameAs(originalFee);
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 保護 Package01 受控讀取的投影失敗原子性。故障注入回傳含有無效項目的 DTO 集合，
    /// 模擬 connector 已成功回應但產品投影尚未完成的邊界；決定性斷言是例外傳出後，
    /// 原本屬於此 request 的表單總額與清單參考均不被半成品覆寫，避免後續同一 request
    /// 以錯誤資料繼續處理，也避免任何共享或跨使用者狀態被引入。
    /// </summary>
    [Fact]
    public async Task Package01_fee_projection_fault_does_not_mutate_the_model()
    {
        var client = new DeferredFeeReadClient(
            Task.FromResult<IReadOnlyList<FeeRecordDto>>(new FeeRecordDto[] { null! }));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new DonationFeeQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);
        var originalFee = new DedicationFee { Amount = 88 };
        var model = new DonationPaymentFormModel
        {
            FullName = "isolated-test-user",
            TotalAmount = 88,
            DedicationFeeList = new List<DedicationFee> { originalFee }
        };
        var contact = new Entity("contact", Guid.Parse("33333333-3333-3333-3333-333333333333"));

        Func<Task> act = () => service.FillFeeListAsync(model, contact, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        model.TotalAmount.Should().Be(88);
        model.DedicationFeeList.Should().ContainSingle().Which.Should().BeSameAs(originalFee);
    }

    /// <summary>
    /// 保護 Package01 金額加總的溢位語意。故障注入兩筆 individually valid、但總和超出
    /// <see cref="int.MaxValue"/> 的 DTO；決定性斷言是服務 fail-closed 並保留原 model，
    /// 而不是讓未檢查的整數環繞產生負的金額或將半成品資料交給後續請求流程。
    /// </summary>
    [Fact]
    public async Task Package01_fee_total_overflow_fails_closed_without_mutating_the_model()
    {
        var client = new DeferredFeeReadClient(
            Task.FromResult<IReadOnlyList<FeeRecordDto>>(new[]
            {
                new FeeRecordDto { Amount = int.MaxValue },
                new FeeRecordDto { Amount = 1 }
            }));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new DonationFeeQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);
        var originalFee = new DedicationFee { Amount = 77 };
        var model = new DonationPaymentFormModel
        {
            FullName = "isolated-test-user",
            TotalAmount = 77,
            DedicationFeeList = new List<DedicationFee> { originalFee }
        };
        var contact = new Entity("contact", Guid.Parse("44444444-4444-4444-4444-444444444444"));

        Func<Task> act = () => service.FillFeeListAsync(model, contact, CancellationToken.None);

        await act.Should().ThrowAsync<OverflowException>();
        model.TotalAmount.Should().Be(77);
        model.DedicationFeeList.Should().ContainSingle().Which.Should().BeSameAs(originalFee);
    }

    /// <summary>
    /// legacy intake 已停止時，受控 fee boundary 必須在進入 ToolUtility CRM 呼叫前 fail closed。
    /// 測試使用已停止的本機 controller；沒有可觀察的 lease 時不允許退回未受控 legacy 呼叫。
    /// </summary>
    [Fact]
    public async Task Stopped_legacy_intake_rejects_before_toolutility_call()
    {
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        await using var controller = new LegacyToolUtilityDrainController();
        (await controller.StopIntakeAndDrainAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None)).Should().Be(LegacyToolUtilityDrainResult.Drained);

        var service = new DonationFeeQueryService(
            utility,
            package01FeeReadClient: null,
            dynamicsAccess: null,
            package01FeeReadsEnabled: false,
            legacyDrainController: controller);
        var model = new DonationPaymentFormModel
        {
            FullName = "isolated-test-user",
            QueryStartDate = new DateTime(2026, 1, 1),
            QueryEndDate = new DateTime(2026, 1, 31)
        };
        var contact = new Entity("contact", Guid.NewGuid());

        Func<Task> act = () => service.FillFeeListAsync(model, contact, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Legacy ToolUtility intake is stopped.");
    }

    /// <summary>
    /// 奉獻稽核的 Package01 路徑必須使用受限的「依 contact、不分日期」operation，並由
    /// service 固定帶入 deployment profile 與 server-owned workload subject。fault injection
    /// fake 記錄所有輸入；決定性斷言是瀏覽器目標只以 GUID 傳入、姓名固定為 null，且回傳
    /// 的 rows/total 是新的 request-local result，不需要也不會接收付款表單模型。
    /// </summary>
    [Fact]
    public async Task Package01_fee_audit_uses_contact_operation_with_null_name_and_request_local_result()
    {
        var client = new AuditFeeReadClient(contactId => Task.FromResult<IReadOnlyList<FeeRecordDto>>(new[]
        {
            new FeeRecordDto
            {
                Amount = 1250m,
                CategoryLabel = "一般奉獻",
                PayWayLabel = "信用卡",
                CreatedOn = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
                PayDate = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero)
            }
        }));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        using var cancellation = new CancellationTokenSource();
        var service = new DonationFeeQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);
        var targetContactId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var result = await service.RetrieveFeeAuditByContactAsync(targetContactId, cancellation.Token);

        result.TotalAmount.Should().Be(1250);
        result.Fees.Should().ContainSingle().Which.Amount.Should().Be(1250);
        typeof(DonationFeeAuditRow).GetProperties()
            .Should().OnlyContain(property => property.SetMethod == null,
                "typed audit rows must be immutable DTOs rather than mutable payment form models");
        result.Fees.Should().NotBeAssignableTo<DonationFeeAuditRow[]>(
            "the request-local result must not expose its copied array for later replacement by another flow");
        var writableFees = result.Fees as IList<DonationFeeAuditRow>;
        writableFees.Should().NotBeNull(
            "the result may expose a read-only collection interface, but the mutation attempt must be rejected");
        Action replaceRow = () => writableFees![0] = new DonationFeeAuditRow(
            "不應寫入",
            DateTime.MinValue,
            DateTime.MinValue,
            "不應寫入",
            0,
            string.Empty,
            string.Empty);
        replaceRow.Should().Throw<NotSupportedException>(
            "a caller must not replace a row after the request-local audit result is published");
        client.ProfileAlias.Should().Be("church-report-test");
        client.WorkloadSubjectId.Should().Be("church-report-service");
        client.ContactId.Should().Be(targetContactId);
        client.ContactName.Should().BeNull();
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 兩個不同聯絡人的 typed audit I/O 以交錯方式完成時，結果不得共用集合、總額或任一
    /// contact 的資料。fake 每個 contact 各有一個 request-local completion source；這能偵測
    /// static/cache/表單模型重用造成的 A/B 資料混入，而不建立 CRM、Session 或背景資源。
    /// </summary>
    [Fact]
    public async Task Package01_fee_audit_keeps_interleaved_contact_results_isolated()
    {
        var firstId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var secondId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var first = new TaskCompletionSource<IReadOnlyList<FeeRecordDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<IReadOnlyList<FeeRecordDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new AuditFeeReadClient(contactId => contactId == firstId ? first.Task : second.Task);
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new DonationFeeQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);

        var firstOperation = service.RetrieveFeeAuditByContactAsync(firstId, CancellationToken.None);
        var secondOperation = service.RetrieveFeeAuditByContactAsync(secondId, CancellationToken.None);
        second.SetResult(new[] { new FeeRecordDto { Amount = 22m, CategoryLabel = "B" } });
        first.SetResult(new[] { new FeeRecordDto { Amount = 11m, CategoryLabel = "A" } });

        var results = await Task.WhenAll(firstOperation, secondOperation);

        results[0].TotalAmount.Should().Be(11);
        results[0].Fees.Should().ContainSingle().Which.Category.Should().Be("A");
        results[1].TotalAmount.Should().Be(22);
        results[1].Fees.Should().ContainSingle().Which.Category.Should().Be("B");
        results[0].Fees.Should().NotBeSameAs(results[1].Fees);
    }

    /// <summary>
    /// 取消與 Int32 總額溢位都必須在 typed audit 結果發布前 fail closed。因為此 API 不接收
    /// DonationPaymentFormModel，測試同時保護「已取消或不合法結果不會改寫 session-owned
    /// form」的設計；取消 token 必須由 controller 原樣傳到底層 client，不可重試或轉舊路徑。
    /// </summary>
    [Fact]
    public async Task Package01_fee_audit_forwards_cancellation_and_rejects_overflow_before_result()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelledClient = new AuditFeeReadClient(_ =>
            Task.FromCanceled<IReadOnlyList<FeeRecordDto>>(new CancellationToken(canceled: true)));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var cancelledService = new DonationFeeQueryService(
            utility,
            cancelledClient,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);

        Func<Task> cancelled = () => cancelledService.RetrieveFeeAuditByContactAsync(Guid.NewGuid(), cancellation.Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        cancelledClient.ObservedCancellationToken.Should().Be(cancellation.Token);

        var overflowingClient = new AuditFeeReadClient(_ => Task.FromResult<IReadOnlyList<FeeRecordDto>>(new[]
        {
            new FeeRecordDto { Amount = int.MaxValue },
            new FeeRecordDto { Amount = 1m }
        }));
        var overflowingService = new DonationFeeQueryService(
            utility,
            overflowingClient,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);

        Func<Task> overflowing = () => overflowingService.RetrieveFeeAuditByContactAsync(Guid.NewGuid(), CancellationToken.None);

        await overflowing.Should().ThrowAsync<OverflowException>();
    }

    private sealed class DeferredFeeReadClient : IPackage01FeeReadClient
    {
        // 此 fake 只記錄本次呼叫的取消權杖，不使用 static 或共享狀態，避免測試彼此污染。
        private readonly Task<IReadOnlyList<FeeRecordDto>> _result;

        public DeferredFeeReadClient(Task<IReadOnlyList<FeeRecordDto>> result) => _result = result;

        public CancellationToken ObservedCancellationToken { get; private set; }

        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactDateRangeAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            DateTime startDate,
            DateTime endDate,
            string? contactName = null,
            CancellationToken cancellationToken = default)
        {
            ObservedCancellationToken = cancellationToken;
            return _result;
        }

        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            string? contactName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FeeRecordDto>> RetrieveFeesByDedicationPeriodAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid dedicationBookingId,
            string paidPeriod,
            string? dedicationBookingName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveFeeEditorRowsByDiscipleLessonAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid discipleLessonId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByContactAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            string? contactName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByDiscipleLessonAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid discipleLessonId,
            string? lessonName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// 僅供 typed audit 契約測試使用的 request-local fake。它不儲存 CRM Entity、Session 或
    /// static state；每個 instance 只記錄單一測試安排的受限參數，以證明 service 沒有把
    /// caller 文字、可變表單或非 deployment-owned routing 資料送往 ProductClient。
    /// </summary>
    private sealed class AuditFeeReadClient : IPackage01FeeReadClient
    {
        private readonly Func<Guid, Task<IReadOnlyList<FeeRecordDto>>> _retrieveByContact;

        public AuditFeeReadClient(Func<Guid, Task<IReadOnlyList<FeeRecordDto>>> retrieveByContact)
            => _retrieveByContact = retrieveByContact;

        public string? ProfileAlias { get; private set; }

        public string? WorkloadSubjectId { get; private set; }

        public Guid ContactId { get; private set; }

        public string? ContactName { get; private set; }

        public CancellationToken ObservedCancellationToken { get; private set; }

        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            string? contactName = null,
            CancellationToken cancellationToken = default)
        {
            ProfileAlias = profileAlias;
            WorkloadSubjectId = workloadSubjectId;
            ContactId = contactId;
            ContactName = contactName;
            ObservedCancellationToken = cancellationToken;
            return _retrieveByContact(contactId);
        }

        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactDateRangeAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            DateTime startDate,
            DateTime endDate,
            string? contactName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FeeRecordDto>> RetrieveFeesByDedicationPeriodAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid dedicationBookingId,
            string paidPeriod,
            string? dedicationBookingName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveFeeEditorRowsByDiscipleLessonAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid discipleLessonId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByContactAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            string? contactName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByDiscipleLessonAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid discipleLessonId,
            string? lessonName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
