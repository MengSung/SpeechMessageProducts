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

    /// <summary>
    /// Windows 外部讀取器只允許 ReadWrite、不允許 Delete，模擬即時觀看日誌造成輪替失敗。
    /// 驗證已接受證據完整保留、兩倍硬上限拒絕新事件且不通知；讀取器釋放後，
    /// 同一失敗例外可重試並恢復輪替。sender 與檔案控制代碼均在刪除目錄前 drain／Dispose。
    /// </summary>
    [Fact]
    public async Task Reader_blocked_rotation_appends_with_hard_cap_and_recovers_after_release()
    {
        if (!OperatingSystem.IsWindows()) return;
        const long maximumFileBytes = 4096;
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "Exception.log");
        var sent = new ConcurrentQueue<string>();
        var accepted = 1;
        var rejected = new IOException("private-rejected-detail");
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory, maximumFileBytes))
            {
                diagnostics.StartNotifications((message, _) => { sent.Enqueue(message); return Task.CompletedTask; });
                Assert.True(diagnostics.Report(new IOException(), "BeforeReader"));
                using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // 最多 64 次已足以填滿 8 KiB；迴圈本身有界，也不會灌滿 64 筆通知佇列。
                    var capReached = false;
                    for (var i = 0; i < 64; i++)
                    {
                        if (!diagnostics.Report(new IOException(), "DuringReader"))
                        {
                            capReached = true;
                            break;
                        }
                        accepted++;
                    }
                    Assert.True(capReached);
                    Assert.InRange(new FileInfo(path).Length, maximumFileBytes + 1, maximumFileBytes * 2);
                    var retained = File.ReadAllLines(path);
                    Assert.Equal(accepted, retained.Length);
                    Assert.Contains("BeforeReader", retained[0]);
                    Assert.False(diagnostics.Report(rejected, "BlockedAtCap"));
                    Assert.Equal(retained, File.ReadAllLines(path));
                }

                Assert.True(diagnostics.Report(rejected, "RecoveredAfterReader"));
                Assert.True(File.Exists(Path.Combine(directory, "Exception.1.log")));
                Assert.InRange(new FileInfo(path).Length, 1, maximumFileBytes);
            }

            var records = Directory.GetFiles(directory, "Exception*.log").SelectMany(File.ReadAllLines).ToArray();
            Assert.Equal(accepted + 1, records.Length);
            Assert.Equal(accepted + 1, sent.Count);
            Assert.DoesNotContain(records, record => record.Contains("BlockedAtCap"));
            Assert.DoesNotContain(sent, record => record.Contains("BlockedAtCap"));
            Assert.Single(records, record => record.Contains("RecoveredAfterReader"));
            Assert.Equal(records.OrderBy(record => record), sent.OrderBy(record => record));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>
    /// Windows 讀取器禁止寫入，直接注入 Append 開啟失敗；既有證據不變且被拒事件不送 LINE。
    /// 釋放 reader 後重試同一例外成功，證明失敗落檔不會污染 weak-key 去重狀態。
    /// </summary>
    [Fact]
    public async Task Reader_blocked_write_preserves_evidence_and_retries_without_false_notification()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "Exception.log");
        var sent = new ConcurrentQueue<string>();
        var exception = new IOException("private-locked-detail");
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                diagnostics.StartNotifications((message, _) => { sent.Enqueue(message); return Task.CompletedTask; });
                Assert.True(diagnostics.Report(new IOException(), "BeforeWriteLock"));
                var original = File.ReadAllText(path);
                using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.False(diagnostics.Report(exception, "RejectedWrite"));
                    Assert.Equal(original, File.ReadAllText(path));
                }
                Assert.True(diagnostics.Report(exception, "RecoveredWrite"));
            }

            var records = File.ReadAllLines(path);
            Assert.Equal(2, records.Length);
            Assert.Equal(2, sent.Count);
            Assert.DoesNotContain(sent, record => record.Contains("RejectedWrite"));
            Assert.Single(sent, record => record.Contains("RecoveredWrite"));
            Assert.Equal(records.OrderBy(record => record), sent.OrderBy(record => record));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>
    /// 以已取消的內部逾時 token 建立 OperationCanceledException，但呼叫者 token 仍有效。
    /// 驗證此功能失敗會落檔並通知一次；只有呼叫者取消可排除，不能以例外內部 token 壓掉逾時。
    /// </summary>
    [Fact]
    public async Task Canceled_internal_timeout_with_active_caller_is_logged_and_notified()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sent = new ConcurrentQueue<string>();
        using var caller = new CancellationTokenSource();
        using var internalTimeout = new CancellationTokenSource();
        internalTimeout.Cancel();
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                diagnostics.StartNotifications((message, _) => { sent.Enqueue(message); return Task.CompletedTask; });
                Assert.False(caller.IsCancellationRequested);
                Assert.True(diagnostics.Report(new OperationCanceledException(internalTimeout.Token),
                    "InternalTimeout", caller.Token));
            }
            var record = Assert.Single(File.ReadAllLines(Path.Combine(directory, "Exception.log")));
            Assert.Equal(record, Assert.Single(sent));
            Assert.Contains("OperationCanceledException", record);
            Assert.Contains("InternalTimeout", record);
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
