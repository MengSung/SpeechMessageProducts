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
