/*
 * 檔案整體說明
 * 目的與定位：本檔由 SpeechMessageProducts.GoodShepherd.Hosting.Tests 擁有，
 * 保護 P5-07 真實 QR 檔案發布服務的完整候選、穩定儲存身分與失敗清理。
 * 主要流程：每案建立專屬暫存根目錄，使用真正 FileStream／Directory.Move 與發布器，
 * 只有 D365 授權及交易接縫使用合成 store；不把此替身聲稱為實機交易測試。
 * 測試注入跨教會拒絕、重複欄位、圖片複製失敗、取消、交易結果不明及同事件競爭，
 * 決定性斷言是完整 31／32 URL、檔案位元組正確、無 staging 殘留、正式檔案數不增加，
 * 以及原例外保留。另由真實 DI factory 產生 PNG 並驗證工作 scope 回到零。
 * 使用邊界與維護：測試 fixture 是目錄及所有輸入 stream 的唯一 owner，Dispose
 * 在所有 await 返回後關閉 stream 並移除本案例根目錄；沒有共用 Session、SDK、
 * token、靜態可變狀態或背景工作。合成圖片只測儲存，不宣稱可解碼；
 * factory 測試則使用正式 renderer。D365 CAS、SDK timeout 及 HTTPS 映射另需 Host 驗收。
 */
using SpeechMessageProducts.GoodShepherd.Hosting.QrImages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Hosting.Outbound;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Tests.QrImages;

/// <summary>真實檔案發布與 DI 工作 scope 的離線整合測試。</summary>
public sealed class LessonQrPublicationTests
{
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
