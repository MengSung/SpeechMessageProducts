/*
檔案整體說明
目的與定位：用真正 Kestrel 與產品組合根驗證 P3-05 滿載的 HTTP 503、claim rollback 及可重送交接，不修改 P3-02 既有 test host。
主要功能與流程：獨立 Host 將 queue 總容量設一，scoped handler 以 barrier 保持第一筆執行；第二筆必須因正式兩秒期限回 503，釋放第一筆後重送第二筆成功。只替換外部驗簽與 SQL，不替換產品 queue。
使用邊界與維護：每案例唯一 ephemeral localhost、HTTP client、Host 及有界合成集合。診斷原始證據僅寫本 task evidence；finally 釋放 barrier，先停止再 Dispose 主機。此離線 HTTP 測試不代表 LINE、SQL 或 D365 實機驗收，沒有部署憑證。
*/
using System.Net;
using System.Text;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>直接經產品端點及正式 queue 的 HTTP 契約，不使用既有 queue 替身。</summary>
public sealed class WebhookQueueHttpTests
{
    /// <summary>總容量包含執行中事件；滿載不得 Commit、必回 503 且可在前筆完成後重送成功。</summary>
    [Fact]
    public async Task Queue_Full_Returns503()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var dedupe = new DeduplicatorDouble();
        using var signature = new SignatureDouble();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln"))) root = root.Parent;
        var directory = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Worktree not found."),
            ".trellis", "tasks", "11-09-p3-05-bounded-queue", "evidence", "http", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        { EnvironmentName = "Testing", ContentRootPath = directory, ApplicationName = typeof(Program).Assembly.GetName().Name });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExceptionNotifications:WriteExceptionLog"] = "true", ["ExceptionNotifications:SendLine"] = "false",
            ["LineWebhook:Queue:Capacity"] = "1"
        });
        builder.Services.AddSingleton<IWebhookSignatureVerifier>(signature);
        builder.Services.AddSingleton<IWebhookEventDeduplicator>(dedupe);
        builder.Services.AddScoped<IWebhookEnvelopeHandler>(_ => new HttpHandler(entered, release, completed));
        await using var app = Program.BuildApplication(builder);
        try
        {
            await app.StartAsync();
            using var client = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
            using var first = await PostAsync(client, "one"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var full = await PostAsync(client, "two"); Assert.Equal(HttpStatusCode.ServiceUnavailable, full.StatusCode);
            Assert.Equal(0, dedupe.Active); Assert.Equal(2, dedupe.DisposedLeases);
            release.TrySetResult(); await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var retry = await PostAsync(client, "two"); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.Equal(3, dedupe.DisposedLeases);
            await app.StopAsync();
            Assert.False(File.Exists(Path.Combine(directory, "Logs", "Exception.log")));
        }
        finally { release.TrySetResult(); await app.StopAsync(); }
    }

    /// <summary>送出固定合成簽章及小型事件，request／content 確定釋放，response 交案例擁有。</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/line/webhook/123");
        request.Content = new StringContent("{\"events\":[{\"type\":\"follow\",\"webhookEventId\":\"" + id +
            "\",\"source\":{\"type\":\"user\",\"userId\":\"A\"}}]}", Encoding.UTF8, "application/json");
        request.Headers.Add("X-Line-Signature", "valid");
        return await client.SendAsync(request);
    }

    /// <summary>每事件獨立的 scoped barrier handler，不保存 envelope 或跨案例狀態。</summary>
    private sealed class HttpHandler(TaskCompletionSource entered, TaskCompletionSource release,
        TaskCompletionSource completed) : IWebhookEnvelopeHandler
    {
        /// <summary>第一筆固定阻塞，後續事件直接完成；真正取消會結束等待，測試 owner 不會漏 task。</summary>
        public async ValueTask HandleAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            if (envelope.WebhookEventId == "one")
            { entered.TrySetResult(); await release.Task.WaitAsync(cancellationToken); completed.TrySetResult(); }
        }
    }
}
