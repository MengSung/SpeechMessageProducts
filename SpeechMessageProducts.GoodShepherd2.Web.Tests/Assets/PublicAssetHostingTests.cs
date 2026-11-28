/*
檔案整體說明
目的與定位：F7公開圖片HTTP與Program接線的離線整合驗證，使用真Kestrel、真store與合成PNG，不連VM、SQL、D365或LINE，不讀秘密。
主要功能與流程：每案先在自己的GUID根產生合法公開Logo，並以真Host確認素材啟用不影響health/live及correlation；啟動只監聽loopback的Development主機，以GET／HEAD驗證PNG、immutable及無cookie；缺設定、目錄、其他副檔名與錯誤路由皆不得公開。獨占鎖住正式檔或將產品目錄換成真junction注入IOException，斷言固定500與共用安全日誌；關閉主機後驗證DI擁有的store已停止接單。
使用邊界與維護：F2Host collection隔離全域diagnostics owner。每案自行擁有client、Host、組態、檔案鎖及根；client先釋放，Host Stop排空後Dispose，最後才刪本案根。建立失敗亦確定釋放，不以離線HTTP或合成租戶代替F7 Step4b公開HTTPS與F5發送驗收。
*/
using System.Net;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SpeechMessage.Assets;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Assets;

/// <summary>以真HTTP驗證實際Program註冊，不直接呼叫未接入主機的handler充當成功。</summary>
[Collection("F2Host")]
public sealed class PublicAssetHostingTests
{
    /// <summary>公開素材startup filter不能攔截非素材端點；以真Kestrel重現部署存活檢查的完整路由，避免只測assets成功而漏掉health404。</summary>
    [Fact]
    public async Task Assets_Enabled_LivenessAndCorrelationRemainAvailable()
    {
        await using var fixture = await AssetHostFixture.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "F7-live-offline");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("F7-live-offline", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>GET返回最終PNG原bytes且HEAD只有標頭；兩者皆immutable及nosniff，不建立Session或cookie。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assets_GetAndHead_ReturnOnlyPublishedPng(bool head)
    {
        await using var fixture = await AssetHostFixture.StartAsync();
        using var request = new HttpRequestMessage(head ? HttpMethod.Head : HttpMethod.Get, fixture.Asset.Url.AbsolutePath);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(fixture.Png.LongLength, response.Content.Headers.ContentLength);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (head) Assert.Empty(bytes); else Assert.Equal(fixture.Png, bytes);
    }

    /// <summary>共享測試網域須依原產品鍵提供已發布Logo；以真store及HTTP證明GoodButler／GoodGuild不被當作組織別名拒絕。</summary>
    [Theory]
    [InlineData("GoodButler")]
    [InlineData("GoodGuild")]
    public async Task Assets_CanonicalProductPaths_ReturnPublishedBytes(string product)
    {
        await using var fixture = await AssetHostFixture.StartAsync(product: product);
        using var response = await fixture.Client.GetAsync(fixture.Asset.Url.AbsolutePath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fixture.Png, await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>正式PNG以外沒有可列舉或讀取的介面；包含穿越、暫存、副檔名偽裝及未知產品皆固定404。</summary>
    [Theory]
    [InlineData("/assets/")]
    [InlineData("/assets/GoodShepherd2/")]
    [InlineData("/assets/GoodShepherd2/00000000000000000000000000000000/a.png")]
    [InlineData("/assets/../private.txt")]
    [InlineData("/assets/GoodShepherd2/%2e%2e%2fprivate.png")]
    [InlineData("/assets/GoodShepherd2/{tenant}/logo.jpg")]
    [InlineData("/assets/GoodShepherd2/{tenant}/.upload-abc.tmp")]
    [InlineData("/assets/unknown/{tenant}/{name}")]
    [InlineData("/assets/Construction/{tenant}/{name}")]
    [InlineData("/assets/Association/{tenant}/{name}")]
    [InlineData("/assets/GoodShepherd2/{tenant}/0000000000000000-0000000000000000.png")]
    public async Task Assets_DirectoryAndInvalidPaths_AreNotPublished(string path)
    {
        await using var fixture = await AssetHostFixture.StartAsync();
        path = path.Replace("{tenant}", fixture.Scope.TenantId.ToString("N"), StringComparison.Ordinal)
            .Replace("{name}", Path.GetFileName(fixture.Asset.Url.AbsolutePath), StringComparison.Ordinal);
        using var response = await fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>部署未啟用時不註冊store或建立替代資料夾；既有主機仍可啟動，磁碟存在的圖片亦不可意外公開。</summary>
    [Fact]
    public async Task Assets_MissingConfiguration_RemainsDisabled()
    {
        await using var fixture = await AssetHostFixture.StartAsync(configured: false);
        Assert.Null(fixture.App.Services.GetService<IPublicAssetStore>());
        using var response = await fixture.Client.GetAsync(fixture.Asset.Url.AbsolutePath);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>正式檔的分享違規是真IOException；分支必須交同一diagnostics且只回空500，不回例外本文或私有實體路徑。</summary>
    [Fact]
    public async Task Assets_FileReadFailure_UsesSharedDiagnostics()
    {
        await using var fixture = await AssetHostFixture.StartAsync();
        using var exclusive = new FileStream(fixture.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var response = await fixture.Client.GetAsync(fixture.Asset.Url.AbsolutePath);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        var log = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Logs", "Exception.log"));
        Assert.Contains("IOException", log, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.FilePath, log, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Scope.TenantId.ToString("N"), log, StringComparison.Ordinal);
    }

    /// <summary>合法URL即使仍指向同一圖片，也不能透過換成junction的產品目錄讀取；本案擁有原目錄及有界子程序，finally先刪link再恢復目錄，Host最後排空。</summary>
    [Fact]
    public async Task Assets_ReplacedProductDirectoryWithJunction_IsRejectedBeforePublication()
    {
        await using var fixture = await AssetHostFixture.StartAsync();
        var product = Path.Combine(fixture.Root, "public-assets", "GoodShepherd2");
        var moved = Path.Combine(fixture.Root, "moved-product");
        Directory.Move(product, moved);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var start = new ProcessStartInfo("cmd.exe")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var value in new[] { "/c", "mklink", "/J", product, moved }) start.ArgumentList.Add(value);
                using var process = new Process { StartInfo = start };
                Assert.True(process.Start());
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                finally
                {
                    if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                    await Task.WhenAll(stdout, stderr);
                }
                Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(product, moved);
            using var response = await fixture.Client.GetAsync(fixture.Asset.Url.AbsolutePath);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var log = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Logs", "Exception.log"));
            Assert.Contains("IOException", log, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Root, log, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Scope.TenantId.ToString("N"), log, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(product) && (File.GetAttributes(product) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(product);
            Directory.Move(moved, product);
        }
    }

    /// <summary>容器持有唯一store owner；停止及Dispose後借用參照不能再上傳，所有來源仍由本案using釋放。</summary>
    [Fact]
    public async Task Assets_HostDisposal_StopsTheRegisteredStore()
    {
        await using var fixture = await AssetHostFixture.StartAsync();
        var store = fixture.App.Services.GetRequiredService<IPublicAssetStore>();
        await fixture.StopHostAsync();
        using var source = new MemoryStream(fixture.Png, writable: false);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.PutTenantLogoAsync(fixture.Scope, source, default));
    }

    /// <summary>固定/assets分支不能發布其他基底路徑或帶credential的網址；錯誤部署必須在啟動時拒絕，不能發布永遠404的URL。</summary>
    [Theory]
    [InlineData("https://assets.example/")]
    [InlineData("https://assets.example/img/")]
    [InlineData("https://assets.example/assets/?secret=x")]
    [InlineData("https://user:password@assets.example/assets/")]
    public async Task Assets_InvalidPublicBase_FailsStartup(string baseUrl)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        { await using var fixture = await AssetHostFixture.StartAsync(publicBase: baseUrl); });
    }
}

/// <summary>單案Host及檔案owner；沒有隱藏重試、外部連線或跨案快取，失敗建立也會完整釋放。</summary>
internal sealed class AssetHostFixture : IAsyncDisposable
{
    private bool _stopped;
    /// <summary>接管已啟動的Host及client；所有參數皆由當案建立，不接受產品秘密或外部目錄。</summary>
    private AssetHostFixture(string root, WebApplication app, HttpClient client, AssetScope scope, PublicAsset asset, string filePath, byte[] png)
    { Root = root; App = app; Client = client; Scope = scope; Asset = asset; FilePath = filePath; Png = png; }
    /// <summary>當案唯一根，Host contentroot與公開素材各有固定子目錄；最後確定清除。</summary>
    internal string Root { get; }
    /// <summary>當案唯一Kestrel及DI owner，不與其他測試共用。</summary>
    internal WebApplication App { get; }
    /// <summary>僅指向loopback且十秒逾時，先於Host釋放。</summary>
    internal HttpClient Client { get; }
    /// <summary>合成權威GUID及固定產品，只供獨立目錄與路由測試。</summary>
    internal AssetScope Scope { get; }
    /// <summary>真store已發布的公開純值，不保存來源或handle。</summary>
    internal PublicAsset Asset { get; }
    /// <summary>只供本案分享違規注入的正式檔路徑，不輸出至日誌。</summary>
    internal string FilePath { get; }
    /// <summary>本案已發布PNG的獨立bytes，用於HTTP內容斷言，fixture釋放後不再使用。</summary>
    internal byte[] Png { get; }

    /// <summary>建立獨立圖片及只有記憶體合成設定的Program；不讀appsettings、User Secrets或程序環境。</summary>
    internal static async Task<AssetHostFixture> StartAsync(bool configured = true, string publicBase = "https://assets.example/assets/", string product = "GoodShepherd2")
    {
        var root = Path.Combine(Path.GetTempPath(), "3p-f7-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var assets = Path.Combine(root, "public-assets");
        var scope = new AssetScope(product, Guid.NewGuid());
        WebApplicationBuilder? builder = null;
        WebApplication? app = null;
        HttpClient? client = null;
        try
        {
            PublicAsset asset;
            await using (var store = new FileSystemPublicAssetStore(new AssetOptions(assets, new Uri("https://assets.example/assets/"))))
            {
                using var image = new Image<Rgba32>(12, 12, Color.Blue);
                using var source = new MemoryStream(); await image.SaveAsPngAsync(source); source.Position = 0;
                asset = await store.PutTenantLogoAsync(scope, source, default);
            }
            var file = Path.Combine(assets, asset.Url.AbsolutePath["/assets/".Length..].Replace('/', Path.DirectorySeparatorChar));
            var png = await File.ReadAllBytesAsync(file);
            builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { EnvironmentName = Environments.Development, ContentRootPath = root, ApplicationName = typeof(PublicAssetHostingTests).Assembly.FullName });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
                ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
                ["ExceptionNotifications:WriteExceptionLog"] = "true", ["ExceptionNotifications:SendLine"] = "false",
                ["ConnectionStrings:Platform"] = "Server=(localdb)\\MSSQLLocalDB;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true",
                ["LineWebhook:Queue:Capacity"] = "8", ["LineWebhook:Queue:PartitionCount"] = "1",
                ["PublicAssets:RootPath"] = configured ? assets : null,
                ["PublicAssets:PublicBaseUrl"] = configured ? publicBase : null
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            app = Program.BuildApplication(builder);
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            return new AssetHostFixture(root, app, client, scope, asset, file, png);
        }
        catch
        {
            client?.Dispose();
            try { if (app is not null) await app.DisposeAsync(); else if (builder is not null) ((IDisposable)builder.Configuration).Dispose(); }
            finally { Directory.Delete(root, recursive: true); }
            throw;
        }
    }

    /// <summary>關閉HTTP後由DI排空及處置store；可重複呼叫，確定Dispose即使Stop故障仍執行。</summary>
    internal async Task StopHostAsync()
    {
        if (_stopped) return;
        _stopped = true; Client.Dispose();
        try { await App.StopAsync(); } finally { await App.DisposeAsync(); }
    }
    /// <summary>確定停止、釋放Host後才刪本案根；不把清理失敗當成測試成功。</summary>
    public async ValueTask DisposeAsync()
    { try { await StopHostAsync(); } finally { Directory.Delete(Root, recursive: true); } }
}
