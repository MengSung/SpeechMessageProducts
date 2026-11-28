/*
檔案整體說明
目的與定位：以離線真暫存檔與合成 sink 故障保護 F5 寫前清冊的持久化順序、固定範圍及有界 owner。
主要功能與流程：驗 CreateNew 不覆寫、每筆返回時檔案已可讀、正式 scope 與 canonical 鍵一致，錯代碼／ID／
旗標／發送結果拒絕，並注入寫前或寫後 flush 失敗、容量上限、並行與 Dispose，確認不會繼續授權後續寫入。
信任與隔離：不讀 ProgramData 真清冊、User Secrets 或網路；每案唯一目錄只放合成 ID，不建立聯絡人或 LINE userId。
資源與失敗：Fixture 唯一擁有暫存根，journal 唯一擁有 sink；真檔案 Dispose 後保留到 fixture 精確刪除，
記憶體 sink 最多測試上限內的資料，Dispose 後釋放其 buffer；並行工作全部 await 排空後才結束 owner。
決定性斷言：輸出每行都是完整 JSON，失敗前後原檔位元組保留、sink 只關閉一次、錯 scope 零新增行；
任何寫入或 flush 未確認後都拒絕後續操作，不以合成成功冒稱真磁碟耐久性、D365 或手機驗收。
*/
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core.Gateway;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>只執行離線檔案與故障 sink，不帶 Live3P 分類。</summary>
[Trait("Category", "Offline")]
[Trait("WP", "F5")]
public sealed class F5PushLiveJournalTests
{
    /// <summary>所有 Channel 文字均為合成數字，不載入 Handoff 或任何真憑證。</summary>
    private const string Channel = "1234567890";

    /// <summary>方法返回就能從獨立讀取 handle 看到完整 JSONL；Dispose 後清冊仍保留，不能自己刪證據。</summary>
    [Fact]
    public void File_EachAppendReadableBeforeReturn_AndDisposeRetainsEvidence()
    {
        using var fixture = new JournalFixture();
        var run = Guid.NewGuid();
        var path = Path.Combine(fixture.Root, run.ToString("N") + ".jsonl");
        using (var journal = F5PushLiveJournal.CreateAt(fixture.Root, run))
        {
            journal.RecordAction("c121", F5LiveAction.CreateStarted);
            var tenant = journal.BindTenant("c121", Guid.NewGuid(), Channel);
            Assert.Equal(SendBatchKeys.Create("GoodShepherd2", Channel, tenant.TenantId, tenant.CorrelationId, 0), tenant.BatchKey);
            // Windows 的讀者也必須允許既有 owner 的寫入 access；writer 自己仍以 FileShare.Read 阻擋其他寫者。
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(input, Encoding.UTF8);
            var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Length);
            foreach (var line in lines) { using var json = JsonDocument.Parse(line); Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind); }
            Assert.Throws<IOException>(() => { using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
        }
        Assert.True(File.Exists(path));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }

