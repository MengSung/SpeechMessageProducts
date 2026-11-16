/*
 * 檔案整體說明
 * 目的與定位：保護 schema2 factory 每筆 async scope、A／B 並行隔離及反覆執行後
 * stream／scoped store 回到零的生命週期契約，不以單次成功假裝證明無保留。
 * 主要流程：真 DI scope 與 factory 消費六十四筆 personal 合成待辦；renderer 非同步
 * 交付有界 PNG，commit 以本筆 ID 與姓名驗證未被其他 scope 覆蓋。另以故障 renderer
 * 及已取消 token 驗證失敗排空。測試沒有正式 SDK、LINE、Host worker 或磁碟 I/O。
 * 使用邊界與維護：容器由測試 await using 擁有；store 由工作 lease 唯一 DisposeAsync，
 * stream 由 publisher 唯一 Dispose；計數以 Interlocked 支援真同時執行，不保存已關閉
 * 服務或姓名清單。停止後只留下整數總計，不能把這些離線探針當成正式 SDK soak。
 */
using LineMessagingProcessor.RichMenus.QrImages;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Hosting.Outbound;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Generation;

/// <summary>直接測試 factory 的真 scope owner，確認跨工作不重用可變服務。</summary>
public sealed class MeetingPersonalQrScopeTests
{
    /// <summary>六十四工作交錯後每筆恰一次提交與釋放；不同教會／contact 不交叉。</summary>
    [Fact]
    public async Task Factory_ConcurrentScopesAndRepeatedDrain_ReturnToBaseline()
    {
        var counts = new Counts();
        await using var provider = Services(counts).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var factory = new LessonQrBundleHostFactory(provider.GetRequiredService<IServiceScopeFactory>());
        for (var batch = 0; batch < 8; batch++)
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var lease = await factory.OpenAsync(Item(), default);
                await lease.Handler.ExecuteAsync(default);
            }));
            Assert.Equal(0, counts.ActiveStores);
            Assert.Equal(0, counts.ActiveStreams);
        }
        Assert.Equal(64, counts.DisposedStores);
        Assert.Equal(64, counts.Commits);
    }

    /// <summary>renderer 在非同步入場後故障；真 lease 最終必須清掉 store，不產生提交。</summary>
    [Fact]
    public async Task Factory_FailedExecution_DisposesAsyncScopedStore()
    {
        var counts = new Counts { FailRender = true };
        await using var provider = Services(counts).BuildServiceProvider();
        var factory = new LessonQrBundleHostFactory(provider.GetRequiredService<IServiceScopeFactory>());
        await using (var lease = await factory.OpenAsync(Item(), default))
            await Assert.ThrowsAsync<IOException>(() => lease.Handler.ExecuteAsync(default));
        Assert.Equal(0, counts.ActiveStores);
        Assert.Equal(0, counts.ActiveStreams);
        Assert.Equal(0, counts.Commits);
        Assert.Equal(1, counts.DisposedStores);
    }

    /// <summary>錯 contact 或來源鍵在 scope 前拒絕，不建立任何 scoped store。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Factory_InvalidWorkIdentity_RejectsBeforeScope(bool contact)
    {
        var counts = new Counts();
        await using var provider = Services(counts).BuildServiceProvider();
        var factory = new LessonQrBundleHostFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var item = Item();
        item = contact ? item with { ContactId = Guid.NewGuid() } : item with { IdempotencyKey = "wrong-source" };
        await Assert.ThrowsAsync<ArgumentException>(() => factory.OpenAsync(item, default).AsTask());
        Assert.Equal(0, counts.ActiveStores);
        Assert.Equal(0, counts.DisposedStores);
    }

    /// <summary>只有本案純值選項與 scoped 探針；不註冊 background consumer。</summary>
    private static IServiceCollection Services(Counts counts) => new ServiceCollection()
        .AddSingleton(counts)
        .AddSingleton<IMeetingPersonalQrOptionsProvider, Options>()
        .AddSingleton(new MeetingPersonalQrStorageOptions(new LessonQrStorageOptions(
            Path.GetTempPath(), "https://images.invalid/")))
        .AddScoped<IMeetingPersonalQrPublicationStore, Store>()
        .AddScoped<ILessonQrImageRenderer, Renderer>()
        .AddScoped<MeetingPersonalQrPublicationService>();

    /// <summary>每筆使用不同正式教會與 contact，依真 source purpose 建立事件鍵。</summary>
    private static OutboundWorkItem Item()
    {
        var payload = MeetingPersonalQrPlanTests.Parse("personal", Guid.NewGuid());
        var church = Guid.NewGuid();
        return new(Guid.NewGuid(), "1", church, payload.RecordId, OutboundWorkType.QrImageGenerate,
            $"{{\"schema\":2,\"kind\":1,\"recordId\":\"{payload.RecordId:D}\",\"contentVersion\":1,\"imageCount\":1}}",
            $"{church:N}-{(int)OutboundWorkType.QrImageGenerate}-{payload.SourceId:N}",
            OutboundWorkStatus.Processing, 0, null, DateTime.UtcNow);
    }

    /// <summary>整數探針，跨 scope 只共享資源計數，不共享身分或業務內容。</summary>
    private sealed class Counts
    {
        /// <summary>同時存活 scoped store 數，排空後必須為零。</summary>
        public int ActiveStores;
        /// <summary>已完成 async Dispose 的 store 總數。</summary>
        public int DisposedStores;
        /// <summary>已移交產品但尚未關閉的 PNG stream 數。</summary>
        public int ActiveStreams;
        /// <summary>完整且內容相符的提交次數。</summary>
        public int Commits;
        /// <summary>注入非同步入場後的 renderer 故障，不建立任何 stream。</summary>
        public bool FailRender;
    }

    /// <summary>無狀態部署設定替身，沒有教會內容保留。</summary>
    private sealed class Options : IMeetingPersonalQrOptionsProvider
    {
        /// <summary>回合成前綴；真教會未設定拒絕矩陣另由註冊測試保護。</summary>
        public MeetingPersonalQrOptions GetOptions(Guid churchId) =>
            new("https://meeting.invalid/?QrCodeId=", "https://personal.invalid/?QrCodeId=");
    }

    /// <summary>每筆唯一 async owner；釋放時清除本筆身分，不保存到 root 容器。</summary>
    private sealed class Store : IMeetingPersonalQrPublicationStore, IMeetingPersonalQrCommit, IAsyncDisposable
    {
        private readonly Counts _counts;
        private Guid _church;
        private Guid _id;
        /// <summary>容器建立即增加 owner 數，由 lease 的 async Dispose 恰一次扣回。</summary>
        public Store(Counts counts) { _counts = counts; Interlocked.Increment(ref counts.ActiveStores); }
        /// <summary>姓名刻意綁本筆教會／contact，跨 scope 重用會使內容斷言失敗。</summary>
        public string FullName => _church.ToString("N") + "/" + _id.ToString("N");
        /// <summary>保存到本筆 scope，不能被下一筆工作覆寫。</summary>
        public IMeetingPersonalQrCommit Authorize(Guid churchId, MeetingPersonalQrPayload payload, CancellationToken token)
        { _church = churchId; _id = payload.RecordId; return this; }
        /// <summary>renderer 將完整內容寫進測試 PNG 尾欄，逐字驗證本筆身分沒有混入其他 scope。</summary>
        public void Commit(IReadOnlyDictionary<string, string> urls, byte[]? personalPng, CancellationToken token)
        {
            Assert.Empty(urls);
            Assert.NotNull(personalPng);
            Assert.Equal($"https://personal.invalid/?QrCodeId={_id:D}@{FullName}",
                System.Text.Encoding.UTF8.GetString(personalPng.AsSpan(8)));
            Interlocked.Increment(ref _counts.Commits);
        }
        /// <summary>清身分後記釋放，不留下 callback、Task 或引用。</summary>
        public ValueTask DisposeAsync()
        {
            _id = Guid.Empty; _church = Guid.Empty;
            Interlocked.Decrement(ref _counts.ActiveStores);
            Interlocked.Increment(ref _counts.DisposedStores);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>非同步交付 PNG 及內容尾欄；只在本次呼叫持有輸入。</summary>
    private sealed class Renderer(Counts counts) : ILessonQrImageRenderer
    {
        /// <summary>先 yield 讓多 scope 真正交錯；故障不建立 stream，成功移交產品 owner。</summary>
        public async Task<Stream> RenderAsync(string content, CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            if (counts.FailRender) throw new IOException("Injected renderer failure.");
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }
                .Concat(System.Text.Encoding.UTF8.GetBytes(content)).ToArray();
            Interlocked.Increment(ref counts.ActiveStreams);
            return new OwnedStream(bytes, counts);
        }
    }

    /// <summary>單一 stream owner 探針，Dispose 後減一且不保留輸入內容於測試清單。</summary>
    private sealed class OwnedStream(byte[] bytes, Counts counts) : MemoryStream(bytes)
    {
        private bool _closed;
        /// <summary>產品成功或故障 finally 必須到此；重複關閉不能製造假負數。</summary>
        protected override void Dispose(bool disposing)
        {
            if (!_closed) { _closed = true; Interlocked.Decrement(ref counts.ActiveStreams); }
            base.Dispose(disposing);
        }
    }
}
