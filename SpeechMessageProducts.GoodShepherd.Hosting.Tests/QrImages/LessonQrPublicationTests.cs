/*
 * 檔案整體說明
 * 目的與定位：本檔由 SpeechMessageProducts.GoodShepherd.Hosting.Tests 擁有，
 * 保護 P5-07 真實 QR 檔案發布服務的完整候選、穩定儲存身分與失敗清理。
 * 主要流程：每案建立專屬暫存根目錄，使用真正 FileStream／Directory.Move 與發布器，
 * 只有 D365 授權及交易接縫使用合成 store；不把此替身聲稱為實機交易測試。
 * 測試注入跨教會拒絕、重複欄位、圖片複製失敗、取消、交易結果不明及同事件競爭，
 * 決定性斷言是完整 31／32 URL、檔案位元組正確、無 staging 殘留、正式檔案數不增加，
 * 以及原例外保留。另由真實 DI factory 產生 PNG 並驗證工作 scope 回到零。
 * Windows rename 案例由測試 owner 在最後一張 CopyTo 時持有前一張 PNG 的唯讀 handle，
 * 刻意不分享 Delete；只觀察本案 MoveDirectory 的真實例外，以 TCS 證明發布已受阻，
 * 不偽造 HResult、不查詢其他程序。驗證短暫鎖釋放、持鎖到期、原 caller 取消，
 * 以及等待中不公開部分正式圖片、全程持有 lease、原 IOException／取消不被清理蓋掉。
 * 每案 finally 取消並 await 發布、關閉讀取 handle；Dispose 解除例外觀察訂閱，
 * 重送回收固定 staging，獨占重開所有檔案確認產品 handle 已排空。
 * 使用邊界與維護：測試 fixture 是目錄及所有輸入 stream 的唯一 owner，Dispose
 * 在所有 await 返回後關閉 stream 並移除本案例根目錄；沒有共用 Session、SDK、
 * token、靜態可變狀態或背景工作。合成圖片只測儲存，不宣稱可解碼；
 * factory 測試則使用正式 renderer。D365 CAS、SDK timeout 及 HTTPS 映射另需 Host 驗收。
 */
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using SpeechMessageProducts.GoodShepherd.Hosting.QrImages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Hosting.Outbound;
using Xunit;
using Xunit.Abstractions;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Tests.QrImages;

