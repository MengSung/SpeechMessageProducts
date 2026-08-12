// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72AttendanceLocalPlanBuilderTests.cs
// 用途：以 TDD 驗證 Slice H attendance 輸入的 ISO Sunday 與固定狀態契約。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 保護 attendance semantic plan 不受文化、平日日期與任意狀態輸入影響。
/// </summary>
public sealed class P72AttendanceLocalPlanBuilderTests
{
    /// <summary>有效 Sunday 與固定 present 狀態可建立 local-only plan，但仍不可 CE dispatch。</summary>
    [Fact]
    public void Build_accepts_iso_sunday_and_fixed_present_state()
    {
        var inputs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["attendanceKey"] = "attendance-a",
            ["weekStartDate"] = "2026-08-09",
            ["presentState"] = "present"
        };

        var result = P72AttendanceLocalPlanBuilder.Build(
            OperationIds.PresentRecordUpsertOnUpload,
            inputs);

        result.Succeeded.Should().BeTrue();
        result.Plan!.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>非 Sunday 日期必須 no-go，不能被當成週報查詢的有效週次。</summary>
    [Fact]
    public void Build_rejects_non_sunday_week_start()
    {
        var result = P72AttendanceLocalPlanBuilder.Build(
            OperationIds.PresentRecordCreateOnDownload,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["attendanceKey"] = "attendance-b",
                ["weekStartDate"] = "2026-08-10",
                ["presentState"] = "absent"
            });

        result.Succeeded.Should().BeFalse();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputValueInvalid);
        result.Plan.Should().BeNull();
    }

    /// <summary>非 ISO 日期或未知 present state 都必須 no-go，不能依目前文化或 caller 猜測。</summary>
    [Theory]
    [InlineData("08/09/2026", "present")]
    [InlineData("2026-08-09", "unknown")]
    public void Build_rejects_non_contract_date_or_state(string date, string state)
    {
        var result = P72AttendanceLocalPlanBuilder.Build(
            OperationIds.PresentRecordUpsertOnUpload,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["attendanceKey"] = "attendance-c",
                ["weekStartDate"] = date,
                ["presentState"] = state
            });

        result.Succeeded.Should().BeFalse();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputValueInvalid);
    }
}
