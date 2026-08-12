// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72AttendanceWeeklyReportDecisionTests.cs
// 用途：保護 Slice H 週報 cardinality 的 local-only 決策：zero-active 可不關聯、
//       exactly-one-active 必須精確關聯、duplicate/unavailable 一律 no-go。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 attendance 週報 cardinality 純決策的 fail-closed contract。
///
/// <para>
/// 這些測試不提供 CRM ID、Entity、Owner、Profile 或 connector。它們只模擬已由未來
/// server-owned read-only query 投影出的「完整性＋列數」，並確保 local 層不會因為沒有
/// 週報就自動建立資料、因為重複資料就任選一筆，或在觀測不完整時假裝可安全寫入。任何
/// 真正的精確週報連結仍必須由受治理 CE executor 以 task-owned fixture、exact read-back
/// 與 cleanup owner 驗證；本決策器不能單獨授權 CRM mutation。
/// </para>
/// </summary>
public sealed class P72AttendanceWeeklyReportDecisionTests
{
    /// <summary>
    /// 保護使用者定義的正常 zero-active 分支：沒有啟用週報並不是錯誤，也不會自動建立
    /// 週報。決定性斷言是結果允許後續建立「不關聯週報」的出席紀錄，且不要求或暴露任何
    /// weekly-report identifier。
    /// </summary>
    [Fact]
    public void Resolve_returns_unlinked_proceed_decision_for_complete_zero_active_observation()
    {
        var decision = P72AttendanceWeeklyReportDecision.Resolve(
            new P72AttendanceWeeklyReportObservation
            {
                IsComplete = true,
                ActiveReportCount = 0
            });

        decision.CanProceed.Should().BeTrue();
        decision.Disposition.Should().Be(P72AttendanceWeeklyReportDisposition.ProceedUnlinked);
        decision.RequiresExactLinkReadBack.Should().BeFalse();
        decision.FailureCategory.Should().Be(P72AttendanceWeeklyReportFailureCategory.None);
    }

    /// <summary>
    /// 保護 exactly-one-active 分支不得被錯誤降級為不關聯。決定性斷言是結果只能要求
    /// 後段以精確、server-resolved lookup 建立連結並 read-back；這個本機結果本身不攜帶
    /// CRM reference，因此不能被 caller 改為另一筆週報。
    /// </summary>
    [Fact]
    public void Resolve_requires_exact_link_readback_for_complete_single_active_observation()
    {
        var decision = P72AttendanceWeeklyReportDecision.Resolve(
            new P72AttendanceWeeklyReportObservation
            {
                IsComplete = true,
                ActiveReportCount = 1
            });

        decision.CanProceed.Should().BeTrue();
        decision.Disposition.Should().Be(P72AttendanceWeeklyReportDisposition.ProceedWithExactLink);
        decision.RequiresExactLinkReadBack.Should().BeTrue();
        decision.FailureCategory.Should().Be(P72AttendanceWeeklyReportFailureCategory.None);
    }

    /// <summary>
    /// 保護目標小組在同一週有兩筆以上啟用週報時必須停止。故障注入提供兩筆完整投影；
    /// 決定性斷言是 no-go，而不是自動挑第一筆、最後一筆或任意 ID，避免跨小組／跨週報
    /// 的錯誤關聯與後續 cleanup 無法精確化。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_complete_duplicate_active_observation()
    {
        var decision = P72AttendanceWeeklyReportDecision.Resolve(
            new P72AttendanceWeeklyReportObservation
            {
                IsComplete = true,
                ActiveReportCount = 2
            });

        decision.CanProceed.Should().BeFalse();
        decision.Disposition.Should().Be(P72AttendanceWeeklyReportDisposition.NoGo);
        decision.FailureCategory.Should().Be(P72AttendanceWeeklyReportFailureCategory.DuplicateActive);
    }

    /// <summary>
    /// 保護 paging、transport fault、schema failure 或其他不完整觀測絕不能被誤當 zero-active。
    /// 故障注入令 count 為零但 <c>IsComplete=false</c>；決定性斷言是 unavailable no-go，
    /// 後段不得 dispatch、retry 或建立不關聯紀錄。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_incomplete_observation_even_when_count_is_zero()
    {
        var decision = P72AttendanceWeeklyReportDecision.Resolve(
            new P72AttendanceWeeklyReportObservation
            {
                IsComplete = false,
                ActiveReportCount = 0
            });

        decision.CanProceed.Should().BeFalse();
        decision.Disposition.Should().Be(P72AttendanceWeeklyReportDisposition.NoGo);
        decision.FailureCategory.Should().Be(P72AttendanceWeeklyReportFailureCategory.Unavailable);
    }

    /// <summary>
    /// 保護不可能的負數 cardinality 不會被正規化、clamp 或轉為 zero-active。此類輸入表示
    /// 上游投影或序列化違反契約，必須有界地回報 unavailable，而非從先前操作或快取補值。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_invalid_negative_cardinality()
    {
        var decision = P72AttendanceWeeklyReportDecision.Resolve(
            new P72AttendanceWeeklyReportObservation
            {
                IsComplete = true,
                ActiveReportCount = -1
            });

        decision.CanProceed.Should().BeFalse();
        decision.Disposition.Should().Be(P72AttendanceWeeklyReportDisposition.NoGo);
        decision.FailureCategory.Should().Be(P72AttendanceWeeklyReportFailureCategory.Unavailable);
    }
}
