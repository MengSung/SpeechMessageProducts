using FluentAssertions;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 固定 Package01 fee 官方 Worker 的 SDK-free request/result wire contract。
/// 測試只建立不可變的 <see cref="WorkerValue"/> 圖形，不持有 Session、認證、端點或 CRM SDK 狀態；
/// 每個 overflow 案例都要求 fail closed，且不得以截斷資料換取成功回應。
/// </summary>
public sealed class OfficialWorkerPackage01ProtocolTests
{
    private static readonly Guid ContactId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset StartDate =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndDate =
        new(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);

    /// <summary>
    /// 證明 Worker revision map 與 Package01 protocol limits 使用單一固定來源，
    /// 避免 Supervisor 與 Worker 對合法最大巢狀結果採用不同的 array/frame 邊界。
    /// </summary>
    [Fact]
    public void Revision_map_and_protocol_limits_match_the_fixed_package01_contract()
    {
        var revisions = OfficialWorkerOperations.CreateRevisionMap();
        var limits = Package01FeeWorkerContract.ProtocolLimits;

        revisions[Package01FeeWorkerContract.CapabilityOperationId]
            .Should().Be(Package01FeeWorkerContract.OperationDefinitionRevision);
        limits.MaximumFrameBytes.Should().Be(1024 * 1024);
        limits.MaximumValueDepth.Should().Be(8);
        limits.MaximumObjectMembers.Should().Be(64);
        limits.MaximumArrayItems.Should().Be(17_604);
        Package01FeeWorkerContract.MaximumPageCount.Should().Be(4);
        Package01FeeWorkerContract.MaximumRowsPerPage.Should().Be(400);
        Package01FeeWorkerContract.MaximumTotalRows.Should().Be(1_600);
        Package01FeeWorkerContract.MaximumCanonicalPageBytes.Should().Be(64 * 1024);
        Package01FeeWorkerContract.MaximumCanonicalResultBytes.Should().Be(256 * 1024);
    }

    /// <summary>
    /// 證明選用的 optional contactName 僅在 protocol 邊界驗證，傳入 CRM client 前即被丟棄；
    /// 因此它不能成為 routing、query、authorization 或任何長生命週期 cache key。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Request_normalization_validates_then_discards_optional_contact_name(bool useNull)
    {
        var request = CreateRequest(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromGuid(ContactId),
            ["startDate"] = WorkerValue.FromUtcDateTime(StartDate),
            ["endDate"] = WorkerValue.FromUtcDateTime(EndDate),
            ["contactName"] = useNull ? WorkerValue.Null() : WorkerValue.FromString("僅供舊呼叫端相容")
        });

        var normalized = OfficialWorkerOperations.PrepareRequestForExecution(request);

