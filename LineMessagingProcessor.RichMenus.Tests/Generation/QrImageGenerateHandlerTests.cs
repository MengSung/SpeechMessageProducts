/*
 * 檔案整體說明
 *
 * 目的與定位：P2-09b三個計畫同名案例的離線契約測試，使用真實P2-09a編碼、
 * ZXing.Net與ImageSharp產圖及反向解碼，不以假PNG或字串自證代替圖片內容。
 * 主要情境：四種舊格式逐字比對；超長內容先拒絕且renderer未呼叫；
 * 成功、下游故障與正常取消後stream不可再讀，真ImageSharp配置須回原基線。
 * 資源與隔離：所有GUID與尾欄皆合成，每案自行擁有CTS、PNG解碼圖片及觀察清單；
 * A/B並行完整await後才比對各自內容，委派不跨工作共用credential或可變狀態。
 * 不連D365／LINE、不寫圖檔；故障替身只觀察真正handler的產圖及Dispose邊界，
 * 不增加測試專用產品方法，也不以GC或finalizer補足遺漏的釋放。
 * 配置計數案例使用禁止平行的collection，避免其他圖片案例影響全域診斷基線。
 */
using LineMessagingProcessor.RichMenus.QrImages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using Xunit;
using ZXing;

namespace LineMessagingProcessor.RichMenus.Tests.Generation;

/// <summary>隔離全域ImageSharp診斷計數的觀察時段，不改產品的平行行為。</summary>
[CollectionDefinition("QR image lifecycle", DisableParallelization = true)]
public sealed class QrImageLifecycleCollection { }

/// <summary>驗證真PNG內容與handler資源owner；所有工作與stream都只屬於當前案例。</summary>
[Collection("QR image lifecycle")]
public sealed class QrImageGenerateHandlerTests
{
    private static readonly Guid Record = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>四種獨立手寫舊格式作oracle；改成白底、漏尾欄或換分隔符都會失敗。</summary>
    [Theory]
    [InlineData(QrImageContentKind.Lesson, "2", "in", "11111111-2222-3333-4444-555555555555_2_in")]
    [InlineData(QrImageContentKind.Meeting, "new_count", "in", "11111111-2222-3333-4444-555555555555@new_count@in")]
    [InlineData(QrImageContentKind.Personal, "suffix", "", "11111111-2222-3333-4444-555555555555@suffix")]
    [InlineData(QrImageContentKind.SmallGroup, "in", "", "11111111-2222-3333-4444-555555555555_in")]
    public async Task Generate_ImageDecodesToSameContent(QrImageContentKind kind,
        string first, string second, string expected)
    {
        Stream? borrowed = null;
        var handler = new QrImageGenerateHandler(new(kind, Record, first, second,
            QrCodePayloadSeparator.AtSign), async (stream, token) =>
        {
            borrowed = stream;
            Assert.Equal(expected, await DecodeAsync(stream, token));
        });
        await handler.ExecuteAsync(default);
        Assert.NotNull(borrowed);
        Assert.False(borrowed.CanRead);
    }

