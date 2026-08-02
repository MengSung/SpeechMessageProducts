using System;
using System.IO;
using System.IO.Pipes;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 使用 Windows 本機具名管線建立官方 CRM Worker 的雙向 IPC 連線。
/// 每次呼叫只配置一個 <see cref="NamedPipeClientStream"/>；若連線失敗，
/// 本類別會先釋放該控制代碼再拋出例外，避免失敗重試累積 Handle 或記憶體。
/// </summary>
public sealed class NamedPipeOfficialWorkerConnector : IOfficialWorkerPipeConnector
{
    private const int DefaultConnectTimeoutMilliseconds = 30_000;
    private const int MaximumConnectTimeoutMilliseconds = 120_000;
    private readonly int _connectTimeoutMilliseconds;

    /// <summary>
    /// 建立具有有限連線等待時間的具名管線連接器。
    /// </summary>
    /// <param name="connectTimeoutMilliseconds">
    /// 等待 Gateway 管線伺服器的毫秒數；必須介於 1 與 120000 之間。
    /// </param>
    public NamedPipeOfficialWorkerConnector(
        int connectTimeoutMilliseconds = DefaultConnectTimeoutMilliseconds)
    {
        if (connectTimeoutMilliseconds <= 0 ||
            connectTimeoutMilliseconds > MaximumConnectTimeoutMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectTimeoutMilliseconds),
                "The named-pipe connection timeout is outside the approved bounds.");
        }

        _connectTimeoutMilliseconds = connectTimeoutMilliseconds;
    }

    /// <inheritdoc />
    public Stream Connect(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException(
                "The worker pipe name is required.",
                nameof(pipeName));
        }

        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(_connectTimeoutMilliseconds);
            return pipe;
        }
        catch
        {
            // 連線尚未把所有權移交給 process host，因此失敗路徑必須在此立即釋放。
            pipe.Dispose();
            throw;
        }
    }
}
