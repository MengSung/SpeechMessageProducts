using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WorkerProtocol;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Supervisor 將已通過 Worker IPC 的 Package01 分頁列資料投影為 SDK-free
/// <see cref="Package01FeeRecord"/> 集合。測試涵蓋順序、nullable 欄位、數值與時間轉換、
/// operation-specific 大小限制及錯誤 shape；投影器不得保存 WorkerValue、Session、Credential、
/// Token、Pipe、Process 或任何跨要求 mutable state。
/// </summary>
public sealed class OfficialWorkerPackage01ProjectionTests
{
    private static readonly DateTimeOffset CreatedOn =
        new(2026, 8, 2, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset PayDate =
        new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 證明多頁結果只依原始 page／row 順序攤平一次，所有 scalar 轉成封閉 DTO，
    /// nullable 欄位維持 null，且不暴露 WorkerValue 或上游 SDK object。
    /// </summary>
    [Fact]
    public void Project_flattens_pages_and_preserves_typed_cells()
    {
        var feeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var result = WorkerValue.FromArray(
        [
            WorkerValue.FromArray(
            [
                CreateRow(
                    feeId,
                    CreatedOn,
                    PayDate,
                    1234.56m,
                    100000001,
                    "信用卡",
                    "奉獻",
                    "備註",
                    "2026-08",
                    "A-001"),
                CreateRow(
                    null,
                    null,
                    null,
                    0m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null)
            ]),
            WorkerValue.FromArray(
            [
                CreateRow(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    CreatedOn.AddDays(1),
                    PayDate.AddDays(1),
                    9m,
                    100000002,
                    "現金",
                    "會費",
                    null,
                    "2026-09",
                    "A-002")
            ])
        ]);

        var projected = Package01FeeWorkerResponseProjector.Project(
            OperationIds.FeeDedicationRetrieveByContactDateRange,
            "9.1",
            result);

        projected.OperationId.Should().Be(OperationIds.FeeDedicationRetrieveByContactDateRange);
        projected.CeVersion.Should().Be("9.1");
        projected.ResponseKind.Should().Be(OperationResponseKind.Package01FeeRecords);
        projected.FeeRecords.Should().HaveCount(3);
        projected.FeeRecords![0].Should().BeEquivalentTo(new Package01FeeRecord
        {
            FeeId = feeId,
            CreatedOn = CreatedOn,
            PayDate = PayDate,
            Amount = 1234.56m,
            PayWayOption = 100000001,
            PayWayLabel = "信用卡",
            CategoryLabel = "奉獻",
            Others = "備註",
            PaidPeriod = "2026-08",
            Name = "A-001"
        });
        projected.FeeRecords[1].Should().BeEquivalentTo(new Package01FeeRecord());
        projected.FeeRecords[2].Name.Should().Be("A-002");
    }

    /// <summary>
    /// 證明 malformed root 在建立任何 DTO 集合前即以 protocol failure 拒絕，
    /// 不把錯誤資料當作空成功或 partial success。
    /// </summary>
    [Fact]
    public void Project_rejects_malformed_result_shape()
    {
        var result = WorkerValue.FromObject(
            new Dictionary<string, WorkerValue>(StringComparer.Ordinal));

        var action = () => Package01FeeWorkerResponseProjector.Project(
            OperationIds.FeeDedicationRetrieveByContactDateRange,
            "9.1",
            result);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.InvalidEnvelope);
    }

    /// <summary>
    /// 證明 Supervisor 重做 operation-specific page／byte 邊界驗證；超過四頁時不得攤平、
    /// 截斷或回傳部分資料。
    /// </summary>
    [Fact]
    public void Project_rejects_operation_specific_result_overflow()
    {
        var emptyPage = WorkerValue.FromArray(Array.Empty<WorkerValue>());
        var result = WorkerValue.FromArray(
            Enumerable.Repeat(emptyPage, 5).ToArray());

        var action = () => Package01FeeWorkerResponseProjector.Project(
            OperationIds.FeeDedicationRetrieveByContactDateRange,
            "9.1",
            result);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.EnvelopeLimitExceeded);
    }

    /// <summary>
    /// 證明此投影器只能處理固定 Package01 operation，避免其他 operation 誤用相同 row shape
    /// 而繞過 registry revision 與 response-kind 邊界。
    /// </summary>
    [Fact]
    public void Project_rejects_another_operation_identity()
    {
        var result = WorkerValue.FromArray(Array.Empty<WorkerValue>());

        var action = () => Package01FeeWorkerResponseProjector.Project(
            OperationIds.RuntimeHealthWhoAmI,
            "9.1",
            result);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.InvalidEnvelope);
    }

    private static WorkerValue CreateRow(
        Guid? feeId,
        DateTimeOffset? createdOn,
        DateTimeOffset? payDate,
        decimal amount,
        int? payWayOption,
        string? payWayLabel,
        string? categoryLabel,
        string? others,
        string? paidPeriod,
        string? name) =>
        WorkerValue.FromArray(
        [
            feeId.HasValue ? WorkerValue.FromGuid(feeId.Value) : WorkerValue.Null(),
            createdOn.HasValue ? WorkerValue.FromUtcDateTime(createdOn.Value) : WorkerValue.Null(),
            payDate.HasValue ? WorkerValue.FromUtcDateTime(payDate.Value) : WorkerValue.Null(),
            WorkerValue.FromDecimal(amount),
            payWayOption.HasValue ? WorkerValue.FromInt64(payWayOption.Value) : WorkerValue.Null(),
            payWayLabel is null ? WorkerValue.Null() : WorkerValue.FromString(payWayLabel),
            categoryLabel is null ? WorkerValue.Null() : WorkerValue.FromString(categoryLabel),
            others is null ? WorkerValue.Null() : WorkerValue.FromString(others),
            paidPeriod is null ? WorkerValue.Null() : WorkerValue.FromString(paidPeriod),
            name is null ? WorkerValue.Null() : WorkerValue.FromString(name)
        ]);
}
