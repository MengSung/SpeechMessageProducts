/*
檔案整體說明
目的與定位：F5 net10 執行階段的最大名單配置量測及連續操作保留檢查，補雙目標行為測試。
主要功能與流程：借同 partial 的離線 fixture，以5000人×5則最大文字測完整 Gateway，
記錄配置／耗時並確認10批資源全部關閉；128次操作後清測試觀察器，再驗原SDK模型可回收。
使用邊界與維護：collection 停用並行以避免其他測試污染 process allocation 計數；配置
上限是寬鬆有界回歸門檻，耗時僅記錄、不假稱正式吞吐。using fixture 擁有 HttpClient，
每次 await 後才清觀察資料，主機 Gateway 保持存活以抓意外欄位保留；無實機或真實名單。
本檔 net10 才量測 process allocation；net48 仍由既有隔離與CTS／HTTP生命週期案例驗證。
*/
#if NET10_0_OR_GREATER
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Line.Messaging;
using LineMessagingProcessor.Core.Gateway;
using Xunit;
using Xunit.Abstractions;

namespace LineMessagingProcessor.Tests.Gateway;

/// <summary>量測過程排除其他 xUnit collection 的配置雜訊，不建立額外程序或背景 owner。</summary>
[CollectionDefinition("F5GatewayAllocation", DisableParallelization = true)]
public sealed class F5GatewayAllocationCollection;

/// <summary>沿用真 Gateway 的合成依賴，觀察器清理與產品資源 Dispose 各自核對。</summary>
[Collection("F5GatewayAllocation")]
public sealed partial class LinePushGatewayTests
{
    private readonly ITestOutputHelper metrics;

    /// <summary>xUnit 擁有輸出器；僅寫數字，不保存或列印名單、本文、憑證或 hash。</summary>
    public LinePushGatewayTests(ITestOutputHelper metrics) => this.metrics = metrics;

    /// <summary>5000人×5則5000字驗10批與有界配置，所有HTTP回應及CTS都須在返回前關閉。</summary>
    [Fact]
    public async Task Send_MaximumAudience_AllocationAndDrain()
    {
        using (var warmup = new Fixture()) await warmup.Send(1);
        using var fixture = new Fixture();
        var users = Users(5000);
        var messages = Enumerable.Range(0, 5).Select(_ => (ISendMessage)new TextMessage(new string('x', 5000))).ToArray();
        var watch = Stopwatch.StartNew();
        var baseline = GC.GetTotalAllocatedBytes(precise: true);
        var result = await fixture.Gateway.SendAsync(fixture.Scope, users, messages,
            MessageCategory.Transactional, "maximum-audience", default);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - baseline;
        watch.Stop();
        metrics.WriteLine("F5 maximum audience: recipients=5000 messages=5 batches=10 allocatedBytes={0} elapsedMs={1}",
            allocated, watch.ElapsedMilliseconds);
        Assert.Equal(5000, result.AcceptedRecipientCount);
        Assert.Equal(10, result.Batches.Count);
        Assert.Equal(10, fixture.Handler.DisposedResponses);
        Assert.Equal(10, fixture.Timeouts.Count);
        foreach (var source in fixture.Timeouts)
            Assert.Throws<ObjectDisposedException>(() => { _ = source.Token; });
        Assert.InRange(allocated, 1, 128L * 1024 * 1024);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>保持Gateway存活的128次發送後，原SDK模型不被保留；觀察器資料清空且每次資源已關閉。</summary>
    [Fact]
    public async Task Send_RepeatedOperations_ModelsCollectibleAndOwnersDrained()
    {
        using var fixture = new Fixture();
        var models = new List<WeakReference>(128);
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        for (var index = 0; index < 128; index++)
        {
            models.Add(await SendObservedAsync(fixture, index));
            foreach (var source in fixture.Timeouts)
                Assert.Throws<ObjectDisposedException>(() => { _ = source.Token; });
            while (fixture.Timeouts.TryTake(out _)) { }
            while (fixture.Handler.Sends.TryDequeue(out _)) { }
            fixture.Store.Claims.Clear();
            fixture.Store.Completed.Clear();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var retained = GC.GetTotalMemory(forceFullCollection: true) - baseline;
        Assert.All(models, model => Assert.False(model.IsAlive));
        Assert.Equal(128, fixture.Handler.DisposedResponses);
        Assert.Empty(fixture.Timeouts);
        Assert.Empty(fixture.Handler.Sends);
        Assert.Empty(fixture.Failures);
        Assert.True(retained < 16L * 1024 * 1024, "Retained managed bytes exceeded the bounded regression allowance.");
        metrics.WriteLine("F5 repeated operations: operations=128 disposedResponses=128 openTimeouts=0 retainedBytes={0}", retained);
        GC.KeepAlive(fixture.Gateway);
    }

    /// <summary>獨立呼叫框架避免測試自身局部變數保留SDK模型；完成Task只返回弱參照，沒有背景工作。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> SendObservedAsync(Fixture fixture, int index)
    {
        var message = new TextMessage(new string('x', 5000));
        var result = await fixture.Gateway.SendAsync(fixture.Scope, Users(1), new ISendMessage[] { message },
            MessageCategory.Operational, "soak-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), default);
        Assert.Equal(1, result.AcceptedRecipientCount);
        return new WeakReference(message);
    }
}
#endif
