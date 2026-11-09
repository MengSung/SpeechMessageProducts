/*
檔案整體說明
目的與定位：保護階段三 Webhook 的真 HTTP 契約，以真正 Kestrel／Host 生命週期驗證產品組合，不使用額外 TestServer 套件。
主要功能與流程：只綁定本機 ephemeral port，向新端點送合成事件並檢查狀態。首輪產品尚未映射路由，預期測試因應有 200 卻得到 404 而失敗；此證據不以編譯錯誤替代功能紅燈。
使用邊界與維護：不讀秘密、不連 LINE、SQL 或 D365；測試 owner 使用 using／await using 依序關閉 HTTP client 及 Host。事件、HTTP 狀態及日後故障注入只屬目前案例，不能保存跨案例可變狀態。
*/
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>真本機 HTTP 探針；每個案例唯一 Host 與 client 保護端點生命週期。</summary>
public sealed class WebhookHostTests
{
    /// <summary>以合成空事件要求新路由回成功；移除 MapLineWebhook 將使此斷言以 404 明確失敗。</summary>
    [Fact]
    public async Task Webhook_AcceptsVerifiedEvents_200()
    {
        await using var host = new WebhookTestHost();
        await host.StartAsync();
        const string body = "{\"events\":[{\"type\":\"follow\",\"webhookEventId\":\"one\"}]}";
        using var response = await host.PostAsync(body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Encoding.UTF8.GetBytes(body), host.Signature.LastBody);
        Assert.Equal("one", Assert.Single(host.Queue.Events).WebhookEventId);
        Assert.Equal(0, host.Dedupe.Active);
        Assert.Equal(1, host.Dedupe.DisposedLeases);
    }
}
