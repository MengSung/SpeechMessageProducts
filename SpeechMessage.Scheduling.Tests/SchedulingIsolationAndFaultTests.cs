/*
檔案整體說明
目的與定位：F4 的跨產品／組織／租戶隔離、真 SQL 故障及生命週期測試，補足九個指定案例以外的 release-blocking 邊界。
主要功能與流程：使用相同本案 LocalDB 的獨立產品 schema、真正 SQL 分散鎖及官方狀態機執行競爭；以三十二筆同鍵寫入、Hash 觸發器、handler 連續失敗及 validator 故障驗證原子發布與有限重試。每輪停止後核對 handler owner、Processing、Server 及 SQL session 基線。
使用邊界與維護：所有 payload、Channel、組織與租戶均為合成值，不呼叫外部服務。僅將本案 SQL 中確切 Scheduled 工作經官方 BackgroundJobClient 轉為 Enqueued，以省去真實時間等待；正式 dispatcher、filter、scope、狀態與交易不被替換。所有注入鎖與觸發器均有 finally 清理，資料庫 owner 最後刪自己的庫並驗檔案消失。
*/
using Hangfire;
using Hangfire.States;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using Xunit;

namespace SpeechMessage.Scheduling.Tests;

/// <summary>只以本機合成 SQL 驗證隔離及故障；一般 suite 執行，不依賴產品主機或實機設定。</summary>
[Trait("WP", "F4")]
public sealed class SchedulingIsolationAndFaultTests
{
    /// <summary>同 scope 三十二個 writer 競爭同穩定鍵，候選完全發布後只執行一份完整 JSON；不依顯示內容去重。</summary>
    [Fact]
    public async Task Schedule_32ConcurrentSameKeyWriters_OneCompleteGeneration()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false);
        var scope = host.Scope();
        var values = Enumerable.Range(0, 32).Select(index => "{\"candidate\":" + index + "}").ToArray();
        await Task.WhenAll(values.Select(payload => Task.Run(() =>
            host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), scope, payload, default))));
        Assert.Equal(1, EnqueuePending(host));
        host.StartManualWorkers();
        await WaitSucceededAsync(database, 1);
        await host.StopAsync();
        var call = Assert.Single(probe.Calls);
        Assert.Equal(scope, call.Scope);
        Assert.Contains(call.Payload, values);
        Assert.Equal(probe.Created, probe.Disposed);
    }

    /// <summary>另一租戶以相同產品／名稱／業務鍵覆寫時必須拒絕；原候選仍能完整執行，不被新租戶替換或混合。</summary>
    [Fact]
    public async Task Schedule_SameKeyDifferentTenant_RejectsAndPreservesOriginal()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false);
        var original = host.Scope();
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), original, "\"original\"", default);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => host.Scheduler.ScheduleAsync(
            host.Key(), host.Clock.UtcNow.AddHours(1), host.Scope(), "\"foreign\"", default));
        Assert.Equal(1, EnqueuePending(host));
        host.StartManualWorkers();
        await WaitSucceededAsync(database, 1);
        await host.StopAsync();
        Assert.Equal((original, "\"original\""), Assert.Single(probe.Calls));
    }

    /// <summary>相同資料庫與業務鍵在兩個產品 schema 各自執行；各 host 只得到自己產品、組織、租戶及 payload，結束後各 schema server 均歸零。</summary>
    [Fact]
    public async Task Products_SameBusinessKey_SeparateSqlSchemasAndDispatchScopes()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var firstProbe = new SchedulingProbe();
        var secondProbe = new SchedulingProbe();
        await using (var first = await SchedulingTestHost.CreateAsync(database, firstProbe))
        await using (var second = await SchedulingTestHost.CreateAsync(database, secondProbe, product: "GoodButler"))
        {
            var firstScope = first.Scope();
            var secondScope = second.Scope();
            await first.Scheduler.ScheduleAsync(first.Key(), first.Clock.UtcNow.AddSeconds(-1), firstScope, "\"product-a\"", default);
            await second.Scheduler.ScheduleAsync(second.Key(), second.Clock.UtcNow.AddSeconds(-1), secondScope, "\"product-b\"", default);
            await WaitSucceededAsync(database, 1);
            await WaitSucceededAsync(database, 1, "scheduler_goodbutler");
            await first.StopAsync();
            await second.StopAsync();
            Assert.Equal((firstScope, "\"product-a\""), Assert.Single(firstProbe.Calls));
            Assert.Equal((secondScope, "\"product-b\""), Assert.Single(secondProbe.Calls));
            Assert.Equal(firstProbe.Created, firstProbe.Disposed);
            Assert.Equal(secondProbe.Created, secondProbe.Disposed);
            Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Server"));
            Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodbutler.Server"));
        }
        await database.AssertDrainAsync();
    }

    /// <summary>caller scope 與固定部署產品或組織不一致時拒絕，SQL 不得留下候選或可被另一主體執行的工作。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Schedule_ForeignDeploymentScope_RejectedBeforePublication(bool foreignProduct)
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false);
        var scope = new JobScope(foreignProduct ? "GoodButler" : host.Product,
            foreignProduct ? host.Organization : Guid.NewGuid(), Guid.NewGuid());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => host.Scheduler.ScheduleAsync(
            host.Key(), host.Clock.UtcNow.AddHours(1), scope, "{}", default));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job"));
        Assert.Empty(probe.Calls);
    }

    /// <summary>取消發生在 worker 取件前，原工作不得執行；另外的 marker 完成證明同一 worker 確實已處理 queue。</summary>
    [Fact]
    public async Task Cancel_BeforeDispatch_RejectsOldGeneration()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false);
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), host.Scope(), "\"cancelled\"", default);
        await host.Scheduler.CancelAsync(host.Key(), default);
        await host.Scheduler.ScheduleAsync(host.Key("marker"), host.Clock.UtcNow.AddHours(1), host.Scope(), "\"marker\"", default);
        Assert.Equal(1, EnqueuePending(host));
        host.StartManualWorkers();
        await WaitSucceededAsync(database, 1);
        await host.StopAsync();
        Assert.Equal("\"marker\"", Assert.Single(probe.Calls).Payload);
        Assert.Equal(probe.Created, probe.Disposed);
    }

    /// <summary>真正 handler 每次拋同類型故障，初次加三次重試後 SQL Failed；再用相同鍵替換成功，證明失敗不污染重試、scope 或後續 generation。</summary>
    [Fact]
    public async Task Handler_Fails_RetriesThreeTimesThenFails_ReplacementExecutes()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe
        {
            Work = (_, payload, _, _) => payload == "\"fail\""
                ? Task.FromException(new InvalidOperationException("F4_PRIVATE_EXCEPTION_SENTINEL"))
                : Task.CompletedTask
        };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, writeExceptionLog: true);
        var scope = host.Scope();
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), scope, "\"fail\"", default);
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Failed'") == 1,
            "Failure did not terminate after three retries.", 25);
        Assert.Equal(4, probe.Calls.Count);
        Assert.Equal(4, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.State WHERE Name='Processing'"));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Failed' AND ExpireAt IS NOT NULL"));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.State WHERE Data LIKE N'%F4_PRIVATE_EXCEPTION_SENTINEL%'"));
        var incident = Assert.Single(await File.ReadAllLinesAsync(Path.Combine(host.DiagnosticsDirectory, "Exception.log")));
        Assert.DoesNotContain("F4_PRIVATE_EXCEPTION_SENTINEL", incident, StringComparison.Ordinal);
        using (var document = JsonDocument.Parse(incident))
            Assert.Equal("System.InvalidOperationException", document.RootElement.GetProperty("ExceptionType").GetString());
        using (var connection = host.Runtime.Storage.GetConnection())
        {
            var failed = Assert.Single(host.Runtime.Storage.GetMonitoringApi().FailedJobs(0, 10));
            Assert.Equal("3", connection.GetJobParameter(failed.Key, "RetryCount"));
        }
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), scope, "\"recovered\"", default);
        await WaitSucceededAsync(database, 1);
        await host.StopAsync();
        Assert.Equal(5, probe.Calls.Count);
        Assert.Single(probe.Calls, call => call.Payload == "\"recovered\"");
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
    }

    /// <summary>權威查詢故障不能當作停用跳過；實際重試三次後 Failed，handler 零呼叫，解除故障後同鍵候選可重新發布並執行。</summary>
    [Fact]
    public async Task Validator_Fault_IsFailureAndRetryable_WithoutHandlerExecution()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe { Allowed = _ => throw new InvalidOperationException("Synthetic validator failure.") };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe);
        var scope = host.Scope();
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), scope, "{}", default);
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Failed'") == 1,
            "Validator fault was swallowed or did not terminate.", 25);
        Assert.Empty(probe.Calls);
        Assert.Equal(4, probe.Validations.Count);
        probe.Allowed = _ => true;
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), scope, "{}", default);
        await WaitSucceededAsync(database, 1);
        await host.StopAsync();
        Assert.Equal(scope, Assert.Single(probe.Calls).Scope);
        Assert.Equal(probe.Created, probe.Disposed);
    }

    /// <summary>真 SQL Hash trigger 在候選發布時拋錯；失敗須向上傳遞且原 generation 保持，移除故障後相同鍵重試成功，未發布孤兒不得執行部分 payload。</summary>
    [Fact]
    public async Task Schedule_RealSqlPublicationFault_PreservesOriginalAndCanRetry()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false);
        var scope = host.Scope();
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), scope, "\"original\"", default);
        var originalGeneration = Assert.Single(await database.QueryAsync(
            "SELECT Value FROM scheduler_goodshepherd2.Hash WHERE Field='generation'", reader => reader.GetString(0)));
        await database.ExecuteAsync("CREATE TRIGGER scheduler_goodshepherd2.F4_Fail ON scheduler_goodshepherd2.Hash AFTER INSERT,UPDATE AS BEGIN THROW 51094,'Synthetic scheduling publication fault.',1; END");
        try
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => host.Scheduler.ScheduleAsync(
                host.Key(), host.Clock.UtcNow.AddHours(1), scope, "\"partial\"", default));
            Assert.Equal(51094, error.Number);
        }
        finally { await database.ExecuteAsync("DROP TRIGGER scheduler_goodshepherd2.F4_Fail"); }
        Assert.Equal(originalGeneration, Assert.Single(await database.QueryAsync(
            "SELECT Value FROM scheduler_goodshepherd2.Hash WHERE Field='generation'", reader => reader.GetString(0))));
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), scope, "\"retry\"", default);
        var candidates = EnqueuePending(host);
        Assert.InRange(candidates, 1, 2);
        host.StartManualWorkers();
        await WaitSucceededAsync(database, candidates);
        await host.StopAsync();
        Assert.Equal("\"retry\"", Assert.Single(probe.Calls).Payload);
        Assert.DoesNotContain(probe.Calls, call => call.Payload == "\"partial\"");
        Assert.Equal(probe.Created, probe.Disposed);
    }

    /// <summary>呼叫端在進入前已取消，不能發布候選；相同穩定鍵後續正常呼叫可成功，沒有取消狀態跨 scope 保留。</summary>
    [Fact]
    public async Task Schedule_CallerCancelled_NoCandidate_ThenCanRetry()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false);
        var scope = host.Scope();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Scheduler.ScheduleAsync(
            host.Key(), host.Clock.UtcNow.AddHours(1), scope, "\"cancelled\"", caller.Token));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job"));
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), scope, "\"retry\"", default);
        Assert.Equal(1, EnqueuePending(host));
        host.StartManualWorkers();
        await WaitSucceededAsync(database, 1);
        await host.StopAsync();
        Assert.Equal("\"retry\"", Assert.Single(probe.Calls).Payload);
    }

    /// <summary>同庫三輪各八筆實際工作及 host 重建；每輪全部完成後驗 scope owner、server、Processing 與 session 歸零，資料庫外層最後驗資料檔消失。</summary>
    [Fact]
    public async Task Lifecycle_ThreeHostCycles_DrainServersScopesAndSqlSessions()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var organization = Guid.NewGuid();
        var probe = new SchedulingProbe();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            await using (var host = await SchedulingTestHost.CreateAsync(database, probe, organization: organization))
            {
                var scopes = Enumerable.Range(0, 8).Select(_ => host.Scope()).ToArray();
                await Task.WhenAll(scopes.Select((scope, index) => host.Scheduler.ScheduleAsync(
                    host.Key("round-" + cycle + "-" + index), host.Clock.UtcNow.AddSeconds(-1), scope, "{}", default)));
                await WaitSucceededAsync(database, (cycle + 1) * 8);
                await host.StopAsync();
                Assert.Equal(probe.Created, probe.Disposed);
                Assert.Equal(0, probe.Active);
                Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Server"));
                Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Processing'"));
                Assert.All(scopes, scope => Assert.Single(probe.Calls, call => call.Scope == scope));
            }
            await database.AssertDrainAsync();
        }
        Assert.Equal(24, probe.Calls.Count);
    }

    /// <summary>同一事件完成後重送、取消再重送均不得重跑；marker 的實際成功證明 worker 已消費後續工作，原成功紀錄仍保持。</summary>
    [Fact]
    public async Task Completed_ScheduleAgainAndCancelThenSchedule_RemainsCompletedOnce()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe);
        var scope = host.Scope();
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), scope, "\"first\"", default);
        await WaitSucceededAsync(database, 1);
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), scope, "\"resend\"", default);
        await host.Scheduler.CancelAsync(host.Key(), default);
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), scope, "\"after-cancel\"", default);
        await host.Scheduler.ScheduleAsync(host.Key("marker"), host.Clock.UtcNow.AddSeconds(-1), scope, "\"marker\"", default);
        await WaitSucceededAsync(database, 2);
        await host.StopAsync();
        Assert.Equal(2, probe.Calls.Count);
        Assert.Single(probe.Calls, call => call.Payload == "\"first\"");
        Assert.Single(probe.Calls, call => call.Payload == "\"marker\"");
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job"));
        Assert.Equal(probe.Created, probe.Disposed);
    }

    /// <summary>故意用官方狀態機重新入列被替換的舊 ID，模擬延遲 transport；舊 generation 必須無 handler 副作用，新 generation 僅執行一次。</summary>
    [Fact]
    public async Task StaleGeneration_ReenqueuedOldJob_DoesNotExecuteOldPayload()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false);
        var scope = host.Scope();
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), scope, "\"old\"", default);
        string oldJob;
        using (var connection = host.Runtime.Storage.GetConnection())
            oldJob = Assert.Single(connection.GetAllItemsFromSet("schedule"));
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), scope, "\"new\"", default);
        Assert.True(new BackgroundJobClient(host.Runtime.Storage, host.Runtime.FilterProvider)
            .ChangeState(oldJob, new EnqueuedState(), "Deleted"));
        Assert.Equal(1, EnqueuePending(host));
        host.StartManualWorkers();
        await WaitSucceededAsync(database, 2);
        await host.StopAsync();
        Assert.Equal((scope, "\"new\""), Assert.Single(probe.Calls));
        Assert.Single(probe.Validations);
        Assert.Equal(probe.Created, probe.Disposed);
    }

    /// <summary>在未啟 worker 前將 ledger 設為 Running，模擬程序在不明副作用後消失；重啟取件必須永久 Failed，不得重試或執行 handler，且失敗有期限。</summary>
    [Fact]
    public async Task UnknownRunningOutcome_FailsClosedWithoutHandlerOrRetry()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var probe = new SchedulingProbe();
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false);
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), host.Scope(), "{}", default);
        await database.ExecuteAsync("UPDATE scheduler_goodshepherd2.Hash SET Value=N'Running' WHERE Field=N'state'");
        Assert.Equal(1, EnqueuePending(host));
        host.StartManualWorkers();
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync(
            "SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Failed' AND ExpireAt IS NOT NULL") == 1,
            "Unknown running outcome was not permanently failed.");
        await host.StopAsync();
        Assert.Empty(probe.Calls);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.State WHERE Name='Processing'"));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Processing'"));
        Assert.Equal("Uncertain", Assert.Single(await database.QueryAsync(
            "SELECT Value FROM scheduler_goodshepherd2.Hash WHERE Field=N'state'", reader => reader.GetString(0))));
    }

    /// <summary>handler 確認進入後由正常 Host Stop 取消；沒有手動 release，必須由真取消 token 排空、標記失敗且不重試，已啟本地告警亦不得寫取消事件。</summary>
    [Fact]
    public async Task Shutdown_CancelsInFlightHandler_NoRetryOrAlertAndNoProcessingResidue()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new SchedulingProbe
        {
            Work = async (_, _, _, ct) =>
            {
                entered.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
            }
        };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, writeExceptionLog: true);
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), host.Scope(), "{}", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(probe.Calls);
        Assert.Equal(0, probe.Active);
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Failed' AND ExpireAt IS NOT NULL"));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Processing'"));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.JobParameter WHERE Name='RetryCount' AND Value<>'0'"));
        Assert.False(File.Exists(Path.Combine(host.DiagnosticsDirectory, "Exception.log")));
    }

    /// <summary>handler 已成功產生合成副作用後，以真 SQL trigger 阻擋 Completed 發布；只能標 Uncertain 並永久失敗，不能因完成寫入故障重送副作用。</summary>
    [Fact]
    public async Task Completion_RealSqlFailureAfterHandlerReturn_UncertainAndNeverRetriesSideEffect()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var effects = 0;
        var probe = new SchedulingProbe
        {
            Work = (_, _, _, _) =>
            {
                Interlocked.Increment(ref effects);
                return Task.CompletedTask;
            }
        };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe, startServer: false, writeExceptionLog: true);
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddHours(1), host.Scope(), "{}", default);
        await database.ExecuteAsync("CREATE TRIGGER scheduler_goodshepherd2.F4_CompletionFail ON scheduler_goodshepherd2.Hash AFTER INSERT,UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Field=N'state' AND Value=N'Completed') THROW 51095,'F4 completion fault sentinel.',1; END");
        try
        {
            Assert.Equal(1, EnqueuePending(host));
            host.StartManualWorkers();
            await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync(
                "SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Failed' AND ExpireAt IS NOT NULL") == 1,
                "Completion SQL fault did not terminate permanently.");
            await host.StopAsync();
        }
        finally { await database.ExecuteAsync("DROP TRIGGER scheduler_goodshepherd2.F4_CompletionFail"); }
        Assert.Equal(1, effects);
        Assert.Single(probe.Calls);
        Assert.Equal(probe.Created, probe.Disposed);
        Assert.Equal(0, probe.Active);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.State WHERE Name='Processing'"));
        Assert.Equal("Uncertain", Assert.Single(await database.QueryAsync(
            "SELECT Value FROM scheduler_goodshepherd2.Hash WHERE Field=N'state'", reader => reader.GetString(0))));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.JobParameter WHERE Name='RetryCount' AND Value<>'0'"));
        var incident = Assert.Single(await File.ReadAllLinesAsync(Path.Combine(host.DiagnosticsDirectory, "Exception.log")));
        using var document = JsonDocument.Parse(incident);
        Assert.Equal("Microsoft.Data.SqlClient.SqlException", document.RootElement.GetProperty("ExceptionType").GetString());
        Assert.DoesNotContain("F4 completion fault sentinel", incident, StringComparison.Ordinal);
    }

    /// <summary>handler 成功返回後 scoped Dispose 拋合成故障，仍已歸還資源計數；正式 dispatcher 必須標 Uncertain、永久 Failed 且只執行一次副作用。</summary>
    [Fact]
    public async Task Scope_DisposeFailureAfterHandlerReturn_UncertainWithoutRetry()
    {
        await using var database = await SchedulingDatabase.CreateAsync();
        var effects = 0;
        var probe = new SchedulingProbe
        {
            DisposeFailure = new InvalidOperationException("F4 dispose sentinel."),
            Work = (_, _, _, _) =>
            {
                Interlocked.Increment(ref effects);
                return Task.CompletedTask;
            }
        };
        await using var host = await SchedulingTestHost.CreateAsync(database, probe);
        await host.Scheduler.ScheduleAsync(host.Key(), host.Clock.UtcNow.AddSeconds(-1), host.Scope(), "{}", default);
        await SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync(
            "SELECT COUNT(*) FROM scheduler_goodshepherd2.Job WHERE StateName='Failed' AND ExpireAt IS NOT NULL") == 1,
            "Scope disposal fault did not permanently fail.");
        await host.StopAsync();
        Assert.Equal(1, effects);
        Assert.Single(probe.Calls);
        Assert.Equal(1, probe.Created);
        Assert.Equal(1, probe.Disposed);
        Assert.Equal(0, probe.Active);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.State WHERE Name='Processing'"));
        Assert.Equal("Uncertain", Assert.Single(await database.QueryAsync(
            "SELECT Value FROM scheduler_goodshepherd2.Hash WHERE Field=N'state'", reader => reader.GetString(0))));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM scheduler_goodshepherd2.JobParameter WHERE Name='RetryCount' AND Value<>'0'"));
    }

    /// <summary>官方狀態機只推進本案現存 Scheduled ID，不新增工作、不改 payload 或 scope；用於刻意省去等待時間的案例。</summary>
    private static int EnqueuePending(SchedulingTestHost host)
    {
        using var connection = host.Runtime.Storage.GetConnection();
        var pending = connection.GetAllItemsFromSet("schedule");
        var client = new BackgroundJobClient(host.Runtime.Storage, host.Runtime.FilterProvider);
        foreach (var job in pending) Assert.True(client.ChangeState(job, new EnqueuedState(), "Scheduled"));
        return pending.Count;
    }

    /// <summary>以 SQL 已提交終態等待實際 dispatcher 完成，不以測試 callback 的開始時點當成成功。</summary>
    private static Task WaitSucceededAsync(SchedulingDatabase database, int count, string schema = "scheduler_goodshepherd2") =>
        SchedulingDatabase.UntilAsync(async () => await database.ScalarAsync("SELECT COUNT(*) FROM " + schema + ".Job WHERE StateName='Succeeded'") == count,
            "Expected completed SQL dispatch count was not observed.");
}
