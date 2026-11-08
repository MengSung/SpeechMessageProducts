/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P2-07 net10 主機的有界對外待辦 worker 與其 Hosting-facing
 * 純值契約。此檔不持有 D365 SDK、HTTP client、Session、credential 或教會設定；
 * 由較低層的 ToolUtility adapter 實作 IOutboundWorkStore，再由每筆工作專屬的
 * IOutboundTaskHandlerScope 建立真正處理器。
 *
 * 主要功能與流程：BackgroundService 每五秒讀取最多五十筆候選，逐筆以 RowVersion
 * CAS 認領，成功後才開啟該筆自己的 handler scope。成功會以同一 owner 版本完成；
 * 429、5xx、逾時依 1／2／4／8／16 分鐘排程重試，第六次失敗改為 Failed 並交給
 * 既有 ExceptionReporting 共用入口。停止時只停止新輪詢，當前 batch 最多排空
 * 三十秒；超時後取消 handler，未完成資料維持 Processing 交由下一輪 stale
 * reclaim，且所有仍在執行的 Task 都由 worker 追蹤並觀察其例外。
 *
 * 使用邊界與維護：IOutboundWorkStore 必須只回傳已由伺服器授權且完成身分驗證的
 * detached snapshot；worker 不把 caller 傳入的教會／使用者值當成權限。每筆 scope
 * 是唯一資源 owner，ExecuteAsync 返回或取消後以 await using 確定性 Dispose；
 * 不在 batch 間快取 scope、token、連線或可變教會設定。測試以離線替身驗證 CAS、
 * 退避、跨 owner 失敗、停機排空及 resource baseline；本檔不宣稱 D365 或 LINE
 * 實機驗收。
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Hosting.Outbound;

/// <summary>Hosting-facing 的三種核定對外待辦類型；數值與 D365 A.4 保持一致。</summary>
public enum OutboundWorkType
{
    /// <summary>產生指定教會的 Rich Menu 圖片與設定。</summary>
    RichMenuGenerate = 100000000,
    /// <summary>產生 QR 圖片。</summary>
    QrImageGenerate = 100000001,
    /// <summary>執行受測試／正式組態約束的撥款查詢。</summary>
    AllotQuery = 100000002
}

/// <summary>Hosting-facing 的對外待辦狀態；不使用 Dataverse 平台 statuscode。</summary>
public enum OutboundWorkStatus
{
    /// <summary>尚待到期並可被認領。</summary>
    Pending = 100000000,
    /// <summary>由某個 RowVersion owner 處理中。</summary>
    Processing = 100000001,
    /// <summary>已完成且不可再次執行。</summary>
    Completed = 100000002,
    /// <summary>已達終止失敗，不再自動重試。</summary>
    Failed = 100000003
}

/// <summary>
/// 單筆 detached 對外待辦快照。它不保存 SDK Entity、連線、credential 或 request
/// 狀態；RowVersion 是跨程序認領及完成回寫的唯一 owner 柵欄。
/// </summary>
public sealed record OutboundWorkItem
{
    /// <summary>
    /// 建立已由 store 讀回的完整快照；缺少正式身分、版本、類型或狀態時 fail closed。
    /// </summary>
    public OutboundWorkItem(
        Guid id,
        string rowVersion,
        Guid churchId,
        Guid? contactId,
        OutboundWorkType type,
        string payloadJson,
        string idempotencyKey,
        OutboundWorkStatus status,
        int attempts,
        DateTime? nextAttemptOn,
        DateTime? claimedOn)
    {
        if (id == Guid.Empty || churchId == Guid.Empty || contactId == Guid.Empty)
            throw new ArgumentException("Outbound work identity is invalid.");
        if (string.IsNullOrWhiteSpace(rowVersion) || rowVersion.Length > 20 ||
            !ulong.TryParse(rowVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version == 0)
            throw new ArgumentException("Outbound work row version is invalid.", nameof(rowVersion));
        if (type is not (OutboundWorkType.RichMenuGenerate or OutboundWorkType.QrImageGenerate or OutboundWorkType.AllotQuery))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (status is not (OutboundWorkStatus.Pending or OutboundWorkStatus.Processing or
            OutboundWorkStatus.Completed or OutboundWorkStatus.Failed))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson.Length > 4000)
            throw new ArgumentException("Outbound work payload is not bounded.", nameof(payloadJson));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new ArgumentException("Outbound work idempotency key is not bounded.", nameof(idempotencyKey));
        if (attempts is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(attempts));
        if ((nextAttemptOn.HasValue && nextAttemptOn.Value.Kind != DateTimeKind.Utc) ||
            (claimedOn.HasValue && claimedOn.Value.Kind != DateTimeKind.Utc) ||
            (status == OutboundWorkStatus.Processing && !claimedOn.HasValue))
            throw new ArgumentException("Outbound work timestamps must be explicit UTC values.");

        Id = id;
        RowVersion = rowVersion;
        ChurchId = churchId;
        ContactId = contactId;
        Type = type;
        PayloadJson = payloadJson;
        IdempotencyKey = idempotencyKey;
        Status = status;
        Attempts = attempts;
        NextAttemptOn = nextAttemptOn;
        ClaimedOn = claimedOn;
    }

