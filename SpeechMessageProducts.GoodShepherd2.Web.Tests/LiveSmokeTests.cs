/*
檔案整體說明
目的與定位：F2 測試環境專屬的公開就緒及 LINE T01 Webhook 設定／測試入口，與一般離線回歸以 Live3P、WP=F2 分開。
主要功能與流程：只存取固定 gs2-test HTTPS 站台及官方 LINE API；T01 的交接設定只從指定 User Secrets 讀取，缺項立即失敗，不改用產品 Channel。設定 Webhook 後呼叫 webhook/test，解析有界回應的 success；另確認公開 live／ready 都回 200。
使用邊界與維護：只有取得測試環境鎖且準備事項齊全後才執行本分類。禁止三個產品 Channel ID 作為 T01，不讀 AdminUserId、不推播、不存任何 token。每案擁有組態、無 cookie／無 redirect 的 HttpClient、request／response／JSON，透過 using 完整釋放；每個 HTTP 十秒、回應最多六十四 KiB，沒有重試或遺棄工作。輸出只使用固定斷言，不複製 token 或供應商本文；一般測試排除此分類，未執行時必須明標實機待補。
*/
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests;

/// <summary>僅供已準備且持鎖的 F2 測試環境使用；不以跳過、替身或零案例冒稱實機成功。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "F2")]
public sealed class LiveSmokeTests
{
    /// <summary>固定測試站必須以有效 TLS 回傳存活及真實 D365 ready；不跟隨轉址或略過憑證。</summary>
    [Fact]
    public async Task Live_TestSite_LiveAndReadyReturn200()
    {
        using var client = Client();
        foreach (var path in new[] { "health/live", "health/ready" })
        {
            using var response = await client.GetAsync(new Uri("https://gs2-test.speechmessage.com.tw/" + path));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>只設定交接中明列的 T01，禁止替代為產品 Channel；API 成功後必須取得 webhook/test success=true。</summary>
    [Fact]
    public async Task Live_T01_SetWebhookAndTest_Succeeds()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("SpeechMessage.ThreeProducts.Handoff", reloadOnChange: false).Build();
        using var owner = (IDisposable)configuration;
        var channelId = configuration["ThreeProducts:Line:T01:ChannelId"];
        var token = configuration["ThreeProducts:Line:T01:ChannelAccessToken"];
        var secret = configuration["ThreeProducts:Line:T01:ChannelSecret"];
        if (string.IsNullOrWhiteSpace(channelId) || channelId.Length > 32 ||
            channelId.Any(character => character is < '0' or > '9') ||
            string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("F2 Live3P requires the dedicated T01 handoff.");
        foreach (var product in new[] { "GoodShepherd2", "GoodButler", "GoodGuild" })
        {
            if (string.Equals(channelId, configuration[$"ThreeProducts:Line:{product}:ChannelId"], StringComparison.Ordinal))
                throw new InvalidOperationException("F2 Live3P cannot use a product channel as T01.");
        }
        if (string.Equals(channelId, "1548216924", StringComparison.Ordinal))
            throw new InvalidOperationException("F2 Live3P cannot use the registered GoodShepherd2 channel.");

        using var client = Client();
        var endpoint = "https://gs2-test.speechmessage.com.tw/webhook/" + channelId;
        using var setRequest = new HttpRequestMessage(HttpMethod.Put,
            "https://api.line.me/v2/bot/channel/webhook/endpoint");
        setRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        setRequest.Content = JsonContent.Create(new { endpoint });
        using var setResponse = await client.SendAsync(setRequest);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);
        using var testRequest = new HttpRequestMessage(HttpMethod.Post,
            "https://api.line.me/v2/bot/channel/webhook/test");
        testRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        testRequest.Content = JsonContent.Create(new { endpoint });
        using var testResponse = await client.SendAsync(testRequest);
        Assert.Equal(HttpStatusCode.OK, testResponse.StatusCode);
        using var result = JsonDocument.Parse(await testResponse.Content.ReadAsByteArrayAsync());
        Assert.True(result.RootElement.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.True, "LINE webhook/test did not report success.");
    }

    /// <summary>每案建立無使用者狀態的唯一傳輸 owner，限制整個回應緩衝與操作時間；呼叫端 using 釋放。</summary>
    /// <returns>不含部署 token 的短生命週期 client，Authorization 只能放單次 request。</returns>
    private static HttpClient Client() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 64 * 1024
    };
}