    /// <summary>個人尾欄使內容超過2048字元；真正handler須在renderer及publish前拒絕。</summary>
    [Fact]
    public async Task Generate_ContentTooLong_RejectedBeforeRender()
    {
        var renderer = new ObservingRenderer();
        var published = false;
        var handler = new QrImageGenerateHandler(new(QrImageContentKind.Personal, Record, new string('x', 2048)),
            (_, _) => { published = true; return Task.CompletedTask; }, renderer);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(default));
        Assert.Equal(0, renderer.Calls);
        Assert.False(published);
    }

    /// <summary>字元仍在codec上限內但UTF8超過圖片預算時，不能先進renderer才失敗。</summary>
    [Fact]
    public async Task Generate_Utf8ByteBudget_RejectedBeforeRender()
    {
        var renderer = new ObservingRenderer();
        var handler = new QrImageGenerateHandler(new(QrImageContentKind.Personal, Record, new string('界', 1000)),
            (_, _) => throw new InvalidOperationException("Must not publish."), renderer);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(default));
        Assert.Equal(0, renderer.Calls);
    }

    /// <summary>2048個UTF8位元組仍須能產生並解碼，防止只驗短GUID而漏掉最大有界畫布。</summary>
    [Fact]
    public async Task Generate_ExactByteBudget_DecodesWithoutTruncation()
    {
        var suffix = new string('x', 2011);
        var handler = new QrImageGenerateHandler(new(QrImageContentKind.Personal, Record, suffix),
            async (stream, token) => Assert.Equal(
                "11111111-2222-3333-4444-555555555555@" + suffix, await DecodeAsync(stream, token)));
        await handler.ExecuteAsync(default);
    }

    /// <summary>真產圖A/B各十次並行後內容各自正確，PNG已Dispose且未Dispose像素數回原基線。</summary>
    [Fact]
    public async Task Handler_ResourcesReturnToBaseline()
    {
        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var renderer = new QrImageRenderer();
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var streams = new Stream?[2];
            var a = new QrImageGenerateHandler(new(QrImageContentKind.Personal, Record, "only-A"),
                async (stream, token) =>
                {
                    streams[0] = stream;
                    Assert.Equal("11111111-2222-3333-4444-555555555555@only-A", await DecodeAsync(stream, token));
                }, renderer);
            var b = new QrImageGenerateHandler(new(QrImageContentKind.Personal, Record, "only-B"),
                async (stream, token) =>
                {
                    streams[1] = stream;
                    Assert.Equal("11111111-2222-3333-4444-555555555555@only-B", await DecodeAsync(stream, token));
                }, renderer);
            await Task.WhenAll(a.ExecuteAsync(default), b.ExecuteAsync(default));
            Assert.All(streams, stream => { Assert.NotNull(stream); Assert.False(stream.CanRead); });
        }
        Assert.Equal(baseline, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    /// <summary>下游真正I/O失敗或caller明確取消時，保留原例外身分並確定性關閉借入PNG。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handler_SinkFailureOrCancellation_ClosesPng(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var renderer = new ObservingRenderer();
        var failure = new IOException("synthetic sink failure");
        var handler = new QrImageGenerateHandler(new(QrImageContentKind.Personal, Record),
            (_, token) =>
            {
                if (cancel)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                throw failure;
            }, renderer);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(cancellation.Token));
        else
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => handler.ExecuteAsync(cancellation.Token)));
        Assert.NotNull(renderer.Output);
        Assert.False(renderer.Output.CanRead);
    }

    /// <summary>已取消的工作不得配置圖片或呼叫下游，避免停機期間啟動新工作。</summary>
    [Fact]
    public async Task Handler_AlreadyCancelled_DoesNotRender()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var renderer = new ObservingRenderer();
        var handler = new QrImageGenerateHandler(new(QrImageContentKind.Personal, Record),
            (_, _) => throw new InvalidOperationException("Must not publish."), renderer);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(cancellation.Token));
        Assert.Equal(0, renderer.Calls);
    }

    /// <summary>解碼真PNG為灰階像素，再由獨立ZXing reader驗證，不重用產品編碼邏輯。</summary>
    private static async Task<string> DecodeAsync(Stream png, CancellationToken token)
    {
        using var image = await Image.LoadAsync<L8>(png, token);
        Assert.Equal(512, image.Width);
        Assert.Equal(512, image.Height);
        var pixels = new byte[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        var result = new BarcodeReaderGeneric().Decode(pixels, image.Width, image.Height,
            RGBLuminanceSource.BitmapFormat.Gray8);
        Assert.NotNull(result);
        Assert.Equal(BarcodeFormat.QR_CODE, result.BarcodeFormat);
        return result.Text;
    }

    /// <summary>單案產圖邊界替身，只回可追蹤stream；清理由真正handler執行，案例不主動代為關閉。</summary>
    private sealed class ObservingRenderer : IQrImageRenderer
    {
        internal int Calls;
        internal Stream? Output;

        /// <summary>記錄呼叫及轉移stream，不保存輸入文字或建背景工作。</summary>
        public Task<Stream> RenderAsync(string content, CancellationToken cancellationToken)
        {
            Calls++;
            Output = new MemoryStream([137, 80, 78, 71]);
            return Task.FromResult(Output);
        }
    }
}
