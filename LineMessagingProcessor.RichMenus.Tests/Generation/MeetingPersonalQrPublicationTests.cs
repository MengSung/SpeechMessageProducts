/*
 * 檔案整體說明
 * 目的與定位：保護 P5-09 完整圖片候選才提交、逐張 stream 所有權與取消排空。
 * 主要流程：使用真暫存檔案系統及合成 scoped 授權／交易接縫；注入第三張失敗、
 * 取消及交易錯誤，斷言零部分 URL 提交、stream 峰值一、釋放回零及 staging 清理。
 * 使用邊界與維護：測試不連 D365、LINE，不把合成交易當作實機原子性證據。
 * 測試方法唯一擁有隨機暫存根，finally 移除該確切根；renderer 交付的 stream
 * 由產品發布器 Dispose，替身只留計數，不保存已關閉的 stream 或跨案例個資。
 * 驗證方式：成功必須提交恰三十八欄或一張 PNG，失敗不得呼叫 commit。
 */
using LineMessagingProcessor.RichMenus.QrImages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using ZXing;

namespace LineMessagingProcessor.RichMenus.Tests.Generation;

/// <summary>對真正發布器故障注入，確認有界持有與全量提交契約。</summary>
[Collection("QR image lifecycle")]
public sealed class MeetingPersonalQrPublicationTests
{
    /// <summary>未配置 meeting binding 的 personal-only 設定不能因出席鏈通過而產圖或接觸 SDK。</summary>
    [Fact]
    public async Task Meeting_MissingBinding_RejectsBeforeSdkRendererAndFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "meeting-qr-" + Guid.NewGuid().ToString("N"));
        var store = new Store();
        var renderer = new Renderer();
        try
        {
            var payload = MeetingPersonalQrPlanTests.Parse("meeting", Guid.NewGuid());
            var service = new MeetingPersonalQrPublicationService(store, new Options(),
                new MeetingPersonalQrStorageOptions(new LessonQrStorageOptions(root,
                    "https://images.invalid/")), renderer);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(Guid.NewGuid(),
                payload, default));
            Assert.Equal(0, store.Authorizations);
            Assert.Equal(0, store.Commits);
            Assert.Equal(0, renderer.Calls);
            Assert.False(Directory.Exists(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>部署 base 雖合法但完整 URL 超過 metadata 五百字時，必須在 SDK／renderer／檔案之前拒絕。</summary>
    [Fact]
    public async Task Meeting_OverlongPublicBaseUrl_RejectsBeforeSdkAndFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "meeting-qr-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store();
            var renderer = new Renderer();
            var payload = MeetingPersonalQrPlanTests.Parse("meeting", Guid.NewGuid());
            var service = new MeetingPersonalQrPublicationService(store, new Options(payload.RecordId),
                new MeetingPersonalQrStorageOptions(new LessonQrStorageOptions(root,
                    "https://images.invalid/" + new string('a', 430) + "/")), renderer);
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(Guid.NewGuid(),
                payload, default));
            Assert.Equal(0, store.Authorizations);
            Assert.Equal(0, store.Commits);
            Assert.Equal(0, renderer.Calls);
            Assert.False(Directory.Exists(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>真正 PNG 可被獨立 reader 解回設定前綴與權威姓名；影像配置須回原基線。</summary>
    [Fact]
    public async Task Personal_RealRenderer_DecodesToAuthoritativeContentAndDrainsAllocations()
    {
        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var root = Path.Combine(Path.GetTempPath(), "personal-qr-" + Guid.NewGuid().ToString("N"));
        var store = new Store();
        var payload = MeetingPersonalQrPlanTests.Parse("personal", Guid.NewGuid());
        var service = new MeetingPersonalQrPublicationService(store, new Options(),
            new MeetingPersonalQrStorageOptions(new LessonQrStorageOptions(root, "https://images.invalid/")));
        await service.ExecuteAsync(Guid.NewGuid(), payload, default);
        Assert.NotNull(store.Png);
        using (var image = Image.Load<L8>(store.Png))
        {
            var pixels = new byte[image.Width * image.Height];
            image.CopyPixelDataTo(pixels);
            var result = new BarcodeReaderGeneric().Decode(pixels, image.Width, image.Height,
                RGBLuminanceSource.BitmapFormat.Gray8);
            Assert.NotNull(result);
            Assert.Equal($"https://personal.invalid/?QrCodeId={payload.RecordId:D}@權威姓名", result.Text);
        }
        Assert.Equal(baseline, MemoryDiagnostics.TotalUndisposedAllocationCount);
        Assert.False(Directory.Exists(root));
    }

    /// <summary>授權失敗發生於任何 renderer／目錄建立前，錯誤保留原物件且不提交。</summary>
    [Fact]
    public async Task AuthorizationFailure_DoesNotRenderOrCreateStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "meeting-qr-" + Guid.NewGuid().ToString("N"));
        var failure = new IOException("Injected source failure.");
        var store = new Store { AuthorizeFailure = failure };
        var renderer = new Renderer();
        var payload = MeetingPersonalQrPlanTests.Parse("meeting", Guid.NewGuid());
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => Create(root, store, renderer, payload)
            .ExecuteAsync(Guid.NewGuid(), payload, default)));
        Assert.Equal(0, renderer.Calls);
        Assert.Equal(0, store.Commits);
        Assert.False(Directory.Exists(root));
    }

    /// <summary>完整組已準備後交易失敗不回成功；保留固定候選供安全重送，無 staging 或 stream 滯留。</summary>
    [Fact]
    public async Task Meeting_CommitFailure_PreservesErrorAndRetriesWithoutAdditionalFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "meeting-qr-" + Guid.NewGuid().ToString("N"));
        try
        {
            var failure = new IOException("Injected commit failure.");
            var store = new Store { CommitFailure = failure };
            var renderer = new Renderer();
            var payload = MeetingPersonalQrPlanTests.Parse("meeting", Guid.NewGuid());
            var service = Create(root, store, renderer, payload);
            var church = Guid.NewGuid();
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(church, payload, default)));
            Assert.Equal(0, store.Commits);
            Assert.Equal(0, renderer.Active);
            Assert.Empty(Directory.EnumerateDirectories(root, ".staging*", SearchOption.AllDirectories));
            store.CommitFailure = null;
            await service.ExecuteAsync(church, payload, default);
            Assert.Equal(1, store.Commits);
            Assert.Equal(38, Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Count());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>逐張寫暫存，三十八張全成功才提交；重送同一路徑不增加正式檔案數。</summary>
    [Fact]
    public async Task Meeting_PublishesCompleteCandidateAndRetryDoesNotDuplicateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "meeting-qr-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store();
            var renderer = new Renderer();
            var payload = MeetingPersonalQrPlanTests.Parse("meeting", Guid.NewGuid());
            var service = Create(root, store, renderer, payload);
            var church = Guid.NewGuid();
            await service.ExecuteAsync(church, payload, CancellationToken.None);
            await service.ExecuteAsync(church, payload, CancellationToken.None);
            Assert.Equal(2, store.Commits);
            Assert.Equal(38, store.Urls!.Count);
            Assert.Equal(38, Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Count());
            Assert.Equal(1, renderer.Peak);
            Assert.Equal(0, renderer.Active);
            Assert.Empty(Directory.EnumerateDirectories(root, ".staging*", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>第三張故障或取消都須關閉先前圖片，移除 staging，且不得發布部分集合。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Meeting_RenderFailureOrCancellation_DrainsWithoutPublication(bool cancel)
    {
        var root = Path.Combine(Path.GetTempPath(), "meeting-qr-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        try
        {
            var store = new Store();
            var renderer = new Renderer { FailAt = 3, Cancellation = cancel ? cancellation : null };
            var payload = MeetingPersonalQrPlanTests.Parse("meeting", Guid.NewGuid());
            var service = Create(root, store, renderer, payload);
            await Assert.ThrowsAnyAsync<Exception>(() => service.ExecuteAsync(Guid.NewGuid(),
                payload, cancellation.Token));
            Assert.Equal(0, store.Commits);
            Assert.Equal(0, renderer.Active);
            if (Directory.Exists(root))
            {
                Assert.Empty(Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories));
                Assert.Empty(Directory.EnumerateDirectories(root, ".staging*", SearchOption.AllDirectories));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>個人從授權資料取得姓名，完整讀取 PNG 再提交；不建立公開目錄。</summary>
    [Fact]
    public async Task Personal_UsesAuthoritativeNameAndDrainsSinglePng()
    {
        var root = Path.Combine(Path.GetTempPath(), "personal-qr-" + Guid.NewGuid().ToString("N"));
        var store = new Store();
        var renderer = new Renderer();
        var payload = MeetingPersonalQrPlanTests.Parse("personal", Guid.NewGuid());
        await Create(root, store, renderer, payload).ExecuteAsync(Guid.NewGuid(), payload, CancellationToken.None);
        Assert.EndsWith($"{payload.RecordId:D}@權威姓名", renderer.LastContent);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1 }, store.Png);
        Assert.Equal(0, renderer.Active);
        Assert.False(Directory.Exists(root));
    }

    /// <summary>單筆測試用純值設定，沒有組態 fallback 或其他教會狀態。</summary>
    private static MeetingPersonalQrPublicationService Create(string root, Store store, Renderer renderer,
        MeetingPersonalQrPayload payload) =>
        new(store, new Options(payload.Kind == "meeting" ? payload.RecordId : Guid.Empty),
            new MeetingPersonalQrStorageOptions(
            new LessonQrStorageOptions(root, "https://images.invalid/")), renderer);

    /// <summary>只提供本案合成前綴；不保存輸入身分。</summary>
    private sealed class Options : IMeetingPersonalQrOptionsProvider
    {
        private readonly Guid _meetingId;
        public Options(Guid meetingId = default) => _meetingId = meetingId;
        /// <summary>測試的各教會使用相同合成網址，但正式檔案仍由 ID 分區。</summary>
        public MeetingPersonalQrOptions GetOptions(Guid churchId) =>
            new("https://meeting.invalid/?QrCodeId=", "https://personal.invalid/?QrCodeId=",
                _meetingId == Guid.Empty ? Array.Empty<Guid>() : new[] { _meetingId });
    }

    /// <summary>合成授權與 commit 觀察器，只在本案保存 detached 值，不擁有背景資源。</summary>
    private sealed class Store : IMeetingPersonalQrPublicationStore, IMeetingPersonalQrCommit
    {
        /// <summary>進入 SDK 授權接縫的次數，URL 預算拒絕前必須為零。</summary>
        public int Authorizations;
        /// <summary>已確認成功提交次數，失敗不得增加。</summary>
        public int Commits;
        /// <summary>本案最後一次完整 detached URL 集合，不持有產品可變字典。</summary>
        public IReadOnlyDictionary<string, string>? Urls;
        /// <summary>本案最後一張 PNG 的副本，僅保留到測試返回。</summary>
        public byte[]? Png;
        /// <summary>授權入場注入的原例外，驗證圖片之前已停止。</summary>
        public Exception? AuthorizeFailure;
        /// <summary>交易接縫注入的原例外，不模擬遠端已成功的未知結果。</summary>
        public Exception? CommitFailure;
        /// <summary>用權威姓名而不是待辦姓名，模擬已完成 scope 驗證的結果。</summary>
        public string FullName => "權威姓名";
        /// <summary>回傳本次測試候選；真正 SDK 歸屬測試在 AspNetCore 專案。</summary>
        public IMeetingPersonalQrCommit Authorize(Guid churchId, MeetingPersonalQrPayload payload,
            CancellationToken token)
        {
            Authorizations++;
            return AuthorizeFailure is null ? this : throw AuthorizeFailure;
        }
        /// <summary>複製提交內容供斷言，拒絕隱含的部分發布。</summary>
        public void Commit(IReadOnlyDictionary<string, string> urls, byte[]? png, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (CommitFailure is not null) throw CommitFailure;
            Commits++;
            Urls = new Dictionary<string, string>(urls);
            Png = png?.ToArray();
        }
    }

    /// <summary>交出一個有界合成 PNG stream，第三張可注入故障；無背景工作。</summary>
    private sealed class Renderer : ILessonQrImageRenderer
    {
        /// <summary>尚未由產品關閉的 streams。</summary>
        public int Active;
        /// <summary>觀察同時持有峰值，逐張流程必須為一。</summary>
        public int Peak;
        /// <summary>產圖入場總數，可斷言授權失敗後零呼叫。</summary>
        public int Calls;
        /// <summary>指定第幾張故障；零表示正常。</summary>
        public int FailAt;
        /// <summary>最後本案內容供權威姓名斷言，不進入共用 root。</summary>
        public string? LastContent;
        /// <summary>測試唯一擁有的取消來源，只在故障點通知，不由 renderer Dispose。</summary>
        public CancellationTokenSource? Cancellation;
        /// <summary>已交出的 stream 由產品唯一擁有；故障前不分配 stream。</summary>
        public Task<Stream> RenderAsync(string content, CancellationToken token)
        {
            LastContent = content;
            if (++Calls == FailAt)
            {
                Cancellation?.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Injected render failure.");
            }
            Active++;
            Peak = Math.Max(Peak, Active);
            return Task.FromResult<Stream>(new OwnedStream(this));
        }
    }

    /// <summary>生命週期替身，關閉時清除 buffer 並恰一次通知 owner，不保留產品引用。</summary>
    private sealed class OwnedStream(Renderer owner) : MemoryStream(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1 })
    {
        private bool _closed;
        /// <summary>讓同步及非同步 Dispose 都可觀察，重複關閉不重複扣計數。</summary>
        protected override void Dispose(bool disposing)
        {
            if (!_closed) { _closed = true; owner.Active--; }
            base.Dispose(disposing);
        }
    }
}
