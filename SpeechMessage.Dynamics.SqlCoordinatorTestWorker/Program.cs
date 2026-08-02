using System.Threading.Channels;

namespace SpeechMessage.Dynamics.SqlCoordinatorTestWorker;

/// <summary>
/// 測試專用 SQL coordinator worker 的受限行程進入點。
/// Program 只擁有啟動參數、stdin reader、stdout bounded channel、writer task 與 shutdown CTS；它不讀取環境組態、
/// 不接受連線字串或認證，並且在每個結束路徑完成 channel、await writer、釋放 stream 及清除短暫參考。
/// </summary>
internal static class Program
{
    private const int OutputChannelCapacity = 16;

    /// <summary>
    /// 啟動固定協定、發送 nonce 繫結 READY，並在 Task 1 階段只接受 STOP。
    /// 後續 runtime 命令會在已觀察其 RED 測試後接入相同 channel；任何未知或過早的有效命令均回報固定 lifecycle 類別，
    /// 不會把例外、設定或資源狀態寫到 stdout。
    /// </summary>
    internal static async Task<int> Main(string[] arguments)
    {
        WorkerStartupArguments? startup = null;
        CancellationTokenSource? shutdownCts = null;
        Channel<WorkerEvent>? output = null;
        Task<int>? writerTask = null;
        BoundedAsciiRecordReader? inputReader = null;
        WorkerRuntime? runtime = null;
        var exitCode = 1;

        try
        {
            if (!WorkerProtocol.TryParseStartupArguments(arguments, out startup) || startup is null)
            {
                return 2;
            }

            shutdownCts = new CancellationTokenSource();
            output = Channel.CreateBounded<WorkerEvent>(new BoundedChannelOptions(OutputChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });

            // stdout 的唯一 writer 是此 task；Console.Out 由 runtime 擁有，Program 與未來 runtime callback 只能寫入 bounded channel。
            writerTask = WorkerProtocol.WriteEventsAsync(
                output.Reader,
                Console.Out,
                startup.Nonce,
                shutdownCts.Token);
            inputReader = new BoundedAsciiRecordReader(Console.OpenStandardInput());
            runtime = new WorkerRuntime(
                startup,
                (workerEvent, cancellationToken) => WorkerProtocol.QueueEventAsync(
                    output.Writer,
                    workerEvent,
                    cancellationToken));

            if (!await TryQueueEventAsync(
                    output.Writer,
                    new WorkerEvent(WorkerEventKind.Ready),
                    shutdownCts.Token).ConfigureAwait(false))
            {
                return 3;
            }

            while (true)
            {
                var input = await inputReader.ReadAsync(shutdownCts.Token).ConfigureAwait(false);
                if (input.Status == WorkerRecordReadStatus.EndOfStream)
                {
                    exitCode = 0;
                    break;
                }

                if (input.Status != WorkerRecordReadStatus.Record ||
                    !WorkerProtocol.TryParseCommand(
                        inputReader.CurrentRecord.Span[..input.Length],
                        startup.Nonce,
                        out var command))
                {
                    await TryQueueEventAsync(
                        output.Writer,
                        new WorkerEvent(WorkerEventKind.Fail, FailureCategory: WorkerFailureCategory.Protocol),
                        shutdownCts.Token).ConfigureAwait(false);
                    exitCode = 4;
                    break;
                }

                if (command.Kind == WorkerCommandKind.Stop)
                {
                    try
                    {
                        await runtime.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        await TryQueueEventAsync(
                                output.Writer,
                                new WorkerEvent(
                                    WorkerEventKind.Fail,
                                    FailureCategory: WorkerFailureCategory.Lifecycle),
                                shutdownCts.Token)
                            .ConfigureAwait(false);
                        exitCode = 6;
                        break;
                    }

                    if (!await TryQueueEventAsync(
                            output.Writer,
                            new WorkerEvent(WorkerEventKind.Stopped),
                            shutdownCts.Token).ConfigureAwait(false))
                    {
                        return 5;
                    }

                    exitCode = 0;
                    break;
                }

                try
                {
                    var workerEvent = await runtime.ExecuteAsync(command, shutdownCts.Token)
                        .ConfigureAwait(false);
                    if (!await TryQueueEventAsync(output.Writer, workerEvent, shutdownCts.Token)
                            .ConfigureAwait(false))
                    {
                        return 5;
                    }
                }
                catch (WorkerRuntimeCommandException exception)
                {
                    await TryQueueEventAsync(
                            output.Writer,
                            new WorkerEvent(WorkerEventKind.Fail, FailureCategory: exception.FailureCategory),
                            shutdownCts.Token)
                        .ConfigureAwait(false);
                    exitCode = 6;
                    break;
                }
                catch
                {
                    await TryQueueEventAsync(
                            output.Writer,
                            new WorkerEvent(WorkerEventKind.Fail, FailureCategory: WorkerFailureCategory.Admission),
                            shutdownCts.Token)
                        .ConfigureAwait(false);
                    exitCode = 6;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (shutdownCts?.IsCancellationRequested == true)
        {
            exitCode = 7;
        }
        finally
        {
            if (runtime is not null)
            {
                try
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    if (exitCode == 0)
                    {
                        exitCode = 9;
                    }
                }
            }

            if (output is not null)
            {
                output.Writer.TryComplete();
            }

            if (writerTask is not null)
            {
                // 先完成 channel 再 await writer，讓 STOPPED/FAIL 等已入列事件有唯一、可觀察的 flush 與釋放路徑。
                var emittedEventCount = await writerTask.ConfigureAwait(false);
                if (exitCode == 0 && emittedEventCount <= 0)
                {
                    exitCode = 8 - emittedEventCount;
                }
            }

            if (inputReader is not null)
            {
                await inputReader.DisposeAsync().ConfigureAwait(false);
            }

            shutdownCts?.Cancel();
            shutdownCts?.Dispose();
            runtime = null;
            startup = null;
            arguments = Array.Empty<string>();
        }

        return exitCode;
    }

    /// <summary>
    /// 將事件交給唯一 stdout writer，並把 channel 關閉或 shutdown 視為本行程不可恢復的輸出失敗。
    /// 此 helper 不建立替代 writer 或 fire-and-forget task，故 stdout、背壓與取消皆有單一 owner。
    /// </summary>
    private static async ValueTask<bool> TryQueueEventAsync(
        ChannelWriter<WorkerEvent> writer,
        WorkerEvent workerEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await WorkerProtocol.QueueEventAsync(writer, workerEvent, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
