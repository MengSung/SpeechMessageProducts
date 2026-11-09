/*
檔案整體說明
目的與定位：以真 Kestrel／Host 保護 Webhook 順序、啟動拒絕、channel 隔離及重送交接；三個替身僅替代外部服務，不是正式驗簽或 SQL 實作。
主要功能與流程：HTTP 驗證一 MB 邊界、驗簽、整批解析、逐事件去重、503 rollback、legacy 開關與原例外安全日誌；固定兩百筆並行事件驗證資源基線。正式 SQL／佇列自動註冊後，省略替身會分別由缺平台啟動快照或缺 handler 守門拒絕；測試精確區分缺項，不弱化為任意例外。
使用邊界與維護：不建立 Golden、不讀秘密、不連外；每個案例 await using 擁有 WebhookTestHost，response 及取消來源確定釋放。反射與 JSON clone 僅保護本包不可變快照，不宣稱正式 SQL 或背景分區已完成。
*/
using System.Net;
using System.Text;
using System.Text.Json;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>真 HTTP 契約及有界故障注入，每個案例的主機、事件集合及診斷 owner 均獨立。</summary>
public sealed class WebhookContractTests
{
    /// <summary>只讀合成 channel 白名單，不含租戶或使用者資料，案例不得修改。</summary>
    private static readonly string[] Channels = ["123", "456"];
    /// <summary>部分成功重試的固定期待值，只供斷言讀取，沒有可變業務狀態。</summary>
    private static readonly string[] CompletedIds = ["one", "two"];
    /// <summary>驗簽、缺標頭、多值標頭及未配置頻道在解析／SQL 前回 401，預期拒絕不通知。</summary>
    [Theory]
    [InlineData("invalid", "123")]
    [InlineData(null, "123")]
    [InlineData("valid,valid", "123")]
    [InlineData("valid", "999")]
    [InlineData("valid", "not-channel")]
    public async Task Webhook_InvalidSignature_401(string? signature, string channel)
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        using var response = await host.PostAsync("not-json", "/api/line/webhook/" + channel, signature);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(host.Queue.Events); Assert.Equal(0, host.Dedupe.DisposedLeases); Assert.Empty(host.ExceptionLog);
    }

    /// <summary>已知長度與真正 chunked 均測超限一 byte，驗簽零次證明上限先於驗簽執行。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Webhook_BodyTooLarge_413(bool chunked)
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        using var response = await host.PostAsync(PaddedBody(1048577), chunked: chunked);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, host.Signature.Calls); Assert.Empty(host.ExceptionLog);
    }

    /// <summary>上限少一 byte 與恰好一 MB 皆允許，不可因緩衝截斷而誤拒合法請求。</summary>
    [Theory]
    [InlineData(1048575, false)]
    [InlineData(1048576, false)]
    [InlineData(1048576, true)]
    public async Task Webhook_PayloadBoundary_AcceptsExactLimit(int bytes, bool chunked)
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        using var response = await host.PostAsync(PaddedBody(bytes), chunked: chunked);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(bytes, host.Signature.LastBody.Length);
    }

    /// <summary>驗簽後才拒絕語法、缺 ID、重複欄位或錯誤重送形狀，整批驗完前不得發布任何事件。</summary>
    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"events\":{}}")]
    [InlineData("{\"events\":[{}]}")]
    [InlineData("{\"events\":[],\"events\":[]}")]
    [InlineData("{\"events\":[{\"webhookEventId\":\"x\",\"type\":\"follow\",\"webhookEventId\":\"y\"}]}")]
    [InlineData("{\"events\":[{\"webhookEventId\":\"x\",\"type\":\"follow\",\"deliveryContext\":{\"isRedelivery\":null}}]}")]
    public async Task Webhook_InvalidPayload_400WithoutPublication(string body)
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        using var response = await host.PostAsync(body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(1, host.Signature.Calls);
        Assert.Empty(host.Queue.Events); Assert.Equal(0, host.Dedupe.DisposedLeases); Assert.Empty(host.ExceptionLog);
    }

    /// <summary>同批重複穩定 ID 拒絕整批；相同顯示內容的不同 ID 必須完整保留，不能用內容去重。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Webhook_BatchIdentity_ValidatesBeforeClaims(bool duplicate)
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        using var response = await host.PostAsync(Batch("one", duplicate ? "one" : "two"));
        Assert.Equal(duplicate ? HttpStatusCode.BadRequest : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(duplicate ? 0 : 2, host.Queue.Events.Count);
        Assert.Equal(duplicate ? 0 : 2, host.Dedupe.DisposedLeases);
    }

    /// <summary>重送相同事件只接收一次，同一 ID 在不同頻道則各自保留，防止跨 channel 去重。</summary>
    [Fact]
    public async Task Dedupe_SameEvent_SecondIsDuplicate_AndChannelsRemainIsolated()
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        foreach (var channel in new[] { "123", "123", "456" })
        {
            using var response = await host.PostAsync(Batch("same"), "/api/line/webhook/" + channel);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(Channels, host.Queue.Events.Select(e => e.ChannelId));
        Assert.Equal(0, host.Dedupe.Active); Assert.Equal(2, host.Dedupe.DisposedLeases);
    }

    /// <summary>滿載回 503 並 rollback，解除後同 ID 可重送；預期容量拒絕不產生例外告警。</summary>
    [Fact]
    public async Task Queue_Full_Returns503_AndRetrySucceeds()
    {
        await using var host = new WebhookTestHost(); await host.StartAsync(); host.Queue.Reject = true;
        using (var response = await host.PostAsync(Batch("retry"))) Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, host.Dedupe.Active); Assert.Empty(host.Queue.Events); Assert.Empty(host.ExceptionLog);
        host.Queue.Reject = false;
        using (var response = await host.PostAsync(Batch("retry"))) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(host.Queue.Events); Assert.Equal(2, host.Dedupe.DisposedLeases);
    }

    /// <summary>SQL、queue、commit、rollback、驗簽基礎設施及內部 timeout 失敗均回 503，上報原例外但不洩漏本文，修復後可重送。</summary>
    [Theory]
    [InlineData("sql")]
    [InlineData("queue")]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("timeout")]
    [InlineData("signature")]
    public async Task Webhook_InfrastructureFailure_503AndSanitizedIncident(string fault)
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        Exception error = fault == "timeout" ? new OperationCanceledException("private-body-marker") : new IOException("private-body-marker");
        switch (fault)
        {
            case "sql": host.Dedupe.Failure = error; break;
            case "queue": host.Queue.Failure = error; break;
            case "commit": host.Dedupe.CommitFailure = error; break;
            case "rollback": host.Queue.Reject = true; host.Dedupe.DisposeFailure = error; break;
            case "signature": host.Signature.Failure = error; break;
            default: host.Queue.Failure = error; break;
        }
        using (var response = await host.PostAsync(Batch("retry"))) Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, host.Dedupe.Active); Assert.Empty(host.Queue.Events);
        Assert.Contains(error.GetType().Name, host.ExceptionLog, StringComparison.Ordinal);
        Assert.DoesNotContain("private-body-marker", host.ExceptionLog, StringComparison.Ordinal);
        Assert.False(host.App.Services.GetRequiredService<ExceptionDiagnostics>().Report(error, "Tests.IdentityProbe"));
        host.Dedupe.Failure = null; host.Dedupe.CommitFailure = null; host.Dedupe.DisposeFailure = null;
        host.Queue.Failure = null; host.Queue.Reject = false; host.Signature.Failure = null;
        using (var response = await host.PostAsync(Batch("retry"))) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(host.Queue.Events);
    }

    /// <summary>批次第二筆失敗後整批重送，第一筆去重、第二筆補入，完成集合仍只有兩筆且 claim 全釋放。</summary>
    [Fact]
    public async Task Webhook_PartialBatchFailure_RetryOnlyUncommittedEvent()
    {
        await using var host = new WebhookTestHost(); await host.StartAsync(); host.Queue.FailOnCall = 2;
        using (var response = await host.PostAsync(Batch("one", "two"))) Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(host.Queue.Events); Assert.Equal(0, host.Dedupe.Active);
        using (var response = await host.PostAsync(Batch("one", "two"))) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CompletedIds, host.Queue.Events.Select(e => e.WebhookEventId));
    }

    /// <summary>queue 主失敗與 rollback 同時失敗時，共用入口必須同時接收兩個原物件；清理故障不可取代原失敗。</summary>
    [Fact]
    public async Task Webhook_QueueAndRollbackFailure_ReportsBothOriginalExceptions()
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        var primary = new IOException("private-primary");
        var cleanup = new InvalidOperationException("private-cleanup");
        host.Queue.Failure = primary; host.Dedupe.DisposeFailure = cleanup;
        using var response = await host.PostAsync(Batch("both"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var diagnostics = host.App.Services.GetRequiredService<ExceptionDiagnostics>();
        Assert.False(diagnostics.Report(primary, "Tests.Primary"));
        Assert.False(diagnostics.Report(cleanup, "Tests.Cleanup"));
        Assert.Contains("IOException", host.ExceptionLog, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", host.ExceptionLog, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", host.ExceptionLog, StringComparison.Ordinal);
        Assert.Equal(0, host.Dedupe.Active);
    }

    /// <summary>destination 是 LINE bot user ID 而非數字 channel；端點不得做兩者相等的錯誤驗證。</summary>
    [Fact]
    public async Task Webhook_Destination_IsNotNumericChannelId()
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        using var response = await host.PostAsync("{\"destination\":\"U-synthetic-bot\",\"events\":[]}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>四組通知開關均經真 Host 啟停；只送成功空事件，確保不讀憑證或真正發 LINE，關閉落檔時不得建立例外檔。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Hosting_OutputSwitches_StartAndStopWithoutExternalTraffic(bool write, bool line)
    {
        await using var host = new WebhookTestHost(configuration: new()
        {
            ["ExceptionNotifications:WriteExceptionLog"] = write ? "true" : "false",
            ["ExceptionNotifications:SendLine"] = line ? "true" : "false"
        });
        await host.StartAsync();
        using var response = await host.PostAsync("{\"events\":[]}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await host.App.StopAsync();
        Assert.Empty(host.ExceptionLog);
        Assert.Equal(0, host.Dedupe.Active);
    }

    /// <summary>
    /// 逐一省略驗簽或去重替身時真 Host 拒啟動；正式 queue 已由 AddLineWebhook 組合，
    /// 因此 queue 缺 handler 的契約由 P3-05 queue registration tests 保護。
    /// </summary>
    [Theory]
    [InlineData(typeof(IWebhookSignatureVerifier))]
    [InlineData(typeof(IWebhookEventDeduplicator))]
    public async Task Webhook_MissingImplementation_StartupFailsClosed(Type missing)
    {
        await using var host = new WebhookTestHost(missing);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.App.StartAsync());
        Assert.Equal(missing == typeof(IWebhookEventDeduplicator)
            ? "Webhook requires a valid ConnectionStrings:Platform startup snapshot."
            : "Webhook requires signature, deduplication and queue implementations.", error.Message);
        Assert.Equal(0, host.Signature.Calls); Assert.Empty(host.Queue.Events);
    }

    /// <summary>即使 Host 開啟平行啟動，缺 queue 也不得先綁定真 HTTP socket；防止部分啟動的短暫未受保護接收窗。</summary>
    [Fact]
    public async Task Webhook_ConcurrentHostStartup_MissingQueueNeverBindsSocket()
    {
        await using var host = new WebhookTestHost(typeof(IWebhookEventQueue),
            configureServices: services => services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.App.StartAsync());
        Assert.DoesNotContain(host.App.Urls, url => new Uri(url).Port != 0);
    }

    /// <summary>真 Main 缺平台連線快照時精確拒啟動，await using 仍完成主機清理；不得用任意初始化例外冒充必要依賴檢查。</summary>
    [Fact]
    public async Task Program_Main_WithoutProductionImplementations_FailsClosed()
    {
        await using var owner = new WebhookTestHost();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Program.Main(
        [
            "--environment", "Testing", "--contentRoot", owner.DirectoryPath, "--urls", "http://127.0.0.1:0",
            "--ExceptionNotifications:WriteExceptionLog", "true", "--ExceptionNotifications:SendLine", "false"
        ]));
        Assert.Equal("Webhook requires a valid ConnectionStrings:Platform startup snapshot.", error.Message);
        Assert.Contains("InvalidOperationException", owner.ExceptionLog, StringComparison.Ordinal);
    }

    /// <summary>啟動探測的 async-only scoped verifier 必須立即釋放；真正 request 重新取得自己的 scope，不可被 singleton 探測器保留。</summary>
    [Fact]
    public async Task StartupProbe_AsyncScopedDependency_DisposesAndDoesNotCapture()
    {
        var created = 0;
        var disposed = 0;
        await using var host = new WebhookTestHost(configureServices: services =>
        {
            services.RemoveAll<IWebhookSignatureVerifier>();
            services.AddScoped<IWebhookSignatureVerifier>(_ =>
            {
                Interlocked.Increment(ref created);
                return new AsyncScopedVerifier(() => Interlocked.Increment(ref disposed));
            });
        });
        await host.StartAsync();
        Assert.Equal(1, created); Assert.Equal(1, disposed);
        using var response = await host.PostAsync("{\"events\":[]}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await host.App.StopAsync();
        Assert.Equal(2, created); Assert.Equal(2, disposed);
    }

    /// <summary>舊路由預設未映射；opt-in 後僅用受信任設定 channel，query 不可覆寫其租戶界線。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Webhook_LegacyRoute_ExplicitOptInOnly(bool enabled)
    {
        await using var host = new WebhookTestHost(configuration: new()
        {
            ["LineWebhook:EnableLegacyCallback"] = enabled ? "true" : "false",
            ["LineWebhook:LegacyChannelId"] = enabled ? "456" : null
        });
        await host.StartAsync();
        using var response = await host.PostAsync(Batch("legacy"), "/api/callback?channelId=123");
        Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        if (enabled) Assert.Equal("456", Assert.Single(host.Queue.Events).ChannelId);
        else Assert.Equal(0, host.Signature.Calls);
    }

    /// <summary>非法開關與缺少／非法頻道拒絕建構主機，不能默認關閉或猜測頻道掩蓋部署錯誤。</summary>
    [Theory]
    [InlineData("true", null)]
    [InlineData("bad-bool", "123")]
    [InlineData("true", "not-channel")]
    [InlineData("true", "123456789012345678901")]
    public void Webhook_LegacyConfiguration_InvalidFailsClosed(string enabled, string? channel)
    {
        Assert.Throws<InvalidOperationException>(() => new WebhookTestHost(configuration: new()
        {
            ["LineWebhook:EnableLegacyCallback"] = enabled, ["LineWebhook:LegacyChannelId"] = channel
        }));
    }

    /// <summary>真 client abort 在驗簽或持有 claim 時都不通知，等 Host 排空才檢查 rollback 與資源基線。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Webhook_CallerCancellation_NoIncidentAndResourcesDrain(bool afterClaim)
    {
        await using var host = new WebhookTestHost(); await host.StartAsync();
        host.Signature.WaitForCancellation = !afterClaim; host.Queue.WaitForCancellation = afterClaim;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/line/webhook/123")
            { Content = new StringContent(Batch("cancel"), Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("X-Line-Signature", "valid");
        var send = host.Client.SendAsync(request, cancellation.Token);
        await (afterClaim ? host.Queue.Entered.Task : host.Signature.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        await host.App.StopAsync();
        Assert.Equal(0, host.Dedupe.Active); Assert.Empty(host.ExceptionLog); Assert.Empty(host.Queue.Events);
        if (afterClaim) Assert.Equal(1, host.Dedupe.DisposedLeases);
    }

    /// <summary>兩個同時存活頻道各一百筆，全部成功且 scope 不混用；Dispose 後事件與 claim 計數歸零。</summary>
    [Fact]
    public async Task ResourceCounts_ReturnToBaseline_ConcurrentChannels()
    {
        var host = new WebhookTestHost();
        try
        {
            await host.StartAsync();
            await Task.WhenAll(Enumerable.Range(0, 100).SelectMany(index => Channels.Select(async channel =>
            {
                using var response = await host.PostAsync(Batch("event-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)), "/api/line/webhook/" + channel);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            })));
            Assert.Equal(200, host.Queue.Events.Count);
            Assert.Equal(100, host.Queue.Events.Count(e => e.ChannelId == "123"));
            Assert.Equal(100, host.Queue.Events.Count(e => e.ChannelId == "456"));
            Assert.Equal(0, host.Dedupe.Active); Assert.Equal(200, host.Dedupe.DisposedLeases);
        }
        finally { await host.DisposeAsync(); }
        Assert.Empty(host.Queue.Events); Assert.Empty(host.Signature.LastBody); Assert.Equal(0, host.Dedupe.Active);
    }

    /// <summary>關閉原文件後仍能讀事件，反射只接受不可變純值屬性，防止 HTTP、credential 或 disposable 進入 queue。</summary>
    [Fact]
    public void Envelope_ContainsNoHttpContext_AndDetachedImmutableEvent()
    {
        WebhookEnvelope envelope;
        using (var document = JsonDocument.Parse(Batch("snapshot")))
            envelope = new WebhookEnvelope("123", document.RootElement.GetProperty("events")[0]);
        Assert.Equal("snapshot", envelope.Event.GetProperty("webhookEventId").GetString());
        Assert.All(typeof(WebhookEnvelope).GetProperties(), property =>
        {
            Assert.Null(property.SetMethod);
            Assert.Contains(property.PropertyType, new[] { typeof(string), typeof(bool), typeof(JsonElement) });
        });
        Assert.DoesNotContain("snapshot", envelope.ToString()!, StringComparison.Ordinal);
    }

    /// <summary>合成事件顯示內容完全相同，只有穩定 ID 不同；讓按顯示文字去重的退化必然失敗。</summary>
    private static string Batch(params string[] ids) => JsonSerializer.Serialize(new
    {
        events = ids.Select(id => new { type = "message", webhookEventId = id,
            deliveryContext = new { isRedelivery = true }, source = new { type = "user", userId = "synthetic-user" },
            message = new { type = "text", id = "synthetic-message", text = "same-display-private-body-marker" } })
    });

    /// <summary>以 JSON 尾端 ASCII 空白構成精確 byte 數，避免靠錯誤標頭謊報大小。</summary>
    private static string PaddedBody(int bytes) => "{\"events\":[]}".PadRight(bytes, ' ');

    /// <summary>只擁有本 scope 的釋放 callback，沒有同步 Dispose，藉此強制 startup probe 必須使用 async scope。</summary>
    private sealed class AsyncScopedVerifier(Action release) : IWebhookSignatureVerifier, IAsyncDisposable
    {
        /// <summary>合成固定驗簽結果只供生命週期測試，正式產品不得註冊此替身或保留 body。</summary>
        public ValueTask<bool> VerifyAsync(string channelId, ReadOnlyMemory<byte> body, string signature, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
        /// <summary>由 scope 唯一呼叫，釋放案例 callback；沒有 HttpContext、連線或跨 scope 快取。</summary>
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }
}
