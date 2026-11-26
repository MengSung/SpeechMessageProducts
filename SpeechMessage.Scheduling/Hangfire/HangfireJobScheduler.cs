/*
檔案整體說明
目的與定位：將中立IJobScheduler轉成單一產品Hangfire SQL工作，提供跨程序的穩定鍵覆寫、週期註冊與取消。
主要功能與流程：完整驗產品／組織／租戶及16KiB深度8的JSON後，取得每工作鍵五秒分散鎖。候選含隨機generation；SQL ledger發布後舊候選即失效，建立後但尚未發布的孤兒無法通過dispatcher核對。週期使用明確IANA時區及Relaxed補跑。
使用邊界與維護：SQL連線、鎖、交易各由單次using擁有，無記憶體鍵快取或跨租戶可變集合。同一鍵不可搬移scope；完成／取消ledger保留30日，未執行一次工作到期後30日，最遠排365日。payload只能存操作純值，任意外部副作用的原子冪等仍由handler資料層保證。
*/
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace SpeechMessage.Scheduling.Hangfire;

/// <summary>固定部署產品的持久化排程器，不接受scope改變既有工作所有權。</summary>
internal sealed class HangfireJobScheduler(HangfireSchedulingRuntime runtime) : IJobScheduler
{
    /// <summary>驗證後在同一工作鎖內發布下一generation；舊queued工作即使刪除失敗也不能執行。</summary>
    public async Task ScheduleAsync(JobKey key, DateTimeOffset runAtUtc, JobScope scope, string payloadJson, CancellationToken ct)
    {
        Validate(key, scope, payloadJson);
        if (runAtUtc.Offset != TimeSpan.Zero || runAtUtc > runtime.Clock.UtcNow.AddDays(365))
            throw new ArgumentException("A bounded UTC schedule is required.", nameof(runAtUtc));
        ct.ThrowIfCancellationRequested();
        using var operation = await runtime.EnterOperationAsync(ct).ConfigureAwait(false);
        using var connection = runtime.Storage.GetConnection();
        var address = Address(key);
        using var held = connection.AcquireDistributedLock(address, TimeSpan.FromSeconds(5));
        ct.ThrowIfCancellationRequested();
        var previous = connection.GetAllEntriesFromHash(address);
        RequireOwnership(previous, key, scope);
        if (previous?.GetValueOrDefault("state") is "Completed" or "Skipped")
            return;
        if (previous?.GetValueOrDefault("state") is "Uncertain" or "Failed")
            throw new InvalidOperationException("The prior work requires explicit reconciliation.");
        var generation = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var job = CreateJob(key, scope, generation, payloadJson);
        var jobId = runtime.Client.Create(job, runAtUtc <= runtime.Clock.UtcNow
            ? new EnqueuedState() : new ScheduledState(runAtUtc.UtcDateTime))
            ?? throw new InvalidOperationException("Scheduling candidate was not created.");
        var ledger = NewLedger(key, scope, generation, "once", jobId);
        Write(connection, address, ledger,
            TimeSpan.FromDays(30) + (runAtUtc > runtime.Clock.UtcNow ? runAtUtc - runtime.Clock.UtcNow : TimeSpan.Zero));
        RemovePrevious(previous, address);
    }

    /// <summary>以同一SQL鎖更新週期及ledger；明確時區與Relaxed使停機錯過任意次只補一個occurrence。</summary>
    public async Task ScheduleRecurringAsync(JobKey key, string cron, string timeZoneId, JobScope scope, string payloadJson, CancellationToken ct)
    {
        Validate(key, scope, payloadJson);
        if (string.IsNullOrEmpty(cron) || cron.Length > 128 || string.IsNullOrEmpty(timeZoneId) || timeZoneId.Length > 128)
            throw new ArgumentException("A bounded cron and IANA zone are required.", nameof(cron));
        // 共用時鐘先驗核定IANA映射；不讓TimeZoneInfo接受Windows名稱或自動回伺服器時區。
        _ = runtime.Clock.ToChurchTime(runtime.Clock.UtcNow.UtcDateTime, timeZoneId);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        ct.ThrowIfCancellationRequested();
        using var operation = await runtime.EnterOperationAsync(ct).ConfigureAwait(false);
        using var connection = runtime.Storage.GetConnection();
        var address = Address(key);
        using var held = connection.AcquireDistributedLock(address, TimeSpan.FromSeconds(5));
        ct.ThrowIfCancellationRequested();
        var previous = connection.GetAllEntriesFromHash(address);
        RequireOwnership(previous, key, scope);
        var generation = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        Manager().AddOrUpdate(address, CreateJob(key, scope, generation, payloadJson), cron,
            new RecurringJobOptions { TimeZone = zone, MisfireHandling = MisfireHandlingMode.Relaxed });
        Write(connection, address, NewLedger(key, scope, generation, "recurring", ""), null);
        if (previous is not null && previous.GetValueOrDefault("kind") == "once" &&
            previous.GetValueOrDefault("job") is { Length: > 0 } oldJob)
            runtime.Client.ChangeState(oldJob, new DeletedState(), null);
    }

