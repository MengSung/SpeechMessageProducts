/*
檔案整體說明
目的與定位：Hangfire固定可啟用的共用dispatcher，將持久化純值交給產品scoped validator及handler，不從job資料反射任意型別。
主要功能與流程：每次取Host operation lease、工作SQL鎖及完整ledger，先驗generation與原始scope。完成的一次工作不重跑；同一次occurrence上次Running且結果不明則明確失敗，後續全新occurrence獨立執行，不被歷史故障永久封鎖。每次另建async DI scope重新查租戶狀態，F11以固定job鍵及本次持久occurrence建立ILogger關聯scope，可選Host Meter涵蓋validator／handler及async scope釋放；成功且scope完整釋放後才發布Completed；正常速率延期交狀態filter重排。
使用邊界與維護：工作鎖只限相同鍵，不把全產品序列化。payload與handler只活至單次操作；scope先DisposeAsync，然後SQL鎖／連線與Host lease釋放。停止取消保留明確Failed且不重送；真正失敗保留原Exception供安全告警，不將其訊息或InnerException交Hangfire持久化。
*/
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpeechMessage.Hosting.Observability;
using System.Security.Cryptography;
using System.Text;

namespace SpeechMessage.Scheduling.Hangfire;

/// <summary>只供Hangfire固定方法啟用；呼叫本型別仍必須通過持久化generation及scope核對。</summary>
public sealed class ScheduledJobDispatcher
{
    private readonly HangfireSchedulingRuntime runtime;
    private readonly IServiceScopeFactory scopes;

    /// <summary>由單一Host factory建立，只借用無request狀態的owner與scope factory。</summary>
    internal ScheduledJobDispatcher(HangfireSchedulingRuntime runtime, IServiceScopeFactory scopes)
    {
        this.runtime = runtime; this.scopes = scopes;
    }

