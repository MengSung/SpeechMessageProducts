/*
 * 檔案整體說明
 *
 * 目的與定位：以真ImageSharp及repo既有Roboto字型驗證P2-08圖面，不連網、不使用OS字型，
 * 合成英文字只用於像素oracle，不冒充繁中字型完整性或實機成果。Host仍需提供完整CJK字型。
 * 主要情境：同一固定版面畫不同完整文字，PNG解碼後尺寸正確且具有不同非白像素，
 * 並在取消與重複產圖後核對ImageSharp未Dispose配置數回到原基線。
 * 資源與隔離：單案using擁有PNG、解碼Image與取消來源，FontCollection只含固定第三方字型；
 * 不訂閱全域事件、不跑finalizer掩蓋資源錯誤、不保留圖面或context到其他案例。
 * 維護驗證：圖片不比較壓縮位元組，避免encoder細節被誤當產品契約；
 * 本檔的文字差異與白底斷言必須在移除DrawText時失敗，不能只檢查檔案存在。
 */
using Line.Messaging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Generation;

/// <summary>保護真正文字合成及deterministic disposal，不以替身PNG冒充renderer。</summary>
public sealed class RichMenuImageRendererTests
{
    /// <summary>實際繪圖不得只回白底；不同文字須形成不同像素，回收不依賴GC。</summary>
    [Fact]
    public async Task Renderer_LocalizedTextChangesPixels_AndAllocationsReturnToBaseline()
    {
        var before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var renderer = Renderer();
        byte[]? previous = null;
        foreach (var text in new[] { "Church A", "Church B", "Church A", "Church B" })
        {
            await using var png = await renderer.RenderAsync(new ImagemapSize(2500, 843),
                [new RichMenuImageLabel(0, 0, 2500, 843, text)], default);
            using var image = await Image.LoadAsync<Rgba32>(png);
            Assert.Equal(2500, image.Width);
            Assert.Equal(843, image.Height);
            var pixels = new byte[2500 * 843 * 4];
            image.CopyPixelDataTo(pixels);
            Assert.Contains(pixels, value => value < 128);
            if (previous is not null) Assert.False(previous.SequenceEqual(pixels));
            previous = pixels;
        }
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderAsync(new ImagemapSize(2500, 843),
            [new RichMenuImageLabel(0, 0, 2500, 843, "Cancelled")], cancellation.Token));
        Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    /// <summary>只讀repo已版控字型，不複製或依賴OS字型；FontCollection不持有開啟檔案。</summary>
    private static RichMenuImageRenderer Renderer()
    {
        var fonts = new FontCollection();
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../SpeechMessageProducts.ChurchReport/wwwroot/css/devextreme/fonts/Roboto-400.ttf"));
        return new RichMenuImageRenderer(fonts.Add(file).CreateFont(48));
    }
}
