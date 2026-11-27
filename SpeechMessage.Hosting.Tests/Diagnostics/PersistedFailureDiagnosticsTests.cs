/*
 * 檔案整體說明
 * 目的與定位：驗證 F3 沙箱 outbox 已持久化失敗經 Hosting 共用診斷入口的離線契約。
 * 使用真實 ExceptionDiagnostics、JSONL 檔案及有界 consumer，只替換最末端 LINE sender；
 * 不連 Dataverse、LINE 或正式主機，不把合成結果宣稱為跨程序送達或原始堆疊證據。
 * 主要功能與流程：以固定 incident、原始 UTC、三種產品及重建例外，檢查 sender 開始時
 * 已能讀取完整落檔、兩端相同安全投影與四種輸出；注入敏感 getter、無效輸入、caller
 * 取消、內部逾時、磁碟拒絕、滿載與 sender 故障，驗證零誤報、既有 acceptance 與清理。
 * 使用邊界與維護：每案 Fixture 唯一擁有暫存目錄及 diagnostics；先 await drain／Dispose，
 * 再刪檔。阻塞 sender 只等待 owner token，finally 回收 active；CTS、讀取串流皆局部釋放。
 * 同例外沿用 weak-key 去重，但同 incident 的新投影與新 owner 可重送；不測試或承諾
 * exactly once，Dataverse CAS owner 才負責跨程序去重。所有秘密均為合成測試字串。
 */
using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Hosting.Tests.Diagnostics;

/// <summary>保護持久事件的窄化入口、共用發布順序及有界資源；以真實輸出作決定性斷言。</summary>
public sealed class PersistedFailureDiagnosticsTests
{
    private static readonly Guid Incident = new("2f543d43-b78e-45ea-9367-fcd05435eb02");
    private static readonly DateTimeOffset Occurrence = new(2026, 10, 2, 3, 4, 5, TimeSpan.Zero);
    private const string Operation = "SlotRequest.ProductMismatch";

