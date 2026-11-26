/*
檔案整體說明
目的與定位：F4 每產品／Channel／操作／UTC 小時之速率額度與四 worker 上限的實際 SQL 測試，防止程序重啟或併發繞過一百次限制。
主要功能與流程：一百二十八次真正 SQL 競爭只允許一百次成功；其他 Channel、建立與刪除操作、大小寫及下一 UTC 小時各自隔離。重建 host 仍從相同持久化額度繼續，不能重置。另以四個已進入 handler 的 barrier 證明第五件需等待，釋放後所有工作完成。
使用邊界與維護：沒有真實 LINE 操作，額度只屬隨機 LocalDB；每次直接操作都有獨立 async scope，處理器中的延期狀態不能漏到下一個 job。barrier 有十秒期限且 finally 釋放，所有 server、scope、連線池與資料庫由唯一 owner 依序排空及刪除。
*/
using Xunit;

namespace SpeechMessage.Scheduling.Tests;

/// <summary>以真正 SQL 競爭保護速率分區及持久性；不以記憶體計數器替代產品預算。</summary>
[Trait("WP", "F4")]
public sealed class RateBudgetSqlTests
{
    /// <summary>一百二十八個獨立 scope 競爭同一鍵，只能一百次成功；另一 Channel、大小寫不同 Channel 與刪除操作不受影響。</summary>
    [Fact]
    public async Task Budget_128ConcurrentAcquisitions_Exactly100AndFullPartitionIsolation()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        var clock = new SchedulingClock(new DateTimeOffset(2037, 2, 1, 3, 25, 0, TimeSpan.Zero));
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false, clock: clock);
        var acquisitions = await Task.WhenAll(Enumerable.Range(0, 128).Select(_ => Task.Run(() =>
            host.AcquireAsync("Channel", "richmenu-create"))));
        Assert.Equal(100, acquisitions.Count(value => value));
        Assert.Equal(28, acquisitions.Count(value => !value));
        Assert.False(await host.AcquireAsync("Channel", "richmenu-create"));
        Assert.True(await host.AcquireAsync("channel", "richmenu-create"));
        Assert.True(await host.AcquireAsync("OtherChannel", "richmenu-create"));
        Assert.True(await host.AcquireAsync("Channel", "richmenu-delete"));
        Assert.Empty(probe.Calls);
    }

    /// <summary>同一資料庫中兩產品的相同 Channel 各有一百次；產品 A 耗盡不能讓 B 先扣額或取得 A 的狀態。</summary>
    [Fact]
    public async Task Budget_SameChannelAcrossProducts_HasSeparate100TokenLimits()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var clock = new SchedulingClock(new DateTimeOffset(2037, 2, 1, 3, 25, 0, TimeSpan.Zero));
        await using var first = await SchedulingTestHost.CreateAsync(database, new SchedulingProbe(), startServer: false, clock: clock);
        await using var second = await SchedulingTestHost.CreateAsync(database, new SchedulingProbe(), startServer: false, clock: clock, product: "GoodButler");
        for (var i = 0; i < 100; i++) Assert.True(await first.AcquireAsync("SharedChannel", "richmenu-create"));
        Assert.False(await first.AcquireAsync("SharedChannel", "richmenu-create"));
        for (var i = 0; i < 100; i++) Assert.True(await second.AcquireAsync("SharedChannel", "richmenu-create"));
        Assert.False(await second.AcquireAsync("SharedChannel", "richmenu-create"));
    }

    /// <summary>重建相同產品與組織的 host 不能清掉已扣額；下一 UTC 整點才恢復一百次，前一小時紀錄不污染新時段。</summary>
    [Fact]
    public async Task Budget_RestartPreservesUsage_OnlyNextUtcHourResets()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var organization = Guid.NewGuid();
        var clock = new SchedulingClock(new DateTimeOffset(2037, 2, 1, 3, 59, 59, TimeSpan.Zero));
        await using (var first = await SchedulingTestHost.CreateAsync(database, new SchedulingProbe(), startServer: false, clock: clock, organization: organization))
        {
            for (var i = 0; i < 100; i++) Assert.True(await first.AcquireAsync("Channel", "richmenu-delete"));
        }
        await database.AssertDrainAsync();
        await using (var second = await SchedulingTestHost.CreateAsync(database, new SchedulingProbe(), startServer: false, clock: clock, organization: organization))
        {
            Assert.False(await second.AcquireAsync("Channel", "richmenu-delete"));
            clock.Set(clock.UtcNow.AddSeconds(1));
            for (var i = 0; i < 100; i++) Assert.True(await second.AcquireAsync("Channel", "richmenu-delete"));
            Assert.False(await second.AcquireAsync("Channel", "richmenu-delete"));
        }
        await database.AssertDrainAsync();
    }

    /// <summary>未知操作與 Channel 路徑分隔輸入必須拒絕，不能產生任意無界預算分區；合法鍵隨後仍能取得一百次。</summary>
    [Theory]
    [InlineData("Channel", "unknown-operation")]
    [InlineData("Channel/Other", "richmenu-create")]
    public async Task Budget_InvalidInput_RejectsWithoutConsumingValidPartition(string channel, string operation)
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        await using var host = await SchedulingTestHost.CreateAsync(database, new SchedulingProbe(), startServer: false);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => host.AcquireAsync(channel, operation));
        Assert.True(await host.AcquireAsync("Channel", "richmenu-create"));
    }

    /// <summary>八件工作由正式 runtime 執行；四件先進 barrier 時第五件尚未進入，釋放後八件都成功且 scoped owner 全釋放，保護 WorkerCount=4。</summary>
    [Fact]
    public async Task Workers_FourConcurrentHandlers_BoundedAndDrained()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var entered = 0;
        var fourEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new SchedulingProbe
        {
            Work = async (_, _, _, ct) =>
            {
                if (Interlocked.Increment(ref entered) == 4) fourEntered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
        };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe);
        try
        {
            for (var i = 0; i < 8; i++)
                await host.Scheduler.ScheduleAsync(host.Key("worker-" + i), host.Clock.UtcNow.AddSeconds(-1), host.Scope(), "{}", default);
            await fourEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(4, Volatile.Read(ref entered));
            Assert.Equal(4, Volatile.Read(ref probe.Active));
            release.TrySetResult();
            await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 8,
                "Four-worker queue failed to complete.");
            await host.StopAsync();
        }
        finally { release.TrySetResult(); }
        Assert.Equal(8, entered);
        Assert.Equal(0, probe.Active);
        Assert.Equal(probe.Created, probe.Disposed);
    }
}
