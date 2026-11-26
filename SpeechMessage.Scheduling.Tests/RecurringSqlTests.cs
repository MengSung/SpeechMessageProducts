/*
檔案整體說明
目的與定位：F4 真實 SQL 的週期時間與停機補跑驗證，以官方 Hangfire 1.8 manager、scheduler 及真正四 worker 執行產品註冊的工作。
主要功能與流程：先由正式 IJobScheduler 註冊、讀回實際 invocation，再用官方 RecurringJobManager 的 nowFactory 將相同 invocation 錨定到 2037 合成時間；測試推進時鐘並驅動官方 RecurringJobScheduler，斷言 SQL occurrence 數、實際 handler 與下一次 UTC 觸發。停機五小時只允許補一個 occurrence。
使用邊界與維護：SQL storage 仍為本案隨機 LocalDB，僅 forwarding wrapper 對 GetUtcDateTime feature 回 false，讓官方 scheduler 使用明確 fake clock。沒有記憶體 storage 或自製 cron 計算，也不修改全域 filter；manual 四 worker 停止排空後才釋放 runtime、連線池及資料庫。來源保留有界合成資料，不讀正式環境。
*/
using Hangfire;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Xunit;

namespace SpeechMessage.Scheduling.Tests;

/// <summary>使用真正 SQL occurrence 與 handler 次數，防止只驗證設定字串卻沒有執行週期工作的假通過。</summary>
[Trait("WP", "F4")]
public sealed class RecurringSqlTests
{
    /// <summary>台北每日九點在 UTC 01:00 才產生一個真正 occurrence；前一秒不得入列，同分鐘再 tick 不得重複。</summary>
    [Fact]
    public Task Recurring_TaipeiTenant_RunsAt0900Local() => VerifyLocalNineAsync("Asia/Taipei", 1);

    /// <summary>東京租戶九點必須在 UTC 00:00 執行，不能偷用台北或伺服器 local；斷言下一次 occurrence 仍是東京九點。</summary>
    [Fact]
    public Task Recurring_NonTaipeiTenant_UsesTenantTimeZone() => VerifyLocalNineAsync("Asia/Tokyo", 0);