    /// <summary>Dataverse 正式主鍵，不是來源事件 ID 或 client 自訂索引。</summary>
    public Guid Id { get; init; }
    /// <summary>本次讀取的資料庫版本；舊 owner 不得覆寫新 owner。</summary>
    public string RowVersion { get; init; }
    /// <summary>伺服器已授權的教會正式 ID。</summary>
    public Guid ChurchId { get; init; }
    /// <summary>選填的正式會友 ID；不得被當成 credential。</summary>
    public Guid? ContactId { get; init; }
    /// <summary>固定的處理器類型。</summary>
    public OutboundWorkType Type { get; init; }
    /// <summary>有界且尚須由 handler 重新驗證的 payload。</summary>
    public string PayloadJson { get; init; }
    /// <summary>跨程序穩定事件鍵。</summary>
    public string IdempotencyKey { get; init; }
    /// <summary>資料庫自訂工作狀態。</summary>
    public OutboundWorkStatus Status { get; init; }
    /// <summary>已排程重試次數，範圍為零至五。</summary>
    public int Attempts { get; init; }
    /// <summary>下一次嘗試的 UTC 到期時間。</summary>
    public DateTime? NextAttemptOn { get; init; }
    /// <summary>最近認領的 UTC 時間。</summary>
    public DateTime? ClaimedOn { get; init; }
}

/// <summary>
/// Hosting 對資料庫的同步邊界。實作必須在 store 內完成組織／租戶授權及正式
/// identity 驗證，並在發布清單前拒絕重複或缺失的正式 ID。
/// </summary>
public interface IOutboundWorkStore
{
    /// <summary>讀取最多指定數量的到期 Pending 或 stale Processing detached 快照。</summary>
    IReadOnlyList<OutboundWorkItem> ReadCandidates(
        DateTime utcNow,
        int maximum,
        CancellationToken cancellationToken);

    /// <summary>以候選 RowVersion 原子認領；競爭失敗回傳 null，其他錯誤原樣拋出。</summary>
    OutboundWorkItem? TryClaim(
        OutboundWorkItem candidate,
        DateTime utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// 以本次認領版本完成、排程重試或終止；舊 owner 競爭失敗回 false。
    /// </summary>
    bool TrySettle(
        OutboundWorkItem claim,
        OutboundWorkStatus status,
        int attempts,
        DateTime utcNow,
        DateTime? nextAttemptOn,
        string? errorCode,
        CancellationToken cancellationToken);
}

/// <summary>每筆工作專屬的處理器；不得把前一筆教會的設定或資源帶入下一筆。</summary>
public interface IOutboundTaskHandler
{
    /// <summary>以呼叫端提供的有界取消 token 執行單筆工作。</summary>
    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 單筆處理器 scope 的唯一資源 owner。worker 會在 handler 完成或取消後確定性
/// Dispose，scope 不可保存 request、Session、credential 或 SDK 連線到下一筆。
/// </summary>
public interface IOutboundTaskHandlerScope : IAsyncDisposable
{
    /// <summary>取得本 scope 專屬且已依工作教會／類型驗證的 handler。</summary>
    IOutboundTaskHandler Handler { get; }
}

/// <summary>依伺服器已驗證的 detached work item 建立單筆處理器 scope。</summary>
public interface IOutboundTaskHandlerFactory
{
    /// <summary>建立工作專屬 scope；失敗不得回傳跨工作共用的可變 handler。</summary>
    ValueTask<IOutboundTaskHandlerScope> OpenAsync(
        OutboundWorkItem item,
        CancellationToken cancellationToken);
}

/// <summary>worker 的可驗證執行參數；預設值就是 P2-07 固定契約。</summary>
public sealed class OutboundTaskWorkerOptions
{
    /// <summary>建立每五秒輪詢、每批五十筆、停止最多三十秒的設定。</summary>
    public OutboundTaskWorkerOptions(
        TimeSpan? pollingInterval = null,
        int batchSize = 50,
        TimeSpan? shutdownTimeout = null)
    {
        PollingInterval = pollingInterval ?? TimeSpan.FromSeconds(5);
        BatchSize = batchSize;
        ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(30);
        if (PollingInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollingInterval));
        if (BatchSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (ShutdownTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
    }

    /// <summary>兩次批次輪詢的最短間隔。</summary>
    public TimeSpan PollingInterval { get; }
    /// <summary>單批最大資料庫候選數量。</summary>
    public int BatchSize { get; }
    /// <summary>停止後目前 batch 的最大排空時間。</summary>
    public TimeSpan ShutdownTimeout { get; }
}

/// <summary>
/// 每五秒輪詢並以 RowVersion 認領對外待辦的 BackgroundService。所有批次與 handler
/// 都是有界、可觀察且有明確清理責任；它不建立第二個通知器或任何未受控佇列。
/// </summary>
public sealed class OutboundTaskWorker : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(8),
        TimeSpan.FromMinutes(16)
    };

