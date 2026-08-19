using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Diagnostics;

/// <summary>
/// 管理 Debug 診斷背景監控的取消來源與工作生命週期。
/// </summary>
/// <remarks>
/// 此型別是監控工作與其 <see cref="CancellationTokenSource"/> 的唯一擁有者。它刻意不使用
/// Host 的 <c>ApplicationStopping</c> token 執行監控，因為 Host 會同步呼叫停止 callback；若
/// callback 在同一顆 token 完成取消前等待監控工作，可能阻塞該工作所需的取消 callback，形成
/// 關機死鎖。停止時固定先取消專屬 token、等待工作結束，最後釋放 token source；整個生命週期
/// 只保存程序級診斷狀態，不保存 request、使用者、租戶、憑證或其他可跨工作流洩漏的資料。
/// </remarks>
internal sealed class DebugTraceMonitorLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _monitorTask;
    private int _disposed;

    private DebugTraceMonitorLifetime(
        CancellationTokenSource cancellationTokenSource,
        Task monitorTask)
    {
        _cancellationTokenSource = cancellationTokenSource;
        _monitorTask = monitorTask;
    }

    /// <summary>
    /// 以此生命週期專屬的取消 token 啟動背景監控。
    /// </summary>
    /// <param name="monitor">
    /// 接受專屬取消 token 並回傳完整生命週期工作的方法；方法必須在收到取消後結束，不得保留
    /// request、使用者或租戶狀態，也不得把 token 註冊轉交給超出本生命週期的長命元件。
    /// </param>
    /// <returns>唯一擁有取消來源與監控工作的生命週期物件。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="monitor"/> 為 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="monitor"/> 回傳 <see langword="null"/> 工作。</exception>
    internal static DebugTraceMonitorLifetime Start(Func<CancellationToken, Task> monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var cancellationTokenSource = new CancellationTokenSource();
        try
        {
            var monitorTask = monitor(cancellationTokenSource.Token)
                ?? throw new InvalidOperationException("診斷背景監控不得回傳 null 工作。");
            return new DebugTraceMonitorLifetime(cancellationTokenSource, monitorTask);
        }
        catch
        {
            cancellationTokenSource.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 先停止接受新的週期工作，再等待監控完成，最後釋放專屬取消來源。
    /// </summary>
    /// <remarks>
    /// 此方法可重複呼叫且只由第一個呼叫者執行清理。監控若因專屬 token 正常取消而擲出
    /// <see cref="OperationCanceledException"/>，視為正常 drain；其他例外會在 token source 已於
    /// <see langword="finally"/> 釋放後傳回呼叫端，讓組合根記錄錯誤並繼續清理 Trace listener。
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _cancellationTokenSource.Cancel();
            try
            {
                _monitorTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
            {
                // 專屬 token 的正常取消代表背景監控已完成 drain，不是停止失敗。
            }
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }
}
