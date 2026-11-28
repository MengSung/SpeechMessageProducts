/*
檔案整體說明
目的與定位：F5 純值憑證 executor 的離線接點測試，重用 F3 真目錄及合成加密來源，
不呼叫真實 LINE 或 Dataverse。
主要功能與流程：觀察一次 401 刷新、第二次停用、已釋放的 Core HTTP 回應及內部期限；
手動 TimeProvider 讓十秒期限在合成委派內確定觸發，避免以長時間等待製造 flaky 測試。
使用邊界與維護：fixture 擁有合成金鑰、目錄 timer 與 HTTP owner，案例 using/await 完整排空。
手動 timer 只在案例內保留，Dispose 解除 callback，無 static、實體 timer 或額外背景工作。
*/
using System.Net;
using LineMessagingProcessor.Core;
using ToolUtilityNameSpace.Channels;
using Xunit;

namespace ToolUtility.Tests.Channels;

/// <summary>對真 F3 owner 進行純值傳輸測試，避免用假 executor 掩蓋 timeout 與 401 的接縫。</summary>
public sealed class ChannelCredentialExecutorTests
{
    /// <summary>第一次 401 旋轉來源，第二次成功，必須使用新憑證且不告警。</summary>
    [Fact]
    public async Task Execute_401_RefreshesOnce_AndReturnsDisposedResponseValue()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        using var handler = new PushHandler();
        using var http = new HttpClient(handler);
        var client = new LinePushClient(http);
        var executor = new ChannelCredentialExecutor(directory);
        var seen = new List<LineChannelCredential>();
        var result = await executor.ExecuteAsync("10001", (credential, ct) =>
        {
            seen.Add(credential);
            if (seen.Count == 1)
            {
                var old = Assert.Single(fixture.Source.Rows);
                var rotated = fixture.Row("10001", revision: "2", suffix: "-rotated");
                fixture.Source.Rows = [new ChannelDirectoryRecord(old.Id, rotated.Product, rotated.ChannelKey,
                    rotated.Status, rotated.Revision, rotated.ModifiedOn, rotated.TokenEncrypted, rotated.SecretEncrypted)];
            }
            return client.PushAsync(credential, Request(), ct);
        }, default);
        Assert.True(result.Success);
        Assert.Equal(2, seen.Count);
        Assert.NotSame(seen[0], seen[1]);
        Assert.Equal(2, fixture.Source.FullReads);
        Assert.Equal(2, handler.DisposedResponses);
        Assert.Empty(fixture.Diagnostics.Failures);
    }

    /// <summary>第二個 401 只停用同版本一次並告警，不准第三次傳輸。</summary>
    [Fact]
    public async Task Execute_Second401_DisablesAndAlertsOnce()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        using var handler = new PushHandler { AlwaysUnauthorized = true };
        using var http = new HttpClient(handler);
        var client = new LinePushClient(http);
        var result = await new ChannelCredentialExecutor(directory).ExecuteAsync("10001",
            (credential, ct) => client.PushAsync(credential, Request(), ct), default);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(2, handler.DisposedResponses);
        Assert.Single(fixture.Source.Writes);
        Assert.Single(fixture.Diagnostics.Failures);
        Assert.Equal(DirectoryChannelStatus.Disabled, Assert.Single(fixture.Source.Rows).Status);
    }

    /// <summary>目錄內部十秒到期是可對帳的傳輸故障，不當 caller 取消，也不在恢復前告警。</summary>
    [Fact]
    public async Task Execute_InternalDeadline_IsRetryValue_NotPrematureAlert()
    {
        using var fixture = new DirectoryFixture();
        var time = new ManualTime();
        using var directory = new DataverseChannelDirectory(fixture.Product, fixture.Source.OrganizationId,
            fixture.Source, fixture.Key, fixture.Clock, fixture.Diagnostics, timeProvider: time);
        using var handler = new PushHandler();
        using var http = new HttpClient(handler);
        var client = new LinePushClient(http);
        var executor = new ChannelCredentialExecutor(directory);
        var result = await executor.ExecuteAsync("10001", async (credential, ct) =>
        {
            time.FireOperation();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return await client.PushAsync(credential, Request(), ct);
        }, default);
        Assert.False(result.Success);
        Assert.Equal(LinePushOutcome.Retryable, result.Outcome);
        Assert.IsAssignableFrom<OperationCanceledException>(result.TransportException);
        Assert.Empty(fixture.Diagnostics.Failures);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, time.ActiveCount); // 只有目錄的十分鐘到期 timer，操作 timer 已釋放。
    }

    /// <summary>明確 caller 預取消不解析、不送訊、不告警。</summary>
    [Fact]
    public async Task Execute_CallerCancellation_NoTransportOrAlert()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ChannelCredentialExecutor(directory).ExecuteAsync("10001", (_, _) =>
                throw new InvalidOperationException("Must not enter delegate."), cts.Token));
        Assert.Equal(0, fixture.Source.OpenCount);
        Assert.Empty(fixture.Diagnostics.Failures);
    }

    /// <summary>全部為合成資料；固定 retry UUID 不代表實際業務事件或手機。</summary>
    private static LinePushRequest Request() => new("U00000000000000000000000000000001",
        new[] { "synthetic" }, new Guid("11111111-2222-3333-4444-555555555555"));

    /// <summary>離線只接受推播，回應內容由正式 Core owner Dispose 並記錄釋放次數。</summary>
    private sealed class PushHandler : HttpMessageHandler
    {
        internal int Calls;
        internal int DisposedResponses;
        internal bool AlwaysUnauthorized;
        /// <summary>首次 401、之後 200；可注入持續拒絕，沒有 socket 或工作執行緒。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = ++Calls;
            return Task.FromResult(new HttpResponseMessage(AlwaysUnauthorized || call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            { Content = new TrackingContent("{}", () => DisposedResponses++) });
        }
    }

    /// <summary>可手動觸發的有限 timer owner；只保留目錄及一個操作，無真實排程器。</summary>
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        internal int ActiveCount => timers.Count(timer => !timer.Disposed);
        /// <summary>逐次建立獨佔 timer，由 CancellationTokenSource 或目錄負責 Dispose。</summary>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            timers.Add(timer);
            return timer;
        }
        /// <summary>只觸發尚存的十秒操作，不觸發十分鐘快照到期 timer。</summary>
        internal void FireOperation()
        {
            foreach (var timer in timers.Where(timer => !timer.Disposed && timer.Due == TimeSpan.FromSeconds(10)).ToArray())
                timer.Fire();
        }
        /// <summary>沒有 native handle，Dispose 清除 callback/state 參照，避免保留 CTS owner。</summary>
        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            private TimerCallback? callback = callback;
            private object? state = state;
            internal bool Disposed;
            internal TimeSpan Due = due;
            /// <summary>只供建立此 timer 的 CTS 改期限，disposed 後拒絕。</summary>
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return !Disposed; }
            /// <summary>呼叫一次由案例明確驅動的 callback，不留下背景工作。</summary>
            internal void Fire() => callback?.Invoke(state);
            /// <summary>確定性解除 callback 與 state，重複釋放安全。</summary>
            public void Dispose() { Disposed = true; callback = null; state = null; }
            /// <summary>無在途 callback 工作，非同步釋放等同已完成的同步清理。</summary>
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