    /// <summary>執行前重查scope，完成後完整清理才發布終態；context與token由Hangfire當次提供。</summary>
    /// <param name="key">持久化的完整伺服器事件鍵。</param>
    /// <param name="scope">只有產品／組織／租戶純值。</param>
    /// <param name="generation">與SQL ledger相同才可執行的候選版本。</param>
    /// <param name="payloadJson">有界操作純值，不得包含秘密或連線。</param>
    /// <param name="context">Hangfire本次工作識別與連線；不保存至handler或共享狀態。</param>
    /// <param name="cancellationToken">Hangfire執行取消；正常停止必須完整drain。</param>
    public async Task ExecuteAsync(JobKey key, JobScope scope, string generation, string payloadJson,
        PerformContext? context, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(context);
            new HangfireJobScheduler(runtime).Validate(key, scope, payloadJson);
            using var operation = await runtime.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
            using var connection = runtime.Storage.GetConnection();
            var address = HangfireJobScheduler.Address(key);
            using var held = connection.AcquireDistributedLock(address, TimeSpan.FromSeconds(5));
            cancellationToken.ThrowIfCancellationRequested();
            var ledger = connection.GetAllEntriesFromHash(address);
            if (ledger is null || ledger.GetValueOrDefault("generation") != generation) return;
            HangfireJobScheduler.RequireOwnership(ledger, key, scope);
            var occurrence = context.BackgroundJob.Id;
            var priorState = ledger.GetValueOrDefault("state");
            var recurring = ledger.GetValueOrDefault("kind") == "recurring";
            if (!recurring && ledger.GetValueOrDefault("job") != occurrence) return;
            var occurrenceAddress = recurring ? "occurrence:" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{address}:{generation}:{occurrence}"))) : null;
            var occurrenceLedger = occurrenceAddress is null ? null : connection.GetAllEntriesFromHash(occurrenceAddress);
            if (occurrenceLedger is not null)
            {
                if (occurrenceLedger.Count != 3 || occurrenceLedger.GetValueOrDefault("generation") != generation ||
                    occurrenceLedger.GetValueOrDefault("job") != occurrence)
                    throw new SchedulingFailureException(new InvalidOperationException("Occurrence identity conflicts."), permanent: true);
                if (occurrenceLedger.GetValueOrDefault("state") is "Completed" or "Skipped") return;
            }
            occurrenceLedger ??= recurring ? new Dictionary<string, string>(StringComparer.Ordinal)
                { ["generation"] = generation, ["job"] = occurrence,
                    ["state"] = ledger.GetValueOrDefault("lastOccurrence") == occurrence ? priorState ?? "Pending" : "Pending" } : null;
            // 週期主ledger只保存最後觀察，不能把某次不明結果變成整個工作鍵永久停擺。
            // 每次occurrence各自保留終態；舊次Running／Failed仍拒絕重送，全新次從Pending開始。
            var executionState = recurring ? occurrenceLedger!.GetValueOrDefault("state") : priorState;

            // 完整主ledger與occurrence必須同一次SQL交易發布；這個local owner不逃離目前工作。
            void Publish(string state, TimeSpan? lifetime)
            {
                ledger["state"] = state;
                if (occurrenceLedger is not null) occurrenceLedger["state"] = state;
                HangfireJobScheduler.Write(connection, address, ledger, lifetime, occurrenceAddress, occurrenceLedger);
            }
            // 失敗處置若再次遇SQL故障，保留兩個真實原因並拒絕盲目重試，而非讓次要例外蓋掉主故障。
            void PublishFailure(string state, Exception primary, bool normalCancellation = false)
            {
                try { Publish(state, recurring ? null : TimeSpan.FromDays(30)); }
                catch (Exception persistence)
                {
                    if (!normalCancellation) runtime.Diagnostics.Report(primary, "Scheduling.Job.PrimaryFailure");
                    runtime.Diagnostics.Report(persistence, "Scheduling.Job.PublishFailure");
                    throw new SchedulingFailureException(normalCancellation ? persistence : primary, permanent: true);
                }
            }
            if (priorState == "Cancelled" || executionState is "Completed" or "Skipped") return;
            if (executionState is "Running" or "Uncertain" or "Failed")
            {
                Publish("Uncertain", recurring ? null : TimeSpan.FromDays(30));
                throw new SchedulingFailureException(new InvalidOperationException(
                    "The prior execution outcome is uncertain and requires reconciliation."), permanent: true);
            }
            ledger["lastOccurrence"] = occurrence;
            Publish("Running", recurring ? null : TimeSpan.FromDays(30));
            var handlerReturned = false;
            try
            {
                var result = await RunScopedAsync(key, scope, payloadJson, occurrence, cancellationToken).ConfigureAwait(false);
                handlerReturned = true;
                Publish(result.DeferredUntil is null ? (result.Skipped ? "Skipped" : "Completed") : "Pending",
                    recurring ? null : TimeSpan.FromDays(30));
                if (result.DeferredUntil is { } resume) throw new SchedulingDeferredException(resume);
            }
            catch (SchedulingDeferredException) { throw; }
            catch (SchedulingFailureException failure) when (failure.Permanent)
            {
                PublishFailure("Uncertain", failure.Original);
                throw;
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                PublishFailure("Failed", exception, normalCancellation: true);
                throw new SchedulingFailureException(exception, permanent: true, normalCancellation: true);
            }
            catch (Exception exception)
            {
                // handler已回傳後若終態寫入失敗，外部副作用已可能完成，不能把它改為可重送。
                PublishFailure(handlerReturned ? "Uncertain" : "Retryable", exception);
                if (handlerReturned) throw new SchedulingFailureException(exception, permanent: true);
                throw;
            }
        }
        catch (SchedulingDeferredException) { throw; }
        catch (SchedulingFailureException) { throw; }
        catch (Exception exception) { throw new SchedulingFailureException(exception); }
    }

    /// <summary>新建單次async scope；清理失敗保留原始主要例外並另外交安全入口，不漏掉依賴釋放。</summary>
    private async Task<(bool Skipped, DateTimeOffset? DeferredUntil)> RunScopedAsync(
        JobKey key, JobScope jobScope, string payload, string occurrence, CancellationToken ct)
    {
        var scope = scopes.CreateAsyncScope();
        IDisposable? observation = null;
        IDisposable? correlationScope = null;
        Exception? primary = null;
        var handlerReturned = false;
        (bool Skipped, DateTimeOffset? DeferredUntil) result = default;
        try
        {
            var services = scope.ServiceProvider;
            observation = services.GetService<HostingMeters>()?.TrackSchedulerJob();
            correlationScope = services.GetService<ILoggerFactory>()?.CreateLogger<ScheduledJobDispatcher>()
                .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = CorrelationIds.ForJob(key.ToString(), occurrence) });
            var state = services.GetRequiredService<ScheduledJobExecution>();
            state.IsExecuting = true;
            var validator = services.GetRequiredService<IScheduledJobScopeValidator>();
            if (!await validator.CanExecuteAsync(jobScope, ct).ConfigureAwait(false)) result = (true, null);
            else
            {
                ct.ThrowIfCancellationRequested();
                var handlers = services.GetServices<IScheduledJobHandler>().Take(65).ToArray();
                if (handlers.Length > 64 || handlers.Any(h =>
                        JobKey.RequireIdentifier(h.Name, "handler") != h.Name) ||
                    handlers.Select(h => h.Name).Distinct(StringComparer.Ordinal).Count() != handlers.Length)
                    throw new InvalidOperationException("Scheduling handler registration conflicts.");
                var handler = handlers.SingleOrDefault(h => string.Equals(h.Name, key.Name, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("No scheduling handler is registered.");
                await handler.ExecuteAsync(jobScope, payload, ct).ConfigureAwait(false);
                handlerReturned = true;
                ct.ThrowIfCancellationRequested();
                result = (false, state.DeferredUntil);
            }
        }
        catch (Exception exception) { primary = exception; }
        finally
        {
            try { await scope.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup)
            {
                if (primary is null) primary = cleanup;
                else runtime.Diagnostics.Report(cleanup, "Scheduling.Job.DisposeScope");
            }
            finally
            {
                try { observation?.Dispose(); }
                catch (Exception cleanup)
                {
                    if (primary is null) primary = cleanup;
                    else runtime.Diagnostics.Report(cleanup, "Scheduling.Job.DisposeObservation");
                }
                finally
                {
                    try { correlationScope?.Dispose(); }
                    catch (Exception cleanup)
                    {
                        if (primary is null) primary = cleanup;
                        else runtime.Diagnostics.Report(cleanup, "Scheduling.Job.DisposeCorrelation");
                    }
                }
            }
        }
        if (primary is not null)
        {
            if (handlerReturned && primary is not OperationCanceledException)
                throw new SchedulingFailureException(primary, permanent: true);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
        return result;
    }
}

/// <summary>每次執行唯一的scoped延期純值；沒有AsyncLocal、static或跨工作的可變參照。</summary>
internal sealed class ScheduledJobExecution
{
    /// <summary>只有dispatcher啟用；一般獨立預算查詢不能替任意工作修改排程。</summary>
    internal bool IsExecuting { get; set; }
    /// <summary>本次handler必須立即返回的最早重排時間，scope關閉後即不可再存取。</summary>
    internal DateTimeOffset? DeferredUntil { get; set; }
}
