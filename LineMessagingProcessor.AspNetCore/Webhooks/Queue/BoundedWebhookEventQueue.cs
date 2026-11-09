/*
檔案整體說明
目的與定位：單一 Host 擁有的總量有界 Webhook queue，銜接 P3-02 claim 與 P3-06 scoped handler。
主要功能與流程：依 channel／source 分區 FIFO，兩秒內取得全域槽位才 Commit，成功後發布 bounded Channel；每分區單 consumer，每事件獨立 async scope 及三十秒 CTS。
使用邊界與維護：permit 同時計入提交中、buffer 與執行中，合計不超 Capacity。未接管的 caller 等待者另限 Capacity，借用至兩秒期限或有界 Commit 完成，FIFO 節點只有訊號。停止先拒收、等 handoff、完成 writer、drain 三十秒、取消並等待合作清理，最後 Dispose。最長正常排隊為 Capacity 個有界 handler 的時間；沒有 user map、HTTP 或憑證。handler 必須合作取消及有界清理，CLR 無法強殺 task，本 owner 不遺棄仍使用 scope 的工作。真 Exception 交共用入口，測試以 barrier、假時鐘及計數驗證。
*/
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Queue;

/// <summary>固定分區與全域容量的背景 owner；DI 唯一 Dispose，registration 永遠只借用。</summary>
public sealed class BoundedWebhookEventQueue : IWebhookEventQueue, IHostedService, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly WebhookQueueOptions options;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim capacity;
    private readonly Partition[] partitions;
    private readonly CancellationTokenSource admissionStop = new();
    private readonly CancellationTokenSource processingStop = new();
    private readonly CancellationTokenSource abortDrain = new();
    private IServiceScopeFactory? scopeFactory;
    private ExceptionDiagnostics? diagnostics;
    private Task[] workers = [];
    private TaskCompletionSource? handoffsFinished;
    private Task? startTask;
    private Task? stopTask;
    private Task? disposeTask;
    private bool validated;
    private bool accepting;
    private bool closing;
    private bool disposed;
    private int retained;
    private int buffered;
    private int active;
    private int handoffs;
    private int scopes;
    private int workerCount;

    /// <summary>建立有限 semaphore／channel，consumer 只在 Start 成功後建立；不接管外借依賴。</summary>
    /// <param name="options">已驗證的總容量及固定分區數。</param>
    /// <param name="scopeFactory">Host 的 scope 工廠，不能是 request provider。</param>
    /// <param name="diagnostics">共用通知 owner，必須活至 queue drain 完成。</param>
    /// <param name="timeProvider">外借計時來源，缺省系統時鐘。</param>
    public BoundedWebhookEventQueue(WebhookQueueOptions options, IServiceScopeFactory scopeFactory,
        ExceptionDiagnostics diagnostics, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.options = options; this.scopeFactory = scopeFactory; this.diagnostics = diagnostics;
        clock = timeProvider ?? TimeProvider.System;
        capacity = new SemaphoreSlim(options.Capacity, options.Capacity);
        partitions = Enumerable.Range(0, options.PartitionCount).Select(_ => new Partition(options.Capacity)).ToArray();
    }

    /// <summary>所有 HostedService.Start 之前驗 handler；探測 scope 完整釋放，不保存 scoped instance。</summary>
    internal async Task ValidateHandlerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) { if (validated || closing) return; }
        await using (var scope = scopeFactory!.CreateAsyncScope())
        {
            if (scope.ServiceProvider.GetService<IWebhookEnvelopeHandler>() is null)
                throw new InvalidOperationException("Webhook queue requires an envelope handler.");
        }
        lock (gate) validated = true;
    }

    /// <summary>驗依賴後啟動恰好 N 個 consumer；重入共用工作，不繼承 caller AsyncLocal。</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return startTask ??= StartCoreAsync(cancellationToken);
        }
    }

    /// <summary>啟動與停止在同一鎖線性化；關機已開始就不再啟動 consumer。</summary>
    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await ValidateHandlerAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            if (closing) return;
            cancellationToken.ThrowIfCancellationRequested();
            if (ExecutionContext.IsFlowSuppressed()) StartWorkers();
            else { using (ExecutionContext.SuppressFlow()) StartWorkers(); }
            accepting = true;
        }
    }

    /// <summary>在鎖內且 flow 已抑制時建立固定 tasks；Stop 等待並清空陣列。</summary>
    private void StartWorkers()
    {
        workerCount = partitions.Length;
        workers = partitions.Select(partition => Task.Run(() => ConsumeAsync(partition))).ToArray();
    }

    /// <summary>分區 FIFO 後兩秒內取得容量才 Commit；成功後不再檢查 caller／Host 取消。</summary>
    /// <param name="envelope">detached 事件，成功後才轉交 queue 所有權。</param>
    /// <param name="registration">endpoint 外借 claim，不入 channel、不 Dispose。</param>
    /// <param name="cancellationToken">提交前 caller 取消需拋出；容量或停止拒絕回 false。</param>
    /// <returns>已提交且完成發布才 true；未提交拒絕可重送。</returns>
    public async ValueTask<bool> TryEnqueueAsync(WebhookEnvelope envelope, IWebhookEventRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();
        var partition = partitions[PartitionIndex(envelope)];
        LinkedListNode<TaskCompletionSource> turn;
        lock (gate)
        {
            if (!accepting || handoffs >= options.Capacity) return false;
            if (handoffs++ == 0) handoffsFinished = NewSignal();
            turn = partition.Offers.AddLast(NewSignal());
            if (ReferenceEquals(partition.Offers.First, turn)) turn.Value.TrySetResult();
        }
        var reserved = false;
        var published = false;
        var committing = false;
        try
        {
            using var deadline = new CancellationTokenSource(WebhookQueueOptions.EnqueueTimeout, clock);
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, admissionStop.Token, deadline.Token);
            try
            {
                // 分區交接先於全域容量，否則後筆持槽等待前筆可能形成容量死結。
                await turn.Value.Task.WaitAsync(waiting.Token).ConfigureAwait(false);
                await capacity.WaitAsync(waiting.Token).ConfigureAwait(false);
                reserved = true;
                lock (gate) retained++;
                waiting.Token.ThrowIfCancellationRequested();
                committing = true;
                await registration.CommitAsync(cancellationToken).ConfigureAwait(false);
                lock (gate)
                {
                    // writer 只在 handoffs 歸零後關閉。局部容量等於全域上限，permit
                    // 保證此處不會等待或失敗，不能傳入 caller token 撤銷已提交事件。
                    if (!partition.Channel.Writer.TryWrite(envelope))
                        throw new InvalidOperationException("Reserved webhook publication invariant failed.");
                    buffered++;
                    published = true;
                }
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (!committing && (deadline.IsCancellationRequested || admissionStop.IsCancellationRequested))
            { return false; }
            catch (Exception exception)
            { diagnostics!.Report(exception, "WebhookQueue.Handoff"); throw; }
        }
        finally
        {
            // 已完成的 Task 不應繼續保留 caller claim 或事件；成功資料只留 channel。
            registration = null!; envelope = null!;
            lock (gate)
            {
                if (reserved && !published) { retained--; capacity.Release(); }
                var wasFirst = ReferenceEquals(partition.Offers.First, turn);
                partition.Offers.Remove(turn);
                if (wasFirst) partition.Offers.First?.Value.TrySetResult();
                if (--handoffs == 0) handoffsFinished!.TrySetResult();
            }
        }
    }

    /// <summary>HostedLifecycle.Stopping 提早拒收；已進 Commit 的呼叫仍須發布，不能提前關 writer。</summary>
    internal void BeginStop()
    {
        lock (gate)
        {
            if (closing) return;
            accepting = false; closing = true;
            admissionStop.Cancel();
        }
    }

    /// <summary>所有停止呼叫共用工作；caller 期限只加速取消 drain，不撤銷在途發布。</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task stopping;
        lock (gate)
        {
            BeginStop();
            stopping = stopTask ??= StopCoreAsync();
        }
        using var registration = cancellationToken.UnsafeRegister(static state => ((BoundedWebhookEventQueue)state!).AbortDrain(), this);
        await stopping.ConfigureAwait(false);
    }

    /// <summary>只設定排空訊號；真正 consumer 取消仍在所有 handoff 結束之後。</summary>
    private void AbortDrain()
    {
        lock (gate) { if (!disposed) abortDrain.Cancel(); }
    }

    /// <summary>等在途發布、完成 writer、最多排空三十秒，再取消餘項並真正等 scope／task 結束。</summary>
    private async Task StopCoreAsync()
    {
        if (startTask is not null)
        {
            try { await startTask.ConfigureAwait(false); }
            catch { /* 啟動故障已由 caller 觀察；仍清理尚未啟動的 channel。 */ }
        }
        if (handoffsFinished is not null) await handoffsFinished.Task.ConfigureAwait(false);
        foreach (var partition in partitions) partition.Channel.Writer.TryComplete();
        var completion = Task.WhenAll(workers);
        try { await completion.WaitAsync(WebhookQueueOptions.DrainTimeout, clock, abortDrain.Token).ConfigureAwait(false); }
        catch (TimeoutException) { processingStop.Cancel(); }
        catch (OperationCanceledException) when (abortDrain.IsCancellationRequested) { processingStop.Cancel(); }
        await completion.ConfigureAwait(false);
        lock (gate) workers = [];
    }

    /// <summary>每分區單 reader；取消後清空 buffer 並歸還每個槽位，不保留已取消事件。</summary>
    private async Task ConsumeAsync(Partition partition)
    {
        try
        {
            while (await partition.Channel.Reader.WaitToReadAsync(processingStop.Token).ConfigureAwait(false))
            {
                while (!processingStop.IsCancellationRequested && partition.Channel.Reader.TryRead(out var envelope))
                {
                    lock (gate) { buffered--; active++; }
                    try { await ProcessAsync(envelope).ConfigureAwait(false); }
                    finally
                    {
                        envelope = null!;
                        lock (gate) { active--; retained--; capacity.Release(); }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (processingStop.IsCancellationRequested) { }
        finally
        {
            lock (gate)
            {
                while (partition.Channel.Reader.TryRead(out _)) { buffered--; retained--; capacity.Release(); }
                workerCount--;
            }
        }
    }

    /// <summary>每事件 scope／CTS 獨立；handler 與清理故障分別傳原例外，不讓 Dispose 遮蔽主錯誤。</summary>
    private async Task ProcessAsync(WebhookEnvelope envelope)
    {
        await using var cancellation = new WebhookEventCancellation(clock, diagnostics!, processingStop.Token);
        AsyncServiceScope scope = default;
        var ownsScope = false;
        try
        {
            scope = scopeFactory!.CreateAsyncScope(); ownsScope = true;
            lock (gate) scopes++;
            var handler = scope.ServiceProvider.GetRequiredService<IWebhookEnvelopeHandler>();
            await handler.HandleAsync(envelope, cancellation.Token).ConfigureAwait(false);
            if (cancellation.TimedOut)
                diagnostics!.Report(new TimeoutException("Webhook handler exceeded its deadline."), "WebhookQueue.Handle");
        }
        catch (OperationCanceledException) when (!cancellation.TimedOut && processingStop.IsCancellationRequested) { }
        catch (Exception exception) { diagnostics!.Report(exception, "WebhookQueue.Handle"); }
        finally
        {
            envelope = null!;
            if (ownsScope)
            {
                try { await scope.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) { diagnostics!.Report(exception, "WebhookQueue.ReleaseScope"); }
                finally { lock (gate) scopes--; }
            }
        }
    }

    /// <summary>以完整 channel 與有型別的來源作穩定 FNV-1a；無 user 使用 group／room，再無則固定 channel 後備。</summary>
    private int PartitionIndex(WebhookEnvelope envelope)
    {
        uint hash = 2166136261;
        AddHash(ref hash, envelope.ChannelId);
        var kind = "channel";
        var identity = string.Empty;
        if (envelope.Event.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "userId", "groupId", "roomId" })
            {
                if (source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                    value.GetString() is { Length: > 0 and <= 256 } id &&
                    source.EnumerateObject().Count(p => p.NameEquals(name)) == 1)
                { kind = name; identity = id; break; }
            }
        }
        AddHash(ref hash, kind); AddHash(ref hash, identity);
        return (int)(hash % (uint)partitions.Length);
    }

    /// <summary>長度分隔消除複合鍵串接歧義，不配置複合字串或 user map；uint 溢位為演算法定義。</summary>
    private static void AddHash(ref uint hash, string value)
    {
        unchecked
        {
            hash = (hash ^ (uint)value.Length) * 16777619;
            foreach (var character in value) hash = (hash ^ character) * 16777619;
        }
    }

    /// <summary>訊號 continuation 非同步，所有權鎖內不能執行外部程式。</summary>
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>一致純計數快照供監控／生命週期驗證，不含 channel、使用者或事件。</summary>
    public WebhookQueueSnapshot GetSnapshot()
    { lock (gate) return new(retained, buffered, active, handoffs, scopes, workerCount); }

    /// <summary>DI 唯一清理入口，重入共用工作；所有 task 結束後才 Dispose semaphore／CTS。</summary>
    public ValueTask DisposeAsync()
    { lock (gate) return new ValueTask(disposeTask ??= DisposeCoreAsync()); }

    /// <summary>清空外借參照，不處置 Host 的 provider、TimeProvider 或 diagnostics。</summary>
    private async Task DisposeCoreAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lock (gate)
        {
            disposed = true;
            capacity.Dispose(); admissionStop.Dispose(); processingStop.Dispose(); abortDrain.Dispose();
            scopeFactory = null; diagnostics = null; handoffsFinished = null;
        }
    }

    /// <summary>固定 channel 與短命交接訊號，沒有使用者字典或 request 參照。</summary>
    private sealed class Partition
    {
        /// <summary>只有已提交 immutable envelope 會寫入，停止後必須排空。</summary>
        internal readonly Channel<WebhookEnvelope> Channel;
        /// <summary>FIFO 節點只存訊號，總數由交接上限限制，返回時必定移除。</summary>
        internal readonly LinkedList<TaskCompletionSource> Offers = new();
        /// <summary>各 channel 容量為全域值，合計實際保留由唯一 semaphore 限制；禁止 drop／unbounded。</summary>
        internal Partition(int capacity)
        {
            Channel = System.Threading.Channels.Channel.CreateBounded<WebhookEnvelope>(new BoundedChannelOptions(capacity)
            { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = false });
        }
    }
}

/// <summary>純計數快照；停止後六項皆零。Handoffs 包含仍由 caller 擁有的有界等待者。</summary>
/// <param name="Retained">提交中、buffer 與執行中的槽位總數。</param>
/// <param name="Buffered">已發布未開始的事件。</param>
/// <param name="Active">執行中的事件。</param>
/// <param name="Handoffs">尚未返回的交接，受 Capacity 及等待期限限制。</param>
/// <param name="Scopes">事件擁有的 DI scope。</param>
/// <param name="Workers">尚未結束的固定 consumer。</param>
public sealed record WebhookQueueSnapshot(int Retained, int Buffered, int Active, int Handoffs, int Scopes, int Workers);
