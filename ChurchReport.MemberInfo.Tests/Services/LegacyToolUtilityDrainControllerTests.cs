using System;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證 legacy ToolUtility 受控入口的 admission、停止接收、排空與確定性釋放契約。
/// 測試只使用記憶體中的 synthetic workload，不建立 CRM、HTTP、SQL、session 或背景資源。
/// </summary>
public sealed class LegacyToolUtilityDrainControllerTests
{
    /// <summary>
    /// 受控 lease 建立後必須增加 active 計數，且釋放一次後回到基線；重複釋放不可 underflow。
    /// </summary>
    [Fact]
    public async Task Acquire_and_double_dispose_returns_active_count_to_baseline()
    {
        await using var controller = new LegacyToolUtilityDrainController();

        await using (var lease = await controller.AcquireAsync(
            LegacyToolUtilityWorkload.Package01FeeRead,
            CancellationToken.None))
        {
            lease.Should().NotBeNull();
            controller.ActiveLeaseCount.Should().Be(1);
            await lease!.DisposeAsync();
        }

        controller.ActiveLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 停止 intake 後不得再建立新 lease，已存在 lease 則仍可排空。
    /// </summary>
    [Fact]
    public async Task Stop_intake_rejects_new_leases_and_drains_existing_lease()
    {
        await using var controller = new LegacyToolUtilityDrainController();
        await using var lease = await controller.AcquireAsync(
            LegacyToolUtilityWorkload.Package01FeeRead,
            CancellationToken.None);

        var stopTask = controller.StopIntakeAndDrainAsync(
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        controller.IsIntakeOpen.Should().BeFalse();
        (await controller.AcquireAsync(
            LegacyToolUtilityWorkload.Package01FeeRead,
            CancellationToken.None)).Should().BeNull();
        stopTask.IsCompleted.Should().BeFalse();

        await lease!.DisposeAsync();

        (await stopTask).Should().Be(LegacyToolUtilityDrainResult.Drained);
    }

    /// <summary>
    /// 未釋放的受控工作在期限後只能回報 timeout；不得被誤判為已排空。
    /// </summary>
    [Fact]
    public async Task Drain_timeout_is_fail_closed_and_does_not_claim_drained()
    {
        await using var controller = new LegacyToolUtilityDrainController();
        await using var lease = await controller.AcquireAsync(
            LegacyToolUtilityWorkload.Package01FeeRead,
            CancellationToken.None);

        var result = await controller.StopIntakeAndDrainAsync(
            TimeSpan.FromMilliseconds(25),
            CancellationToken.None);

        result.Should().Be(LegacyToolUtilityDrainResult.DrainTimeout);
        controller.ActiveLeaseCount.Should().Be(1);
    }

    /// <summary>
    /// 取消 drain waiter 只影響該 waiter，不得改變另一個 workload 的 lease 計數。
    /// </summary>
    [Fact]
    public async Task Cancelled_drain_does_not_release_another_workload_lease()
    {
        await using var controller = new LegacyToolUtilityDrainController();
        await using var lease = await controller.AcquireAsync(
            LegacyToolUtilityWorkload.Package01FeeRead,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        var drainTask = controller.StopIntakeAndDrainAsync(
            TimeSpan.FromSeconds(2),
            cancellation.Token);
        cancellation.Cancel();

        (await drainTask).Should().Be(LegacyToolUtilityDrainResult.Cancelled);
        controller.ActiveLeaseCount.Should().Be(1);
    }

    /// <summary>
    /// controller 被釋放後不得保留 waiter 或允許新的 admission。
    /// </summary>
    [Fact]
    public async Task Dispose_stops_intake_and_leaves_no_waiter()
    {
        var controller = new LegacyToolUtilityDrainController();
        await controller.DisposeAsync();

        controller.IsIntakeOpen.Should().BeFalse();
        (await controller.AcquireAsync(
            LegacyToolUtilityWorkload.Package01FeeRead,
            CancellationToken.None)).Should().BeNull();
        (await controller.StopIntakeAndDrainAsync(
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None)).Should().Be(LegacyToolUtilityDrainResult.Disposed);
    }
}
