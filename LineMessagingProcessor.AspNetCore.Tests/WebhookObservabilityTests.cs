/*
檔案整體說明
目的與定位：F11 Webhook背景邊界的離線整合測試，以真正有界queue及DI scope確認事件關聯編號不被HTTP scope或前一事件污染。
主要功能與流程：一百筆合成事件交正式queue，每筆handler讀取自己的envelope與ILogger scope，將相同編號交測試擁有的發送紀錄sink；另以barrier阻擋一筆處理，驗證資源計數涵蓋執行中而非只buffer。sink是純值替身，不代表尚未合併的F5 SDK或實機發送。
使用邊界與維護：每案擁有DI容器、diagnostics、Meter與logger provider；finally先解除barrier並完整Stop／Dispose queue，再關容器及Meter。所有事件為合成識別，沒有外部SQL、LINE或D365，不保留HttpContext、token或跨案callback；本案不以清空Meter掩蓋未歸還資源。
*/
using System.Collections.Concurrent;
using System.Text.Json;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpeechMessage.Hosting.Observability;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>使用真實queue排空與獨立logger scope的合成跨層契約，不宣稱供應商或F5實機結果。</summary>
public sealed class WebhookObservabilityTests
{
    /// <summary>一百個事件的envelope、ILogger scope與發送紀錄替身逐筆同編號，沒有前一事件的scope洩漏。</summary>
    [Fact]
    public async Task Correlation_WebhookToSendRecord_SameId()
    {
        var correlation = typeof(WebhookEnvelope).GetProperty("CorrelationId");
        Assert.NotNull(correlation);
        var sent = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var capture = new ScopeCapture();
        await using var fixture = new Fixture((envelope, _) =>
        {
            var id = Assert.IsType<string>(correlation.GetValue(envelope));
            Assert.Equal("wh-" + envelope.WebhookEventId, id);
            Assert.Equal(id, capture.Correlation());
            Assert.True(sent.TryAdd(envelope.WebhookEventId, id));
            if (sent.Count == 100) complete.TrySetResult();
            return ValueTask.CompletedTask;
        }, capture);
        await fixture.Queue.StartAsync(default);
        for (var index = 0; index < 100; index++)
        {
            await using var claim = new WebhookQueueClaim();
            Assert.True(await fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("event-" + index), claim, default));
        }
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.Queue.StopAsync(default);
        Assert.Equal(100, sent.Count);
        Assert.Equal(new HostingMeterSnapshot(0, 0, 0, 0), fixture.Meters.GetSnapshot());
        Assert.Null(capture.Correlation());
    }

    /// <summary>處理中的事件已離開buffer仍計入queue深度，完成scope釋放才歸零；故障時亦由finally解除測試barrier。</summary>
    [Fact]
    public async Task Queue_MeterIncludesActiveLease_AndDrains()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new Fixture(async (_, _) => { entered.TrySetResult(); await release.Task; });
        try
        {
            await fixture.Queue.StartAsync(default);
            await using var claim = new WebhookQueueClaim();
            Assert.True(await fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("blocked"), claim, default));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, fixture.Queue.GetSnapshot().Buffered);
            Assert.Equal(1, fixture.Meters.GetSnapshot().WebhookQueueDepth);
        }
        finally { release.TrySetResult(); await fixture.Queue.StopAsync(default); }
        Assert.Equal(0, fixture.Meters.GetSnapshot().WebhookQueueDepth);
    }

    /// <summary>單案依賴owner；容器與queue確定結束後才關閉Meter，不建立外部連線或檔案。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly ExceptionDiagnostics diagnostics = new(Path.GetTempPath(), outputOptions: new(false, false));
        internal HostingMeters Meters { get; } = new();
        internal BoundedWebhookEventQueue Queue { get; }
        internal Fixture(Func<WebhookEnvelope, CancellationToken, ValueTask> callback, ScopeCapture? capture = null)
        {
            var registrations = new ServiceCollection();
            registrations.AddSingleton(Meters);
            registrations.AddSingleton(diagnostics);
            registrations.AddLogging(builder => { if (capture is not null) builder.AddProvider(capture); });
            registrations.AddScoped<IWebhookEnvelopeHandler>(_ => new Handler(callback));
            registrations.AddBoundedWebhookQueue(new WebhookQueueOptions(128, 4));
            services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            Queue = Assert.IsType<BoundedWebhookEventQueue>(services.GetRequiredService<IWebhookEventQueue>());
        }
        /// <summary>先停止並Dispose queue及logger，再釋放外借diagnostics與Meter；不以取消放棄仍使用scope的工作。</summary>
        public async ValueTask DisposeAsync()
        {
            try { await services.DisposeAsync(); }
            finally { try { await diagnostics.DisposeAsync(); } finally { Meters.Dispose(); } }
        }
    }

    /// <summary>單次DI scope的純委派替身，不持連線或背景工作。</summary>
    private sealed class Handler(Func<WebhookEnvelope, CancellationToken, ValueTask> callback) : IWebhookEnvelopeHandler
    {
        /// <summary>只在當次事件呼叫測試sink，沒有快取或跨事件資料。</summary>
        public ValueTask HandleAsync(WebhookEnvelope envelope, CancellationToken cancellationToken) => callback(envelope, cancellationToken);
    }

    /// <summary>借用logging原有scope provider作當次讀取，不自建AsyncLocal或保存歷史scope。</summary>
    private sealed class ScopeCapture : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? scopes;
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new NullLogger();
        public void Dispose() => scopes = null;
        internal string? Correlation()
        {
            string? result = null;
            scopes?.ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> values)
                    foreach (var pair in values)
                        if (pair.Key == "CorrelationId") result = pair.Value as string;
            }, 0);
            return result;
        }
        /// <summary>不輸出任何內容；ScopeProvider由LoggerFactory統一處理，純logger不保存資源。</summary>
        private sealed class NullLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) { }
        }
    }
}
