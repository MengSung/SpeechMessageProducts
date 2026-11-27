/*
 * 檔案整體說明
 *
 * 目的與定位：保護 DeploymentEngine 匯出備份在 Windows 真實檔案占用下的完整發布、
 * 有界恢復及原始錯誤契約。沿用每案例獨占的合成 Fixture，不連 D365、LINE 或 VM，
 * 不讀憑證，也不把本機檔案系統測試當成組織或外部掃描程式的實機證據。
 *
 * 主要功能與流程：先輸出沒有組件的完整舊快照，再在記憶體加入合成組件，
 * 以不分享 Delete 的真實 FileStream 阻擋覆寫；只觀察本次發布執行緒真正 MoveFile
 * 拋出的例外作因果屏障，驗證等待期間舊檔不變、暫存已完整關閉、解鎖後原子恢復。
 * 持續占用及合成唯讀目的檔分別保護固定等待上限、第一次例外身分與 finally 暫存清理。
 *
 * 使用邊界與維護：測試擁有讀取 handle、唯讀屬性及 FirstChanceException 訂閱；
 * 訂閱按唯一發布執行緒與框架 MoveFile 堆疊過濾，不偽造 HRESULT、不觀察其他程序。
 * 首次例外由最長五秒的測試屏障暫停，讓舊檔／候選斷言不消耗產品的一秒重試預算。
 * 每案 finally 先解鎖或還原測試屬性、放行屏障，再 await 排空唯一發布工作，最後解除訂閱；
 * Fixture 最後刪除自己的根目錄。僅保留首個及最近一個例外與計數，不累積重試紀錄，
 * 沒有跨案例共用資料、背景 timer 或未觀察工作；獨占重開檔案證明產品 handle 已釋放。
 * 決定性斷言涵蓋完整新舊 JSON、同一原例外、有限失敗次數、零暫存及零觀察資源。
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>
/// 透過真實 WriteExport 與本機檔案鎖驗證備份發布；只有 SDK 資料來源使用既有合成服務。
/// 各案獨占目錄及觀察 owner，不能以重跑原測試代替占用故障注入。
/// </summary>
public sealed class DeploymentExportPublicationTests
{
    private readonly ITestOutputHelper output;

    /// <summary>借用案例輸出器，只記合成故障的型別、錯誤碼、次數與等待，不記產品資料。</summary>
    /// <param name="output">由測試框架擁有的本案例輸出器。</param>
    public DeploymentExportPublicationTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// 首次真實覆寫已受阻才解鎖，原發布必須成功；等待中舊 bytes 不變，完整候選已 flush
    /// 並關閉。移除重試、先刪舊檔或原地覆寫，都會破壞這些斷言。
    /// </summary>
    [Fact]
    public async Task WriteExport_ReleasedWindowsReaderLock_PublishesCompleteSnapshotAndCleansTemporary()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires real Windows file sharing semantics.");
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "export.json");
        fixture.Engine.WriteExport(Fixture.Organization, path);
        var original = File.ReadAllBytes(path);
        fixture.SeedAssembly();
        using var probe = new WindowsMoveProbe(path, holdReader: true);
        var publication = probe.PublishAsync(fixture.Engine);
        try
        {
            var blocked = await probe.FirstFailure.WaitAsync(TimeSpan.FromSeconds(5));
            AssertWindowsMoveError(blocked);
            Assert.False(publication.IsCompleted);
            Assert.Equal(original, File.ReadAllBytes(path));
            var temporary = Assert.Single(Directory.GetFiles(fixture.Root, ".*.tmp"));
            AssertCompleteAssemblySnapshot(temporary);
            probe.ReleaseReader();
            probe.ResumeMove();
            await publication.WaitAsync(TimeSpan.FromSeconds(5));
            AssertCompleteAssemblySnapshot(path);
            Assert.Empty(Directory.GetFiles(fixture.Root, ".*.tmp"));
            Assert.Equal(0, fixture.SecretReads);
            Assert.Empty(fixture.Service.Writes);
            output.WriteLine($"Released reader: {blocked.GetType().Name}, HResult=0x{blocked.HResult:X8}, moves={probe.MoveFailures}.");
        }
        finally
        {
            probe.ReleaseReader();
            probe.ResumeMove();
            await Record.ExceptionAsync(() => publication);
        }

        probe.Dispose();
        Assert.Equal(0, probe.ActiveResources);
    }

    /// <summary>
    /// 持續拒絕 Delete share 時，發布須於固定期限失敗並保留第一次真例外實例，
    /// 不可包裝成逾時、無限重試或留下暫存；解鎖後新一次匯出仍能完成且無殘留 handle。
    /// </summary>
    [Fact]
    public async Task WriteExport_PersistentWindowsReaderLock_ReturnsOriginalFailureWithinBoundAndRecovers()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires real Windows file sharing semantics.");
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "export.json");
        fixture.Engine.WriteExport(Fixture.Organization, path);
        var original = File.ReadAllBytes(path);
        fixture.SeedAssembly();
        using var probe = new WindowsMoveProbe(path, holdReader: true);
        var publication = probe.PublishAsync(fixture.Engine);
        try
        {
            var blocked = await probe.FirstFailure.WaitAsync(TimeSpan.FromSeconds(5));
            AssertWindowsMoveError(blocked);
            probe.ResumeMove();
            var failure = await Record.ExceptionAsync(() => publication.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(blocked, failure);
            Assert.InRange(probe.BlockedElapsed.TotalMilliseconds, 800, 5000);
            Assert.InRange(probe.MoveFailures, 2, 50);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(fixture.Root, ".*.tmp"));
            Assert.Empty(fixture.Service.Writes);
            output.WriteLine($"Persistent reader: HResult=0x{blocked.HResult:X8}, moves={probe.MoveFailures}, elapsed={probe.BlockedElapsed.TotalMilliseconds:F0}ms; original exception preserved.");
        }
        finally
        {
            probe.ReleaseReader();
            probe.ResumeMove();
            await Record.ExceptionAsync(() => publication);
        }

        probe.Dispose();
        Assert.Equal(0, probe.ActiveResources);
        fixture.Engine.WriteExport(Fixture.Organization, path);
        AssertCompleteAssemblySnapshot(path);
        Assert.Empty(Directory.GetFiles(fixture.Root, ".*.tmp"));
    }

    /// <summary>
    /// 唯讀目的檔是真實永久拒絕，不得被清除屬性、吞錯或無限等待；必須有限返回同一
    /// UnauthorizedAccessException，保留舊快照及屬性，並清除本次候選。finally 只還原測試檔屬性。
    /// </summary>
    [Fact]
    public async Task WriteExport_ReadOnlyDestination_PreservesOriginalPermanentFailureAndCleansTemporary()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires real Windows file sharing semantics.");
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "export.json");
        fixture.Engine.WriteExport(Fixture.Organization, path);
        var original = File.ReadAllBytes(path);
        var attributes = File.GetAttributes(path);
        fixture.SeedAssembly();
        using var probe = new WindowsMoveProbe(path, holdReader: false);
        File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        Task? publication = null;
        try
        {
            publication = probe.PublishAsync(fixture.Engine);
            var blocked = await probe.FirstFailure.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<UnauthorizedAccessException>(blocked);
            Assert.Equal(unchecked((int)0x80070005), blocked.HResult);
            probe.ResumeMove();
            var failure = await Record.ExceptionAsync(() => publication.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(blocked, failure);
            Assert.InRange(probe.BlockedElapsed.TotalMilliseconds, 0, 5000);
            Assert.InRange(probe.MoveFailures, 1, 50);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
            Assert.Empty(Directory.GetFiles(fixture.Root, ".*.tmp"));
            Assert.Empty(fixture.Service.Writes);
            output.WriteLine($"Read-only destination: HResult=0x{blocked.HResult:X8}, moves={probe.MoveFailures}, elapsed={probe.BlockedElapsed.TotalMilliseconds:F0}ms; original exception and attributes preserved.");
        }
        finally
        {
            File.SetAttributes(path, attributes);
            probe.ResumeMove();
            if (publication is not null) await Record.ExceptionAsync(() => publication);
        }

        probe.Dispose();
        Assert.Equal(0, probe.ActiveResources);
        fixture.Engine.WriteExport(Fixture.Organization, path);
        AssertCompleteAssemblySnapshot(path);
        Assert.Empty(Directory.GetFiles(fixture.Root, ".*.tmp"));
    }

    /// <summary>
    /// 獨占開啟完整候選或正式檔，證明 writer 已關閉，並以手定組件內容驗證 JSON 不是舊代或半份。
    /// stream 及 JsonDocument 都由此同步斷言擁有，返回前確定性釋放。
    /// </summary>
    /// <param name="path">本案例已建立的候選或正式檔路徑。</param>
    private static void AssertCompleteAssemblySnapshot(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        using var json = JsonDocument.Parse(stream);
        Assert.Equal("goodshepherdback", json.RootElement.GetProperty("Organization").GetString());
        Assert.Equal("SpeechMessage.Dynamics.Plugins", json.RootElement.GetProperty("ManagedAssemblyName").GetString());
        Assert.Equal("AQID", json.RootElement.GetProperty("Assembly").GetProperty("Content").GetString());
        Assert.Equal("1.0.0.0", json.RootElement.GetProperty("Assembly").GetProperty("Version").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("Steps").GetArrayLength());
        Assert.Equal(64, json.RootElement.GetProperty("Sha256").GetString()!.Length);
    }

    /// <summary>只接受 Windows 真實覆寫回報的完整 HRESULT，不合成或改寫錯誤碼。</summary>
    /// <param name="error">本次 MoveFile 首次拋出的原始例外。</param>
    private static void AssertWindowsMoveError(Exception error)
    {
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Contains(error.HResult, new[]
        {
            unchecked((int)0x80070005), unchecked((int)0x80070020), unchecked((int)0x80070021)
        });
    }

    /// <summary>
    /// 本案例的真實鎖與只讀例外觀察 owner。只觀察單一 Task.Run 內的發布執行緒；
    /// 首次失敗完成 TCS 後最多等五秒，讓測試在產品重試計時前驗證完整候選，再明確放行。
    /// 例外去重只保留最近一個物件，沒有無界集合。測試必須先解鎖、放行並 await 工作，
    /// 再 Dispose 解除全域事件、關閉唯一讀取 handle 及釋放屏障，不遺留等待執行緒。
    /// </summary>
    private sealed class WindowsMoveProbe : IDisposable
    {
        private readonly string path;
        private readonly TaskCompletionSource<Exception> firstFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim resumeMove = new(initialState: false);
        private FileStream? reader;
        private Exception? lastObserved;
        private int publishingThread;
        private int moveFailures;
        private int observing;
        private int gateDisposed;
        private long blockedAt;

        /// <summary>僅對本案例目的檔選擇性持有讀取鎖；訂閱由此 owner 的 Dispose 唯一解除。</summary>
        /// <param name="path">Fixture 獨占的合成 export 路徑。</param>
        /// <param name="holdReader">true 以 Share.Read 拒絕刪除共享；false 只觀察唯讀故障。</param>
        internal WindowsMoveProbe(string path, bool holdReader)
        {
            this.path = path;
            if (holdReader) reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            AppDomain.CurrentDomain.FirstChanceException += ObserveMoveFailure;
            observing = 1;
        }

        /// <summary>真正框架搬移第一次失敗才完成，不依賴任意延遲或檔案建立通知猜測時序。</summary>
        internal Task<Exception> FirstFailure => firstFailure.Task;
        /// <summary>本次工作實際搬移失敗次數；同一原例外再次 throw 不重複計數。</summary>
        internal int MoveFailures => Volatile.Read(ref moveFailures);
        /// <summary>自第一次受阻起計算的單調時間，與 Fixture 的合成業務時鐘無關。</summary>
        internal TimeSpan BlockedElapsed => Stopwatch.GetElapsedTime(Volatile.Read(ref blockedAt));
        /// <summary>本 owner 的讀取 handle、事件訂閱及屏障 owner 數；Dispose 後必須為零。</summary>
        internal int ActiveResources => (reader is null ? 0 : 1) + Volatile.Read(ref observing) +
            (Volatile.Read(ref gateDisposed) == 0 ? 1 : 0);

        /// <summary>啟動唯一同步發布工作並限定觀察執行緒；呼叫端 finally 必須 await 排空。</summary>
        /// <param name="engine">只借用至本次工作返回的真實待測核心。</param>
        /// <returns>必須由測試觀察成功或失敗的唯一工作。</returns>
        internal Task PublishAsync(DeploymentEngine engine) => Task.Run(() =>
        {
            Volatile.Write(ref publishingThread, Environment.CurrentManagedThreadId);
            try { engine.WriteExport(Fixture.Organization, path); }
            finally { Volatile.Write(ref publishingThread, 0); }
        });

        /// <summary>僅讀取本次發布的框架 MoveFile 故障，不記其他案例或產品錯誤、不改變例外。</summary>
        private void ObserveMoveFailure(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId != Volatile.Read(ref publishingThread) ||
                args.Exception is not (IOException or UnauthorizedAccessException) ||
                ReferenceEquals(lastObserved, args.Exception) ||
                args.Exception.StackTrace?.Contains("System.IO.FileSystem.MoveFile", StringComparison.Ordinal) != true)
                return;
            lastObserved = args.Exception;
            Interlocked.Increment(ref moveFailures);
            Interlocked.CompareExchange(ref blockedAt, Stopwatch.GetTimestamp(), 0);
            if (firstFailure.TrySetResult(args.Exception))
            {
                // 只暫停第一次真例外；五秒保險上限及測試 finally 都可放行，不改寫框架故障。
                resumeMove.Wait(TimeSpan.FromSeconds(5));
            }
        }

        /// <summary>完成首次故障的快照斷言後放行；必須在 await 發布前呼叫，finally 亦可重複放行。</summary>
        internal void ResumeMove() => resumeMove.Set();

        /// <summary>屏障確認已受阻後才解鎖；原子移交 handle 讓正常及 finally 清理可以重複執行。</summary>
        internal void ReleaseReader() => Interlocked.Exchange(ref reader, null)?.Dispose();

        /// <summary>發布排空後解除唯一訂閱並關閉 handle；不刪除尚由 Fixture 擁有的快照。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref observing, 0) != 0)
                AppDomain.CurrentDomain.FirstChanceException -= ObserveMoveFailure;
            ReleaseReader();
            if (Interlocked.Exchange(ref gateDisposed, 1) == 0) resumeMove.Dispose();
        }
    }
}