    /// <summary>
    /// 三種產品皆以同一 stable incident／原始 UTC 發布；sender 當場讀取完整真檔，
    /// 驗證先落檔再通知。投影保留接收端型別／符號／HResult，明標重建且不偽稱原始堆疊。
    /// </summary>
    [Theory]
    [InlineData("GoodShepherd2")]
    [InlineData("GoodButler")]
    [InlineData("GoodGuild")]
    public async Task Persisted_identity_and_safe_projection_are_flushed_before_sender(string product)
    {
        await using var f = new Fixture();
        var exception = Build();
        Assert.True(Report(f.Diagnostics, exception, product: product));
        await f.Diagnostics.DisposeAsync();

        var record = Assert.Single(File.ReadAllLines(f.LogPath));
        Assert.Equal(record, Assert.Single(f.Sent));
        Assert.Equal(record + "\r\n", Assert.Single(f.ObservedLogs));
        Assert.DoesNotContain("synthetic-secret", record, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(record);
        var value = json.RootElement;
        Assert.Equal("2f543d43b78e45ea9367fcd05435eb02", value.GetProperty("IncidentId").GetString());
        Assert.Equal("2026-10-02T03:04:05+00:00", value.GetProperty("Utc").GetString());
        Assert.Equal(product, value.GetProperty("Product").GetString());
        Assert.Equal(Operation, value.GetProperty("Operation").GetString());
        Assert.Equal("Reconstructed", value.GetProperty("ExceptionProjection").GetString());
        Assert.Equal("System.InvalidOperationException", value.GetProperty("ExceptionType").GetString());
        Assert.Equal(exception.HResult, value.GetProperty("HResult").GetInt32());
        Assert.EndsWith(".Build", value.GetProperty("Location").GetString(), StringComparison.Ordinal);
        Assert.Contains(".Build:", value.GetProperty("Stack").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(".cs", value.GetProperty("Stack").GetString(), StringComparison.Ordinal);
        Assert.Equal(9, value.EnumerateObject().Count());
    }

    /// <summary>敏感 Message／Data／ToString getter 一旦被讀即拋錯；仍須成功輸出安全 metadata。</summary>
    [Fact]
    public async Task Sensitive_exception_members_are_never_read()
    {
        await using var f = new Fixture();
        Assert.True(Report(f.Diagnostics, new NoRead()));
        await f.Diagnostics.DisposeAsync();
        var record = Assert.Single(File.ReadAllLines(f.LogPath));
        Assert.Equal(record, Assert.Single(f.Sent));
        Assert.DoesNotContain("synthetic-secret", record, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(record);
        Assert.EndsWith("+NoRead", json.RootElement.GetProperty("ExceptionType").GetString(), StringComparison.Ordinal);
    }

    /// <summary>四種啟動快照各自控制真實檔案與 sender；bool 表示落檔或入列接受，並非送達。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Four_output_combinations_preserve_acceptance(bool write, bool send)
    {
        await using var f = new Fixture(write, send);
        Assert.Equal(write || send, Report(f.Diagnostics, new InvalidOperationException()));
        await f.Diagnostics.DisposeAsync();
        Assert.Equal(write, File.Exists(f.LogPath));
        Assert.Equal(write, Directory.Exists(f.DirectoryPath));
        Assert.Equal(send ? 1 : 0, f.Sent.Count);
        if (send)
        {
            using var json = JsonDocument.Parse(Assert.Single(f.Sent));
            Assert.Equal(Incident.ToString("N"), json.RootElement.GetProperty("IncidentId").GetString());
        }
    }

    /// <summary>大小寫、空值、空白、未知產品與動態 operation 均拒絕，不得產生任何檔案或通知。</summary>
    [Theory]
    [InlineData(null, Operation)]
    [InlineData("", Operation)]
    [InlineData("GoodShepherd2 ", Operation)]
    [InlineData("goodshepherd2", Operation)]
    [InlineData("ChurchReport", Operation)]
    [InlineData("synthetic-secret", Operation)]
    [InlineData("GoodShepherd2", null)]
    [InlineData("GoodShepherd2", "")]
    [InlineData("GoodShepherd2", "slotrequest.ProductMismatch")]
    [InlineData("GoodShepherd2", "SlotRequest.ProductMismatch\nsynthetic-secret")]
    public async Task Invalid_product_or_operation_has_zero_output(string? product, string? operation)
    {
        await using var f = new Fixture();
        Assert.Throws<ArgumentException>(() => Report(f.Diagnostics, new InvalidOperationException(),
            product: product!, operation: operation!));
        await f.Diagnostics.DisposeAsync();
        Assert.Empty(f.Sent);
        Assert.False(Directory.Exists(f.DirectoryPath));
    }

    /// <summary>空 GUID、非零 UTC offset 及 null 例外在發布前拒絕，連本地日誌也不可建立。</summary>
    [Fact]
    public async Task Invalid_identity_time_or_exception_has_zero_output()
    {
        await using var f = new Fixture();
        Assert.Throws<ArgumentException>(() => Report(f.Diagnostics, new InvalidOperationException(), incident: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Report(f.Diagnostics, new InvalidOperationException(),
            occurrence: Occurrence.ToOffset(TimeSpan.FromHours(8))));
        Assert.Throws<ArgumentNullException>(() => Report(f.Diagnostics, null!));
        await f.Diagnostics.DisposeAsync();
        Assert.Empty(f.Sent);
        Assert.False(Directory.Exists(f.DirectoryPath));
    }

    /// <summary>只有明確已取消的 caller token 排除 OCE；weak marker 防止外層舊 Report 再次誤報。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Caller_cancellation_has_zero_output(bool write, bool send)
    {
        await using var f = new Fixture(write, send);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var exception = new OperationCanceledException(caller.Token);
        Assert.False(Report(f.Diagnostics, exception, cancellationToken: caller.Token));
        Assert.False(f.Diagnostics.Report(exception, "Outer"));
        await f.Diagnostics.DisposeAsync();
        Assert.Empty(f.Sent);
        Assert.False(Directory.Exists(f.DirectoryPath));
    }

    /// <summary>內部已取消 token 不代表 caller 取消；取消期間的非 OCE 故障也不得被靜默略過。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Internal_timeout_and_non_cancellation_failure_remain_actionable(bool internalTimeout)
    {
        await using var f = new Fixture();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Exception exception = internalTimeout ? new OperationCanceledException(canceled.Token) : new IOException("synthetic-secret");
        Assert.True(Report(f.Diagnostics, exception, cancellationToken: internalTimeout ? default : canceled.Token));
        await f.Diagnostics.DisposeAsync();
        using var json = JsonDocument.Parse(Assert.Single(f.Sent));
        Assert.Equal(internalTimeout ? "System.OperationCanceledException" : "System.IO.IOException",
            json.RootElement.GetProperty("ExceptionType").GetString());
        Assert.Single(File.ReadAllLines(f.LogPath));
    }

    /// <summary>
    /// 同一例外跨新舊入口只接受一次；同 incident 的新重建例外仍可重送，
    /// 新 owner 亦可重送，證明未建立持久 incident 快取或誤稱 exactly once。
    /// </summary>
    [Fact]
    public async Task Weak_deduplication_does_not_become_durable_incident_deduplication()
    {
        await using var f = new Fixture();
        var exception = new InvalidOperationException();
        Assert.True(Report(f.Diagnostics, exception));
        Assert.False(Report(f.Diagnostics, exception));
        Assert.False(f.Diagnostics.Report(exception, "Outer"));
        Assert.True(Report(f.Diagnostics, new InvalidOperationException()));
        var previouslyReported = new InvalidOperationException();
        Assert.True(f.Diagnostics.Report(previouslyReported, "Original"));
        Assert.False(Report(f.Diagnostics, previouslyReported));
        await f.Diagnostics.DisposeAsync();
        Assert.Equal(3, f.Sent.Count);

        await using (var restarted = new ExceptionDiagnostics(f.DirectoryPath))
        {
            restarted.StartNotifications((message, _) => { f.Sent.Enqueue(message); return Task.CompletedTask; });
            Assert.True(Report(restarted, new InvalidOperationException()));
        }
        Assert.Equal(4, File.ReadAllLines(f.LogPath).Length);
        Assert.Equal(3, f.Sent.Count(message => message.Contains(Incident.ToString("N"), StringComparison.Ordinal)));
    }

    /// <summary>
    /// A／B 產品併行發布不同 incident 與精確 UTC，逐筆驗證不混用產品或時間；
    /// 真檔與 sender 完整集合一致，drain 後才讀取，無跨案共享例外或可變候選。
    /// </summary>
    [Fact]
    public async Task Concurrent_products_keep_incident_product_and_original_time_isolated()
    {
        await using var f = new Fixture();
        var expected = Enumerable.Range(0, 12).Select(i => new
        {
            Id = Guid.NewGuid(), Product = i % 2 == 0 ? "GoodShepherd2" : "GoodGuild",
            Utc = Occurrence.AddTicks(i + 1)
        }).ToDictionary(item => item.Id);
        Parallel.ForEach(expected.Values, item => Assert.True(Report(f.Diagnostics,
            new InvalidOperationException("synthetic-secret-" + item.Product),
            incident: item.Id, occurrence: item.Utc, product: item.Product)));
        await f.Diagnostics.DisposeAsync();
        var records = File.ReadAllLines(f.LogPath);
        Assert.Equal(12, records.Length);
        Assert.Equal(records.Order(StringComparer.Ordinal), f.Sent.Order(StringComparer.Ordinal));
        var observed = new HashSet<Guid>();
        foreach (var record in records)
        {
            using var json = JsonDocument.Parse(record);
            var value = json.RootElement;
            var id = Guid.ParseExact(value.GetProperty("IncidentId").GetString()!, "N");
            Assert.True(observed.Add(id));
            var item = expected[id];
            Assert.Equal(item.Product, value.GetProperty("Product").GetString());
            Assert.Equal(item.Utc, value.GetProperty("Utc").GetDateTimeOffset());
            Assert.DoesNotContain("synthetic-secret", record, StringComparison.Ordinal);
        }
    }

    /// <summary>以檔案占用目錄注入落檔失敗，禁止 LINE；修復後同例外／incident 可重試接受。</summary>
    [Fact]
    public async Task Failed_write_prevents_line_and_does_not_poison_retry()
    {
        await using var f = new Fixture();
        File.WriteAllText(f.DirectoryPath, "synthetic filesystem blocker");
        try
        {
            var exception = new IOException("synthetic-secret");
            Assert.False(Report(f.Diagnostics, exception));
            Assert.Empty(f.Sent);
            File.Delete(f.DirectoryPath);
            Assert.True(Report(f.Diagnostics, exception));
            await f.Diagnostics.DisposeAsync();
            Assert.Single(f.Sent);
            Assert.Single(File.ReadAllLines(f.LogPath));
        }
        finally { if (File.Exists(f.DirectoryPath)) File.Delete(f.DirectoryPath); }
    }

    /// <summary>sender 真正失敗時仍回傳 acceptance；只追加相同 incident 的狀態，不重試或遞迴通知。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delivery_failure_keeps_acceptance_and_does_not_recurse(bool write)
    {
        var attempts = 0;
        await using var f = new Fixture(write, sender: (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new IOException("synthetic-secret-provider");
        });
        Assert.True(Report(f.Diagnostics, new InvalidOperationException()));
        await f.Diagnostics.DisposeAsync();
        Assert.Equal(1, attempts);
        Assert.Equal(write, File.Exists(f.LogPath));
        if (write)
        {
            var records = File.ReadAllLines(f.LogPath);
            Assert.Equal(2, records.Length);
            using var status = JsonDocument.Parse(records[1]);
            Assert.Equal("LineDeliveryFailed", status.RootElement.GetProperty("Status").GetString());
            Assert.Equal(Incident.ToString("N"), status.RootElement.GetProperty("Incident").GetString());
            Assert.DoesNotContain("synthetic-secret", string.Join("", records), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 可取消 sender 阻塞後精確填滿 64 筆 queue；第 66 筆的 bool 依落檔開關，
    /// 雙開保留同 incident 的 LineQueueFull，LINE-only 零檔案。Dispose 取消後 active 歸零。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Full_queue_and_disposal_preserve_bounded_lifecycle(bool write)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        await using var f = new Fixture(write, sender: async (_, token) =>
        {
            Interlocked.Increment(ref active);
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { Interlocked.Decrement(ref active); }
        });
        Assert.True(Report(f.Diagnostics, new InvalidOperationException()));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var i = 0; i < 64; i++) Assert.True(Report(f.Diagnostics, new InvalidOperationException()));
        Assert.Equal(write, Report(f.Diagnostics, new InvalidOperationException()));
        await f.Diagnostics.DisposeAsync();
        Assert.Equal(0, Volatile.Read(ref active));
        Assert.False(Report(f.Diagnostics, new InvalidOperationException()));
        if (write)
        {
            var records = File.ReadAllLines(f.LogPath);
            Assert.Equal(66, records.Count(record => record.Contains("\"IncidentId\"", StringComparison.Ordinal)));
            using var full = JsonDocument.Parse(Assert.Single(records,
                record => record.Contains("LineQueueFull", StringComparison.Ordinal)));
            Assert.Equal(Incident.ToString("N"), full.RootElement.GetProperty("Incident").GetString());
        }
        else Assert.False(Directory.Exists(f.DirectoryPath));
    }

    /// <summary>
    /// 以接收端重新拋出的同型別例外提供本地堆疊；原 outbox 例外未跨程序傳入。
    /// 使用短方法符號，讓完整測試符號仍在既有 80 字投影上限內，不要求產品放寬上限。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InvalidOperationException Build()
    {
        try { throw new InvalidOperationException("synthetic-secret-message"); }
        catch (InvalidOperationException exception)
        {
            exception.Data["synthetic-secret-key"] = "synthetic-secret-data";
            return exception;
        }
    }

    /// <summary>
    /// 以精確 public ABI 綁定真正入口並執行；缺入口時產生測試 assertion RED，
    /// 不靠原始碼字串或編譯失敗冒充行為驗證，delegate 不包裝被測例外。
    /// </summary>
    private static bool Report(ExceptionDiagnostics diagnostics, Exception exception, Guid? incident = null,
        DateTimeOffset? occurrence = null, string product = "GoodShepherd2", string operation = Operation,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(ExceptionDiagnostics).GetMethod("ReportPersistedFailure",
            new[] { typeof(Exception), typeof(Guid), typeof(DateTimeOffset), typeof(string), typeof(string), typeof(CancellationToken) });
        Assert.NotNull(method);
        Assert.True(method.GetParameters()[5].IsOptional);
        var report = method.CreateDelegate<Func<Exception, Guid, DateTimeOffset, string, string, CancellationToken, bool>>(diagnostics);
        return report(exception, incident ?? Incident, occurrence ?? Occurrence, product, operation, cancellationToken);
    }

    /// <summary>每案唯一目錄與診斷 owner；真檔讀取發生於 sender 入口，所有觀察在 drain 後斷言。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        /// <summary>僅由測試建立的隨機路徑；清理前先排空本案 owner，不接觸部署目錄。</summary>
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "f3-persisted-" + Guid.NewGuid().ToString("N"));
        /// <summary>固定日誌檔名，與產品部署／request 值無關。</summary>
        internal string LogPath => Path.Combine(DirectoryPath, "Exception.log");
        /// <summary>真正受測的診斷 owner；替身不另建 queue、檔案 writer 或通知流程。</summary>
        internal ExceptionDiagnostics Diagnostics { get; }
        internal readonly ConcurrentQueue<string> Sent = new();
        internal readonly ConcurrentQueue<string> ObservedLogs = new();

        /// <summary>套用真實啟動快照；預設 sender 讀已發布真檔，故障案只替換最末端傳輸。</summary>
        internal Fixture(bool write = true, bool send = true, Func<string, CancellationToken, Task>? sender = null)
        {
            Diagnostics = new ExceptionDiagnostics(DirectoryPath, outputOptions: new ExceptionOutputOptions(write, send));
            Diagnostics.StartNotifications(sender ?? ((message, _) =>
            {
                if (write)
                {
                    using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    ObservedLogs.Enqueue(reader.ReadToEnd());
                }
                Sent.Enqueue(message);
                return Task.CompletedTask;
            }));
        }

        /// <summary>成功／故障均等待 diagnostics 關閉；之後刪除唯一測試目錄，確保無檔案控制代碼殘留。</summary>
        public async ValueTask DisposeAsync()
        {
            await Diagnostics.DisposeAsync();
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }

    /// <summary>所有敏感文字存取都拋錯的合成例外；只允許沿用既有型別／位置安全投影。</summary>
    private sealed class NoRead : Exception
    {
        /// <summary>getter 故障注入：任何讀取都使受測入口失敗，不能靜默讀取後再過濾。</summary>
        public override string Message => throw new InvalidOperationException("synthetic-secret-message-read");
        /// <summary>getter 故障注入：不允許讀取可能保存 request／credential 的任意字典。</summary>
        public override IDictionary Data => throw new InvalidOperationException("synthetic-secret-data-read");
        /// <summary>格式化故障注入：不可使用完整例外文字作為日誌或 LINE 本文。</summary>
        public override string ToString() => throw new InvalidOperationException("synthetic-secret-tostring-read");
    }
}
