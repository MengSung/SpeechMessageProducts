/*
檔案整體說明
目的與定位：F11共用關聯編號與四項實際UpDownCounter的離線契約測試，不呼叫外部SQL、D365或LINE。
主要功能與流程：以反射取得新增公開入口，先在功能不存在時產生決定性RED，再檢查HTTP字元白名單、Webhook／工作固定命名，以及一百次四類租約並行存在與重入釋放的計數。
使用邊界與維護：每案唯一擁有Meter及短命計數租約；finally釋放所有租約後才釋放Meter。不儲存身分、秘密或全域listener；測試以實際公開計數快照驗證排空，不把合成租約寫成實機發送或SDK結果。
*/
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Hosting.Observability;
using Xunit;

namespace SpeechMessage.Hosting.Tests.Observability;

/// <summary>公開操作契約的離線回歸；沒有部署依賴或跨案可變狀態。</summary>
public sealed class HostingTelemetryTests
{
    /// <summary>不合法caller標頭必須產生新的安全編號，不能讓換行、空白或過長內容流入scope。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad header")]
    [InlineData("evil\r\nsecret")]
    [InlineData("a_b")]
    public void Correlation_InvalidHeader_Replaced(string? input)
    {
        var type = Contract("CorrelationIds");
        var actual = Assert.IsType<string>(type.GetMethod("ForHttp")!.Invoke(null, new object?[] { input }));
        Assert.InRange(actual.Length, 1, 64);
        Assert.All(actual, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c == '-'));
        Assert.NotEqual(input, actual);
    }

    /// <summary>合法且邊界長度的編號原樣保存，不以正規化破壞同一操作的追查。</summary>
    [Fact]
    public void Correlation_ValidBoundaryHeader_Preserved()
    {
        var type = Contract("CorrelationIds");
        var id = new string('A', 64);
        Assert.Equal(id, type.GetMethod("ForHttp")!.Invoke(null, new object?[] { id }));
        var rejected = Assert.IsType<string>(type.GetMethod("ForHttp")!.Invoke(null, new object?[] { id + "A" }));
        Assert.NotEqual(id + "A", rejected);
    }

    /// <summary>provider事件及持久工作識別採獨立命名規則；不把HTTP輸入上限錯套至來源可信的關聯編號。</summary>
    [Fact]
    public void Correlation_EventAndJob_StableSeparatedNamespaces()
    {
        var type = Contract("CorrelationIds");
        Assert.Equal("wh-event_1.2", type.GetMethod("ForWebhook")!.Invoke(null, new object[] { "event_1.2" }));
        Assert.Equal("job-GoodShepherd2:health:daily-42", type.GetMethod("ForJob")!.Invoke(null,
            new object[] { "GoodShepherd2:health:daily", "42" }));
    }

    /// <summary>一百筆各持有四項實際工作租約，逐筆重入Dispose後四計數全回零，Meter停止不能偽造歸零。</summary>
    [Fact]
    public void Shutdown_CountersReturnToBaseline()
    {
        var type = Contract("HostingMeters");
        using var meter = Assert.IsAssignableFrom<IDisposable>(Activator.CreateInstance(type));
        var leases = new List<IDisposable>();
        try
        {
            for (var index = 0; index < 100; index++)
                foreach (var name in new[] { "TrackWebhook", "TrackDataverseLease", "TrackSchedulerJob", "TrackPushBatch" })
                    leases.Add(Assert.IsAssignableFrom<IDisposable>(type.GetMethod(name)!.Invoke(meter, null)));
            AssertCounts(type, meter, 100);
        }
        finally { foreach (var lease in leases) { lease.Dispose(); lease.Dispose(); } }
        AssertCounts(type, meter, 0);
    }

    /// <summary>真HTTP middleware拒絕多值標頭，回應及TraceIdentifier保持同值；例外原樣傳播，不把下一層失敗變成成功。</summary>
    [Fact]
    public async Task Correlation_Middleware_MultipleHeadersReplacedAndFailurePreserved()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = new Microsoft.Extensions.Primitives.StringValues(["one", "two"]);
        var primary = new InvalidOperationException("synthetic");
        var middleware = new CorrelationIdMiddleware(_ => throw primary, NullLoggerFactory.Instance);
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        Assert.Same(primary, actual);
        Assert.NotEqual("one", context.TraceIdentifier);
        Assert.NotEqual("two", context.TraceIdentifier);
        Assert.Equal(context.TraceIdentifier, context.Response.Headers["X-Correlation-Id"].ToString());
    }

    /// <summary>直接監聽正式Meter的四個UpDownCounter，必須各收到+1/-1，且不能夾帶高基數或身分tag。</summary>
    [Fact]
    public void Meters_PublishActualUntaggedUpDownMeasurements()
    {
        var observed = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        using var meters = new HostingMeters();
        // MeterListener為程序級觀察器，只訂閱本案owner，不能把其他並行案例的計數混入本案。
        var expectedMeter = Assert.IsType<Meter>(typeof(HostingMeters).GetField("meter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(meters));
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (!ReferenceEquals(instrument.Meter, expectedMeter)) return;
            Assert.Equal("SpeechMessage.Hosting", instrument.Meter.Name);
            Assert.IsType<UpDownCounter<long>>(instrument);
            current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            Assert.Equal(0, tags.Length);
            if (!observed.TryGetValue(instrument.Name, out var values)) observed.Add(instrument.Name, values = new List<long>());
            values.Add(value);
        });
        listener.Start();
        using (meters.TrackWebhook()) { }
        using (meters.TrackDataverseLease()) { }
        using (meters.TrackSchedulerJob()) { }
        using (meters.TrackPushBatch()) { }
        Assert.Equal(4, observed.Count);
        Assert.All(observed.Values, values => Assert.Equal(new long[] { 1, -1 }, values));
    }

    /// <summary>關機前仍有在途資源時明確拒絕，不以清空四計數掩蓋原owner未排空；歸還後可重試正常Dispose。</summary>
    [Fact]
    public void Meters_DisposeWhileActive_RefusesUntilRealDrain()
    {
        using var meters = new HostingMeters();
        using var lease = meters.TrackWebhook();
        Assert.Throws<InvalidOperationException>(() => meters.Dispose());
        Assert.Equal(1, meters.GetSnapshot().WebhookQueueDepth);
        lease.Dispose();
        meters.Dispose();
        Assert.Throws<ObjectDisposedException>(() => meters.TrackWebhook());
    }

    /// <summary>故障注入同步Meter監聽器於取得或扣回時拋錯；觀測故障不可中斷queue／SDK的finally清理或使Dispose對外失敗。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Meters_ThrowingObserver_DoesNotInterruptResourceRelease(bool onAcquisition)
    {
        using var meters = new HostingMeters();
        var expectedMeter = Assert.IsType<Meter>(typeof(HostingMeters).GetField("meter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(meters));
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        { if (ReferenceEquals(instrument.Meter, expectedMeter)) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        { if (onAcquisition ? value > 0 : value < 0) throw new IOException("synthetic-meter-listener-canary"); });
        listener.Start();
        var lease = meters.TrackWebhook();
        var failure = Record.Exception(lease.Dispose);
        Assert.Equal(0, meters.GetSnapshot().WebhookQueueDepth);
        Assert.Null(failure);
        lease.Dispose();
    }

    /// <summary>僅反射正式組件既定公開型別；缺功能是明確斷言失敗，而不是測試無法編譯。</summary>
    private static Type Contract(string name)
    {
        var type = typeof(StartupGuards).Assembly.GetType("SpeechMessage.Hosting.Observability." + name);
        Assert.NotNull(type);
        return type;
    }

    /// <summary>從正式監控快照逐項查驗，不由測試的租約數推算假結果。</summary>
    private static void AssertCounts(Type type, object meter, long expected)
    {
        var snapshot = type.GetMethod("GetSnapshot")!.Invoke(meter, null)!;
        foreach (var name in new[] { "WebhookQueueDepth", "DataverseLeasesActive", "SchedulerJobsRunning", "PushBatchesInflight" })
            Assert.Equal(expected, snapshot.GetType().GetProperty(name)!.GetValue(snapshot));
    }
}
