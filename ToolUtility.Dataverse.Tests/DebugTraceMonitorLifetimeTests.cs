using System;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Diagnostics;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 Debug 診斷背景監控的停止契約。測試以永不自行完成的延遲模擬長時間 GC
/// 監控，關鍵斷言是停止程序必須先取消自己擁有的 token，再等待背景工作完成；如此可
/// 避免在 Host 的 <c>ApplicationStopping</c> 同步 callback 內等待同一顆 token 而死鎖。
/// 測試不建立檔案、不保留 request 或使用者資料，且每個取消來源都由測試決定性釋放。
/// </summary>
public sealed class DebugTraceMonitorLifetimeTests
{
    /// <summary>
    /// 故障注入是以無限延遲模擬仍在執行的 GC 監控；決定性斷言是
    /// <see cref="DebugTraceMonitorLifetime.Dispose"/> 能在一秒內完成，且監控確實收到取消。
    /// 若實作先等待 task 再取消其專屬 token，此測試會逾時並揭露停止死鎖。
    /// </summary>
    [Fact]
    public async Task Dispose_cancels_owned_token_before_waiting_for_monitor()
    {
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = DebugTraceMonitorLifetime.Start(async cancellationToken =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
            }
        });

        await Task.Run(lifetime.Dispose).WaitAsync(TimeSpan.FromSeconds(1));

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        lifetime.Dispose();
    }
}
