/*
檔案整體說明
目的與定位：F6在固定gs2-test測試站的唯讀實機驗收，與一般離線測試以Live3P及WP=F6分開，不讀秘密或借用手機身分。
主要功能與流程：以保留TLS驗證、禁止轉址與cookie的短期HTTP client，核對兩個LIFF靜態檔與本工作樹位元組雜湊相同，再確認公開config僅含核定app ID及Development旗標。匿名whoami與合成租戶ping必須拒絕且不發cookie，避免公開頁面200掩蓋設定遺失或資料端點開放。
使用邊界與維護：只GET固定測試網域，不設定Webhook、不呼叫LINE、不建立租戶或SQL資料；不以匿名拒絕冒稱真實登入或跨租戶驗收。每案唯一擁有client、handler、response與JSON，using確定釋放；HTTP十秒、回應六十四KiB，沒有重試、共享使用者狀態或遺棄工作。失敗只輸出固定契約及公開app識別，不輸出HTTP本文、token或LINE userId；手機與FS／F3資料驗收仍獨立待補。
*/
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Liff;

/// <summary>固定測試站的唯讀F6探針；使用真實HTTPS，不以離線替身或零案例代表實機通過。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "F6")]
public sealed class LiffLiveSmokeTests
{
    /// <summary>兩個公開檔必須與本線來源完全相同，公開設定必須是核定app而非空值或其他部署映射。</summary>
    [Fact]
    public async Task Live_Liff_PublicAssetsAndConfiguration_MatchAuthorizedDeployment()
    {
        using var client = CreateClient();
        var root = RepositoryRoot();
        foreach (var file in new[] { "whoami.html", "liff-router.js" })
        {
            using var response = await client.GetAsync("https://gs2-test.speechmessage.com.tw/liff/" + file);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var actual = await response.Content.ReadAsByteArrayAsync();
            var expected = await File.ReadAllBytesAsync(Path.Combine(root, "SpeechMessageProducts.GoodShepherd2.Web", "wwwroot", "liff", file));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), Convert.ToHexString(SHA256.HashData(actual)));
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
        using var settings = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root,
            "SpeechMessageProducts.GoodShepherd2.Web", "appsettings.Development.json")));
        var expectedId = settings.RootElement.GetProperty("Liff").GetProperty("LiffId").GetString();
        Assert.False(string.IsNullOrEmpty(expectedId));
        using var configuration = await client.GetAsync("https://gs2-test.speechmessage.com.tw/api/liff/config");
        Assert.Equal(HttpStatusCode.OK, configuration.StatusCode);
        using var json = JsonDocument.Parse(await configuration.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.Equal(expectedId, json.RootElement.GetProperty("liffId").GetString());
        Assert.True(json.RootElement.GetProperty("isDevelopment").GetBoolean());
        Assert.False(configuration.Headers.Contains("Set-Cookie"));
    }

    /// <summary>不帶任何token存取資料端點，真實filter必須回401且沒有Session／cookie；合成GUID不是授權來源。</summary>
    [Fact]
    public async Task Live_Liff_AnonymousDataRequests_AreUnauthorizedWithoutCookies()
    {
        using var client = CreateClient();
        foreach (var path in new[] { "whoami", "tenant/11111111-1111-1111-1111-111111111111/ping" })
        {
            using var response = await client.GetAsync("https://gs2-test.speechmessage.com.tw/api/liff/" + path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }

    /// <summary>由測試輸出向上找到本工作樹，僅讀核定公開來源；找不到就拒絕，不跨到整合工作樹找替代檔。</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("F6 test checkout was not found.");
    }

    /// <summary>每案獨立無cookie的傳輸owner，固定有界逾時與緩衝；呼叫端using連同handler完整釋放。</summary>
    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 64 * 1024
    };
}