    /// <summary>同一 runId 的舊證據不可截斷或覆寫，拒絕後 byte 與原檔完全一致。</summary>
    [Fact]
    public void File_ExistingRun_RefusedWithoutOverwrite()
    {
        using var fixture = new JournalFixture();
        var run = Guid.NewGuid();
        using (var journal = F5PushLiveJournal.CreateAt(fixture.Root, run)) journal.RecordAction("c121", F5LiveAction.CreateStarted);
        var path = Path.Combine(fixture.Root, run.ToString("N") + ".jsonl");
        var before = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => F5PushLiveJournal.CreateAt(fixture.Root, run));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>相同綁定冪等、兩租戶正式 ID 不同；不同內容不能覆寫，且拒絕時不新增證據行。</summary>
    [Fact]
    public void Binding_CompleteScopeIsImmutable_AndSeparateTenantsRemainSeparate()
    {
        using var sink = new MemorySink();
        using var journal = F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink);
        var firstId = Guid.NewGuid();
        var first = journal.BindTenant("c121", firstId, Channel);
        var lines = sink.Count;
        Assert.Equal(first, journal.BindTenant("c121", firstId, Channel));
        Assert.Equal(lines, sink.Count);
        Assert.Throws<InvalidDataException>(() => journal.BindTenant("c121", Guid.NewGuid(), Channel));
        Assert.Throws<InvalidDataException>(() => journal.BindTenant("c121", firstId, "9876543210"));
        Assert.Throws<InvalidDataException>(() => journal.BindTenant("c124", firstId, Channel));
        Assert.Equal(lines, sink.Count);
        var second = journal.BindTenant("c124", Guid.NewGuid(), Channel);
        Assert.NotEqual(first.TenantId, second.TenantId);
        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
        Assert.Equal(SendBatchKeys.Create("GoodShepherd2", Channel, second.TenantId, second.CorrelationId, -1), second.BatchKey);
    }

    /// <summary>scope 的代碼、正式 ID 及 Channel 格式完整驗證；錯輸入不會被寫成合法證據。</summary>
    [Theory]
    [InlineData("c201", false, Channel)]
    [InlineData("c121", true, Channel)]
    [InlineData("c121", false, "not-a-channel")]
    [InlineData("c121", false, "")]
    public void Binding_InvalidScope_ZeroAppend(string code, bool empty, string channel)
    {
        using var sink = new MemorySink();
        using var journal = F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink);
        var count = sink.Count;
        Assert.Throws<InvalidDataException>(() => journal.BindTenant(code, empty ? Guid.Empty : Guid.NewGuid(), channel));
        Assert.Equal(count, sink.Count);
    }

    /// <summary>旗標候選只能屬同輪已綁 c124，不接受其他租戶、marker、代碼、未改值或非數字版本。</summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("tenant")]
    [InlineData("marker")]
    [InlineData("code")]
    [InlineData("same")]
    [InlineData("version")]
    public void Flag_WriteAheadRequiresExactBoundScope(string fault)
    {
        var run = Guid.NewGuid();
        using var sink = new MemorySink();
        using var journal = F5PushLiveJournal.CreateForTesting(run, sink);
        var tenant = journal.BindTenant("c124", Guid.NewGuid(), Channel);
        var candidate = new F5LiveFlagWrite(fault == "tenant" ? Guid.NewGuid() : tenant.TenantId,
            fault == "code" ? "c121" : "c124", fault == "marker" ? Guid.NewGuid().ToString("N") : run.ToString("N"),
            true, fault == "same", fault == "version" ? "secret-like-input" : "41");
        var count = sink.Count;
        if (fault == "valid") { journal.SaveFlag(candidate); Assert.Equal(count + 1, sink.Count); }
        else { Assert.Throws<InvalidDataException>(() => journal.SaveFlag(candidate)); Assert.Equal(count, sink.Count); }
        Assert.DoesNotContain("secret-like-input", sink.Text, StringComparison.Ordinal);
    }

    /// <summary>正式結果的 ID、Outcome 與單人數都必須精確；既有正式 ID 不能跨租戶或被改成另一筆。</summary>
    [Fact]
    public void SendConfirmation_RequiresExactFinalFacts_AndDoesNotMergeIdentities()
    {
        using var sink = new MemorySink();
        using var journal = F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink);
        journal.BindTenant("c121", Guid.NewGuid(), Channel);
        journal.BindTenant("c124", Guid.NewGuid(), Channel);
        var id = Guid.NewGuid();
        var accepted = new SendBatchSummary(id, 1, SendRecordOutcome.Accepted);
        journal.ConfirmSend("c121", accepted);
        var count = sink.Count;
        journal.ConfirmSend("c121", accepted);
        Assert.Equal(count, sink.Count);
        Assert.Throws<InvalidDataException>(() => journal.ConfirmSend("c124", new SendBatchSummary(id, 1, SendRecordOutcome.Rejected)));
        Assert.Throws<InvalidDataException>(() => journal.ConfirmSend("c121", accepted with { RecordId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => journal.ConfirmSend("c124", new SendBatchSummary(Guid.Empty, 1, SendRecordOutcome.Rejected)));
        Assert.Throws<InvalidDataException>(() => journal.ConfirmSend("c124", new SendBatchSummary(Guid.NewGuid(), 2, SendRecordOutcome.Rejected)));
        Assert.Throws<InvalidDataException>(() => journal.ConfirmSend("c124", new SendBatchSummary(Guid.NewGuid(), 1, SendRecordOutcome.Accepted)));
        Assert.Equal(count, sink.Count);
        journal.ConfirmSend("c124", new SendBatchSummary(Guid.NewGuid(), 1, SendRecordOutcome.Rejected));
        Assert.Equal(count + 1, sink.Count);
    }

    /// <summary>沒有綁定不能記發送、旗標或清理結果；未知 enum 也不能進入正式 JSONL。</summary>
    [Fact]
    public void UnboundAndUnknownActions_RefusedWithoutAppend()
    {
        using var sink = new MemorySink();
        using var journal = F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink);
        var count = sink.Count;
        Assert.Throws<InvalidDataException>(() => journal.RecordAction("c121", F5LiveAction.SendStarted));
        Assert.Throws<InvalidDataException>(() => journal.RecordAction("c201", F5LiveAction.CreateStarted));
        Assert.Throws<InvalidDataException>(() => journal.RecordAction("c121", (F5LiveAction)999));
        Assert.Throws<InvalidDataException>(() => journal.ConfirmSend("c121", new SendBatchSummary(Guid.NewGuid(), 1, SendRecordOutcome.Accepted)));
        Assert.Throws<InvalidDataException>(() => journal.SaveFlag(new F5LiveFlagWrite(Guid.NewGuid(), "c124",
            Guid.NewGuid().ToString("N"), true, false, "41")));
        Assert.Equal(count, sink.Count);
    }

    /// <summary>在寫入前故障或寫後 flush 故障都不發布綁定；原例外保留，後續操作一律拒絕。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendFailure_OriginalExceptionAndNoFurtherUse(bool afterWrite)
    {
        using var sink = new MemorySink();
        using var journal = F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink);
        var failure = new IOException("Synthetic disk failure.");
        sink.NextFailure = failure;
        sink.FailAfterWrite = afterWrite;
        Assert.Same(failure, Assert.Throws<IOException>(() => journal.BindTenant("c121", Guid.NewGuid(), Channel)));
        var count = sink.Count;
        Assert.Throws<InvalidOperationException>(() => journal.RecordAction("c121", F5LiveAction.CreateStarted));
        Assert.Equal(count, sink.Count);
    }

    /// <summary>header flush 失敗時工廠也必須關閉已接管 sink，不能把 owner 遺留在未回傳的物件。</summary>
    [Fact]
    public void HeaderFailure_DisposesSinkExactlyOnce()
    {
        var sink = new MemorySink { NextFailure = new IOException("Synthetic header failure.") };
        Assert.Throws<IOException>(() => F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink));
        Assert.Equal(1, sink.DisposeCalls);
    }

    /// <summary>初始化與關閉同時故障時，兩個原例外都保留，清理故障不能蓋掉寫前證據的真正失敗。</summary>
    [Fact]
    public void HeaderAndDisposeFailure_PreservesBothExceptions()
    {
        var first = new IOException("Synthetic append failure.");
        var second = new IOException("Synthetic close failure.");
        var sink = new MemorySink { NextFailure = first, DisposeFailure = second };
        var failure = Assert.Throws<AggregateException>(() => F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink));
        Assert.Same(first, failure.InnerExceptions[0]);
        Assert.Same(second, failure.InnerExceptions[1]);
        Assert.Equal(1, sink.DisposeCalls);
    }

    /// <summary>不允許空 run、相對路徑或 repo 內位置；拒絕在真正開檔前發生，不留下清冊。</summary>
    [Fact]
    public void File_InvalidRootOrRun_RefusedBeforeOpening()
    {
        using var fixture = new JournalFixture();
        Assert.Throws<InvalidDataException>(() => F5PushLiveJournal.CreateAt(fixture.Root, Guid.Empty));
        Assert.Throws<InvalidDataException>(() => F5PushLiveJournal.CreateAt("relative-f5-root", Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => F5PushLiveJournal.CreateAt(DeploymentCli.FindRepositoryRoot(), Guid.NewGuid()));
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    /// <summary>筆數與位元組各自先限額；達上限後永久拒用，不能無界落檔或忽略失敗繼續 SDK 操作。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Capacity_IsBoundedAndFaultsClosed(bool records)
    {
        using var sink = new MemorySink();
        using var journal = F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink,
            maximumRecords: records ? 2 : 256, maximumBytes: records ? 262144 : 512);
        Assert.Throws<InvalidDataException>(() =>
        {
            for (var index = 0; index < 300; index++) journal.RecordAction("c121", F5LiveAction.CreateStarted);
        });
        Assert.True(records ? sink.Count == 2 : sink.Bytes <= 512);
        Assert.Throws<InvalidOperationException>(() => journal.RecordAction("c121", F5LiveAction.CreateStarted));
    }

    /// <summary>同輪並行保存只產生完整獨立 JSONL；所有工作 join 後重複 Dispose 安全且不再接受操作。</summary>
    [Fact]
    public async Task ConcurrentAppends_AreWholeAndBounded_AndDisposeIsIdempotent()
    {
        var sink = new MemorySink();
        var journal = F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => journal.RecordAction("c121", F5LiveAction.CreateStarted))));
            Assert.Equal(33, sink.Count);
            foreach (var line in sink.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            { using var document = JsonDocument.Parse(line); Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind); }
        }
        finally { journal.Dispose(); }
        journal.Dispose();
        Assert.Equal(1, sink.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() => journal.RecordAction("c121", F5LiveAction.CreateStarted));
    }

    /// <summary>
    /// 專用關閉執行緒在真正的 Monitor 競爭點等待，才允許釋放 append 屏障；不以 Task 尚未排程當作排空證據。
    /// sink 另驗 Dispose 時沒有進行中的 append；移除 journal 關閉鎖的 mutation 必須失敗，所有工作在 finally 排空。
    /// </summary>
    [Fact]
    public async Task Dispose_WaitsForInFlightAppend_ThenReleasesOwner()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var closing = new ManualResetEventSlim();
        using var closed = new ManualResetEventSlim();
        var sink = new MemorySink();
        using var journal = F5PushLiveJournal.CreateForTesting(Guid.NewGuid(), sink);
        sink.BeforeAppend = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
        var write = Task.Run(() => journal.RecordAction("c121", F5LiveAction.CreateStarted));
        Exception? closeFailure = null;
        var dispose = new Thread(() =>
        {
            closing.Set();
            try { journal.Dispose(); }
            catch (Exception failure) { closeFailure = failure; }
            finally { closed.Set(); }
        }) { IsBackground = true };
        var started = false;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            dispose.Start();
            started = true;
            Assert.True(closing.Wait(TimeSpan.FromSeconds(5)));
            // 專用執行緒在 closing 之後只有 Dispose，唯一可等待處是 journal 的 Monitor。
            // 移除鎖會完成或擲出 sink 的重疊斷言；兩者都不能冒稱正在等待排空。
            Assert.True(SpinWait.SpinUntil(() => closed.IsSet ||
                (dispose.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            Assert.False(closed.IsSet);
            Assert.Equal(0, sink.DisposeCalls);
        }
        finally
        {
            release.Set();
            try { await write; }
            finally { if (started) Assert.True(dispose.Join(TimeSpan.FromSeconds(5))); }
        }
        Assert.Null(closeFailure);
        Assert.Equal(1, sink.DisposeCalls);
        Assert.Equal(2, sink.Count);
    }

    /// <summary>每案合成 sink 只保留有界測試輸出；不擁有檔案或網路，Dispose 後釋放 buffer。</summary>
    private sealed class MemorySink : IF5LiveJournalSink
    {
        private MemoryStream? buffer = new();
        private bool disposed;
        private int inAppend;
        /// <summary>已確認 Append 次數，用於反證失敗後仍繼續寫入。</summary>
        internal int Count { get; private set; }
        /// <summary>目前位元組數，測試容量不依賴 journal 內部計數器。</summary>
        internal long Bytes => buffer?.Length ?? 0;
        /// <summary>複製合成輸出供 JSON 斷言，不包含外部真實資料。</summary>
        internal string Text => Encoding.UTF8.GetString((buffer ?? throw new ObjectDisposedException(nameof(MemorySink))).ToArray());
        /// <summary>下一次 Append 注入的原例外，單次消費後不再保留。</summary>
        internal IOException? NextFailure { get; set; }
        /// <summary>模擬已寫部分證據但 flush 未確認，不能誤報成功。</summary>
        internal bool FailAfterWrite { get; set; }
        /// <summary>實際首次關閉次數，重複 owner Dispose 不得再關同一資源。</summary>
        internal int DisposeCalls { get; private set; }
        /// <summary>由單案同步屏障觀察正在 flush 的生命週期，不把工作交給不可追蹤的背景執行。</summary>
        internal Action? BeforeAppend { get; set; }
        /// <summary>關閉 buffer 後才丟出的合成清理故障，原 append 故障仍須保留。</summary>
        internal IOException? DisposeFailure { get; init; }

        /// <summary>由被測 journal 串行呼叫；容量由測試本身有界，故障前後不建立額外工作。</summary>
        public void Append(ReadOnlySpan<byte> line)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Assert.Equal(1, Interlocked.Increment(ref inAppend));
            try
            {
                BeforeAppend?.Invoke();
                var failure = NextFailure;
                NextFailure = null;
                if (failure is not null && !FailAfterWrite) throw failure;
                buffer!.Write(line);
                Count++;
                if (failure is not null) throw failure;
            }
            finally { Interlocked.Decrement(ref inAppend); }
        }

        /// <summary>一次性關閉記憶體 buffer，測試只在 Dispose 前讀取內容。</summary>
        public void Dispose()
        {
            if (disposed) return;
            Assert.Equal(0, Volatile.Read(ref inAppend));
            disposed = true;
            DisposeCalls++;
            var owner = buffer;
            buffer = null;
            BeforeAppend = null;
            owner?.Dispose();
            if (DisposeFailure is not null) throw DisposeFailure;
        }
    }

    /// <summary>唯一擁有本案暫存根；journal 只關檔不刪證據，fixture 最後才刪自己建立的根。</summary>
    private sealed class JournalFixture : IDisposable
    {
        /// <summary>建立不可與其他案例重複的目錄，不接受環境或使用者給定路徑。</summary>
        internal JournalFixture() => Directory.CreateDirectory(Root);
        /// <summary>固定暫存根下的本案唯一絕對路徑。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "f5-journal-" + Guid.NewGuid().ToString("N"));
        /// <summary>所有工作與 handle 已排空後只精確刪除此根，殘留 handle 會使測試失敗。</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
