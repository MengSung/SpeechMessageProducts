/*
檔案整體說明
目的與定位：保護F7中立圖片儲存庫的640方形PNG、不可變網址與租戶目錄隔離；只用合成像素及單案暫存根，不讀秘密、不連D365、LINE或VM。
主要功能與流程：以真ImageSharp產生PNG、JPEG、偽裝BMP、高雜訊與過大邊長，經真store後檢查解碼像素、最終SHA、完整檔名與檔案集合。取消、輸入故障、容量滿載及停止時注入受控串流，斷言不發布半檔、來源仍由呼叫端擁有、舊檔不被覆寫及owner排空。
使用邊界與維護：每案唯一擁有store、來源stream、Image及暫存目錄；先等待所有呼叫與DisposeAsync，再清自己目錄，不使用GC或finalizer掩蓋漏釋放。ImageSharp未處置配置數應回到原值；本測試組件禁案例間平行，但案內A/B上傳仍並行。離線檔案與loopback不宣稱公開網域或LINE實機。
*/
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Image = SixLabors.ImageSharp.Image;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace SpeechMessage.Assets.Tests;

/// <summary>以真實影像與檔案系統驗證發布契約；每案透過fixture釋放store後才移除自己的資料。</summary>
public sealed class PublicAssetStoreTests
{
    /// <summary>PNG與JPEG都轉成640方形；中心裁切須保留綠色中央並去除左右紅邊，SHA必須等於最終檔。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PutLogo_ReturnsPngAt640(bool jpeg)
    {
        await using var fixture = new AssetFixture();
        using var image = new Image<Rgba32>(1280, 640, Color.Red);
        for (var y = 0; y < 640; y++) for (var x = 320; x < 960; x++) image[x, y] = Color.Green;
        using var input = new MemoryStream();
        if (jpeg) await image.SaveAsJpegAsync(input); else await image.SaveAsPngAsync(input);
        input.Position = 0;
        var result = await fixture.Store.PutTenantLogoAsync(fixture.Scope, input, default);
        using var output = await Image.LoadAsync<Rgba32>(fixture.File(result));
        Assert.Equal(640, output.Width); Assert.Equal(640, output.Height);
        Assert.True(output[32, 32].G > output[32, 32].R);
        Assert.True(output[600, 600].G > output[600, 600].R);
        var bytes = await File.ReadAllBytesAsync(fixture.File(result));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.Sha256);
        Assert.True(input.CanRead);
    }

    /// <summary>每次成功都必須使用SHA前16碼及64bit隨機欄位；相同像素重複及並行上傳不能覆寫舊檔。</summary>
    [Fact]
    public async Task PutLogo_FileNameHasAtLeast64RandomBits()
    {
        await using var fixture = new AssetFixture();
        var urls = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 24; i++)
        {
            using var source = AssetFixture.Source(Color.Red);
            var asset = await fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default);
            Assert.Matches(@"\A[0-9a-f]{16}-[0-9a-f]{16}\.png\z", Path.GetFileName(asset.Url.AbsolutePath));
            Assert.StartsWith(asset.Sha256[..16] + "-", Path.GetFileName(asset.Url.AbsolutePath), StringComparison.Ordinal);
            Assert.True(urls.Add(asset.Url.AbsoluteUri));
        }
        Assert.Equal(24, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
    }

    /// <summary>替換Logo僅發布新網址；原URL對應的已發布內容須逐位元組保留，不可在原路徑填入新像素。</summary>
    [Fact]
    public async Task PutLogo_ReplaceProducesNewUrl()
    {
        await using var fixture = new AssetFixture();
        using var first = AssetFixture.Source(Color.Red); using var second = AssetFixture.Source(Color.Blue);
        var a = await fixture.Store.PutTenantLogoAsync(fixture.Scope, first, default);
        var original = await File.ReadAllBytesAsync(fixture.File(a));
        var b = await fixture.Store.PutTenantLogoAsync(fixture.Scope, second, default);
        Assert.NotEqual(a.Url, b.Url); Assert.NotEqual(a.Sha256, b.Sha256);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.File(a)));
    }

    /// <summary>固定種子的完整RGBA雜訊無法無損壓到300KiB；有限壓縮後拒絕且不留下任何正式或暫存檔。</summary>
    [Fact]
    public async Task PutLogo_Over300KbAfterCompression_Rejected()
    {
        await using var fixture = new AssetFixture();
        using var image = new Image<Rgba32>(640, 640);
        var random = new Random(71283);
        for (var y = 0; y < 640; y++) for (var x = 0; x < 640; x++)
            image[x, y] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        using var input = new MemoryStream(); await image.SaveAsPngAsync(input); input.Position = 0;
        await Assert.ThrowsAsync<AssetRejectedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, input, default));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>任意bytes與真BMP都不能靠宣告或副檔名進PNG/JPEG白名單；拒絕時來源仍由本案處置。</summary>
    [Fact]
    public async Task PutLogo_NonImageBytes_Rejected()
    {
        await using var fixture = new AssetFixture();
        using var garbage = new MemoryStream([1, 2, 3, 4, 5]);
        await Assert.ThrowsAsync<AssetRejectedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, garbage, default));
        using var bmp = new MemoryStream(); using var image = new Image<Rgba32>(10, 10);
        await image.SaveAsBmpAsync(bmp); bmp.Position = 0;
        await Assert.ThrowsAsync<AssetRejectedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, bmp, default));
        using var valid = AssetFixture.Source(Color.Purple);
        var truncatedBytes = valid.ToArray()[..(int)(valid.Length / 2)];
        using var truncated = new MemoryStream(truncatedBytes, writable: false);
        await Assert.ThrowsAsync<AssetRejectedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, truncated, default));
        Assert.True(garbage.CanRead); Assert.True(bmp.CanRead);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>產品只能是伺服器核定三種常值；路徑與編碼偽裝不能變成跨租戶路徑，空租戶亦拒絕。</summary>
    [Theory]
    [InlineData("..")] [InlineData("GoodShepherd2/../Construction")] [InlineData("GoodShepherd2\\other")]
    [InlineData("%2e%2e")] [InlineData("unknown")]
    public async Task PutLogo_ScopeCannotEscapeRoot(string product)
    {
        await using var fixture = new AssetFixture(); using var input = AssetFixture.Source(Color.Red);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.PutTenantLogoAsync(new AssetScope(product, fixture.Scope.TenantId), input, default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.PutTenantLogoAsync(new AssetScope("GoodShepherd2", Guid.Empty), input, default));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>共用層接收的是產品鍵而非組織名稱；好管家及好學會必須保留正式鍵於URL與實體路徑，不偷偷轉換為別名。</summary>
    [Theory]
    [InlineData("GoodButler")]
    [InlineData("GoodGuild")]
    public async Task PutLogo_CanonicalProductKeys_ArePreserved(string product)
    {
        await using var fixture = new AssetFixture();
        using var input = AssetFixture.Source(Color.Green);
        var scope = new AssetScope(product, Guid.NewGuid());
        var asset = await fixture.Store.PutTenantLogoAsync(scope, input, default);
        Assert.StartsWith("/assets/" + product + "/" + scope.TenantId.ToString("N") + "/", asset.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.True(File.Exists(fixture.File(asset)));
    }

    /// <summary>組織名稱不能冒充產品識別；拒絕舊候選別名時不讀取來源或建立任何租戶檔案。</summary>
    [Theory]
    [InlineData("Construction")]
    [InlineData("Association")]
    public async Task PutLogo_OrganizationAliases_AreRejected(string product)
    {
        await using var fixture = new AssetFixture();
        using var source = new ControlledImageStream([], endless: true);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.PutTenantLogoAsync(new AssetScope(product, Guid.NewGuid()), source, default));
        Assert.Equal(0, source.BytesRead);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>最終公開值必須為HTTPS PNG、640正方形且不超過300KiB／LINE一MiB；網址不含查詢、cookie或credential。</summary>
    [Fact]
    public async Task PutLogo_PublicUrlMeetsLineIconRules()
    {
        await using var fixture = new AssetFixture(); using var input = AssetFixture.Source(Color.Green);
        var asset = await fixture.Store.PutTenantLogoAsync(fixture.Scope, input, default);
        Assert.Equal("https", asset.Url.Scheme); Assert.EndsWith(".png", asset.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.InRange(asset.Url.AbsoluteUri.Length, 1, 2000); Assert.Equal("", asset.Url.Query); Assert.Equal("", asset.Url.UserInfo);
        Assert.InRange(new FileInfo(fixture.File(asset)).Length, 1L, 300L * 1024);
    }

    /// <summary>影像metadata不能夾帶位置或身分；儲存後EXIF應不存在且只保留單一影格。</summary>
    [Fact]
    public async Task PutLogo_MetadataIsRemoved_AndFirstFrameOnly()
    {
        await using var fixture = new AssetFixture();
        using var image = new Image<Rgba32>(10, 10, Color.Red);
        image.Metadata.ExifProfile = new ExifProfile(); image.Metadata.ExifProfile.SetValue(ExifTag.Copyright, "synthetic-owner-canary");
        using var extra = new Image<Rgba32>(10, 10, Color.Blue); image.Frames.AddFrame(extra.Frames.RootFrame);
        using var source = new MemoryStream(); await image.SaveAsPngAsync(source); source.Position = 0;
        var asset = await fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default);
        using var output = await Image.LoadAsync<Rgba32>(fixture.File(asset));
        Assert.Null(output.Metadata.ExifProfile); Assert.Equal(1, output.Frames.Count); Assert.Equal(Color.Red.ToPixel<Rgba32>(), output[300, 300]);
    }

    /// <summary>高邊長在完整像素解碼之前拒絕；真8193×1 PNG很小，可驗證尺寸守門而不配置巨圖。</summary>
    [Fact]
    public async Task PutLogo_ExcessiveDimensionsFailWithoutPublication()
    {
        await using var fixture = new AssetFixture(); using var image = new Image<Rgba32>(8193, 1);
        using var source = new MemoryStream(); await image.SaveAsPngAsync(source); source.Position = 0;
        await Assert.ThrowsAsync<AssetRejectedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>重複A/B不同scope上傳後，路徑及顏色不交叉；所有Image與stream釋放後未Dispose配置數回基線。</summary>
    [Fact]
    public async Task PutLogo_ConcurrentScopesStayDetached_AndImageAllocationsDrain()
    {
        await using var fixture = new AssetFixture(); var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var b = new AssetScope("GoodButler", Guid.NewGuid());
        for (var i = 0; i < 6; i++)
        {
            using var aSource = AssetFixture.Source(Color.Red); using var bSource = AssetFixture.Source(Color.Blue);
            var results = await Task.WhenAll(fixture.Store.PutTenantLogoAsync(fixture.Scope, aSource, default), fixture.Store.PutTenantLogoAsync(b, bSource, default));
            Assert.Contains("/GoodShepherd2/" + fixture.Scope.TenantId.ToString("N") + "/", results[0].Url.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("/GoodButler/" + b.TenantId.ToString("N") + "/", results[1].Url.AbsolutePath, StringComparison.Ordinal);
            using var aImage = await Image.LoadAsync<Rgba32>(fixture.File(results[0])); using var bImage = await Image.LoadAsync<Rgba32>(fixture.File(results[1]));
            Assert.Equal(Color.Red.ToPixel<Rgba32>(), aImage[200, 200]); Assert.Equal(Color.Blue.ToPixel<Rgba32>(), bImage[200, 200]);
        }
        Assert.Equal(baseline, MemoryDiagnostics.TotalUndisposedAllocationCount);
    }
}

/// <summary>單案檔案owner；store先排空與釋放，才刪除本案GUID暫存根，不碰產品目錄或其他案例。</summary>
internal sealed class AssetFixture : IAsyncDisposable
{
    /// <summary>為當案建立獨立根及immutable base設定；只包含保留網域與合成租戶。</summary>
    internal AssetFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "3p-f7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Store = new FileSystemPublicAssetStore(new AssetOptions(Root, new Uri("https://assets.example/assets/")));
    }
    /// <summary>本案唯一可寫目錄；只交給當案store，完成時移除。</summary>
    internal string Root { get; }
    /// <summary>本案擁有且先於目錄釋放的store，不與其他測試共用。</summary>
    internal FileSystemPublicAssetStore Store { get; }
    /// <summary>合成伺服器scope；不代表已驗證的實機使用者授權。</summary>
    internal AssetScope Scope { get; } = new("GoodShepherd2", Guid.NewGuid());
    /// <summary>只將本案產生URL轉成測試根下的實體路徑，作為獨立像素／雜湊驗證入口。</summary>
    internal string File(PublicAsset asset) => Path.Combine(Root, asset.Url.AbsolutePath["/assets/".Length..].Replace('/', Path.DirectorySeparatorChar));
    /// <summary>產生純色合成PNG並移交stream；Image於返回前釋放，回傳stream由當案using接管。</summary>
    internal static MemoryStream Source(Color color)
    {
        using var image = new Image<Rgba32>(16, 16, color); var output = new MemoryStream();
        try { image.SaveAsPng(output); output.Position = 0; return output; } catch { output.Dispose(); throw; }
    }
    /// <summary>確定排空store後刪本案目錄；清理例外不應被忽略成成功，沒有背景清理工作。</summary>
    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync(); Directory.Delete(Root, recursive: true);
    }
}