        normalized.Parameters.Keys.Should().BeEquivalentTo(
            new[] { "contactId", "startDate", "endDate" });
        normalized.Parameters.Should().NotContainKey("contactName");
        normalized.Parameters["contactId"].Scalar.Should().Be(ContactId.ToString("N"));
        normalized.Parameters["startDate"].Scalar.Should().Be(StartDate.UtcTicks.ToString());
        normalized.Parameters["endDate"].Scalar.Should().Be(EndDate.UtcTicks.ToString());
    }

    /// <summary>
    /// 證明 Supervisor 使用 parameter-only 共用入口時，optional contactName 仍先受 wire string
    /// 上限約束才可被丟棄；否則直接 executor caller 可用超大相容性字串避開 request envelope gate，
    /// 在 admission 前造成不必要的記憶體保留。
    /// </summary>
    [Fact]
    public void Parameter_normalization_rejects_oversized_optional_contact_name_before_discard()
    {
        var parameters = new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromGuid(ContactId),
            ["startDate"] = WorkerValue.FromUtcDateTime(StartDate),
            ["endDate"] = WorkerValue.FromUtcDateTime(EndDate),
            ["contactName"] = WorkerValue.FromString(new string(
                'x',
                Package01FeeWorkerContract.ProtocolLimits.MaximumStringUtf8Bytes + 1))
        };

        var action = () => Package01FeeWorkerContract.ValidateAndNormalizeParameters(parameters);

        action.Should().Throw<WorkerProtocolException>();
    }

    /// <summary>
    /// 證明 caller 無法加入未登錄欄位或以錯誤 kind 偽裝 contactId；
    /// 兩者都必須在 CRM client 執行前成為 protocol failure。
    /// </summary>
    [Fact]
    public void Request_rejects_unknown_members_and_wrong_value_kinds()
    {
        var unknownMember = CreateRequest(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromGuid(ContactId),
            ["startDate"] = WorkerValue.FromUtcDateTime(StartDate),
            ["endDate"] = WorkerValue.FromUtcDateTime(EndDate),
            ["routeHint"] = WorkerValue.FromString("crm91")
        });
        var wrongKind = CreateRequest(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromString(ContactId.ToString("D")),
            ["startDate"] = WorkerValue.FromUtcDateTime(StartDate),
            ["endDate"] = WorkerValue.FromUtcDateTime(EndDate)
        });
        var wrongContactNameKind = CreateRequest(
            new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
            {
                ["contactId"] = WorkerValue.FromGuid(ContactId),
                ["startDate"] = WorkerValue.FromUtcDateTime(StartDate),
                ["endDate"] = WorkerValue.FromUtcDateTime(EndDate),
                ["contactName"] = WorkerValue.FromInt64(1)
            });

        AssertProtocolFailure(() => OfficialWorkerOperations.PrepareRequestForExecution(unknownMember));
        AssertProtocolFailure(() => OfficialWorkerOperations.PrepareRequestForExecution(wrongKind));
        AssertProtocolFailure(() => OfficialWorkerOperations.PrepareRequestForExecution(
            wrongContactNameKind));
    }

    /// <summary>
    /// 證明缺少必要日期、空 GUID 或反向日期區間都在 bounded request 邊界失敗，
    /// 不會建立 CRM 查詢或讓上游例外掩蓋 caller 的 malformed shape。
    /// </summary>
    [Fact]
    public void Request_rejects_missing_required_values_empty_guid_and_date_inversion()
    {
        var missingEndDate = CreateRequest(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromGuid(ContactId),
            ["startDate"] = WorkerValue.FromUtcDateTime(StartDate)
        });
        var emptyContactId = CreateRequest(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromGuid(Guid.Empty),
            ["startDate"] = WorkerValue.FromUtcDateTime(StartDate),
            ["endDate"] = WorkerValue.FromUtcDateTime(EndDate)
        });
        var invertedDates = CreateRequest(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromGuid(ContactId),
            ["startDate"] = WorkerValue.FromUtcDateTime(EndDate),
            ["endDate"] = WorkerValue.FromUtcDateTime(StartDate)
        });

        AssertProtocolFailure(() => OfficialWorkerOperations.PrepareRequestForExecution(missingEndDate));
        AssertProtocolFailure(() => OfficialWorkerOperations.PrepareRequestForExecution(emptyContactId));
        AssertProtocolFailure(() => OfficialWorkerOperations.PrepareRequestForExecution(invertedDates));
    }

    /// <summary>
    /// 證明四頁、每頁四百列、每列十欄的合法最大結果可通過 shape 與 canonical codec；
    /// 巢狀 array item 總數恰為 17,604，且完整 response frame 仍小於 1 MiB。
    /// </summary>
    [Fact]
    public void Legal_maximum_nested_result_is_accepted_without_truncation()
    {
        var row = CreateRow();
        var page = WorkerValue.FromArray(Enumerable.Repeat(row, 400).ToArray());
        var result = WorkerValue.FromArray(Enumerable.Repeat(page, 4).ToArray());

        OfficialWorkerOperations.ValidateResult(
            Package01FeeWorkerContract.CapabilityOperationId,
            result);
        var codec = new WorkerEnvelopeCodec(Package01FeeWorkerContract.ProtocolLimits);
        var payload = codec.SerializeResponse(WorkerResponseV1.Success(
            WorkerProtocolVersion.Current,
            "0123456789abcdef0123456789abcdef",
            Guid.NewGuid(),
            result));

        payload.Length.Should().BeLessThanOrEqualTo(
            Package01FeeWorkerContract.ProtocolLimits.MaximumFrameBytes);
        result.Items.Should().HaveCount(4);
        result.Items!.Sum(item => item.Items!.Count).Should().Be(1_600);
    }

    /// <summary>
    /// 證明第五頁即使沒有列也不會被靜默忽略；整個結果必須以固定 typed exception 失敗。
    /// </summary>
    [Fact]
    public void Result_rejects_page_count_overflow_without_truncation()
    {
        var emptyPage = WorkerValue.FromArray(Array.Empty<WorkerValue>());
        var result = WorkerValue.FromArray(Enumerable.Repeat(emptyPage, 5).ToArray());

        AssertResultLimitExceeded(result);
    }

    /// <summary>
    /// 證明單頁第四百零一列不會被截斷為四百列；overflow 必須使用固定例外回報。
    /// </summary>
    [Fact]
    public void Result_rejects_rows_per_page_overflow_without_truncation()
    {
        var result = WorkerValue.FromArray(
        [
            WorkerValue.FromArray(Enumerable.Repeat(CreateRow(), 401).ToArray())
        ]);

        AssertResultLimitExceeded(result);
    }

    /// <summary>
    /// 證明總列數超過 1,600 時整體失敗；即使超額列位於額外頁面，也不可回傳前 1,600 列。
    /// </summary>
    [Fact]
    public void Result_rejects_total_row_overflow_without_partial_success()
    {
        var row = CreateRow();
        var fullPage = WorkerValue.FromArray(Enumerable.Repeat(row, 400).ToArray());
        var finalPage = WorkerValue.FromArray(new[] { row });
        var result = WorkerValue.FromArray(
        [
            fullPage,
            fullPage,
            fullPage,
            fullPage,
            finalPage
        ]);

        AssertResultLimitExceeded(result);
    }

    /// <summary>
    /// 證明 canonical page bytes 超過 64 KiB 時 fail closed；測試字串仍各自位於 protocol string 上限內，
    /// 因此失敗原因是頁面 aggregate bytes，而不是任意較小的單欄截斷規則。
    /// </summary>
    [Fact]
    public void Result_rejects_canonical_page_byte_overflow_without_truncation()
    {
        var largeValue = new string('x', 16 * 1024);
        var oversizedRow = WorkerValue.FromArray(
        [
            WorkerValue.Null(),
            WorkerValue.Null(),
            WorkerValue.Null(),
            WorkerValue.FromDecimal(0m),
            WorkerValue.Null(),
            WorkerValue.FromString(largeValue),
            WorkerValue.FromString(largeValue),
            WorkerValue.FromString(largeValue),
            WorkerValue.FromString(largeValue),
            WorkerValue.Null()
        ]);
        var result = WorkerValue.FromArray(
        [
            WorkerValue.FromArray(new[] { oversizedRow })
        ]);

        AssertResultLimitExceeded(result);
    }

    /// <summary>
    /// 證明錯誤的 page/row shape 與空 feeId 都是 protocol failure，而非 result-too-large；
    /// Worker generation 因 malformed upstream payload 必須由外層 protocol lifecycle 處理。
    /// </summary>
    [Fact]
    public void Result_rejects_malformed_shapes_and_empty_fee_guid_as_protocol_failures()
    {
        var objectRoot = WorkerValue.FromObject(
            new Dictionary<string, WorkerValue>(StringComparer.Ordinal));
        var oversizedObjectRoot = WorkerValue.FromObject(
            Enumerable.Range(0, 65).ToDictionary(
                index => "field" + index,
                _ => WorkerValue.Null(),
                StringComparer.Ordinal));
        var shortRow = WorkerValue.FromArray(CreateRow().Items!.Take(9).ToArray());
        var emptyGuidRow = CreateRow(feeId: WorkerValue.FromGuid(Guid.Empty));

        AssertProtocolFailure(() => OfficialWorkerOperations.ValidateResult(
            Package01FeeWorkerContract.CapabilityOperationId,
            objectRoot));
        AssertProtocolFailure(() => OfficialWorkerOperations.ValidateResult(
            Package01FeeWorkerContract.CapabilityOperationId,
            oversizedObjectRoot));
        AssertProtocolFailure(() => OfficialWorkerOperations.ValidateResult(
            Package01FeeWorkerContract.CapabilityOperationId,
            WorkerValue.FromArray([WorkerValue.FromArray([shortRow])])));
        AssertProtocolFailure(() => OfficialWorkerOperations.ValidateResult(
            Package01FeeWorkerContract.CapabilityOperationId,
            WorkerValue.FromArray([WorkerValue.FromArray([emptyGuidRow])])));
    }

    /// <summary>
    /// 證明 amount 不可為 Null，且 nullable payWayOption 一旦存在就必須落在 Int32 範圍；
    /// 這些 typed cell 違規皆屬 protocol failure，不能被分類為容量 overflow。
    /// </summary>
    [Fact]
    public void Result_rejects_wrong_cell_kinds_and_out_of_range_pay_way_as_protocol_failures()
    {
        var missingAmountCells = CreateRow().Items!.ToArray();
        missingAmountCells[3] = WorkerValue.Null();
        var oversizedPayWayCells = CreateRow().Items!.ToArray();
        oversizedPayWayCells[4] = WorkerValue.FromInt64((long)int.MaxValue + 1L);

        AssertProtocolFailure(() => OfficialWorkerOperations.ValidateResult(
            Package01FeeWorkerContract.CapabilityOperationId,
            WorkerValue.FromArray(
            [
                WorkerValue.FromArray([WorkerValue.FromArray(missingAmountCells)])
            ])));
        AssertProtocolFailure(() => OfficialWorkerOperations.ValidateResult(
            Package01FeeWorkerContract.CapabilityOperationId,
            WorkerValue.FromArray(
            [
                WorkerValue.FromArray([WorkerValue.FromArray(oversizedPayWayCells)])
            ])));
    }

    private static WorkerRequestV1 CreateRequest(
        IReadOnlyDictionary<string, WorkerValue> parameters) =>
        new(
            WorkerProtocolVersion.Current,
            "0123456789abcdef0123456789abcdef",
            Guid.NewGuid(),
            "profile-generation-0001",
            Package01FeeWorkerContract.OperationDefinitionRevision,
            Package01FeeWorkerContract.CapabilityOperationId,
            EndDate.AddMinutes(1).UtcTicks,
            parameters);

    private static WorkerValue CreateRow(WorkerValue? feeId = null) =>
        WorkerValue.FromArray(
        [
            feeId ?? WorkerValue.FromGuid(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            WorkerValue.FromUtcDateTime(StartDate),
            WorkerValue.FromUtcDateTime(EndDate),
            WorkerValue.FromDecimal(123.45m),
            WorkerValue.FromInt64(2),
            WorkerValue.FromString("信用卡"),
            WorkerValue.Null(),
            WorkerValue.FromString(string.Empty),
            WorkerValue.FromString("2026-01"),
            WorkerValue.FromString("奉獻者")
        ]);

    private static void AssertResultLimitExceeded(WorkerValue result)
    {
        var action = () => OfficialWorkerOperations.ValidateResult(
            Package01FeeWorkerContract.CapabilityOperationId,
            result);

        action.Should().Throw<OfficialWorkerResultLimitExceededException>();
    }

    private static void AssertProtocolFailure(Action action)
    {
        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.InvalidEnvelope);
    }
}