    /// <summary>每小時工作從 00:10 停機到 05:10，官方 Relaxed 真正補一筆並實際執行；重複 tick 不增加工作，下一次為 06:00。</summary>
    [Fact]
    public async Task MissedRuns_AfterDowntime_RunOnce()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var clock = new SchedulingClock(new DateTimeOffset(2037, 2, 1, 0, 10, 0, TimeSpan.Zero));
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false, clock: clock);
        await host.Scheduler.ScheduleRecurringAsync(host.Key(), "0 * * * *", "Asia/Taipei", host.Scope(), "\"hourly\"", default);
        ReanchorOfficialRegistration(host);
        clock.Set(clock.UtcNow.AddHours(5));
        Tick(host);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job"));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Enqueued'"));
        using (var connection = host.Runtime.Storage.GetConnection())
            Assert.Equal(new DateTime(2037, 2, 1, 6, 0, 0, DateTimeKind.Utc), Assert.Single(connection.GetRecurringJobs()).NextExecution);
        host.StartManualWorkers();
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 1,
            "Missed occurrence did not execute.");
        Tick(host);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job"));
        await host.StopAsync();
        Assert.Single(probe.Calls);
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
    }

    /// <summary>先讓兩個官方 occurrence 各完成一次，再將第一個已成功的 ID 真正重入 queue；SQL 新增一次 Processing 後 handler 次數仍為二，保護舊 occurrence tombstone。</summary>
    [Fact]
    public async Task Recurring_OlderCompletedOccurrenceReenqueued_AfterNewOccurrence_DoesNotExecuteAgain()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var clock = new SchedulingClock(new DateTimeOffset(2037, 2, 1, 0, 10, 0, TimeSpan.Zero));
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false, clock: clock);
        var scope = host.Scope();
        await host.Scheduler.ScheduleRecurringAsync(host.Key(), "0 * * * *", "Asia/Taipei", scope, "\"recurring\"", default);
        ReanchorOfficialRegistration(host);
        clock.Set(new DateTimeOffset(2037, 2, 1, 1, 0, 0, TimeSpan.Zero));
        Tick(host);
        var firstJob = Assert.Single(await database.QueryAsync(
            "SELECT CONVERT(nvarchar(32),Id) FROM scheduler_goodshepherd2.Job", reader => reader.GetString(0)));
        host.StartManualWorkers();
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync(
            "SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 1,
            "First recurring occurrence did not complete.");
        clock.Set(new DateTimeOffset(2037, 2, 1, 2, 0, 0, TimeSpan.Zero));
        Tick(host);
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync(
            "SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 2,
            "Second recurring occurrence did not complete.");
        Assert.Equal(2, probe.Calls.Count);
        Assert.True(new BackgroundJobClient(host.Runtime.Storage, host.Runtime.FilterProvider)
            .ChangeState(firstJob, new EnqueuedState(), "Succeeded"));
        await SchedulingDatabase.UntilAsync(async () =>
            await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.State WHERE Name='Processing'") == 3 &&
            await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 2,
            "Reenqueued old occurrence was not actually consumed.");
        await host.StopAsync();
        Assert.Equal(2, probe.Calls.Count);
        Assert.All(probe.Calls, call => Assert.Equal((scope, "\"recurring\""), call));
        Assert.Equal(2, probe.Validations.Count);
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
        Assert.Equal(2, await database.ScalarAsync(
            "SELECT COUNT(DISTINCT [Key]) FROM scheduler_goodshepherd2.Hash WHERE [Key] LIKE 'occurrence:%' AND ExpireAt IS NOT NULL"));
    }

    /// <summary>前一次以真SQL trigger造成完成發布失敗，或模擬中斷／取消的已存狀態；下一次必須獨立執行，舊occurrence重送仍拒絕，不能讓單次不明永久癱瘓週期。</summary>
    /// <param name="prior">sql-fault走實際失敗路徑；其他值模擬程序退出時已持久化的狀態。</param>
    [Theory]
    [InlineData("sql-fault")]
    [InlineData("Running")]
    [InlineData("Uncertain")]
    [InlineData("Failed")]
    public async Task Recurring_FailedOrUnknownOccurrence_NextRunsButOldNeverReruns(string prior)
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var clock = new SchedulingClock(new DateTimeOffset(2037, 2, 1, 0, 10, 0, TimeSpan.Zero));
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false, clock: clock);
        await host.Scheduler.ScheduleRecurringAsync(host.Key(), "0 * * * *", "Asia/Taipei", host.Scope(), "\"recurring\"", default);
        ReanchorOfficialRegistration(host);
        if (prior == "sql-fault")
            await database.ExecuteAsync("CREATE TRIGGER scheduler_goodshepherd2.F4_RecurringCompletionFail ON scheduler_goodshepherd2.Hash AFTER INSERT,UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Field=N'state' AND Value=N'Completed') THROW 51096,'Synthetic recurring completion failure.',1; END");
        string firstJob;
        try
        {
            clock.Set(new DateTimeOffset(2037, 2, 1, 1, 0, 0, TimeSpan.Zero));
            Tick(host);
            firstJob = Assert.Single(await database.QueryAsync(
                "SELECT CONVERT(nvarchar(32),Id) FROM scheduler_goodshepherd2.Job", reader => reader.GetString(0)));
            host.StartManualWorkers();
            await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync(
                "SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName IN ('Succeeded','Failed')") == 1,
                "First recurring occurrence did not finish.");
            Assert.Single(probe.Calls);
            if (prior == "sql-fault")
                Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Failed' AND ExpireAt IS NOT NULL"));
            else
                // 三種固定測試文字皆由InlineData提供；只改本案兩份state，模擬中斷時已提交的ledger。
                await database.ExecuteAsync("UPDATE scheduler_goodshepherd2.Hash SET Value=N'" + prior + "' WHERE Field=N'state'");
        }
        finally
        {
            if (prior == "sql-fault")
                await database.ExecuteAsync("DROP TRIGGER scheduler_goodshepherd2.F4_RecurringCompletionFail");
        }
        clock.Set(new DateTimeOffset(2037, 2, 1, 2, 0, 0, TimeSpan.Zero));
        Tick(host);
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync(
            "SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName IN ('Succeeded','Failed')") == 2,
            "Second recurring occurrence did not finish.");
        Assert.Equal(2, probe.Calls.Count);
        var client = new BackgroundJobClient(host.Runtime.Storage, host.Runtime.FilterProvider);
        Assert.True(client.ChangeState(firstJob, new EnqueuedState(), prior == "sql-fault" ? "Failed" : "Succeeded"));
        await SchedulingDatabase.UntilAsync(async () =>
            await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.State WHERE Name='Processing'") == 3 &&
            await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName IN ('Succeeded','Failed')") == 2,
            "Old failed occurrence was not actually consumed.");
        Assert.Equal(2, probe.Calls.Count);
        // 舊次不明轉永久失敗後，第三個全新occurrence仍不能被主ledger的歷史狀態再次封鎖。
        clock.Set(new DateTimeOffset(2037, 2, 1, 3, 0, 0, TimeSpan.Zero));
        Tick(host);
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync(
            "SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName IN ('Succeeded','Failed')") == 3,
            "Third recurring occurrence did not finish.");
        await host.StopAsync();
        Assert.Equal(3, probe.Calls.Count);
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
    }

    /// <summary>共用實際觸發流程；以 UTC 瞬間斷言前後邊界及下一天，程式不自行推算 cron 或用語系代表時區。</summary>
    private static async Task VerifyLocalNineAsync(string zone, int utcHour)
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var due = new DateTimeOffset(2037, 2, 2, utcHour, 0, 0, TimeSpan.Zero);
        var clock = new SchedulingClock(due.AddMinutes(-1));
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false, clock: clock);
        var scope = host.Scope();
        await host.Scheduler.ScheduleRecurringAsync(host.Key(), "0 9 * * *", zone, scope, "\"local-nine\"", default);
        ReanchorOfficialRegistration(host);
        clock.Set(due.AddSeconds(-1));
        Tick(host);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job"));
        clock.Set(due);
        Tick(host);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Enqueued'"));
        using (var connection = host.Runtime.Storage.GetConnection())
        {
            var recurring = Assert.Single(connection.GetRecurringJobs());
            Assert.Equal(due.UtcDateTime, recurring.LastExecution);
            Assert.Equal(due.AddDays(1).UtcDateTime, recurring.NextExecution);
        }
        host.StartManualWorkers();
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Succeeded'") == 1,
            "Local-nine occurrence did not execute.");
        clock.Set(due.AddSeconds(30));
        Tick(host);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job"));
        await host.StopAsync();
        Assert.Equal((scope, "\"local-nine\""), Assert.Single(probe.Calls));
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
    }

    /// <summary>沿用正式註冊的 invocation、cron、時區與 misfire 選項；只重設時間錨點，避免 SQL 實時及假時鐘混用。</summary>
    private static void ReanchorOfficialRegistration(SchedulingTestHost host)
    {
        using var connection = host.Runtime.Storage.GetConnection();
        var recurring = Assert.Single(connection.GetRecurringJobs());
        var hash = connection.GetAllEntriesFromHash("recurring-job:" + recurring.Id);
        var misfire = hash.TryGetValue("Misfire", out var storedMisfire)
            ? Enum.Parse<MisfireHandlingMode>(storedMisfire) : MisfireHandlingMode.Relaxed;
        Assert.Equal(MisfireHandlingMode.Relaxed, misfire);
        var manager = new RecurringJobManager(host.Runtime.Storage, host.Runtime.FilterProvider,
            new DefaultTimeZoneResolver(), () => host.Clock.UtcNow.UtcDateTime);
        manager.RemoveIfExists(recurring.Id);
        manager.AddOrUpdate(recurring.Id, recurring.Job, recurring.Cron, new RecurringJobOptions
        {
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(recurring.TimeZoneId),
            MisfireHandling = misfire
        });
    }

    /// <summary>同步驅動官方 scheduler 的單次有界輪詢；沒有額外常駐 task，返回時本次交易已提交。</summary>
    private static void Tick(SchedulingTestHost host)
    {
        var storage = new ClockDrivenSqlStorage(host.Runtime.Storage);
        var scheduler = new RecurringJobScheduler(new BackgroundJobFactory(host.Runtime.FilterProvider),
            TimeSpan.FromMilliseconds(1), new DefaultTimeZoneResolver(), () => host.Clock.UtcNow.UtcDateTime);
        scheduler.Execute(new BackgroundProcessContext("f4-fake-clock", storage, new Dictionary<string, object>(),
            Guid.NewGuid(), CancellationToken.None, CancellationToken.None, CancellationToken.None));
    }

    /// <summary>只借用真正 SQL storage；僅讓官方排程器改讀 nowFactory，交易、鎖、queue 及資料全部仍經 SQL。</summary>
    private sealed class ClockDrivenSqlStorage(JobStorage inner) : JobStorage
    {
        /// <summary>保留底層一致性能力，不以假時鐘改變 SQL 可見性。</summary>
        public override bool LinearizableReads => inner.LinearizableReads;
        /// <summary>觀察仍委派真正 SQL monitoring API。</summary>
        public override IMonitoringApi GetMonitoringApi() => inner.GetMonitoringApi();
        /// <summary>連線由呼叫方 using，wrapper 不快取、不取代或自行擁有。</summary>
        public override IStorageConnection GetConnection() => inner.GetConnection();
        /// <summary>唯讀連線亦由呼叫方 using，保持 SQL 原生實作。</summary>
        public override IStorageConnection GetReadOnlyConnection() => inner.GetReadOnlyConnection();
        /// <summary>除資料庫 UTC 外保留所有真 SQL feature，避免繞過批次或鎖語意。</summary>
        public override bool HasFeature(string featureId) => featureId != JobStorageFeatures.Connection.GetUtcDateTime && inner.HasFeature(featureId);
        /// <summary>保留正式狀態處理器，不自行實作 enqueue 或成功狀態。</summary>
        public override IEnumerable<IStateHandler> GetStateHandlers() => inner.GetStateHandlers();
    }
}