    private readonly IOutboundWorkStore _store;
    private readonly IOutboundTaskHandlerFactory _handlerFactory;
    private readonly Action<Exception, string, CancellationToken> _reportFailure;
    private readonly TimeProvider _timeProvider;
    private readonly OutboundTaskWorkerOptions _options;
    private readonly CancellationTokenSource _shutdownDeadline = new();
    private readonly ConcurrentDictionary<Task, byte> _activeTasks = new();
    private int _shutdownStarted;
    private int _disposeRequested;
    private int _deadlineDisposed;

    /// <summary>
    /// 建立 worker。通知委派預設使用既有 ExceptionReporting 共用入口，不保存任何
    /// request 或使用者狀態；測試可注入 operation-local recorder。
    /// </summary>
    public OutboundTaskWorker(
        IOutboundWorkStore store,
        IOutboundTaskHandlerFactory handlerFactory,
        Action<Exception, string, CancellationToken>? reportFailure = null,
        TimeProvider? timeProvider = null,
        OutboundTaskWorkerOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
        _reportFailure = reportFailure ?? ((exception, operation, token) =>
            ExceptionReporting.Report(exception, operation, token));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new OutboundTaskWorkerOptions();
    }

    /// <summary>
    /// 先啟動停機 deadline，再讓 BackgroundService 停止新輪詢並等待當前 batch。
    /// 外部取消可更早結束等待；已在執行的 task 仍由個別 handler token 與追蹤表觀察。
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
            _shutdownDeadline.CancelAfter(_options.ShutdownTimeout);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 停止後才釋放 worker 自有 deadline CTS；若仍有不合作 handler，先保留 CTS
    /// 讓其 token／registration 有效，待最後一個追蹤 task 完成後再確定性 Dispose。
    /// </summary>
    public override void Dispose()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        try { _shutdownDeadline.Cancel(); }
        catch (ObjectDisposedException) { }
        TryDisposeDeadline();
        base.Dispose();
    }

