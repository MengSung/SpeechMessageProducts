using FluentAssertions;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Orchestration;

public sealed class RichMenuExpirationSweepWorkflowTests
{
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

    private sealed class CapturingAssignmentWorkflow : ILineRichMenuAssignmentWorkflow
    {
        public List<string> Calls { get; } = new();

        public Task<LineRichMenuAssignmentResult> AssignAsync(
            string lineUserId,
            string menuKey,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"assign:{lineUserId}:{menuKey}");
            return Task.FromResult(LineRichMenuAssignmentResult.Linked(null, menuKey, "rich-menu-restored", changed: true));
        }

        public Task AssignOrThrowAsync(
            string lineUserId,
            string menuKey,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"assign-or-throw:{lineUserId}:{menuKey}");
            return Task.CompletedTask;
        }

        public Task<LineRichMenuAssignmentResult> UnassignAsync(
            string lineUserId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"unassign:{lineUserId}");
            return Task.FromResult(LineRichMenuAssignmentResult.Unlinked(null, changed: true));
        }

        public Task UnassignOrThrowAsync(
            string lineUserId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"unassign-or-throw:{lineUserId}");
            return Task.CompletedTask;
        }
    }
}
