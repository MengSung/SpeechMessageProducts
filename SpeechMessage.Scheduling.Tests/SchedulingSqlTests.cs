/*
檔案整體說明
目的與定位：F4 一般測試的排程公開契約，使用真正 Hangfire SQL、正式 runtime 及合成 scoped 處理器，驗證覆寫、重啟、停止及執行前隔離。
主要功能與流程：每案建立隨機 F4Schedule_ LocalDB；先排程再透過 handler 次數與 SQL 終態確認真實 dispatch。故障、scope 切換及取消均保留前一完整候選，不以只查字串或設定值冒充行為。純 JobKey 測試不需資料庫。
使用邊界與維護：沒有 SqlIntegration 類別，因此一般 suite 必須執行；不連網站、LINE、CRM 或部署資料庫。每案先停止並 Dispose host，再由資料庫 owner 驗 session、資料庫及資料檔零殘留。所有等待有界，合成探針不包含憑證或個資。
*/
using System.Globalization;
using Hangfire;
using Hangfire.States;
using Xunit;

namespace SpeechMessage.Scheduling.Tests;

/// <summary>公開排程行為的實際 SQL 驗證；測試 owner 不存取任何真實租戶或外部服務。</summary>
[Trait("WP", "F4")]
public sealed class SchedulingSqlTests
{
    /// <summary>注入冒號及斜線到 BusinessKey；兩者都必須在任何 SQL I/O 前拋出參數錯誤，避免鍵分段或路徑混淆。</summary>
    [Fact]
    public void JobKey_PathDelimiter_Rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new JobKey("GoodShepherd2", "capture", "event:part"));
        Assert.ThrowsAny<ArgumentException>(() => new JobKey("GoodShepherd2", "capture", "event/part"));
    }

    /// <summary>同鍵兩次先後覆寫未到期工作；真實 worker 只執行第二份 payload，SQL 只保留一個成功執行。</summary>
    [Fact]
    public async Task Schedule_SameKeyTwice_ExecutesOnce()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe);
        var scope = host.Scope();
        var due = TimeProvider.System.GetUtcNow().AddSeconds(2);
        await host.Scheduler.ScheduleAsync(host.Key(), due, scope, "{\"version\":1}", default);
        await host.Scheduler.ScheduleAsync(host.Key(), due, scope, "{\"version\":2}", default);
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 1,
            "Replacement did not reach SQL Succeeded.");
        await host.StopAsync();
        var call = Assert.Single(probe.Calls);
        Assert.Equal(scope, call.Scope);
        Assert.Equal("{\"version\":2}", call.Payload);
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
    }

    /// <summary>完整停止並釋放第一個 host，再連相同合成 SQL 啟動；第二個 marker 證明 worker 已工作，原完成 generation 仍只呼叫一次。</summary>
    [Fact]
    public async Task Restart_CompletedJob_NotRerun()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        var organization = Guid.NewGuid();
        await using (var first = await SchedulingTestHost.CreateAsync(database, probe, organization: organization))
        {
            await first.Scheduler.ScheduleAsync(first.Key(), TimeProvider.System.GetUtcNow().AddSeconds(-1), first.Scope(), "\"first\"", default);
            await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 1,
                "Initial job did not complete.");
        }
        await database.AssertDrainAsync();
        await using (var second = await SchedulingTestHost.CreateAsync(database, probe, organization: organization))
        {
            await second.Scheduler.ScheduleAsync(second.Key("marker"), TimeProvider.System.GetUtcNow().AddSeconds(-1), second.Scope(), "\"marker\"", default);
            await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 2,
                "Restart marker did not complete.");
        }
        Assert.Equal(2, probe.Calls.Count);
        Assert.Single(probe.Calls, call => call.Payload == "\"first\"");
        Assert.Single(probe.Calls, call => call.Payload == "\"marker\"");
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
    }

    /// <summary>handler 進入後才啟動 Stop，再釋放其有界 barrier；停止須完成或明確失敗，不能留下 Processing、執行中工作或未釋放 scope。</summary>
    [Fact]
    public async Task Shutdown_InFlightJob_CompletesOrMarkedFailed()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new SchedulingProbe
        {
            Work = async (_, _, _, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
        };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe);
        try
        {
            await host.Scheduler.ScheduleAsync(host.Key(), TimeProvider.System.GetUtcNow().AddSeconds(-1), host.Scope(), "{}", default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, Volatile.Read(ref probe.Active));
            var stopping = host.StopAsync();
            release.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(40));
        }
        finally { release.TrySetResult(); }
        Assert.Equal(0, probe.Active);
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Processing'"));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName IN ('Succeeded','Failed')"));
    }

    /// <summary>排程後才停用同一租戶；等待 SQL 完成跳過後驗證 guard 曾重查原 scope，handler 與任何外部動作均未執行。</summary>
    [Fact]
    public async Task Job_TenantDisabledAfterSchedule_NotExecuted()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var enabled = 1;
        var probe = new SchedulingProbe { Allowed = _ => Volatile.Read(ref enabled) == 1 };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe);
        var scope = host.Scope();
        await host.Scheduler.ScheduleAsync(host.Key(), TimeProvider.System.GetUtcNow().AddSeconds(2), scope, "{}", default);
        Interlocked.Exchange(ref enabled, 0);
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 1,
            "Disabled tenant job did not reach a completed skip.");
        await host.StopAsync();
        Assert.Contains(scope, probe.Validations);
        Assert.Empty(probe.Calls);
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
    }

    /// <summary>先真 SQL 消耗一百次建立額度，再由正式 dispatch 嘗試第 101 次；不得執行副作用，SQL 須延期到下一 UTC 整點且不增加重試，整點後同工作可執行一次。</summary>
    [Fact]
    public async Task RateBudget_101stRichMenuCreateInHour_DeferredToNextHour()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var clock = new SchedulingClock(new DateTimeOffset(2037, 2, 1, 3, 25, 0, TimeSpan.Zero));
        var effects = 0;
        var probe = new SchedulingProbe
        {
            Work = async (scope, _, budget, ct) =>
            {
                if (!await budget.TryAcquireAsync(scope.Product, "synthetic-channel", "richmenu-create", ct)) return;
                Interlocked.Increment(ref effects);
            }
        };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, clock: clock);
        for (var i = 0; i < 100; i++) Assert.True(await host.AcquireAsync("synthetic-channel", "richmenu-create"));
        await host.Scheduler.ScheduleAsync(host.Key(), TimeProvider.System.GetUtcNow().AddSeconds(-1), host.Scope(), "{}", default);
        await SchedulingDatabase.UntilAsync(async () => probe.Calls.Count == 1 &&
            await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Scheduled'") == 1,
            "Exhausted job was not deferred.");
        Assert.Equal(0, Volatile.Read(ref effects));
        string jobId;
        using (var connection = host.Runtime.Storage.GetConnection())
        {
            jobId = Assert.Single(connection.GetAllItemsFromSet("schedule"));
            var state = connection.GetStateData(jobId);
            Assert.Equal("Scheduled", state.Name);
            Assert.Equal(clock.UtcNow.Date.AddHours(4), DateTime.Parse(state.Data["EnqueueAt"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
            Assert.True(connection.GetJobParameter(jobId, "RetryCount") is null or "0");
        }
        clock.Set(new DateTimeOffset(2037, 2, 1, 4, 0, 0, TimeSpan.Zero));
        // 此處只在假時鐘到期後用官方狀態機推進已存在的同一工作；實際延期時刻已由上方 SQL 狀態斷言。
        Assert.True(new BackgroundJobClient(host.Runtime.Storage, host.Runtime.FilterProvider).ChangeState(jobId, new EnqueuedState(), "Scheduled"));
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 1,
            "Deferred job did not execute at the next-hour budget.");
        await host.StopAsync();
        Assert.Equal(2, probe.Calls.Count);
        Assert.Equal(1, effects);
        Assert.Equal(probe.Created, probe.Disposed);
    }
}
