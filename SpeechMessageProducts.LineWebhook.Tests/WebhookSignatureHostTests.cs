/*
檔案整體說明

目的與定位：以真 Kestrel／Host 驗證 P3-03 的正式啟動守門；只使用合成部署設定，
保護空 secret channel 不會在未驗簽狀態下開始接收 HTTP。

主要功能與流程：省略 P3-02 的驗簽替身，讓產品 composition 接入正式 resolver／
verifier，再以空 secret mapping 啟動 Host。測試只斷言啟動拒絕及固定錯誤分類，
不讀實際 User Secrets、不送 LINE、不連 SQL、不建立公開端點。

使用邊界與維護：WebApplication、client 及測試設定由案例 await using／Host owner
釋放；錯誤訊息不得含 secret 值。通過只代表離線啟動契約成立，不代表 DMZ、IIS
或 LINE 平台實機驗收。
*/
using LineMessagingProcessor.AspNetCore.Webhooks;
using System.Net;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>驗證正式 Webhook Host 對空 secret 的 fail-closed 啟動契約。</summary>
public sealed class WebhookSignatureHostTests
{
    /// <summary>已配置 channel 若 secret 空白，Host 必須在綁定 HTTP 前拒絕啟動。</summary>
    [Fact]
    public async Task Webhook_EmptySecretChannel_RejectedAtStartup()
    {
        var configuration = WebhookSignatureTestHost.CreateConfiguration();
        configuration["Deployment:Organizations:Alpha:TestChannelSecret"] = "";
        await using var host = new WebhookSignatureTestHost(configuration);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.App.StartAsync());

        Assert.Contains("Webhook channel secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("goodshepherdback", error.Message, StringComparison.Ordinal);
    }

    /// <summary>正式驗簽器拒絕另一頻道的簽章，在解析、SQL 與 queue 前回 401 且不通知。</summary>
    [Fact]
    public async Task Webhook_InvalidSignature_401()
    {
        await using var host = new WebhookSignatureTestHost();
        await host.StartAsync();
        using var response = await host.PostAsync("{\"events\":[]}", "123", WebhookSignatureTestHost.BetaSecret);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(host.Queue.Events);
        Assert.Equal(0, host.Dedupe.DisposedLeases);
        Assert.Empty(host.ExceptionLog);
    }

    /// <summary>長度已知與 chunked 皆拒絕多出一 byte 的 body，正式驗簽組合不影響端點容量契約。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Webhook_BodyTooLarge_413(bool chunked)
    {
        await using var host = new WebhookSignatureTestHost();
        await host.StartAsync();
        using var response = await host.PostAsync("{\"events\":[]}".PadRight(WebhookOptions.MaximumBodyBytes + 1), chunked: chunked);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(host.Queue.Events);
        Assert.Equal(0, host.Dedupe.DisposedLeases);
        Assert.Empty(host.ExceptionLog);
    }
}
