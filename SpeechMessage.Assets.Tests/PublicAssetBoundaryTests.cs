/*
檔案整體說明
目的與定位：F7影像與檔案系統的補充邊界測試，保護JPEG方向、PNG文字與透明度、解碼前像素限制，以及真正來源故障不可誤分類的契約；只用合成內容及當案暫存目錄。
主要功能與流程：真ImageSharp產生帶Orientation的JPEG及壓縮文字PNG，發布後獨立讀像素與metadata；8000×5001的純色PNG測像素上限。Windows以原生mklink建立junction，確定store拒絕跨根發布；其他平台使用symbolic link。建立link失敗會明確失敗而非靜默跳過。受控來源注入EndOfStreamException，斷言同一原始IO保留且不產生候選檔。
使用邊界與維護：Image、來源、store與暫存根皆由單案owner釋放；link先刪除link自身，再刪自己的空目標根，不遞迴穿越。所有上傳Task必須await，沒有未追蹤背景工作或正式資料。這些離線故障與資源斷言不能當VM、公開網域或LINE驗收。
*/
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SpeechMessage.Assets.Tests;

/// <summary>以真解碼、檔案連結及原始故障驗證公開素材的邊界，不使用假store或直接比對實作字串。</summary>
public sealed class PublicAssetBoundaryTests
{
    /// <summary>JPEG的Orientation=6應先順時針校正再裁切，輸出EXIF移除，不能把方向錯誤當成metadata安全。</summary>
    [Fact]
    public async Task PutLogo_JpegOrientationIsAppliedBeforeMetadataRemoval()
    {
        await using var fixture = new AssetFixture();
        using var image = new Image<Rgba32>(32, 32, Color.Red);
        for (var y = 16; y < 32; y++) for (var x = 0; x < 32; x++) image[x, y] = Color.Blue;
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        using var source = new MemoryStream(); await image.SaveAsJpegAsync(source); source.Position = 0;
        var result = await fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default);
        using var output = await Image.LoadAsync<Rgba32>(fixture.File(result));
        Assert.True(output[64, 320].B > output[64, 320].R);
        Assert.True(output[576, 320].R > output[576, 320].B);
        Assert.Null(output.Metadata.ExifProfile);
    }

    /// <summary>壓縮文字不得流入公開PNG，透明像素則必須保留；輸入200KiB合成文字不等於允許無界解壓。</summary>
    [Fact]
    public async Task PutLogo_PngTextIsRemovedWhileAlphaIsPreserved()
    {
        await using var fixture = new AssetFixture();
        using var image = new Image<Rgba32>(32, 32, new Rgba32(23, 41, 60, 96));
        image.Metadata.GetPngMetadata().TextData.Add(new PngTextData("Comment", new string('x', 200 * 1024), "", ""));
        using var source = new MemoryStream();
        await image.SaveAsync(source, new PngEncoder { TextCompressionThreshold = 0 }); source.Position = 0;
        var result = await fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default);
        using var output = await Image.LoadAsync<Rgba32>(fixture.File(result));
        Assert.Empty(output.Metadata.GetPngMetadata().TextData);
        Assert.InRange(output[320, 320].A, (byte)95, (byte)97);
    }

    /// <summary>兩邊都合法但總像素超過40M仍必須拒絕；真壓縮PNG僅作尺寸測試，不要求完整解碼後才拒絕。</summary>
    [Fact]
    public async Task PutLogo_CombinedPixelLimitRejectsBeforePublication()
    {
        await using var fixture = new AssetFixture();
        using var image = new Image<Rgba32>(8000, 5001, Color.White);
        using var source = new MemoryStream(); await image.SaveAsPngAsync(source); source.Position = 0;
        await Assert.ThrowsAsync<AssetRejectedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>產品子目錄若被換成連結，不能在外部目標寫入；測試先解除link再清自己的外部根，不跟隨連結遞迴刪除。</summary>
    [Fact]
    public async Task PutLogo_ReparseProductDirectoryCannotEscapeDedicatedRoot()
    {
        await using var fixture = new AssetFixture();
        var outside = Path.Combine(Path.GetTempPath(), "3p-f7-outside-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(fixture.Root, "GoodShepherd2");
        Directory.CreateDirectory(outside);
        try
        {
            await CreateDirectoryLinkAsync(link, outside);
            using var source = AssetFixture.Source(Color.Red);
            await Assert.ThrowsAsync<IOException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default));
            Assert.Empty(Directory.GetFileSystemEntries(outside));
        }
        finally
        {
            if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(link);
            Directory.Delete(outside, recursive: false);
        }
    }

    /// <summary>借入來源的EOF例外可能是傳輸中斷，必須保留同一真Exception，不能以解碼例外catch掩蓋為壞圖片。</summary>
    [Fact]
    public async Task PutLogo_SourceEndOfStreamRemainsTheOriginalIoFailure()
    {
        await using var fixture = new AssetFixture();
        var failure = new EndOfStreamException("synthetic-upstream-failure");
        using var source = new ControlledImageStream([], failure: failure);
        Assert.Same(failure, await Assert.ThrowsAsync<EndOfStreamException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default)));
        Assert.False(source.WasDisposed);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>建立不需符號連結權限的Windows junction；程序及輸出由本案有界擁有，逾時先終止排空再釋放。</summary>
    private static async Task CreateDirectoryLinkAsync(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new ProcessStartInfo("cmd.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(value);
        using var process = new Process { StartInfo = start };
        Assert.True(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(stdout, stderr);
        }
        Assert.Equal(0, process.ExitCode);
    }
}
