using FluentAssertions;
using SpeechMessage.Dynamics.Tests.Support;
using SqlCoordinatorWorkerProtocol = SpeechMessage.Dynamics.Tests.Support.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 SQL durable host-slot coordinator 的跨行程測試邊界。
/// 這些測試只建立具有限制生命週期的測試專用子行程，且只透過固定、nonce 綁定的協定交換非機密事件；
/// 絕不將 CRM 連線、認證、權杖或父行程的例外與輸出交給子行程。
/// </summary>
public sealed class CrossProcessSqlRuntimeHostSlotCoordinatorTests
{
    /// <summary>
    /// 確認父端只接受符合固定欄位數、正確 nonce 與已宣告事件名稱的 worker 輸出。
    /// 這個純記憶體測試先固定跨行程信任邊界，避免之後的 stdout reader 將任意文字、例外或 stderr
    /// 視為可回傳給測試的資料。
    /// </summary>
    [Fact]
    public void Parent_protocol_accepts_only_nonce_bound_fixed_events()
    {
        const string nonce = "0123456789abcdef0123456789abcdef";

        var ready = SqlCoordinatorWorkerProtocol.ParseWorkerEvent($"P1 {nonce} READY", nonce);

        ready.Kind.Should().Be(WorkerEventKind.Ready);
        ready.Nonce.Should().Be(nonce);
        ready.FencingToken.Should().BeNull();

        var malformed = () => SqlCoordinatorWorkerProtocol.ParseWorkerEvent(
            $"P1 {nonce} READY unexpected-field",
            nonce);

        malformed.Should().Throw<InvalidOperationException>()
            .WithMessage("Worker protocol event is malformed.");
    }

    /// <summary>
    /// 親行程只會對本次產生的 namespace 與 worker 公開的舊 fencing token 執行一次受限 SQL mutation，
    /// 接著 worker 必須自行偵測續租 CAS 已失敗並發出 LEASE_LOST；失去 lease 後不能再取得新的 work permit。
    /// 每個 exit path 都會先讓 child 透過 <c>await using</c> 結束，再用獨立且有界的 cleanup Token 刪除這個唯一生成 namespace；
    /// 因此已取消的情境 timeout 不會遺留 durable 列，也不會刪除其他測試或部署資料。
    /// </summary>
    [LiveSqlFact]
    public async Task Live_sql_cross_process_fencing_loss_rejects_later_work()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        readyTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var runId = Guid.NewGuid().ToString("N");
        var request = WorkerStartRequest.Create(
            runId,
            Guid.NewGuid(),
            "a",
            Guid.NewGuid().ToString("N"));
        var connectionString = Environment.GetEnvironmentVariable(
            SqlRuntimeHostSlotCoordinatorTests.LiveConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                "The opt-in LocalDB connection selector is unavailable for generated namespace fencing.");

        try
        {
            await RunScenarioAsync();
        }
        finally
        {
            await CleanupGeneratedNamespaceAsync(connectionString, request.LeaseNamespaceId);
        }

        async Task RunScenarioAsync()
        {
            await using var worker = await CrossProcessSqlCoordinatorWorker.StartAsync(
                request,
                timeout.Token);

            (await worker.ReadEventAsync(WorkerEventKind.Ready, readyTimeout.Token)).Kind
                .Should().Be(WorkerEventKind.Ready);
            var hostReady = await worker.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostReady,
                timeout.Token);
            var oldFencingToken = hostReady.FencingToken
                ?? throw new InvalidOperationException("The worker did not publish a positive host fencing token.");
            oldFencingToken.Should().BePositive();
            (await worker.SendAndWaitAsync(
                WorkerCommand.AcquireWork,
                WorkerEventKind.WorkHeld,
                timeout.Token)).PositiveValue.Should().Be(oldFencingToken);
            (await worker.SendAndWaitAsync(
                WorkerCommand.ReleaseWork,
                WorkerEventKind.WorkReleased,
                timeout.Token)).Kind.Should().Be(WorkerEventKind.WorkReleased);
            (await worker.SendAndWaitAsync(
                WorkerCommand.AcquireWork,
                WorkerEventKind.WorkHeld,
                timeout.Token)).PositiveValue.Should().Be(oldFencingToken);

            var affectedRows = await CrossProcessSqlCoordinatorFencer.FenceExactlyOneLeaseAsync(
                connectionString,
                request.LeaseNamespaceId,
                oldFencingToken,
                timeout.Token);

