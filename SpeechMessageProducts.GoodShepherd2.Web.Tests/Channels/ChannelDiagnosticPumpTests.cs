/*
檔案整體說明
目的與定位：驗證 F3 持久診斷真的接入主機，且唯一輪詢工作在停止時先取消、排空再歸還 SDK；離線時鐘與 mock 不代表實機驗收。
主要功能與流程：以手動 TimeProvider 觸發正式 PeriodicTimer，真正 SlotRequestDiagnosticReceiver 借用合成 Async2 SDK。成功空批次提供正控制，故障後下一 tick 仍能執行；取消測試刻意暫緩 SDK 返回，斷言停止不可提早完成。
使用邊界與維護：每案 owner 管理 diagnostics、timer、pump 及暫存資料，先排空 pump 再 drain diagnostics，最後刪自身路徑。所有 barrier 有測試期限且在 finally 放行；不讀 secrets、不呼叫網路，也不留下背景 task。組合根案例使用真 DI 核對服務恰好一次，不能以未接線 helper 代替產品驗證。
*/
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd2.Web.Channels;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Channels;

/// <summary>真計時器協定與接收器的生命週期斷言，全部依賴限定單一測試。</summary>
public sealed class ChannelDiagnosticPumpTests
{
    /// <summary>沒有 tick 不查庫，tick 真正借用接收器；停止後 timer 失效且 lease 已歸還。</summary>
    [Fact]
    public async Task Tick_InvokesReceiverAndStopDisposesTimer()
    {
        await using var env = new PumpEnvironment();
        await env.Pump.StartAsync(default);
        Assert.Equal(0, env.Acquired);
        env.Time.Tick();
        await env.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await env.Pump.StopAsync(default);
        Assert.Equal(1, env.Acquired); Assert.Equal(1, env.Returned);
        Assert.True(env.Time.TimerDisposed);
        env.Time.Tick(); Assert.Equal(1, env.Acquired);
    }

    /// <summary>SDK 已收到取消但尚未排空時，停止必須等待；不可將連線 owner 提早釋放。</summary>
    [Fact]
    public async Task Stop_WaitsForCancelledSdkToDrain()
    {
        await using var env = new PumpEnvironment { Block = true };
        await env.Pump.StartAsync(default);
        env.Time.Tick();
        try
        {
            await env.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopping = env.Pump.StoppingAsync(new CancellationToken(true));
            await env.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stopping.IsCompleted); Assert.Equal(0, env.Returned);
            env.Release.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, env.Returned); Assert.Equal(1, env.Faulted);
        }
        finally { env.Release.TrySetResult(); }
    }

    /// <summary>組合根只有完整設定才加一個 pump，接收器與主機使用相同固定組織及既有 manager。</summary>
    [Fact]
    public async Task Composition_RegistersOneDiagnosticPump()
    {
        await using var env = new PumpEnvironment();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseKestrel();
        builder.Configuration["Channels:OrganizationId"] = env.Organization.ToString("D");
        builder.Configuration["LineChannels:TokenKey"] = Convert.ToBase64String(new byte[64]);
        builder.Services.AddSingleton<IChurchClock, ChurchClock>();
        builder.Services.AddSingleton(env.Diagnostics);
        builder.Services.AddSingleton(env.Manager.Object);
        GoodShepherd2ChannelHosting.AddGoodShepherd2Channels(builder);
        await using var app = builder.Build();
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<GoodShepherd2ChannelDiagnosticPump>());
        Assert.NotNull(app.Services.GetRequiredService<SlotRequestDiagnosticReceiver>());
        Assert.Equal(0, env.Acquired);
    }

    /// <summary>每案唯一測試 owner，SDK 沒有敏感資料且只接受 WhoAmI 及診斷查詢。</summary>
    private sealed class PumpEnvironment : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "f3-diagnostic-pump-" + Guid.NewGuid().ToString("N"));
        internal Guid Organization { get; } = Guid.NewGuid();
        internal ExceptionDiagnostics Diagnostics { get; }
        internal ManualTime Time { get; } = new();
        internal Mock<IDataverseConnectionManager> Manager { get; } = new(MockBehavior.Strict);
        internal GoodShepherd2ChannelDiagnosticPump Pump { get; }
        internal TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Block;
        internal int Acquired, Returned, Faulted;
        /// <summary>有合法成功空批次的 Async2，阻擋模式會在收到取消後仍等待明確放行才返回。</summary>
        internal PumpEnvironment()
        {
            Diagnostics = new ExceptionDiagnostics(root, outputOptions: new ExceptionOutputOptions(true, false));
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = service.As<IOrganizationServiceAsync2>();
            sdk.Setup(value => value.ExecuteAsync(It.IsAny<OrganizationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WhoAmIResponse { Results = new ParameterCollection { ["OrganizationId"] = Organization } });
            sdk.Setup(value => value.RetrieveMultipleAsync(It.IsAny<QueryBase>(), It.IsAny<CancellationToken>()))
                .Returns<QueryBase, CancellationToken>(async (_, ct) =>
                {
                    Read.TrySetResult();
                    if (Block)
                    {
                        using var registration = ct.Register(() => Cancelled.TrySetResult());
                        await Release.Task;
                    }
                    ct.ThrowIfCancellationRequested();
                    return new EntityCollection();
                });
            Manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns<CancellationToken>(ct =>
            {
                ct.ThrowIfCancellationRequested(); Acquired++;
                var lease = new Mock<IClientLease>(MockBehavior.Strict);
                lease.SetupGet(value => value.Service).Returns(service.Object);
                lease.Setup(value => value.MarkFaulted()).Callback(() => Faulted++);
                lease.Setup(value => value.Dispose()).Callback(() => Returned++);
                return lease.Object;
            });
            var receiver = new SlotRequestDiagnosticReceiver("GoodShepherd2", Organization, Manager.Object.Acquire, new ChurchClock(), Diagnostics);
            Pump = new GoodShepherd2ChannelDiagnosticPump(receiver, Diagnostics, Time);
        }
        /// <summary>無論斷言成功或失敗都放行 SDK、排空 pump 與共用診斷，最後才刪本案目錄。</summary>
        public async ValueTask DisposeAsync()
        {
            Release.TrySetResult();
            await Pump.DisposeAsync(); await Diagnostics.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    /// <summary>只實作計時器協定的手動時間來源；測試主動觸發，沒有真執行緒或牆鐘等待。</summary>
    private sealed class ManualTime : TimeProvider
    {
        private TimerCallback? callback;
        private object? state;
        internal bool TimerDisposed;
        /// <summary>PeriodicTimer 借用本 timer，直到 pump 明確關閉；僅保存固定 callback。</summary>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback; this.state = state;
            return new ManualTimer(this);
        }
        /// <summary>同步觸發已登錄 timer，關閉後不得再喚起工作。</summary>
        internal void Tick() { if (!TimerDisposed) callback?.Invoke(state); }
        /// <summary>沒有背景資源的 timer owner，Dispose 清除 callback 及 state 參照。</summary>
        private sealed class ManualTimer(ManualTime owner) : ITimer
        {
            /// <summary>測試不調整週期，只尊重是否已關閉。</summary>
            public bool Change(TimeSpan dueTime, TimeSpan period) => !owner.TimerDisposed;
            /// <summary>確定性清除唯一 callback，避免測試完成後保留 pump。</summary>
            public void Dispose() { owner.TimerDisposed = true; owner.callback = null; owner.state = null; }
            /// <summary>非同步關閉共用同步清理，不建立額外 task。</summary>
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