    /// <summary>先發布取消tombstone再移除Hangfire入口；任何部分刪除故障都不能恢復已撤回版本。</summary>
    public async Task CancelAsync(JobKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!string.Equals(key.Product, runtime.Options.Product, StringComparison.Ordinal))
            throw new ArgumentException("The work belongs to another deployment.", nameof(key));
        ct.ThrowIfCancellationRequested();
        using var operation = await runtime.EnterOperationAsync(ct).ConfigureAwait(false);
        using var connection = runtime.Storage.GetConnection();
        var address = Address(key);
        using var held = connection.AcquireDistributedLock(address, TimeSpan.FromSeconds(5));
        ct.ThrowIfCancellationRequested();
        var ledger = connection.GetAllEntriesFromHash(address);
        if (ledger is null) return;
        if (ledger.GetValueOrDefault("key") != key.ToString())
            throw new InvalidOperationException("Scheduling identity conflicts with stored work.");
        if (ledger.GetValueOrDefault("kind") == "once" && ledger.GetValueOrDefault("state") is "Completed" or "Skipped")
            return;
        ledger["generation"] = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        ledger["state"] = "Cancelled";
        Write(connection, address, ledger, TimeSpan.FromDays(30));
        RemovePrevious(ledger, address);
    }

    /// <summary>排程與dispatch共用邊界，純值scope與部署相符才能存取SQL或建立處理器。</summary>
    internal void Validate(JobKey key, JobScope scope, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(scope);
        if (!string.Equals(key.Product, runtime.Options.Product, StringComparison.Ordinal) ||
            !string.Equals(scope.Product, key.Product, StringComparison.Ordinal) ||
            scope.OrganizationId != runtime.Options.OrganizationId || scope.TenantId == Guid.Empty)
            throw new ArgumentException("The scheduling scope does not match the deployment.", nameof(scope));
        ArgumentNullException.ThrowIfNull(payloadJson);
        if (Encoding.UTF8.GetByteCount(payloadJson) > 16 * 1024)
            throw new ArgumentException("Scheduling payload exceeds its bound.", nameof(payloadJson));
        using var document = JsonDocument.Parse(payloadJson, new JsonDocumentOptions { MaxDepth = 8 });
    }

    /// <summary>SHA只限制SQL索引長度；實際取出後仍比對原始三段鍵與scope，不以hash當授權。</summary>
    internal static string Address(JobKey key) => "schedule:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));

    /// <summary>已存在鍵不可轉給另一租戶；完整原始鍵亦守護雜湊碰撞或資料毀損。</summary>
    internal static void RequireOwnership(Dictionary<string, string>? ledger, JobKey key, JobScope scope)
    {
        if (ledger is not null && (ledger.Count > 12 ||
            ledger.GetValueOrDefault("key") != key.ToString() ||
            ledger.GetValueOrDefault("scope") != JsonSerializer.Serialize(scope)))
            throw new InvalidOperationException("Stored scheduling ownership does not match.");
    }

    /// <summary>以單一SQL交易發布完整ledger及期限；支援SQL持久化hash，不接受不具期限能力的後備store。</summary>
    internal static void Write(IStorageConnection connection, string address, Dictionary<string, string> ledger, TimeSpan? lifetime,
        string? occurrenceAddress = null, Dictionary<string, string>? occurrence = null)
    {
        using var transaction = connection.CreateWriteTransaction();
        if (transaction is not JobStorageTransaction sql)
            throw new NotSupportedException("Scheduling requires transactional SQL hash expiry.");
        transaction.SetRangeInHash(address, ledger);
        if (lifetime is { } expiry) sql.ExpireHash(address, expiry);
        else sql.PersistHash(address);
        if (occurrenceAddress is not null && occurrence is not null)
        {
            // 週期每次occurrence有獨立30日tombstone；與主ledger同交易，避免較舊重送在新一次完成後重跑。
            transaction.SetRangeInHash(occurrenceAddress, occurrence);
            sql.ExpireHash(occurrenceAddress, TimeSpan.FromDays(30));
        }
        transaction.Commit();
    }

    /// <summary>傳遞純值給固定dispatcher；PerformContext與取消token由Hangfire在執行時替換，不持久化request。</summary>
    private static Job CreateJob(JobKey key, JobScope scope, string generation, string payload) =>
        Job.FromExpression<ScheduledJobDispatcher>(dispatcher =>
            dispatcher.ExecuteAsync(key, scope, generation, payload, null!, CancellationToken.None));

    /// <summary>建立新候選完整ledger，沒有先前handler、使用者或半成品參照。</summary>
    private static Dictionary<string, string> NewLedger(JobKey key, JobScope scope, string generation, string kind, string job) =>
        new(StringComparer.Ordinal)
        {
            ["key"] = key.ToString(), ["scope"] = JsonSerializer.Serialize(scope), ["generation"] = generation,
            ["kind"] = kind, ["state"] = "Pending", ["job"] = job, ["lastOccurrence"] = ""
        };

    /// <summary>只撤回本工作鍵的舊入口；工作鎖及generation比對仍是競爭安全邊界。</summary>
    private void RemovePrevious(Dictionary<string, string>? previous, string address)
    {
        if (previous is null) return;
        if (previous.GetValueOrDefault("kind") == "recurring") Manager().RemoveIfExists(address);
        else if (previous.GetValueOrDefault("job") is { Length: > 0 } oldJob)
            runtime.Client.ChangeState(oldJob, new DeletedState(), null);
    }

    /// <summary>明確借用本產品store、filter、時區及共用UTC時鐘，不讀取Hangfire全域來源。</summary>
    private RecurringJobManager Manager() => new(runtime.Storage, runtime.FilterProvider,
        new global::Hangfire.DefaultTimeZoneResolver(), () => runtime.Clock.UtcNow.UtcDateTime);
}