            affectedRows.Should().Be(1);
            (await worker.ReadEventAsync(WorkerEventKind.LeaseLost, timeout.Token)).Kind
                .Should().Be(WorkerEventKind.LeaseLost);
            (await worker.SendAndWaitAsync(
                WorkerCommand.AcquireWork,
                WorkerEventKind.WorkDenied,
                timeout.Token)).Kind.Should().Be(WorkerEventKind.WorkDenied);
            await worker.RequestGracefulStopAsync(timeout.Token);
        }
    }

    /// <summary>
    /// 證明三個獨立 OS worker 共同使用同一 durable namespace 時，drain 中的第一個 worker 在其 permit 釋放前仍保留 host slot。
    /// 測試先要求兩個 host 與兩個 work permit，接著確認第三個 host 被拒絕；只有第一個 worker 釋放 permit、完成 drain
    /// 並經過 SQL quarantine 後，第三個 worker 才可取得 slot。所有等待都由單一取消來源界定，讓 live LocalDB 無回應時不遺留 child 行程；
    /// finally 則在所有 child Dispose 後以獨立且有界的 cleanup Token 移除僅屬於本案例的 namespace，避免 timeout 遺留 durable 列。
    /// </summary>
    [LiveSqlFact]
    public async Task Live_sql_cross_process_capacity_and_graceful_drain()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        readyTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var runId = Guid.NewGuid().ToString("N");
        var organizationId = Guid.NewGuid();
        var firstRequest = WorkerStartRequest.Create(runId, organizationId, "a", Guid.NewGuid().ToString("N"));
        var secondRequest = WorkerStartRequest.Create(runId, organizationId, "b", Guid.NewGuid().ToString("N"));
        var thirdRequest = WorkerStartRequest.Create(runId, organizationId, "c", Guid.NewGuid().ToString("N"));
        var connectionString = Environment.GetEnvironmentVariable(
            SqlRuntimeHostSlotCoordinatorTests.LiveConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                "The opt-in LocalDB connection selector is unavailable for generated namespace cleanup.");

        try
        {
            await RunScenarioAsync();
        }
        finally
        {
            await CleanupGeneratedNamespaceAsync(connectionString, firstRequest.LeaseNamespaceId);
        }

        async Task RunScenarioAsync()
        {
            await using var first = await CrossProcessSqlCoordinatorWorker.StartAsync(
                firstRequest,
                timeout.Token);
            await using var second = await CrossProcessSqlCoordinatorWorker.StartAsync(
                secondRequest,
                timeout.Token);
            await using var third = await CrossProcessSqlCoordinatorWorker.StartAsync(
                thirdRequest,
                timeout.Token);

            // READY 不依賴 SQL schema 或 host acquisition，故以更短期限先隔離 process/protocol 問題；
            // 後續完整情境仍共用較長但固定的 timeout，避免 LocalDB 斷言變成無上限等待。
            (await first.ReadEventAsync(WorkerEventKind.Ready, readyTimeout.Token)).Kind
                .Should().Be(WorkerEventKind.Ready);
            (await second.ReadEventAsync(WorkerEventKind.Ready, readyTimeout.Token)).Kind
                .Should().Be(WorkerEventKind.Ready);
            (await third.ReadEventAsync(WorkerEventKind.Ready, readyTimeout.Token)).Kind
                .Should().Be(WorkerEventKind.Ready);

            (await first.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostReady,
                timeout.Token)).FencingToken.Should().BePositive();
            (await second.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostReady,
                timeout.Token)).FencingToken.Should().BePositive();
            (await first.SendAndWaitAsync(
                WorkerCommand.AcquireWork,
                WorkerEventKind.WorkHeld,
                timeout.Token)).PositiveValue.Should().BePositive();
            (await second.SendAndWaitAsync(
                WorkerCommand.AcquireWork,
                WorkerEventKind.WorkHeld,
                timeout.Token)).PositiveValue.Should().BePositive();

            await first.SendAndWaitAsync(
                WorkerCommand.BeginDrain,
                WorkerEventKind.DrainBegin,
                timeout.Token);
            await third.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostDenied,
                timeout.Token);
            await first.SendAndWaitAsync(
                WorkerCommand.ReleaseWork,
                WorkerEventKind.WorkReleased,
                timeout.Token);
            await first.SendAndWaitAsync(
                WorkerCommand.AwaitDrain,
                WorkerEventKind.Drained,
                timeout.Token);

            // worker runtime 的固定 quarantine 為兩秒；額外緩衝只容許 SQL 時鐘與排程收斂，
            // 不會替換成任意輪詢或無上限等待，因此第三個 worker 的重新取得仍是可重現的 durability 斷言。
            await Task.Delay(TimeSpan.FromMilliseconds(2200), timeout.Token);

            (await third.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostReady,
                timeout.Token)).FencingToken.Should().BePositive();

            await first.RequestGracefulStopAsync(timeout.Token);
            await second.RequestGracefulStopAsync(timeout.Token);
            await third.RequestGracefulStopAsync(timeout.Token);
        }
    }

    /// <summary>
    /// 以三個獨立 OS worker 實際填滿兩個 durable host slot，再強制終止其中一個持有者，
    /// 驗證替代 worker 不能因為 parent 已觀察到 child exit 就提早接手。測試先明確拒絕替代取得，再以一個
    /// 有界 30 秒 TTL delay 後執行恰好一次 post-TTL acquire；SQL coordinator 會在該 acquire 內將過期列轉為
    /// 2 秒 quarantine，因此該次仍必須拒絕。第二個有界 2.2 秒 delay 過後才允許最後一次 acquire 成功。
    /// 這兩個時間點直接對應 SQL state transition，沒有輪詢、retry loop 或延長本機 lease，並保護 crash 後舊程序可能尚未被網路完全觀察到時的
    /// aggregate capacity/fencing 邊界。每一個 child 都由 <c>await using</c> 唯一擁有；crash API 會等待
    /// 被殺 worker 的 process tree 離開，其他 worker 則先收到 STOP 再由 Dispose 路徑確認退出。最外層 finally
    /// 一律使用新的有限 cleanup token，且只刪除此測試產生的 namespace，因此 assertion、協定或 child 失敗都不會
    /// 遺留 worker 或擴大刪除到其他 LocalDB 資料。
    /// </summary>
    [LiveSqlFact]
    public async Task Live_sql_cross_process_crash_waits_for_ttl_and_quarantine_before_replacement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(70));
        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        readyTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var runId = Guid.NewGuid().ToString("N");
        var organizationId = Guid.NewGuid();
        var crashedRequest = WorkerStartRequest.Create(runId, organizationId, "a", Guid.NewGuid().ToString("N"));
        var survivingRequest = WorkerStartRequest.Create(runId, organizationId, "b", Guid.NewGuid().ToString("N"));
        var replacementRequest = WorkerStartRequest.Create(runId, organizationId, "c", Guid.NewGuid().ToString("N"));
        var connectionString = Environment.GetEnvironmentVariable(
            SqlRuntimeHostSlotCoordinatorTests.LiveConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                "The opt-in LocalDB connection selector is unavailable for generated namespace cleanup.");

        try
        {
            await RunScenarioAsync();
        }
        finally
        {
            await CleanupGeneratedNamespaceAsync(connectionString, crashedRequest.LeaseNamespaceId);
        }

        async Task RunScenarioAsync()
        {
            await using var crashed = await CrossProcessSqlCoordinatorWorker.StartAsync(
                crashedRequest,
                timeout.Token);
            await using var surviving = await CrossProcessSqlCoordinatorWorker.StartAsync(
                survivingRequest,
                timeout.Token);
            await using var replacement = await CrossProcessSqlCoordinatorWorker.StartAsync(
                replacementRequest,
                timeout.Token);

            (await crashed.ReadEventAsync(WorkerEventKind.Ready, readyTimeout.Token)).Kind
                .Should().Be(WorkerEventKind.Ready);
            (await surviving.ReadEventAsync(WorkerEventKind.Ready, readyTimeout.Token)).Kind
                .Should().Be(WorkerEventKind.Ready);
            (await replacement.ReadEventAsync(WorkerEventKind.Ready, readyTimeout.Token)).Kind
                .Should().Be(WorkerEventKind.Ready);

            (await crashed.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostReady,
                timeout.Token)).FencingToken.Should().BePositive();
            (await surviving.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostReady,
                timeout.Token)).FencingToken.Should().BePositive();

            await crashed.TerminateForCrashAsync(timeout.Token);
            (await replacement.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostDenied,
                timeout.Token)).Kind.Should().Be(WorkerEventKind.HostDenied);

            // 此延遲只等待固定 lease TTL；下一次 acquire 必須實際執行 SQL expiry -> quarantine transition。
            await Task.Delay(TimeSpan.FromMilliseconds(30200), timeout.Token);

            (await replacement.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostDenied,
                timeout.Token)).Kind.Should().Be(WorkerEventKind.HostDenied);

            // Quarantine 由前一個 post-TTL acquire 在 SQL 端開始；只等待固定 2 秒加小量排程裕度，不輪詢資料列。
            await Task.Delay(TimeSpan.FromMilliseconds(2200), timeout.Token);

            (await replacement.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostReady,
                timeout.Token)).FencingToken.Should().BePositive();

            await surviving.RequestGracefulStopAsync(timeout.Token);
            await replacement.RequestGracefulStopAsync(timeout.Token);
        }
    }

    /// <summary>
    /// 在已取得真實 LocalDB host slot 的獨立 worker 中注入固定、同機且有界的 coordinator outage，
    /// 要求 worker 只在故障 coordinator 的 ActiveDatabaseOperations 回到零、原 host slot 已安全 drain/release 後
    /// 發出 <c>OUTAGE_CLEAN</c>。接著驗證同一個 worker 拒絕新的 work 與 host admission，防止故障時退回
    /// 本機無限制配額或保留已失效的 readiness。child、stdin/stdout/stderr 與 runtime 均仍由 worker wrapper 的
    /// async disposal 唯一擁有；外層 finally 使用獨立有界 token 清除唯一生成 namespace，避免 timeout 取消 cleanup。
    /// </summary>
    [LiveSqlFact]
    public async Task Live_sql_cross_process_coordinator_outage_fails_closed_and_cleans_operations()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        readyTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var runId = Guid.NewGuid().ToString("N");
        var request = WorkerStartRequest.Create(
            runId,
            Guid.NewGuid(),
            "a",
            Guid.NewGuid().ToString("N"));
        var connectionString = Environment.GetEnvironmentVariable(
            SqlRuntimeHostSlotCoordinatorTests.LiveConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                "The opt-in LocalDB connection selector is unavailable for generated namespace cleanup.");

        try
        {
            await RunScenarioAsync();
        }
        finally
        {
            await CleanupGeneratedNamespaceAsync(connectionString, request.LeaseNamespaceId);
        }

        async Task RunScenarioAsync()
        {
            await using var worker = await CrossProcessSqlCoordinatorWorker.StartAsync(
                request,
                timeout.Token);

            (await worker.ReadEventAsync(WorkerEventKind.Ready, readyTimeout.Token)).Kind
                .Should().Be(WorkerEventKind.Ready);
            (await worker.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostReady,
                timeout.Token)).FencingToken.Should().BePositive();
            (await worker.SendAndWaitAsync(
                WorkerCommand.OutageProbe,
                WorkerEventKind.OutageClean,
                timeout.Token)).Kind.Should().Be(WorkerEventKind.OutageClean);
            (await worker.SendAndWaitAsync(
                WorkerCommand.AcquireWork,
                WorkerEventKind.WorkDenied,
                timeout.Token)).Kind.Should().Be(WorkerEventKind.WorkDenied);
            (await worker.SendAndWaitAsync(
                WorkerCommand.AcquireHost,
                WorkerEventKind.HostDenied,
                timeout.Token)).Kind.Should().Be(WorkerEventKind.HostDenied);
            await worker.RequestGracefulStopAsync(timeout.Token);
        }
    }

    /// <summary>
    /// 確認測試只會直接啟動已建置的 worker 可執行檔，並在有界期限內取得唯一且 nonce 綁定的 READY 事件。
    /// 此測試刻意不使用 <c>dotnet run</c>；因此在 worker 尚未建置時，失敗必須明確指出可執行檔無法解析，
    /// 藉此證明後續容量測試確實跨越 OS 行程邊界，而非退回同一個測試行程。
    /// </summary>
    [LiveSqlFact]
    public async Task Live_sql_cross_process_worker_returns_nonce_bound_ready()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runId = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");

        await using var worker = await CrossProcessSqlCoordinatorWorker.StartAsync(
            WorkerStartRequest.Create(runId, Guid.NewGuid(), "a", nonce),
            timeout.Token);

        var ready = await worker.ReadEventAsync(WorkerEventKind.Ready, timeout.Token);

        ready.Kind.Should().Be(WorkerEventKind.Ready);
        ready.Nonce.Should().Be(nonce);
    }

    /// <summary>
    /// 以每次呼叫新建且有界的取消來源清理唯一生成的 durable namespace；它不共用情境逾時 Token，
    /// 因此 assertion、協定失敗或情境逾時後，仍會在所有 <c>await using</c> worker 完成 Dispose 的 finally 路徑中執行。
    /// 清理器本身再次限制 LocalDB 與 <c>cross-process-</c> 加 32 位小寫十六進位 namespace，
    /// 所以這個 owner 不能擴大成刪除其他測試、部署或使用者控制的 SQL 資料；若有 SQL 問題，受控例外會讓測試明確失敗而非靜默保留列。
    /// </summary>
    private static async Task CleanupGeneratedNamespaceAsync(
        string connectionString,
        string leaseNamespaceId)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await CrossProcessSqlCoordinatorNamespaceCleanup.DeleteGeneratedNamespaceAsync(
                connectionString,
                leaseNamespaceId,
                cleanupTimeout.Token)
            .ConfigureAwait(false);
    }
}