/// <summary>真實檔案發布與 DI 工作 scope 的離線整合測試。</summary>
public sealed class LessonQrPublicationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>只借用本案例輸出器記錄合成鎖的真實錯誤碼與等待證據，不輸出產品資料。</summary>
    public LessonQrPublicationTests(ITestOutputHelper output) => _output = output;

    /// <summary>兩種入口都只提交一次完整欄位，檔案內容及 URL 路徑逐項正確。</summary>
    [Theory]
    [InlineData(false, 31)]
    [InlineData(true, 32)]
    public async Task Publication_WritesCompleteBundleBeforeCommit(bool poll, int count)
    {
        using var fixture = new Fixture();
        var images = fixture.Images(poll);
        fixture.Store.OnCommit = urls =>
        {
            Assert.Equal(count, urls.Count);
            Assert.Equal(count, fixture.Files().Length);
            foreach (var image in images)
            {
                Assert.EndsWith(image.Descriptor.FieldName + ".png", urls[image.Descriptor.FieldName]);
                Assert.Equal(new byte[] { 137, 80, 78, 71 }, File.ReadAllBytes(
                    fixture.Files().Single(path => Path.GetFileName(path) == image.Descriptor.FieldName + ".png")));
            }
        };
        await fixture.Publish(images);
        Assert.Equal(1, fixture.Store.Commits);
        fixture.AssertNoStaging();
    }

    /// <summary>重送或跨程序同事件競爭只核對既有不可變圖片，不增加檔案或改寫內容。</summary>
    [Fact]
    public async Task Publication_ConcurrentRetry_ReusesCanonicalFiles()
    {
        using var fixture = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => fixture.Publish(fixture.Images())));
        Assert.Equal(31, fixture.Files().Length);
        Assert.Equal(12, fixture.Store.Commits);
        fixture.AssertNoStaging();
    }

    /// <summary>
    /// 真實不分享 Delete 的 PNG 讀取鎖必須先阻擋原發布的 Move，釋放後才一次公開完整組。
    /// 移除產品瞬時等待會在解鎖前使 operation 失敗；lease／正式路徑／提交及位元組斷言
    /// 保護原子性，第二次合法重送不增加圖片。finally 排空發布，owner 關閉 handle 及訂閱。
    /// </summary>
    [Theory]
    [InlineData(false, 31)]
    [InlineData(true, 32)]
    public async Task Publication_TransientWindowsRenameLock_ReleasesThenCommitsCompleteBundle(bool poll, int count)
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires real Windows rename sharing semantics.");
        using var fixture = new Fixture();
        var images = fixture.Images(poll).ToArray();
        var parent = Path.Combine(fixture.Root, fixture.Church.ToString("N"), fixture.Lesson.ToString("N"));
        var staging = Path.Combine(parent, $".staging-v1-{count}");
        var final = Path.Combine(parent, $"v1-{count}");
        using var blocker = new WindowsRenameLockStream(staging, images[0].Descriptor.FieldName + ".png");
        images[^1] = new(images[^1].Descriptor, blocker);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        fixture.Store.OnCommit = urls =>
        {
            Assert.Equal(count, urls.Count);
            Assert.True(Directory.Exists(final));
            Assert.False(Directory.Exists(staging));
            Assert.Equal(count, fixture.Files().Length);
            foreach (var image in images)
            {
                Assert.EndsWith(image.Descriptor.FieldName + ".png", urls[image.Descriptor.FieldName]);
                Assert.Equal(new byte[] { 137, 80, 78, 71 },
                    File.ReadAllBytes(Path.Combine(final, image.Descriptor.FieldName + ".png")));
            }
        };
        var operation = fixture.Publish(images, cancellation.Token);
        try
        {
            var original = await blocker.Blocked.WaitAsync(TimeSpan.FromSeconds(5));
            AssertWindowsRenameError(original);
            _output.WriteLine($"Observed real blocked MoveDirectory: HResult=0x{original.HResult:X8}.");
            // TCS 已證明真正 Move 拋錯；短觀察窗只檢查發布仍在等鎖，不以延遲製造故障。
            await Task.Delay(75, cancellation.Token);
            Assert.False(operation.IsCompleted);
            Assert.Equal(0, fixture.Store.Commits);
            Assert.False(Directory.Exists(final));
            Assert.Equal(count, Directory.GetFiles(staging, "*.png").Length);
            fixture.AssertLeaseHeld(count);
            blocker.ReleaseReader();
            await operation;
            _output.WriteLine($"Released lock: HResult=0x{original.HResult:X8}, move failures={blocker.MoveFailures}.");
        }
        finally
        {
            cancellation.Cancel();
            blocker.ReleaseReader();
            await Record.ExceptionAsync(() => operation);
        }
        blocker.Dispose();
        Assert.Equal(0, blocker.ActiveResources);
        Assert.Equal(1, fixture.Store.Commits);
        fixture.AssertNoStaging();
        fixture.AssertPublicationHandlesReleased(count);
        fixture.Store.OnCommit = null;
        await fixture.Publish(fixture.Images(poll));
        Assert.Equal(2, fixture.Store.Commits);
        Assert.Equal(count, fixture.Files().Length);
        fixture.AssertNoStaging();
        fixture.AssertPublicationHandlesReleased(count);
    }

    /// <summary>
    /// 真實讀取鎖持續到固定等待到期時，必須失敗且回傳第一次 Move 的同一 IOException。
    /// 清理也受鎖時不得蓋掉原例外；只保留一組固定 staging、不提交，解鎖後重送完整回收。
    /// 移除期限、改丟 TimeoutException 或回傳最後一次 I/O 例外都會使此測試失敗。
    /// </summary>
    [Fact]
    public async Task Publication_PersistentWindowsRenameLock_PreservesOriginalIoAndRecoversOnRetry()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires real Windows rename sharing semantics.");
        using var fixture = new Fixture();
        var images = fixture.Images().ToArray();
        var parent = Path.Combine(fixture.Root, fixture.Church.ToString("N"), fixture.Lesson.ToString("N"));
        var staging = Path.Combine(parent, ".staging-v1-31");
        using var blocker = new WindowsRenameLockStream(staging, images[0].Descriptor.FieldName + ".png");
        images[^1] = new(images[^1].Descriptor, blocker);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var operation = fixture.Publish(images, cancellation.Token);
        try
        {
            var original = await blocker.Blocked.WaitAsync(TimeSpan.FromSeconds(5));
            AssertWindowsRenameError(original);
            _output.WriteLine($"Observed real blocked MoveDirectory: HResult=0x{original.HResult:X8}.");
            var error = await Assert.ThrowsAsync<IOException>(() => operation);
            Assert.Same(original, error);
            Assert.InRange(blocker.BlockedElapsed.TotalMilliseconds, 800, 3000);
            Assert.True(blocker.MoveFailures > 1);
            Assert.Equal(0, fixture.Store.Commits);
            Assert.False(Directory.Exists(Path.Combine(parent, "v1-31")));
            Assert.Single(Directory.GetDirectories(parent, ".staging-*"));
            Assert.InRange(fixture.Files().Length, 1, 31);
            fixture.AssertLeaseAvailable(31);
            _output.WriteLine($"Expired lock: HResult=0x{error.HResult:X8}, move failures={blocker.MoveFailures}, " +
                $"elapsed={blocker.BlockedElapsed.TotalMilliseconds:F0}ms; same original IOException, commits=0.");
        }
        finally
        {
            cancellation.Cancel();
            blocker.ReleaseReader();
            await Record.ExceptionAsync(() => operation);
        }
        blocker.Dispose();
        Assert.Equal(0, blocker.ActiveResources);
        fixture.AssertPublicationHandlesReleased(31);
        await fixture.Publish(fixture.Images());
        Assert.Equal(1, fixture.Store.Commits);
        Assert.Equal(31, fixture.Files().Length);
        fixture.AssertNoStaging();
        fixture.AssertPublicationHandlesReleased(31);
    }

    /// <summary>
    /// 已證明 Move 受真實鎖阻擋後取消原 caller，須保留其 token、不提交並釋放產品 lease。
    /// 鎖仍在時清理失敗不得取代取消；測試 owner 解鎖及解除訂閱後，重送回收唯一 staging。
    /// 若等待未傳遞 caller token、取消後提交或有 handle 留存，此案必定失敗。
    /// </summary>
    [Fact]
    public async Task Publication_CancelledWindowsRenameLock_PreservesCallerAndDrainsBeforeRetry()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires real Windows rename sharing semantics.");
        using var fixture = new Fixture();
        var images = fixture.Images().ToArray();
        var parent = Path.Combine(fixture.Root, fixture.Church.ToString("N"), fixture.Lesson.ToString("N"));
        var staging = Path.Combine(parent, ".staging-v1-31");
        using var blocker = new WindowsRenameLockStream(staging, images[0].Descriptor.FieldName + ".png");
        images[^1] = new(images[^1].Descriptor, blocker);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var operation = fixture.Publish(images, cancellation.Token);
        try
        {
            var original = await blocker.Blocked.WaitAsync(TimeSpan.FromSeconds(5));
            AssertWindowsRenameError(original);
            _output.WriteLine($"Observed real blocked MoveDirectory: HResult=0x{original.HResult:X8}.");
            Assert.False(operation.IsCompleted);
            fixture.AssertLeaseHeld(31);
            var cancelledAt = Stopwatch.GetTimestamp();
            cancellation.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.InRange(Stopwatch.GetElapsedTime(cancelledAt).TotalMilliseconds, 0, 1000);
            Assert.Equal(0, fixture.Store.Commits);
            Assert.False(Directory.Exists(Path.Combine(parent, "v1-31")));
            Assert.Single(Directory.GetDirectories(parent, ".staging-*"));
            Assert.InRange(fixture.Files().Length, 1, 31);
            fixture.AssertLeaseAvailable(31);
            _output.WriteLine($"Cancelled lock: HResult=0x{original.HResult:X8}; original caller token, commits=0.");
        }
        finally
        {
            cancellation.Cancel();
            blocker.ReleaseReader();
            await Record.ExceptionAsync(() => operation);
        }
        blocker.Dispose();
        Assert.Equal(0, blocker.ActiveResources);
        fixture.AssertPublicationHandlesReleased(31);
        await fixture.Publish(fixture.Images());
        Assert.Equal(1, fixture.Store.Commits);
        Assert.Equal(31, fixture.Files().Length);
        fixture.AssertNoStaging();
        fixture.AssertPublicationHandlesReleased(31);
    }

    /// <summary>只接受 Windows 回報的精確 access denied／sharing／lock violation HRESULT，不合成錯誤碼。</summary>
    private static void AssertWindowsRenameError(IOException error) => Assert.Contains(error.HResult,
        new[] { unchecked((int)0x80070005), unchecked((int)0x80070020), unchecked((int)0x80070021) });

    /// <summary>交易結果不明保留可能已被引用的圖片；重送仍提交完整組且不成長。</summary>
    [Fact]
    public async Task Publication_UnknownCommitOutcome_PreservesImagesForSafeRetry()
    {
        using var fixture = new Fixture();
        var original = new TimeoutException("Synthetic uncertain transaction.");
        fixture.Store.OnCommit = _ => throw original;
        Assert.Same(original, await Assert.ThrowsAsync<TimeoutException>(() => fixture.Publish(fixture.Images())));
        Assert.Equal(31, fixture.Files().Length);
        fixture.AssertNoStaging();
        fixture.Store.OnCommit = null;
        await fixture.Publish(fixture.Images());
        Assert.Equal(31, fixture.Files().Length);
    }

    /// <summary>清理被外部 handle 阻擋時保留原交易逾時，解除鎖後重送回收固定 staging。</summary>
    [Fact]
    public async Task Publication_CleanupFailure_DoesNotReplaceOriginalTimeout()
    {
        using var fixture = new Fixture();
        FileStream? held = null;
        var original = new TimeoutException("Synthetic uncertain transaction.");
        fixture.Store.OnCommit = _ =>
        {
            var staging = Path.Combine(fixture.Root, fixture.Church.ToString("N"),
                fixture.Lesson.ToString("N"), ".staging-v1-31");
            Directory.CreateDirectory(staging);
            held = new FileStream(Path.Combine(staging, "locked"), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None);
            throw original;
        };
        try
        {
            Assert.Same(original, await Assert.ThrowsAsync<TimeoutException>(() => fixture.Publish(fixture.Images())));
        }
        finally { held?.Dispose(); }
        fixture.Store.OnCommit = null;
        await fixture.Publish(fixture.Images());
        Assert.Equal(31, fixture.Files().Length);
        fixture.AssertNoStaging();
    }

    /// <summary>不同教會的授權拒絕必須發生在任何圖片檔案 I/O 之前。</summary>
    [Fact]
    public async Task Publication_AuthorizationFailure_WritesNothing()
    {
        using var fixture = new Fixture();
        fixture.Store.Deny = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Publish(fixture.Images()));
        Assert.Empty(fixture.Files());
        Assert.Equal(0, fixture.Store.Commits);
        fixture.AssertNoStaging();
    }

    /// <summary>同消費集合重複欄位不能以覆寫掩蓋，必須在授權及寫檔前拒絕。</summary>
    [Fact]
    public async Task Publication_DuplicateField_RejectsWholeCandidate()
    {
        using var fixture = new Fixture();
        var images = fixture.Images().ToArray();
        images[1] = images[0];
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Publish(images));
        Assert.Equal(0, fixture.Store.Authorizations);
        Assert.Empty(fixture.Files());
    }

    /// <summary>同事件圖片位元組不同時拒絕，不覆蓋既有已發布圖片。</summary>
    [Fact]
    public async Task Publication_ConflictingBytes_FailsWithoutOverwrite()
    {
        using var fixture = new Fixture();
        await fixture.Publish(fixture.Images());
        var changed = fixture.Images().ToArray();
        changed[0].Png.Position = 0;
        changed[0].Png.WriteByte(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Publish(changed));
        Assert.Equal(1, fixture.Store.Commits);
        Assert.All(fixture.Files(), path => Assert.Equal(137, File.ReadAllBytes(path)[0]));
        fixture.AssertNoStaging();
    }

    /// <summary>中途讀取圖片失敗，已寫入暫存全部清掉，不能提交或留下部分正式組。</summary>
    [Fact]
    public async Task Publication_CopyFailure_CleansPartialStaging()
    {
        using var fixture = new Fixture();
        var images = fixture.Images().ToArray();
        using var broken = new BrokenStream();
        images[4] = new(images[4].Descriptor, broken);
        await Assert.ThrowsAsync<IOException>(() => fixture.Publish(images));
        Assert.Empty(fixture.Files());
        Assert.Equal(0, fixture.Store.Commits);
        fixture.AssertNoStaging();
    }

    /// <summary>取消在圖片複製中發生時仍須排空關閉檔案並清除全部 staging。</summary>
    [Fact]
    public async Task Publication_Cancellation_CleansPartialStaging()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var images = fixture.Images().ToArray();
        using var cancelling = new CancellingStream(cancellation);
        images[4] = new(images[4].Descriptor, cancelling);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Publish(images, cancellation.Token));
        Assert.Empty(fixture.Files());
        Assert.Equal(0, fixture.Store.Commits);
        fixture.AssertNoStaging();
    }

    /// <summary>程序中斷留下固定 staging，由下一個取得獨占 lease 的 owner 清除後完整重建。</summary>
    [Fact]
    public async Task Publication_InterruptedStaging_IsRecoveredWithoutOrphanGrowth()
    {
        using var fixture = new Fixture();
        var stale = Path.Combine(fixture.Root, fixture.Church.ToString("N"),
            fixture.Lesson.ToString("N"), ".staging-v1-31");
        Directory.CreateDirectory(stale);
        File.WriteAllBytes(Path.Combine(stale, "partial.png"), new byte[] { 0 });
        await fixture.Publish(fixture.Images());
        Assert.Equal(31, fixture.Files().Length);
        fixture.AssertNoStaging();
    }

    /// <summary>其他 owner 仍持有 lease 時取消，不得刪除對方 staging 或釋放對方 handle。</summary>
    [Fact]
    public async Task Publication_LeaseWaitCancellation_PreservesOtherOwner()
    {
        using var fixture = new Fixture();
        var parent = Path.Combine(fixture.Root, fixture.Church.ToString("N"), fixture.Lesson.ToString("N"));
        Directory.CreateDirectory(parent);
        using var lease = new FileStream(Path.Combine(parent, ".v1-31.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var staging = Path.Combine(parent, ".staging-v1-31");
        Directory.CreateDirectory(staging);
        var marker = Path.Combine(staging, "partial.png");
        File.WriteAllBytes(marker, new byte[] { 17 });
        using var cancellation = new CancellationTokenSource();
        var operation = fixture.Publish(fixture.Images(), cancellation.Token);
        Assert.False(operation.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(new byte[] { 17 }, File.ReadAllBytes(marker));
        Assert.Equal(0, fixture.Store.Commits);
    }

    /// <summary>另一課程即使內容相同仍保留獨立正式路徑，不按顯示文字合併。</summary>
    [Fact]
    public async Task Publication_DifferentLesson_HasIndependentIdentity()
    {
        using var fixture = new Fixture();
        await fixture.Publish(fixture.Images());
        var another = Guid.NewGuid();
        await fixture.Service.PublishAndCommitAsync(fixture.Church, another, 1,
            fixture.Images(lessonId: another), default);
        Assert.Equal(62, fixture.Files().Length);
        fixture.AssertNoStaging();
    }

    /// <summary>真實註冊、factory、renderer 與檔案 publisher 串接；lease 關閉釋放 scoped store。</summary>
    [Fact]
    public async Task Registration_RealFactoryAndPublisher_DrainJobScope()
    {
        using var fixture = new Fixture();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lessons:Qr:StorageDirectory"] = fixture.Root,
            ["Lessons:Qr:PublicBaseUrl"] = "https://images.example/lessons/",
            [$"Lessons:Qr:Churches:{fixture.Church:D}:RegularPrefix"] = Fixture.Regular,
            [$"Lessons:Qr:Churches:{fixture.Church:D}:PollPrefix"] = Fixture.Poll
        }).Build();
        var counts = new Counts();
        var collection = new ServiceCollection().AddLessonQrPublication(config);
        collection.AddScoped<ILessonQrPublicationStore>(_ => new ScopedStore(counts));
        await using var provider = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
        var factory = provider.GetRequiredService<LessonQrBundleHostFactory>();
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var payload = $"{{\"schema\":1,\"lessonId\":\"{fixture.Lesson:D}\",\"contentVersion\":1,\"imageCount\":31,\"includePoll\":0}}";
            var item = new OutboundWorkItem(Guid.NewGuid(), "1", fixture.Church, null,
                OutboundWorkType.QrImageGenerate, payload, "synthetic-key",
                OutboundWorkStatus.Processing, 0, null, DateTime.UtcNow);
            await using (var lease = await factory.OpenAsync(item, default))
                await lease.Handler.ExecuteAsync(default);
            Assert.Equal(0, counts.Active);
        }
        Assert.Equal(3, counts.Disposed);
        Assert.Equal(31, fixture.Files().Length);
        Assert.All(fixture.Files(), path => Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            File.ReadAllBytes(path).Take(8)));
        fixture.AssertNoStaging();
    }

    /// <summary>來源只產生版本一，未知版本在任何儲存前拒絕，避免無界目錄成長。</summary>
    [Fact]
    public async Task Publication_UnsupportedVersion_FailsBeforeIo()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.PublishAndCommitAsync(
            fixture.Church, fixture.Lesson, 2, fixture.Images(), default));
        Assert.Empty(fixture.Files());
        Assert.Equal(0, fixture.Store.Authorizations);
    }

    /// <summary>每案唯一目錄及 stream owner；所有工作返回後才清理，無共用測試狀態。</summary>
    private sealed class Fixture : IDisposable, ILessonQrBundleOptionsProvider
    {
        internal const string Regular = "https://front.example/?QrCodeId=";
        internal const string Poll = "https://poll.example/?QrCodeId=";
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "lesson-qr-" + Guid.NewGuid().ToString("N"));
        internal readonly Guid Church = Guid.NewGuid();
        internal readonly Guid Lesson = Guid.NewGuid();
        internal readonly Store Store = new();
        private readonly System.Collections.Concurrent.ConcurrentBag<Stream> _streams = new();
        internal LessonQrBundlePublicationService Service { get; }

        /// <summary>建立真正發布器，但不建立 SDK 或公開 HTTP server。</summary>
        internal Fixture() => Service = new(Store, this, new(Root, "https://images.example/lessons/"));

        /// <summary>只提供本案例教會前綴，不使用全域後備或名稱分支。</summary>
        public LessonQrBundleOptions GetOptions(Guid churchId) =>
            churchId == Church ? new(Regular, Poll) : throw new UnauthorizedAccessException();

        /// <summary>建立固定候選與有界合成圖片，每個 stream 登記至 fixture 唯一 owner。</summary>
        internal IReadOnlyList<LessonQrRenderedImage> Images(bool poll = false, Guid? lessonId = null) =>
            LessonQrBundlePlan.Create(lessonId ?? Lesson, 1, poll, GetOptions(Church)).Images.Select(descriptor =>
            {
                var png = new MemoryStream(new byte[] { 137, 80, 78, 71 });
                _streams.Add(png);
                return new LessonQrRenderedImage(descriptor, png);
            }).ToArray();

        /// <summary>await 真正檔案發布；輸入 stream 仍由本 fixture 擁有。</summary>
        internal Task Publish(IReadOnlyList<LessonQrRenderedImage> images, CancellationToken token = default) =>
            Service.PublishAndCommitAsync(Church, Lesson, 1, images, token);

        /// <summary>只盤點本測試獨占目錄的圖片，不掃描父目錄或產品資料。</summary>
        internal string[] Files() => Directory.Exists(Root) ?
            Directory.GetFiles(Root, "*.png", SearchOption.AllDirectories) : Array.Empty<string>();

        /// <summary>成功、取消與失敗都不得殘留本次呼叫的暫存目錄。</summary>
        internal void AssertNoStaging()
        {
            if (Directory.Exists(Root))
                Assert.Empty(Directory.GetDirectories(Root, ".staging-*", SearchOption.AllDirectories));
        }

        /// <summary>等候 rename 時以真正獨占開檔確認同事件 lease 仍由發布 owner 持有。</summary>
        internal void AssertLeaseHeld(int count) => Assert.Throws<IOException>(() =>
        {
            using var competing = OpenLease(count);
        });

        /// <summary>發布已排空時可獨占重開 lease，證明失敗／取消未遺留產品 owner。</summary>
        internal void AssertLeaseAvailable(int count)
        {
            using var lease = OpenLease(count);
        }

        /// <summary>只重開本案固定 lease，不建立背景工作，回傳 handle 由斷言呼叫端 using 關閉。</summary>
        private FileStream OpenLease(int count) => new(Path.Combine(Root, Church.ToString("N"),
            Lesson.ToString("N"), $".v1-{count}.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        /// <summary>讀取鎖解除後獨占重開全部圖片與 lease，驗證產品寫入／驗證 handle 已確定關閉。</summary>
        internal void AssertPublicationHandlesReleased(int count)
        {
            AssertLeaseAvailable(count);
            foreach (var path in Files())
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }

        /// <summary>所有 await 完成後先關閉輸入 stream，再移除案例專屬絕對目錄。</summary>
        public void Dispose()
        {
            foreach (var stream in _streams) stream.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>合成授權及提交觀察器，故障直接向外傳遞，不模擬 D365 原子性證據。</summary>
    private sealed class Store : ILessonQrPublicationStore, ILessonQrPublicationCommit
    {
        internal int Authorizations;
        internal int Commits;
        internal bool Deny;
        internal Action<IReadOnlyDictionary<string, string>>? OnCommit;
        /// <summary>可注入跨教會拒絕，決定性確認圖片 I/O 前已授權。</summary>
        public ILessonQrPublicationCommit Authorize(Guid churchId, Guid lessonId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Authorizations);
            if (Deny) throw new UnauthorizedAccessException("Synthetic authorization failure.");
            return this;
        }
        /// <summary>觀察完整候選或注入結果不明；次數以原子計數支援競爭案例。</summary>
        public void Commit(IReadOnlyDictionary<string, string> urls, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            OnCommit?.Invoke(urls);
            Interlocked.Increment(ref Commits);
        }
    }

    /// <summary>中途讀取故障替身；可讀與有界但複製時失敗，fixture 呼叫端負責 Dispose。</summary>
    private sealed class BrokenStream : MemoryStream
    {
        /// <summary>維持合法有界 stream 外觀，測試真正寫入中途的清理。</summary>
        internal BrokenStream() : base(new byte[] { 137, 80, 78, 71 }) { }
        /// <summary>注入非取消 I/O 故障，不允許發布部分候選。</summary>
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken token) =>
            throw new IOException("Synthetic image read failure.");
    }

    /// <summary>複製中取消，驗證已建立的 FileStream 在返回前關閉且目錄清除。</summary>
    private sealed class CancellingStream(CancellationTokenSource owner) : MemoryStream(new byte[] { 137, 80, 78, 71 })
    {
        /// <summary>明確取消呼叫端 token，保留標準取消型別供上層分類。</summary>
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken token)
        {
            owner.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 最後一張 PNG 的測試輸入 owner，CopyTo 時才開住前一張已關閉的候選，模擬獨立讀取者。
    /// 唯一 handle 不分享 Delete，真正 Windows MoveDirectory 因此受阻；例外觀察只篩本案
    /// staging 路徑及 MoveDirectory 堆疊，不產生／改寫例外、不列舉外部 handle、不發告警。
    /// TCS 只保存第一次真例外，沒有輪詢 task；finally 必須先排空發布再 Dispose 本 owner，
    /// 解除 AppDomain 訂閱並關閉 handle，避免測試靜態事件保留 fixture 或跨案例干擾。
    /// </summary>
    private sealed class WindowsRenameLockStream : MemoryStream
    {
        private readonly string _staging;
        private readonly string _firstImage;
        private readonly TaskCompletionSource<IOException> _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private FileStream? _reader;
        private int _observing;
        private int _moveFailures;
        private long _blockedAt;

        /// <summary>固定本案路徑並訂閱只讀故障觀察；owner Dispose 必須與建構一一配對。</summary>
        internal WindowsRenameLockStream(string staging, string firstImage) : base(new byte[] { 137, 80, 78, 71 })
        {
            _staging = staging;
            _firstImage = firstImage;
            AppDomain.CurrentDomain.FirstChanceException += ObserveMoveFailure;
            _observing = 1;
        }

        /// <summary>只有真正搬移已拋出本案 IOException 才完成，作為釋放鎖的因果屏障。</summary>
        internal Task<IOException> Blocked => _blocked.Task;
        /// <summary>只計算本案 Move 的故障，證明重試曾發生；不累積例外集合。</summary>
        internal int MoveFailures => Volatile.Read(ref _moveFailures);
        /// <summary>自第一次受阻起的單調時間，驗證固定等待上限，不依本地日期或時區。</summary>
        internal TimeSpan BlockedElapsed => Stopwatch.GetElapsedTime(Volatile.Read(ref _blockedAt));
        /// <summary>本 owner 的讀取 handle 與事件訂閱都必須回到零，不含已 await 的 TCS 結果。</summary>
        internal int ActiveResources => (_reader is null ? 0 : 1) + Volatile.Read(ref _observing);

        /// <summary>先複製最後一張，再讀取前一張；不接觸仍以 Share.None 寫入的當前目的 stream。</summary>
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken token)
        {
            await base.CopyToAsync(destination, bufferSize, token);
            _reader = new FileStream(Path.Combine(_staging, _firstImage),
                FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        /// <summary>只觀察框架真正 Move 的第一個 throw，不捕捉清理、lease 或其他案例例外。</summary>
        private void ObserveMoveFailure(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is IOException error &&
                error.Message.Contains(_staging, StringComparison.Ordinal) &&
                error.StackTrace?.Contains("System.IO.FileSystem.MoveDirectory", StringComparison.Ordinal) == true)
            {
                Interlocked.Increment(ref _moveFailures);
                Interlocked.CompareExchange(ref _blockedAt, Stopwatch.GetTimestamp(), 0);
                _blocked.TrySetResult(error);
            }
        }

        /// <summary>測試在已證明原發布受阻後解鎖；Interlocked 轉移唯一 owner，重複清理安全。</summary>
        internal void ReleaseReader() => Interlocked.Exchange(ref _reader, null)?.Dispose();

        /// <summary>所有發布 await 返回後解除靜態事件參照、關閉讀取 handle，最後關閉合成輸入。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Interlocked.Exchange(ref _observing, 0) != 0)
                    AppDomain.CurrentDomain.FirstChanceException -= ObserveMoveFailure;
                ReleaseReader();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>僅保存單一 DI 測試的資源計數，不保存課程、圖片或身分。</summary>
    private sealed class Counts
    {
        internal int Active;
        internal int Disposed;
    }

    /// <summary>真 DI scope 擁有的替身；Dispose 是唯一減少 active 的清理路徑。</summary>
    private sealed class ScopedStore : ILessonQrPublicationStore, ILessonQrPublicationCommit, IDisposable
    {
        private readonly Counts _counts;
        /// <summary>每個工作建立一次，禁止由 singleton 捕獲。</summary>
        internal ScopedStore(Counts counts) { _counts = counts; counts.Active++; }
        /// <summary>本案例只驗 DI／檔案串接，授權故障由其他案例負責。</summary>
        public ILessonQrPublicationCommit Authorize(Guid churchId, Guid lessonId, CancellationToken token) => this;
        /// <summary>斷言真正 publisher 提供完整固定欄位組，不冒稱 SDK 執行。</summary>
        public void Commit(IReadOnlyDictionary<string, string> urls, CancellationToken token) => Assert.Equal(31, urls.Count);
        /// <summary>scope 釋放時回基線，不能延長資源到下一筆工作。</summary>
        public void Dispose() { _counts.Active--; _counts.Disposed++; }
    }
}
