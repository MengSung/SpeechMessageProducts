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
}
