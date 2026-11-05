using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>
/// 保護降低共用 D365 伺服器負載的兩秒輪詢契約；只使用假 SDK 與虛擬時鐘，不建立連線或等待真實時間。
/// 故障注入涵蓋延遲追蹤、完整三分鐘逾時、caller 取消及查詢失敗；各出口均要求刪除本次 task、timer 回到零。
/// </summary>
public sealed class SmokePollingTests
{
    /// <summary>第三次才讀到 trace，應只等待兩次各兩秒；立即成功時不建立任何 delay timer。</summary>
    /// <param name="poll">出現合成 trace 的輪詢次序。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Smoke_TraceArrives_UsesTwoSecondCadenceAndCleansUp(int poll)
    {
        using var f = new Fixture();
        f.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready";
        f.Service.TraceAvailableAfterPoll = poll;
        var start = f.Clock.GetUtcNow();

        Assert.True(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));

        Assert.Equal(poll, f.Service.TraceQueryCount);
        Assert.Equal(poll - 1, f.Clock.DelayCount);
        Assert.All(f.Clock.ObservedDelays, delay => Assert.Equal(TimeSpan.FromSeconds(2), delay));
        Assert.Equal(TimeSpan.FromSeconds((poll - 1) * 2), f.Clock.GetUtcNow() - start);
        AssertCleaned(f);
    }

    /// <summary>沒有 trace 時只輪詢九十次並恰於虛擬三分鐘停止，不能把輪詢頻率降低後意外放大總期限。</summary>
    [Fact]
    public async Task Smoke_NoTrace_StopsAtThreeMinutesWithNinetyPolls()
    {
        using var f = new Fixture();
        var start = f.Clock.GetUtcNow();
        Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
        Assert.Equal(90, f.Service.TraceQueryCount);
        Assert.Equal(90, f.Clock.DelayCount);
        Assert.Equal(TimeSpan.FromMinutes(3), f.Clock.GetUtcNow() - start);
        Assert.All(f.Clock.ObservedDelays, delay => Assert.Equal(TimeSpan.FromSeconds(2), delay));
        AssertCleaned(f);
    }

    /// <summary>第一個延遲期間注入 caller 取消，必須保留 OperationCanceledException 而非誤當三分鐘逾時。</summary>
    [Fact]
    public async Task Smoke_CallerCancelsDuringDelay_ThrowsAndDeletesOwnTask()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.Clock.OnDelay = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Engine.SmokeAsync("p0-ping-trace", cancellation.Token));
        Assert.Equal(1, f.Service.TraceQueryCount);
        AssertCleaned(f);
    }

    /// <summary>在第一次查詢注入真實失敗實例；核心往外拋供 CLI 統一通知，finally 仍清除唯一測試 task。</summary>
    [Fact]
    public async Task Smoke_QueryFails_PreservesOriginalFailureAndCleanup()
    {
        using var f = new Fixture();
        var failure = new InvalidOperationException("synthetic-query-failure");
        f.Service.RetrieveFailure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None)));
        AssertCleaned(f);
    }

    /// <summary>取消已發生或情境未核定時，在建立任何業務記錄或 timer 之前拒絕。</summary>
    [Fact]
    public async Task Smoke_PreflightRejects_DoesNotCreateTaskOrTimer()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Engine.SmokeAsync("p0-ping-trace", cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Engine.SmokeAsync("invalid-scenario", CancellationToken.None));
        Assert.Empty(f.Service.Writes);
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    /// <summary>以 SDK 寫入與 timer 計數確認所有出口確定性釋放，不以 GC 或固定等待推測完成。</summary>
    private static void AssertCleaned(Fixture fixture)
    {
        Assert.Empty(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(fixture.Service.Writes.Where(write => write == "Delete:task"));
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }
}
