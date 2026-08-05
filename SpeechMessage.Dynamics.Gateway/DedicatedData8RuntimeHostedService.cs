using Microsoft.Extensions.Hosting;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Gateway;

/// <summary>
/// Dedicated Gateway 的 Data8 runtime 終止 owner。
/// Generic Host 停止時明確 await runtime 的非同步釋放，避免只靠同步 DI Dispose 而遺留 drain 中的
/// pool、permit、client、CTS 或 host-slot renewal。此服務不建立 timer、背景迴圈或第二個 runtime。
/// </summary>
public sealed class DedicatedData8RuntimeHostedService : IHostedService
{
    private readonly Data8ProfileRuntime _runtime;

    /// <summary>取得由同一個 Gateway ServiceProvider 擁有的唯一 Data8 runtime。</summary>
    public DedicatedData8RuntimeHostedService(Data8ProfileRuntime runtime)
        => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    /// <summary>runtime 已在 DI composition 時完整驗證；啟動不預熱 client 或建立外部連線。</summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>以 host 停止路徑 await pool drain 與 admission cleanup；runtime 自身保證 idempotency。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => _runtime.DisposeAsync().AsTask();
}
