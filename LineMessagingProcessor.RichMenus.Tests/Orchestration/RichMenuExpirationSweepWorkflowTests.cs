using FluentAssertions;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Orchestration;

/// <summary>
/// 驗證到期掃描 workflow 如何還原暫時性 RichMenu 狀態。
/// 測試涵蓋「回復上一個選單」、「沒有上一個選單時解除綁定」與「未到期不處理」三種核心路徑。
/// </summary>
public sealed class RichMenuExpirationSweepWorkflowTests
{
    /// <summary>
    /// 使用者狀態已到期且有 PreviousMenuKey 時，sweep 應呼叫 assignment workflow 指派回上一個選單。
    /// </summary>
    [Fact]
    public async Task SweepAsync_restores_previous_menu_for_expired_state()
    {
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var stateStore = new InMemoryRichMenuStateStore();
        await stateStore.SetAsync(new RichMenuUserState(
            "U-expired",
            "campaign-menu",
            "member-main",
            now.AddMinutes(-1),
            now.AddMinutes(-10)));
        var assignment = new CapturingAssignmentWorkflow();
        var workflow = new RichMenuExpirationSweepWorkflow(stateStore, assignment);

        var report = await workflow.SweepAsync(now);

        report.ScannedCount.Should().Be(1);
        report.RestoredCount.Should().Be(1);
        assignment.Calls.Should().Equal("assign:U-expired:member-main");
    }

    /// <summary>
    /// 使用者狀態已到期但沒有 PreviousMenuKey 時，sweep 應解除使用者個人 RichMenu 綁定。
    /// </summary>
    [Fact]
    public async Task SweepAsync_unassigns_expired_state_without_previous_menu()
    {
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var stateStore = new InMemoryRichMenuStateStore();
        await stateStore.SetAsync(new RichMenuUserState(
            "U-expired",
            "temporary-menu",
            previousMenuKey: null,
            expiresAt: now,
            updatedAt: now.AddMinutes(-5)));
        var assignment = new CapturingAssignmentWorkflow();
        var workflow = new RichMenuExpirationSweepWorkflow(stateStore, assignment);

        var report = await workflow.SweepAsync(now);

        report.ScannedCount.Should().Be(1);
        report.RestoredCount.Should().Be(1);
        assignment.Calls.Should().Equal("unassign:U-expired");
    }

    /// <summary>
    /// 使用者狀態尚未到期時，sweep 不應呼叫 assignment workflow，也不應把它計入 scanned/restored。
    /// </summary>
    [Fact]
    public async Task SweepAsync_ignores_states_that_have_not_expired()
    {
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var stateStore = new InMemoryRichMenuStateStore();
        await stateStore.SetAsync(new RichMenuUserState(
            "U-active",
            "campaign-menu",
            "member-main",
            now.AddMinutes(5),
            now.AddMinutes(-1)));
        var assignment = new CapturingAssignmentWorkflow();
        var workflow = new RichMenuExpirationSweepWorkflow(stateStore, assignment);

        var report = await workflow.SweepAsync(now);

        report.ScannedCount.Should().Be(0);
        report.RestoredCount.Should().Be(0);
        assignment.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// 捕捉 sweep workflow 對 assignment workflow 的呼叫順序與參數。
    /// 這個 fake 不模擬 LINE provider，只用來驗證到期判斷後的 orchestration 決策。
    /// </summary>
    private sealed class CapturingAssignmentWorkflow : ILineRichMenuAssignmentWorkflow
    {
        /// <summary>
        /// 依序記錄 assign / unassign 呼叫，讓測試能直接 assert sweep 的輸出行為。
        /// </summary>
        public List<string> Calls { get; } = new();

        /// <summary>
        /// 記錄還原到上一個 menuKey 的呼叫。
        /// </summary>
        public Task<LineRichMenuAssignmentResult> AssignAsync(
            string lineUserId,
            string menuKey,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"assign:{lineUserId}:{menuKey}");
            return Task.FromResult(LineRichMenuAssignmentResult.Linked(null, menuKey, "rich-menu-restored", changed: true));
        }

        /// <summary>
        /// 記錄 OrThrow assign 呼叫；目前 sweep 不使用此方法，保留以完整實作介面。
        /// </summary>
        public Task AssignOrThrowAsync(
            string lineUserId,
            string menuKey,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"assign-or-throw:{lineUserId}:{menuKey}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 記錄解除個人 RichMenu 綁定的呼叫。
        /// </summary>
        public Task<LineRichMenuAssignmentResult> UnassignAsync(
            string lineUserId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"unassign:{lineUserId}");
            return Task.FromResult(LineRichMenuAssignmentResult.Unlinked(null, changed: true));
        }

        /// <summary>
        /// 記錄 OrThrow unassign 呼叫；目前 sweep 不使用此方法，保留以完整實作介面。
        /// </summary>
        public Task UnassignOrThrowAsync(
            string lineUserId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"unassign-or-throw:{lineUserId}");
            return Task.CompletedTask;
        }
    }
}
