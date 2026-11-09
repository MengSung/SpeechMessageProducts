/*
檔案整體說明
目的與定位：保護 P3-04 正式 SQL lease 與 P3-05 真佇列的交接，禁止把已收到的 COMMIT 成功確認改成失敗而漏事件。
主要功能與流程：以窄 provider 接縫在成功回傳 COMMIT 結果時注入 caller 晚取消、內部 deadline 晚到或命令清理故障，真正佇列必須發布一次；未確認的提交故障仍由既有 uncertain 測試保護。
使用邊界與維護：本檔只測程序內確認邊界，不宣稱解決程序崩潰或 SQL 已提交但確認遺失的持久性缺口。每案例分別擁有 SQL probe、queue、clock 與 token，停止並排空後檢查命令、連線、scope、timer 計數歸零；沒有真實憑證、外部服務或共用使用者狀態。
*/
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>以真 queue 與正式 SQL lease 驗證已確認結果的最終性，接縫只取代 provider I/O。</summary>
public sealed class WebhookDedupeAcknowledgementTests
{
    /// <summary>已確認成功後的取消或釋放失敗只影響後續清理，不撤回發布；每筆恰一次且無 rollback／不確定誤報。</summary>
    [Theory]
    [InlineData("caller-cancel")]
    [InlineData("deadline")]
    [InlineData("command-dispose")]
    public async Task AcknowledgedCommit_LateCancellationOrCleanupFailure_StillPublishesOnce(string fault)
    {
        await using var probe = new WebhookSqlProbe();
        using var caller = new CancellationTokenSource();
        var processed = WebhookQueueFixture.Signal(); var deliveries = 0; var commits = 0; var rollbacks = 0;
        var cleanup = new IOException("synthetic-private-cleanup-marker");
        if (fault == "command-dispose") probe.CommitCommandDisposeFailure = cleanup;
        probe.Execute = (command, _) =>
        {
            if (command.CommandText.StartsWith("COMMIT", StringComparison.Ordinal))
            {
                commits++;
                if (fault == "caller-cancel") caller.Cancel();
                if (fault == "deadline") probe.Clock.Advance(TimeSpan.FromSeconds(5));
            }
            if (command.CommandText.Contains("ROLLBACK", StringComparison.Ordinal)) rollbacks++;
            return Task.FromResult(1);
        };
        await using var queue = new WebhookQueueFixture((_, _) =>
        {
            Interlocked.Increment(ref deliveries); processed.TrySetResult(); return ValueTask.CompletedTask;
        });
        await queue.StartAsync();
        var envelope = WebhookQueueFixture.Envelope("acknowledged-commit");
        await using (var claim = await probe.Dedupe.TryRegisterAsync(envelope, caller.Token))
        {
            Assert.NotNull(claim);
            Assert.True(await queue.Queue.TryEnqueueAsync(envelope, claim, caller.Token));
            await processed.Task.WaitAsync(WebhookQueueFixture.Limit);
        }
        await queue.Queue.StopAsync(default);
        Assert.Equal(1, commits); Assert.Equal(1, deliveries); Assert.Equal(0, rollbacks);
        var path = Path.Combine(probe.LogPath, "Exception.log");
        var log = File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
        Assert.DoesNotContain("CommitUncertain", log, StringComparison.Ordinal);
        if (fault == "command-dispose") Assert.False(probe.ReportAgain(cleanup));
        queue.AssertBaseline(); probe.AssertBaseline();
    }
}
