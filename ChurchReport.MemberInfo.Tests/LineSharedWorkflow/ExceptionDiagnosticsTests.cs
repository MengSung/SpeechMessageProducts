using System.Collections.Concurrent;
using System.Text.Json;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>
/// 保護正式環境錯誤落檔、通知隔離及有限生命週期。使用測試專用目錄與可取消 sender，
/// 絕不讀取部署憑證或呼叫真實 LINE；每個測試自行釋放 owner 後才刪除目錄。
/// </summary>
public sealed class ExceptionDiagnosticsTests
{
    /// <summary>以檔案占用應為目錄的位置注入磁碟錯誤；落檔失敗時不得開始 LINE 發送。</summary>
    [Fact]
    public async Task Failed_file_write_prevents_line_delivery()
    {
        var path = Path.GetTempFileName();
        var sent = 0;
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(path))
            {
                diagnostics.StartNotifications((_, _) => { sent++; return Task.CompletedTask; });
                Assert.False(diagnostics.Report(new Exception("private"), "WriteFailure"));
            }
            Assert.Equal(0, sent);
        }
        finally { File.Delete(path); }
    }

    /// <summary>阻塞 sender 後灌入超過容量的故障；每筆先落檔，佇列满留下狀態，關機取消後回基準。</summary>
    [Fact]
    public async Task Saturation_keeps_all_file_records_and_shutdown_cancels_sender()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                diagnostics.StartNotifications(async (_, token) =>
                {
                    Interlocked.Increment(ref active);
                    started.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                    finally { Interlocked.Decrement(ref active); }
                });
                diagnostics.Report(new Exception(), "Start");
                await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                for (var i = 0; i < 100; i++) diagnostics.Report(new Exception("secret"), "Burst");
            }
            Assert.Equal(0, active);
            var text = File.ReadAllText(Path.Combine(directory, "Exception.log"));
            Assert.Equal(101, File.ReadLines(Path.Combine(directory, "Exception.log")).Count(l => l.Contains("IncidentId")));
            Assert.Contains("LineQueueFull", text);
            Assert.DoesNotContain("secret", text);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    /// <summary>注入含機密內容的例外；日誌與告警只含定位 metadata，重複通報同一實例一次。</summary>
    [Fact]
    public async Task Report_writes_file_before_notification_and_deduplicates_without_sensitive_values()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sent = new ConcurrentQueue<string>();
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                diagnostics.StartNotifications((message, token) =>
                {
                    Assert.True(File.Exists(Path.Combine(directory, "Exception.log")));
                    sent.Enqueue(message);
                    return Task.CompletedTask;
                });
                var exception = new InvalidOperationException("password=private-user-A");
                diagnostics.Report(exception, "Payment.Create");
                diagnostics.Report(exception, "Outer");
            }
            Assert.Single(sent);
            var lines = File.ReadAllLines(Path.Combine(directory, "Exception.log"));
            Assert.Single(lines);
            Assert.DoesNotContain("private-user-A", lines[0]);
            Assert.Contains("InvalidOperationException", sent.Single());
            using var json = JsonDocument.Parse(lines[0]);
            Assert.Contains(json.RootElement.GetProperty("IncidentId").GetString()!, sent.Single());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>LINE 失敗只落本地且 consumer 可繼續；正常取消排除，但未取消的逾時仍記錄。</summary>
    [Fact]
    public async Task Notification_failure_is_logged_without_recursion_and_timeout_is_actionable()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sends = 0;
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                diagnostics.StartNotifications((_, _) =>
                {
                    Interlocked.Increment(ref sends);
                    throw new IOException("secret-provider-response");
                });
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                diagnostics.Report(new OperationCanceledException(cancellation.Token), "Canceled", cancellation.Token);
                diagnostics.Report(new TimeoutException("timeout-secret"), "Failed");
            }
            Assert.Equal(1, sends);
            var log = File.ReadAllText(Path.Combine(directory, "Exception.log"));
            Assert.Contains("TimeoutException", log);
            Assert.Contains("LineDeliveryFailed", log);
            Assert.DoesNotContain("secret", log);
            Assert.DoesNotContain("Canceled", log);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>大量 A/B 併行失敗不混入私人內容；小檔案輪替維持有限備份且釋放後可刪除。</summary>
    [Fact]
    public async Task Concurrent_reports_rotate_with_bounded_storage_and_release_handles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory, maximumFileBytes: 4096))
            {
                Parallel.For(0, 200, i => diagnostics.Report(new Exception("user-" + i), "Concurrent"));
            }
            var files = Directory.GetFiles(directory, "Exception*.log");
            Assert.InRange(files.Length, 2, 6);
            foreach (var file in files)
            {
                Assert.True(new FileInfo(file).Length <= 4096);
                Assert.DoesNotContain("user-", File.ReadAllText(file));
                foreach (var line in File.ReadLines(file)) using (JsonDocument.Parse(line)) { }
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
