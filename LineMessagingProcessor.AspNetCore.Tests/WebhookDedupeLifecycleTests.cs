/*
檔案整體說明
目的與定位：以窄連線故障接縫驗 P3-04 每個 SQL 階段期限、提交不確定的原例外與獨立 rollback 所有權。
主要功能與流程：每案例自己建立 probe、手動時鐘與 callback，同步訊號確認操作已進入後才推進五秒；斷言原故障身分、沒有誤判重複或重試提交，全部命令、連線與 timer 歸零。
使用邊界與維護：本檔只驗離線失敗與生命週期，不宣稱 SQL 主鍵、交易持鎖或遠端部署成功。所有 callback、token 與診斷由單案例 owner 清理，不保存真實帳密或跨案例狀態，真 SQL 成功另以整合案例證明。
*/
using System.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>決定性取消與失敗狀態機，避免以 fake success 掩蓋缺少正式服務。</summary>
public sealed class WebhookDedupeLifecycleTests
{
    /// <summary>各步驟真正進入後推進五秒，必須取消而非成功或 null，並釋放全部 owner。</summary>
    [Theory]
    [InlineData("open")]
    [InlineData("BEGIN")]
    [InlineData("INSERT")]
    [InlineData("COMMIT")]
    [InlineData("ROLLBACK")]
    public async Task EverySqlStage_HasIndependentFiveSecondDeadline(string stage)
    {
        await using var probe = new WebhookSqlProbe(); var entered = WebhookQueueFixture.Signal();
        async Task Block(CancellationToken token) { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        if (stage == "open") probe.Open = Block;
        probe.Execute = async (command, token) =>
        { if (command.CommandText.Contains(stage, StringComparison.Ordinal)) await Block(token); return 1; };
        var dedupe = probe.Dedupe;
        Task operation;
        LineMessagingProcessor.AspNetCore.Webhooks.IWebhookEventRegistration? claim = null;
        if (stage is "COMMIT" or "ROLLBACK")
        {
            claim = await dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("deadline"), default); Assert.NotNull(claim);
            operation = stage == "COMMIT" ? claim.CommitAsync(default).AsTask() : claim.DisposeAsync().AsTask();
        }
        else operation = dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("deadline"), default).AsTask();
        try
        {
            await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
            await probe.Clock.WaitForTimersAsync(TimeSpan.FromSeconds(5)); probe.Clock.Advance(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(WebhookQueueFixture.Limit));
        }
        finally
        {
            if (claim is not null)
            {
                if (stage == "ROLLBACK") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => claim.DisposeAsync().AsTask());
                else await claim.DisposeAsync();
            }
        }
        probe.AssertBaseline();
    }

    /// <summary>HTTP 取消後 Dispose 的 rollback 使用獨立 token，不能被 caller 提前取消而遺留鎖。</summary>
    [Fact]
    public async Task Rollback_IgnoresCancelledCaller_AndUsesTypedParameters()
    {
        await using var probe = new WebhookSqlProbe(); var rollbacks = 0; var inserts = 0;
        probe.Execute = (command, token) =>
        {
            Assert.False(token.IsCancellationRequested);
            if (command.CommandText.StartsWith("INSERT", StringComparison.Ordinal))
            {
                inserts++; Assert.Equal(4, command.Parameters.Count);
                Assert.Equal(DbType.AnsiString, command.Parameters[0].DbType); Assert.Equal(20, command.Parameters[0].Size);
                Assert.Equal(DbType.AnsiString, command.Parameters[1].DbType); Assert.Equal(64, command.Parameters[1].Size);
                Assert.Equal(DbType.DateTime2, command.Parameters[2].DbType); Assert.Equal(3, command.Parameters[2].Scale);
                Assert.Equal(DbType.Boolean, command.Parameters[3].DbType);
            }
            if (command.CommandText.Contains("ROLLBACK", StringComparison.Ordinal)) rollbacks++;
            return Task.FromResult(1);
        };
        using var caller = new CancellationTokenSource();
        var claim = await probe.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("cancel"), caller.Token); Assert.NotNull(claim);
        await caller.CancelAsync(); await claim.DisposeAsync(); await claim.DisposeAsync();
        Assert.Equal(1, inserts); Assert.Equal(1, rollbacks); probe.AssertBaseline();
    }

    /// <summary>提交錯誤保留原物件並標記不確定；Dispose 不再 rollback 或重試 INSERT，避免把已提交結果誤當未提交。</summary>
    [Fact]
    public async Task CommitUncertain_PreservesOriginalAndNeverRetriesOrRollsBack()
    {
        await using var probe = new WebhookSqlProbe(); var failure = new IOException("private-commit-marker"); var commits = 0; var rollbacks = 0;
        probe.Execute = (command, _) =>
        {
            if (command.CommandText.StartsWith("COMMIT", StringComparison.Ordinal)) { commits++; throw failure; }
            if (command.CommandText.Contains("ROLLBACK", StringComparison.Ordinal)) rollbacks++;
            return Task.FromResult(1);
        };
        var claim = await probe.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("uncertain"), default); Assert.NotNull(claim);
        try { Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => claim.CommitAsync(default).AsTask())); }
        finally { await claim.DisposeAsync(); }
        Assert.Equal(1, commits); Assert.Equal(0, rollbacks); Assert.False(probe.ReportAgain(failure));
        var log = await File.ReadAllTextAsync(Path.Combine(probe.LogPath, "Exception.log"));
        Assert.Contains("CommitUncertain", log, StringComparison.Ordinal); Assert.DoesNotContain("private-commit-marker", log, StringComparison.Ordinal);
        probe.AssertBaseline();
    }

    /// <summary>INSERT 及 rollback 同時失敗仍拋 INSERT 原錯誤，兩原例外交共享入口，不能由 finally 覆蓋主錯誤。</summary>
    [Fact]
    public async Task InsertAndRollbackFailure_PreservesPrimaryAndReleasesResources()
    {
        await using var probe = new WebhookSqlProbe(); var primary = new IOException("private-insert"); var cleanup = new InvalidOperationException("private-rollback");
        probe.Execute = (command, _) =>
        {
            if (command.CommandText.StartsWith("INSERT", StringComparison.Ordinal)) throw primary;
            if (command.CommandText.Contains("ROLLBACK", StringComparison.Ordinal)) throw cleanup;
            return Task.FromResult(1);
        };
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => probe.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("failure"), default).AsTask()));
        Assert.False(probe.ReportAgain(cleanup)); probe.AssertBaseline();
    }

    /// <summary>正式每日工作失敗後仍存活；停止 token 已取消也須等 SQL callback 真正排空才返回。</summary>
    [Fact]
    public async Task Cleanup_StopWaitsForRealDrain_AndReleasesTimer()
    {
        await using var probe = new WebhookSqlProbe(); var entered = WebhookQueueFixture.Signal(); var cancelling = WebhookQueueFixture.Signal(); var release = WebhookQueueFixture.Signal();
        probe.Execute = async (command, token) =>
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.Ordinal))
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelling.TrySetResult(); await release.Task; throw; }
            }
            return 0;
        };
        Assert.NotNull(probe.Dedupe);
        var service = Assert.Single(probe.Provider.GetServices<IHostedService>().Where(s => s.GetType().Name == "SqlWebhookCleanupService"));
        await service.StartAsync(default);
        try
        {
            await probe.Clock.WaitForTimersAsync(TimeSpan.FromDays(1)); probe.Clock.Advance(TimeSpan.FromDays(1)); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
            var stopped = service.StopAsync(new CancellationToken(true)); await cancelling.Task.WaitAsync(WebhookQueueFixture.Limit);
            Assert.False(stopped.IsCompleted); release.TrySetResult(); await stopped.WaitAsync(WebhookQueueFixture.Limit);
        }
        finally { release.TrySetResult(); await service.StopAsync(default); }
        probe.AssertBaseline();
    }

    /// <summary>Commit 未完成時 Dispose 必等待同一工作，成功後不 rollback；兩次 Commit 只呼叫一次 SQL。</summary>
    [Fact]
    public async Task CommitAndDisposeRace_DrainsOneCommitWithoutRollback()
    {
        await using var probe = new WebhookSqlProbe(); var entered = WebhookQueueFixture.Signal(); var release = WebhookQueueFixture.Signal(); var commits = 0; var rollbacks = 0;
        probe.Execute = async (command, _) =>
        {
            if (command.CommandText.StartsWith("COMMIT", StringComparison.Ordinal)) { commits++; entered.TrySetResult(); await release.Task; }
            if (command.CommandText.Contains("ROLLBACK", StringComparison.Ordinal)) rollbacks++;
            return 1;
        };
        var claim = await probe.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("race"), default); Assert.NotNull(claim);
        var commit = claim.CommitAsync(default).AsTask(); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        var repeated = claim.CommitAsync(default).AsTask(); var dispose = claim.DisposeAsync().AsTask();
        try { Assert.False(dispose.IsCompleted); }
        finally { release.TrySetResult(); await Task.WhenAll(commit, repeated, dispose); }
        await claim.DisposeAsync(); Assert.Equal(1, commits); Assert.Equal(0, rollbacks); probe.AssertBaseline();
    }

    /// <summary>提交前已取消不送 COMMIT，Dispose 仍 rollback；內部提交不確定與明確未開始須分清楚。</summary>
    [Fact]
    public async Task CancelledBeforeCommit_RollsBackWithoutSendingCommit()
    {
        await using var probe = new WebhookSqlProbe(); var commits = 0; var rollbacks = 0;
        probe.Execute = (command, _) =>
        {
            if (command.CommandText.StartsWith("COMMIT", StringComparison.Ordinal)) commits++;
            if (command.CommandText.Contains("ROLLBACK", StringComparison.Ordinal)) rollbacks++;
            return Task.FromResult(1);
        };
        var claim = await probe.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("cancel-before"), default); Assert.NotNull(claim);
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => claim.CommitAsync(new CancellationToken(true)).AsTask()); }
        finally { await claim.DisposeAsync(); }
        Assert.Equal(0, commits); Assert.Equal(1, rollbacks); probe.AssertBaseline();
    }

    /// <summary>每天最多二十批、每批五百；第一天故障不殺掉排程，第二天正常清理仍遵守批次上限。</summary>
    [Fact]
    public async Task Cleanup_FailureSurvivesAndNextDayHasBoundedBatches()
    {
        await using var probe = new WebhookSqlProbe(); var failure = new IOException("private-cleanup-marker"); var attempts = 0;
        probe.Execute = (command, _) =>
        {
            if (!command.CommandText.StartsWith("DELETE", StringComparison.Ordinal)) return Task.FromResult(0);
            Assert.Equal(500, command.Parameters[0].Value); Assert.Equal(DbType.DateTime2, command.Parameters[1].DbType); Assert.Equal(3, command.Parameters[1].Scale);
            Assert.Contains("ReceivedUtc < @cutoff", command.CommandText, StringComparison.Ordinal);
            if (++attempts == 1) throw failure;
            return Task.FromResult(500);
        };
        Assert.NotNull(probe.Dedupe);
        var service = Assert.Single(probe.Provider.GetServices<IHostedService>().Where(s => s.GetType().Name == "SqlWebhookCleanupService"));
        await service.StartAsync(default);
        try
        {
            await probe.Clock.WaitForTimersAsync(TimeSpan.FromDays(1)); probe.Clock.Advance(TimeSpan.FromDays(1));
            await probe.Clock.WaitForTimersAsync(TimeSpan.FromDays(1)); Assert.Equal(1, attempts); Assert.False(probe.ReportAgain(failure));
            probe.Clock.Advance(TimeSpan.FromDays(1)); await probe.Clock.WaitForTimersAsync(TimeSpan.FromDays(1)); Assert.Equal(21, attempts);
        }
        finally { await service.StopAsync(default); }
        probe.AssertBaseline();
    }

    /// <summary>單輪三十秒預算到期會中止後續批次，不能二十批各花五秒而累積一百秒。</summary>
    [Fact]
    public async Task Cleanup_TotalBudgetCancelsBeforeBatchLimit()
    {
        await using var probe = new WebhookSqlProbe(); var batches = 0;
        probe.Execute = (command, _) =>
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.Ordinal)) { batches++; probe.Clock.Advance(TimeSpan.FromSeconds(2)); }
            return Task.FromResult(500);
        };
        Assert.NotNull(probe.Dedupe);
        var service = Assert.Single(probe.Provider.GetServices<IHostedService>().Where(s => s.GetType().Name == "SqlWebhookCleanupService"));
        await service.StartAsync(default);
        try
        {
            await probe.Clock.WaitForTimersAsync(TimeSpan.FromDays(1)); probe.Clock.Advance(TimeSpan.FromDays(1));
            await probe.Clock.WaitForTimersAsync(TimeSpan.FromDays(1)); Assert.Equal(15, batches);
            Assert.Contains("OperationCanceledException", await File.ReadAllTextAsync(Path.Combine(probe.LogPath, "Exception.log")), StringComparison.Ordinal);
        }
        finally { await service.StopAsync(default); }
        probe.AssertBaseline();
    }

    /// <summary>命令執行及 Dispose 同時故障仍拋執行原物件；清理故障另行通知且所有計數歸零。</summary>
    [Fact]
    public async Task CommandDisposeFailure_DoesNotReplaceSqlFailure()
    {
        await using var probe = new WebhookSqlProbe(); var primary = new IOException("private-execute"); var cleanup = new InvalidOperationException("private-command-dispose");
        probe.InsertCommandDisposeFailure = cleanup;
        probe.Execute = (command, _) => command.CommandText.StartsWith("INSERT", StringComparison.Ordinal) ? Task.FromException<int>(primary) : Task.FromResult(1);
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => probe.Dedupe.TryRegisterAsync(WebhookQueueFixture.Envelope("dispose-failure"), default).AsTask()));
        Assert.False(probe.ReportAgain(cleanup)); probe.AssertBaseline();
    }
}
