/*
檔案整體說明
目的與定位：F11 在固定 gs2-test 測試站的唯讀實機入口，確認發布後的四項就緒彙總與 HTTP correlation 管線，不以舊 F2 的空白 200 冒稱新版部署完成。
主要功能與流程：只 GET 固定 HTTPS 的 live／ready，保留 TLS 驗證並禁止轉址及 Cookie。ready 必須 200、no-store、空本文及原樣安全 correlation；另一案以含句點的不合法 header 驗證完整替換，live 仍無 I/O 回 200。類別 Live3P 與 WP=F11 只在明確篩選時執行。
使用邊界與維護：每案唯一擁有短生命週期 HttpClient、handler、request 與 response，using 確定釋放；十五秒期限及六十四 KiB 緩衝，無重試或背景工作。測試不讀 Secrets、不改 Channel、不建租戶、不發送 LINE，也不能代替 VM 排程註冊、D365 發送紀錄或停機排空證據。驗收前須依 02A 確認部署已完成且沒有另一線在寫同資源；失敗只輸出固定契約，不輸出供應商本文或部署機密。
*/
using System.Net;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>只讀核定測試站，藉新版獨有回應契約辨認部署，不將匿名存活解讀成業務或租戶授權。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "F11")]
public sealed class ObservabilityLiveTests
{
    /// <summary>所有四項真 probe 完成才可 200；新版 no-store 及安全 correlation 必須同時存在，本文及 Cookie 均不得夾帶資料。</summary>
    [Fact]
    public async Task Live_FourProbeReadiness_HasSafeEmptyResponseAndCorrelation()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://gs2-test.speechmessage.com.tw/health/ready");
        var correlation = "F11-live-" + Guid.NewGuid().ToString("N");
        request.Headers.Add("X-Correlation-Id", correlation);
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, "F11 live readiness did not pass all four probes.");
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values), "F11 correlation middleware was not observed.");
        Assert.Equal(correlation, Assert.Single(values!));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>帶句點的 header 不符合唯一 ASCII 字母／數字／減號契約；必須新生安全值且不反射任何原文，存活端點仍回 200。</summary>
    [Fact]
    public async Task Live_InvalidCorrelationHeader_IsReplacedOnLiveEndpoint()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://gs2-test.speechmessage.com.tw/health/live");
        request.Headers.Add("X-Correlation-Id", "invalid.synthetic.value");
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, "F11 live endpoint did not return its fixed success response.");
        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values), "F11 correlation middleware was not observed.");
        var actual = Assert.Single(values!);
        Assert.InRange(actual.Length, 1, 64);
        Assert.All(actual, character => Assert.True(char.IsAsciiLetterOrDigit(character) || character == '-'));
        Assert.NotEqual("invalid.synthetic.value", actual);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>每案固定有界傳輸且不接受替代網域或憑證忽略，呼叫端 using 連同 handler 關閉所有 stream／connection owner。</summary>
    private static HttpClient CreateClient() => new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = 64 * 1024
    };
}
