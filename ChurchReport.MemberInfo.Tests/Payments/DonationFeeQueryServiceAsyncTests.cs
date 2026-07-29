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

public sealed class DonationFeeQueryServiceAsyncTests
{
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
