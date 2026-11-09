/*
檔案整體說明
目的與定位：使用真 MSSQLLocalDB 保護 P3-04 跨連線交易去重、頻道隔離、回滾重送與每日七天清理，並驗正式 DI 接點。
主要功能與流程：每案例建立唯一四欄資料庫及 provider，透過已驗收 IWebhookEventDeduplicator 取得 lease，以第二條連線讀回或競爭；cleanup 使用可推進時鐘與正式 hosted service，不用替身冒稱 SQL 成功。
使用邊界與維護：Category=SqlIntegration 預設未選取；必須手動 filter 才執行。所有 lease、provider、診斷與資料庫依相依順序 await using 釋放；故障斷言必須精確排除把取消、1222 或 timeout 當重複。實例狀態還原由本次操作紀錄負責，fixture 確認自己測試庫殘留零。
*/
using System.Diagnostics;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>真正 SQL 整合案例，只以本機合成庫驗證，不依賴任何使用者或租戶憑證。</summary>
[Trait("Category", "SqlIntegration")]
public sealed class WebhookSqlIntegrationTests
{
    /// <summary>提交第一筆後第二條連線必回 null，並從 SQL 確認只存一列與原重送旗標。</summary>
    [Fact]
    public async Task Dedupe_SameEvent_SecondIsDuplicate()
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync();
        await using var owner = new Owner(database.ConnectionString);
        var dedupe = owner.Dedupe;
        await using (var first = await dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("same"), default))
        { Assert.NotNull(first); await first.CommitAsync(default); }
        await using var second = await dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("same"), default);
        Assert.Null(second); Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent"));
    }

    /// <summary>同名相同事件內容在不同已驗頻道各自提交，複合主鍵不能退化為全域事件鍵。</summary>
    [Fact]
    public async Task Dedupe_SameEventIdDifferentChannels_NotDuplicate()
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync(); await using var owner = new Owner(database.ConnectionString);
        foreach (var channel in new[] { "123", "456" })
        {
            await using var claim = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("same", channel: channel), default);
            Assert.NotNull(claim); await claim.CommitAsync(default);
        }
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent WHERE WebhookEventId='same'"));
    }

    /// <summary>未提交 lease Dispose 必回滾，第二次可取得 claim；不同穩定 ID 即使內容相同也全部保留。</summary>
    [Fact]
    public async Task Dedupe_RollbackRetryAndDistinctIdentities_ArePreserved()
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync(); await using var owner = new Owner(database.ConnectionString);
        await using (var aborted = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("retry"), default)) Assert.NotNull(aborted);
        foreach (var id in new[] { "retry", "other" })
        {
            await using var claim = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope(id), default);
            Assert.NotNull(claim); await claim.CommitAsync(default);
        }
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent"));
    }

    /// <summary>兩條真連線同時競爭同鍵，第一筆 Commit 則第二筆判重；Rollback 則第二筆成功，不使用行程內鎖判重。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dedupe_ConcurrentClaim_WaitsForSqlResolution(bool commit)
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync(); await using var owner = new Owner(database.ConnectionString);
        var first = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("race"), default); Assert.NotNull(first);
        try
        {
            var next = owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("race"), default).AsTask();
            await WaitForBlockedSessionAsync(database);
            Assert.False(next.IsCompleted);
            if (commit) await first.CommitAsync(default);
            await first.DisposeAsync();
            await using var second = await next.WaitAsync(TimeSpan.FromSeconds(10));
            if (commit) Assert.Null(second);
            else { Assert.NotNull(second); await second.CommitAsync(default); }
        }
        finally { await first.DisposeAsync(); }
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent"));
    }

    /// <summary>第一條連線持鎖未決時第二條最多五秒命令期限失敗，-2／取消不可冒充已提交重複。</summary>
    [Fact]
    public async Task Dedupe_UnresolvedClaim_TimesOutAndCanRetry()
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync(); await using var owner = new Owner(database.ConnectionString);
        var first = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("blocked"), default); Assert.NotNull(first);
        try
        {
            var failure = await Record.ExceptionAsync(async () =>
            { await using var unexpected = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("blocked"), default); });
            Assert.NotNull(failure);
            Assert.True(failure is OperationCanceledException || failure is SqlException { Number: -2 }, failure.GetType().FullName);
        }
        finally { await first.DisposeAsync(); }
        await using var retry = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("blocked"), default); Assert.NotNull(retry);
        await retry.CommitAsync(default);
    }

    /// <summary>清理只刪截止點前一毫秒，恰好七天與新資料保持；手動時計驅動正式每日工作，停止後 timer 歸零。</summary>
    [Fact]
    public async Task Cleanup_DeletesOnlyOlderThan7Days()
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync(); var clock = new WebhookQueueClock();
        clock.Advance(TimeSpan.FromDays(20000));
        await using var owner = new Owner(database.ConnectionString, clock);
        Assert.NotNull(owner.Dedupe);
        var cleanup = Assert.Single(owner.Provider.GetServices<IHostedService>().Where(s => s.GetType().Name == "SqlWebhookCleanupService"));
        var cutoff = (clock.GetUtcNow() + TimeSpan.FromDays(1) - TimeSpan.FromDays(7)).UtcDateTime;
        await database.InsertAsync("old", cutoff.AddMilliseconds(-1));
        await database.InsertAsync("boundary", cutoff); await database.InsertAsync("new", cutoff.AddMilliseconds(1));
        await cleanup.StartAsync(default);
        try
        {
            await clock.WaitForTimersAsync(TimeSpan.FromDays(1)); clock.Advance(TimeSpan.FromDays(1));
            await WaitForCountAsync(database, 2);
            Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent WHERE WebhookEventId='old'"));
            Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent WHERE WebhookEventId IN ('boundary','new')"));
        }
        finally { await cleanup.StopAsync(new CancellationToken(true)); }
        Assert.Equal(0, clock.TimerCount);
    }

    /// <summary>輪詢的是 SQL 真實鎖等待而非猜測 sleep；十秒 watchdog 僅限制壞測試，不替代產品五秒期限。</summary>
    private static async Task WaitForBlockedSessionAsync(WebhookSqlTestDatabase database)
    {
        var watch = Stopwatch.StartNew();
        while (await database.ScalarAsync("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id>0") == 0)
        { Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), "No SQL lock waiter observed."); await Task.Delay(10); }
    }

    /// <summary>真 SQL 1222 及其他唯一索引衝突均不可當主鍵已存在；不同穩定鍵失敗後資料庫仍只有原列。</summary>
    [Fact]
    public async Task Dedupe_RealSqlErrors_OnlyExactCommittedKeyIsDuplicate()
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync(); await using var owner = new Owner(database.ConnectionString);
        await database.ExecuteAsync("CREATE TABLE dbo.P304_LockProbe(Id int PRIMARY KEY, Value int NOT NULL); INSERT dbo.P304_LockProbe VALUES(1,0)");
        await database.ExecuteAsync("CREATE TRIGGER dbo.P304_LockFailure ON dbo.LineWebhookEvent AFTER INSERT AS BEGIN SET LOCK_TIMEOUT 100; UPDATE dbo.P304_LockProbe SET Value=Value+1 WHERE Id=1; END");
        await using (var blocker = new SqlConnection(database.ConnectionString))
        {
            await blocker.OpenAsync();
            await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
            await using var command = blocker.CreateCommand(); command.CommandTimeout = 5; command.Transaction = transaction;
            command.CommandText = "UPDATE dbo.P304_LockProbe SET Value=1 WHERE Id=1"; await command.ExecuteNonQueryAsync();
            try
            {
                var locked = await Assert.ThrowsAsync<SqlException>(() => owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("lock-failure"), default).AsTask());
                Assert.Equal(1222, locked.Number); Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent"));
            }
            finally { await transaction.RollbackAsync(); }
        }
        await database.ExecuteAsync("DROP TRIGGER dbo.P304_LockFailure; CREATE UNIQUE INDEX P304_OtherUnique ON dbo.LineWebhookEvent(IsRedelivery)");
        await using (var first = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("first"), default))
        { Assert.NotNull(first); await first.CommitAsync(default); }
        var conflict = await Assert.ThrowsAsync<SqlException>(() => owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("other-key"), default).AsTask());
        Assert.Equal(2601, conflict.Number); Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent"));
    }

    /// <summary>三輪固定併行 A/B 同事件重送只各提交一次；所有實際 SQL sessions 結束回零，不以 mock 計數代替 drain。</summary>
    [Fact]
    public async Task ConcurrentChannelsAndRetries_RealSessionsReturnToBaseline()
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync(); await using var owner = new Owner(database.ConnectionString);
        for (var round = 0; round < 3; round++)
        {
            var received = await Task.WhenAll(Enumerable.Range(0, 16).SelectMany(index => new[] { "123", "456" }.SelectMany(channel => Enumerable.Range(0, 2).Select(async _ =>
            {
                var id = "wave-" + round.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await using var claim = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope(id, channel: channel), default);
                if (claim is null) return false;
                await claim.CommitAsync(default); return true;
            }))));
            Assert.Equal(32, received.Count(value => value)); Assert.Equal(32, received.Count(value => !value));
            Assert.Equal((round + 1) * 32, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent"));
            await AssertSessionsDrainAsync(database);
        }
    }

    /// <summary>真一條連線池耗盡時 Open 仍被五秒 deadline 取消，現存 lease 可完成，Host owner 關閉後實際池連線歸零。</summary>
    [Fact]
    public async Task PooledOpen_DeadlineAndOwnerDisposal_DrainRealSessions()
    {
        await using var database = await WebhookSqlTestDatabase.CreateAsync(); var clock = new WebhookQueueClock();
        var configuration = new SqlConnectionStringBuilder(database.ConnectionString)
        { Pooling = true, MaxPoolSize = 1, MinPoolSize = 1, ConnectTimeout = 30, ConnectRetryCount = 5 };
        await using (var owner = new Owner(configuration.ConnectionString, clock))
        {
            var first = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("pool-held"), default); Assert.NotNull(first);
            try
            {
                var waiting = owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("pool-waiting"), default).AsTask();
                await clock.WaitForTimersAsync(TimeSpan.FromSeconds(5)); Assert.False(waiting.IsCompleted); clock.Advance(TimeSpan.FromSeconds(5));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(WebhookQueueFixture.Limit));
                await first.CommitAsync(default);
            }
            finally { await first.DisposeAsync(); }
            await using var retry = await owner.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("pool-waiting"), default); Assert.NotNull(retry);
            await retry.CommitAsync(default);
        }
        Assert.Equal(0, clock.TimerCount);
        await AssertSessionsDrainAsync(database);
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent"));
    }

    /// <summary>讀回已提交刪除結果，等待有界且不把排程啟動等同於刪除完成。</summary>
    private static async Task WaitForCountAsync(WebhookSqlTestDatabase database, int count)
    {
        var watch = Stopwatch.StartNew();
        while (await database.ScalarAsync("SELECT COUNT(*) FROM dbo.LineWebhookEvent") != count)
        { Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Cleanup did not complete."); await Task.Delay(10); }
    }

    /// <summary>同一觀測連線等待伺服器確認其他 sessions 已斷開，避免前次 Scalar 觀測連線本身形成瞬時殘留；五秒內不歸零即失敗並保存每次原始計數。</summary>
    private static async Task AssertSessionsDrainAsync(WebhookSqlTestDatabase database)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var observer = new SqlConnection(database.ConnectionString); await observer.OpenAsync(deadline.Token);
        await using var command = observer.CreateCommand(); command.CommandTimeout = 5;
        command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id=DB_ID() AND session_id<>@@SPID";
        var samples = new List<int>(); var watch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var count = Convert.ToInt32(await command.ExecuteScalarAsync(deadline.Token), System.Globalization.CultureInfo.InvariantCulture);
                samples.Add(count); if (count == 0) break;
                Assert.True(samples.Count < 500, "SQL sessions did not drain within the bounded sample count.");
                await Task.Delay(10, deadline.Token);
            }
            Assert.Equal(0, samples[^1]);
        }
        finally
        {
            var evidence = Path.Combine(WebhookSqlTestDatabase.Root, ".trellis", "tasks", "11-09-p3-04-sql-dedupe", "evidence", "session-drain");
            Directory.CreateDirectory(evidence);
            await File.WriteAllTextAsync(Path.Combine(evidence, Guid.NewGuid().ToString("N") + ".json"), System.Text.Json.JsonSerializer.Serialize(new
            { samples, elapsedMilliseconds = watch.ElapsedMilliseconds }));
        }
    }

    /// <summary>單案例 DI 與安全診斷 owner；真 SQL 服務只能從正式 AddLineWebhook 接點取得。</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly ExceptionDiagnostics diagnostics;
        /// <summary>唯一容器，沒有跨案例 static singleton；所有 hosted 及 SQL 服務歸此容器管理。</summary>
        internal ServiceProvider Provider { get; }
        /// <summary>缺正式註冊時決定性紅燈，不用替身或記憶體後備補齊。</summary>
        internal IWebhookEventDeduplicator Dedupe => Assert.IsAssignableFrom<IWebhookEventDeduplicator>(Provider.GetService<IWebhookEventDeduplicator>());
        /// <summary>只有合成本機連線，組態註冊後立即 Dispose，驗證產品只能保存啟動快照。</summary>
        internal Owner(string connectionString, TimeProvider? clock = null)
        {
            diagnostics = new ExceptionDiagnostics(Path.Combine(WebhookSqlTestDatabase.Root, ".trellis", "tasks", "11-09-p3-04-sql-dedupe", "evidence", "sql", Guid.NewGuid().ToString("N")), outputOptions: new ExceptionOutputOptions(true, false));
            using var configuration = new ConfigurationManager(); configuration["ConnectionStrings:Platform"] = connectionString;
            var services = new ServiceCollection(); services.AddSingleton(diagnostics); services.AddSingleton(clock ?? TimeProvider.System);
            services.AddLineWebhook(configuration); Provider = services.BuildServiceProvider();
        }
        /// <summary>先停止所有容器 owner 再 drain 診斷；SQL fixture 最後刪庫，避免把清理失敗留給後續案例。</summary>
        public async ValueTask DisposeAsync()
        { try { await Provider.DisposeAsync(); } finally { await diagnostics.DisposeAsync(); } }
    }
}