    /// <summary>主輪詢迴圈；停止 token 只阻止新批次，排空 deadline 由 StopAsync 管理。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<OutboundWorkItem> candidates;
            try
            {
                candidates = _store.ReadCandidates(
                    UtcNow(),
                    _options.BatchSize,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Report(exception, "OutboundTaskWorker.ReadCandidates", stoppingToken);
                await DelayAsync(stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                ValidateCandidates(candidates);
            }
            catch (Exception exception)
            {
                Report(exception, "OutboundTaskWorker.CandidateValidation", stoppingToken);
                await DelayAsync(stoppingToken).ConfigureAwait(false);
                continue;
            }

            var batch = new List<Task>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (stoppingToken.IsCancellationRequested) break;
                var task = ProcessCandidateAsync(candidate, _shutdownDeadline.Token);
                _activeTasks.TryAdd(task, 0);
                _ = task.ContinueWith(
                    completed =>
                    {
                        _activeTasks.TryRemove(completed, out _);
                        _ = completed.Exception;
                        TryDisposeDeadline();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                batch.Add(task);
            }

            if (batch.Count > 0)
            {
                var all = Task.WhenAll(batch);
                try
                {
                    await all.WaitAsync(_shutdownDeadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_shutdownDeadline.IsCancellationRequested)
                {
                    // deadline 到期後不再阻塞 Host；individual task 仍被追蹤且會觀察例外，
                    // 未完成資料不執行 settle，保留 Processing 交給 stale reclaim。
                    return;
                }
            }

            if (stoppingToken.IsCancellationRequested) return;
            await DelayAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>執行一筆工作：認領、建立 scope、成功完成或以固定策略排程失敗。</summary>
    private async Task ProcessCandidateAsync(OutboundWorkItem candidate, CancellationToken operationToken)
    {
        OutboundWorkItem? claim;
        try
        {
            claim = _store.TryClaim(candidate, UtcNow(), operationToken);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Report(exception, "OutboundTaskWorker.Claim", operationToken);
            return;
        }

        if (claim is null) return;

        try
        {
            await using var scope = await _handlerFactory.OpenAsync(claim, operationToken)
                .ConfigureAwait(false);
            await scope.Handler.ExecuteAsync(operationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            // 停機 deadline 或外部取消時不寫回；保留 Processing 供下一輪 stale reclaim。
            return;
        }
        catch (Exception exception)
        {
            SettleFailure(claim, exception, operationToken);
            return;
        }

        if (operationToken.IsCancellationRequested) return;
        try
        {
            _store.TrySettle(
                claim,
                OutboundWorkStatus.Completed,
                claim.Attempts,
                UtcNow(),
                null,
                null,
                operationToken);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            // 取消不能把已返回的同步 I/O 重新解讀成失敗；未完成回寫交 stale reclaim。
        }
        catch (Exception exception)
        {
            Report(exception, "OutboundTaskWorker.SettleCompleted", operationToken);
        }
    }

    /// <summary>
    /// 將可恢復錯誤轉成下一個 UTC 到期時間；超過第五次已排程重試才終止。
    /// 不把 exception message、payload 或使用者資料寫入 D365 error code。
    /// </summary>
    private void SettleFailure(OutboundWorkItem claim, Exception exception, CancellationToken operationToken)
    {
        var retryable = TryGetRetryCode(exception, out var errorCode);
        var now = UtcNow();
        var retry = retryable && claim.Attempts < RetryDelays.Length;
        var status = retry ? OutboundWorkStatus.Pending : OutboundWorkStatus.Failed;
        var attempts = retry ? claim.Attempts + 1 : claim.Attempts;
        DateTime? next = retry ? now.Add(RetryDelays[claim.Attempts]) : null;

        bool settled;
        try
        {
            settled = _store.TrySettle(claim, status, attempts, now, next, errorCode, operationToken);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception settleException)
        {
            Report(settleException, "OutboundTaskWorker.Settle", operationToken);
            return;
        }

        if (settled && status == OutboundWorkStatus.Failed)
            Report(exception, "OutboundTaskWorker.TerminalFailure", operationToken);
    }

    /// <summary>只接受規定的 HTTP／逾時失敗，其他例外直接終止且保留真正例外。</summary>
    private static bool TryGetRetryCode(Exception exception, out string errorCode)
    {
        if (exception is TimeoutException ||
            exception is OperationCanceledException)
        {
            errorCode = "Timeout";
            return true;
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
        {
            errorCode = "Http429";
            return true;
        }

        if (exception is HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError })
        {
            errorCode = "Http5xx";
            return true;
        }

        errorCode = "Unhandled";
        return false;
    }

    /// <summary>通知真正失敗；正常 owner cancellation 不進共用告警入口。</summary>
    private void Report(Exception exception, string operation, CancellationToken token)
    {
        if (exception is OperationCanceledException && token.IsCancellationRequested) return;
        try { _reportFailure(exception, operation, token); }
        catch { /* 通知器不得覆蓋 worker 原始生命週期；其自身失敗由共用入口處理。 */ }
    }

    /// <summary>使用明確 UTC 時鐘，不讀取主機 local 時間。</summary>
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// 在建立任何 handler 前驗證同一消費集合的正式資料列身分唯一且有界；
    /// 不以名稱、payload 或位置去重，重複 ID 直接拒絕整批。
    /// </summary>
    private void ValidateCandidates(IReadOnlyList<OutboundWorkItem> candidates)
    {
        if (candidates is null || candidates.Count > _options.BatchSize)
            throw new ArgumentException("Outbound candidate batch exceeds its bounded contract.");
        var ids = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.Id == Guid.Empty || !ids.Add(candidate.Id))
                throw new ArgumentException("Outbound candidate identity is missing or duplicated.");
        }
    }

    /// <summary>最後一個 active task 完成後釋放 worker 自有 CTS，避免 timer／registration 留存。</summary>
    private void TryDisposeDeadline()
    {
        if (Volatile.Read(ref _disposeRequested) == 0 || !_activeTasks.IsEmpty ||
            Interlocked.Exchange(ref _deadlineDisposed, 1) != 0)
            return;
        _shutdownDeadline.Dispose();
    }

    /// <summary>輪詢等待只保存 worker token，不保存 request／教會／使用者狀態。</summary>
    private Task DelayAsync(CancellationToken stoppingToken) =>
        Task.Delay(_options.PollingInterval, stoppingToken);
}
