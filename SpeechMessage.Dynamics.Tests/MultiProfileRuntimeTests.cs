// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs
// 目的：驗證多 Profile Runtime Manager 的 Alias 路由、Admission→Queue→Current Active Runtime 順序、
//       原子 replace-and-drain、Active+最多一個 Draining、強制取消與確定性 Shutdown。
//
// 所有 Client／Admission／Runtime 都是程序內 tracking fake，不連真實 CRM、ADFS、SQL 或 WinRM。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 固定 Local／Central Gateway 共用 Multi-Profile Manager 的核心生命週期與隔離語意。
/// 測試特別要求 Queue 中尚未 dispatch 的工作不能持有舊 Runtime；只有取得 Admission Permit 後才取得當下 Active Generation，
/// 且任何 Runtime acquisition failure 都要先釋放 Permit，避免 ActivePermits、Memory 與 Host Slot 永久保留。
/// </summary>
public sealed class MultiProfileRuntimeTests
{
    /// <summary>
    /// 證明 Manager 只有在每個初始 Profile 的 Admission Manager 已取得安全 Host Slot 後才標記 Ready。
    /// 這個順序避免 Gateway 在尚未受跨主機 fencing 與 Aggregate Budget 保護時先接受流量。
    /// </summary>
    [Fact]
    public async Task Initialization_ensures_each_profile_host_slot_before_ready()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm82"), CreateDefinition("crm91"));

        await manager.InitializeAsync(CancellationToken.None);

        manager.GetSnapshot().IsReady.Should().BeTrue();
        factory.GetRuntime("crm82", 1).Admission.EnsureHostSlotCount.Should().Be(1);
        factory.GetRuntime("crm91", 1).Admission.EnsureHostSlotCount.Should().Be(1);
    }

    /// <summary>
    /// 證明 crm82 與 crm91 只會路由到各自 Alias Slot 的 Active Runtime Client，
    /// Manager 不會因為兩者同在一個 Process 就共用 Client、Token、Credential 或 Transport。
    /// </summary>
    [Fact]
    public async Task Crm82_and_crm91_route_to_their_own_active_runtime()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm82"), CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);

        var result82 = await manager.ExecuteAsync(CreateRequest("crm82"));
        var result91 = await manager.ExecuteAsync(CreateRequest("crm91"));

        result82.Succeeded.Should().BeTrue();
        result91.Succeeded.Should().BeTrue();
        factory.GetRuntime("crm82", 1).Client.ExecutionCount.Should().Be(1);
        factory.GetRuntime("crm91", 1).Client.ExecutionCount.Should().Be(1);
    }

    /// <summary>
    /// 證明未知 Alias 會在 Secret、Admission、Runtime Factory 與 Transport 之前 fail closed。
    /// 這可防止任意 URL Alias 被錯誤送到預設 CRM Endpoint，也避免透過錯誤差異列舉內部 Profile。
    /// </summary>
    [Fact]
    public async Task Unknown_alias_fails_before_runtime_factory_or_transport()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var creationCountBeforeUnknownAlias = factory.CreateCount;

        var result = await manager.ExecuteAsync(CreateRequest("unknown-profile"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.NotReady);
        factory.CreateCount.Should().Be(creationCountBeforeUnknownAlias);
        factory.GetRuntime("crm91", 1).Client.ExecutionCount.Should().Be(0);
    }

    /// <summary>
    /// 證明新 Generation 在同一 Alias Slot 鎖內原子發布後，後續要求只能取得新 Runtime；
    /// 發布前已持有舊 Lease 的工作仍留在 Draining Generation，直到自行完成或超過有界取消期限。
    /// </summary>
    [Fact]
    public async Task Replacement_publishes_new_generation_atomically_and_old_generation_gets_no_new_leases()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var oldRuntime = factory.GetRuntime("crm91", 1);
        oldRuntime.TryAcquireExecution(out var heldOldLease).Should().BeTrue();

        var replacementTask = manager.ReplaceAsync(CreateDefinition("crm91"));
        await WaitUntilAsync(
            () => manager.GetSnapshot().Profiles.Any(profile => profile.Key.Generation == 2),
            "new generation publication");

        var result = await manager.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeTrue();
        oldRuntime.Client.ExecutionCount.Should().Be(0);
        factory.GetRuntime("crm91", 2).Client.ExecutionCount.Should().Be(1);

        await heldOldLease!.DisposeAsync();
        await replacementTask;
    }

    /// <summary>
    /// 證明舊 Generation 的既有工作未釋放前，Runtime 不會 Dispose Client／Token／Handler；
    /// Lease 歸零後 replacement 才完成並標記舊 Runtime 已回收。
    /// </summary>
    [Fact]
    public async Task Existing_old_generation_work_drains_before_disposal()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var oldRuntime = factory.GetRuntime("crm91", 1);
        oldRuntime.TryAcquireExecution(out var heldOldLease).Should().BeTrue();

        var replacementTask = manager.ReplaceAsync(CreateDefinition("crm91"));
        await WaitUntilAsync(() => oldRuntime.State == DynamicsProfileRuntimeState.Draining, "old runtime drain start");

        oldRuntime.DisposeCount.Should().Be(0);
        replacementTask.IsCompleted.Should().BeFalse();

        await heldOldLease!.DisposeAsync();
        await replacementTask;
        oldRuntime.DisposeCount.Should().Be(1);
        oldRuntime.State.Should().Be(DynamicsProfileRuntimeState.Disposed);
    }

    /// <summary>
    /// 證明第一個 replacement 尚在建立／warm-up／drain 時，第三個 live Generation 不會被配置。
    /// 第二個平行 Replace 必須在呼叫 Factory 前失敗，避免同一 Alias 同時存在 Active 加兩個 Draining Runtime。
    /// </summary>
    [Fact]
    public async Task Rapid_third_replacement_is_rejected_without_allocating_a_generation()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var oldRuntime = factory.GetRuntime("crm91", 1);
        oldRuntime.TryAcquireExecution(out var heldOldLease).Should().BeTrue();

        var firstReplacement = manager.ReplaceAsync(CreateDefinition("crm91"));
        await WaitUntilAsync(
            () => manager.GetSnapshot().Profiles.Count(profile => profile.Key.ProfileAlias == "crm91") == 2,
            "active plus one draining publication");
        var createCountBeforeRejectedReplacement = factory.CreateCount;

        var act = async () => await manager.ReplaceAsync(CreateDefinition("crm91"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        factory.CreateCount.Should().Be(createCountBeforeRejectedReplacement);

        await heldOldLease!.DisposeAsync();
        await firstReplacement;
    }

    /// <summary>
    /// 證明舊 Generation 已完成狀態與資源清理、只是在 cleanup 結尾回報錯誤時，Manager 仍會把該錯誤交還操作者，
    /// 但不會繼續以 <c>slot.Draining</c> 強引用已 Disposed 的 Runtime。這個區分可同時保住可觀察性與可恢復性：
    /// cleanup failure 不可被吞掉，而已無法再次清理或執行的 Client／Token／Handler graph 也不可永久阻塞後續設定替換。
    /// 第二次 Replace 必須能建立下一個 Generation，且任一時刻仍只存在一個 Active 與最多一個 Draining Runtime。
    /// </summary>
    [Fact]
    public async Task Disposed_draining_cleanup_failure_is_reported_and_does_not_block_later_replacement()
    {
        await using var factory = new TrackingRuntimeFactory();
        var cleanupFailure = new InvalidOperationException("Injected disposed runtime cleanup failure.");
        factory.FailNextRuntimeDisposal("crm91", cleanupFailure);
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var originalRuntime = factory.GetRuntime("crm91", 1);

        var firstReplacement = async () => await manager.ReplaceAsync(CreateDefinition("crm91"));

        var thrown = await firstReplacement.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(cleanupFailure);
        originalRuntime.State.Should().Be(DynamicsProfileRuntimeState.Disposed);
        originalRuntime.DisposeCount.Should().Be(1);
        var snapshotAfterFailure = manager.GetSnapshot();
        snapshotAfterFailure.Profiles.Should().ContainSingle();
        snapshotAfterFailure.Profiles[0].Key.Generation.Should().Be(2);
        snapshotAfterFailure.Profiles[0].State.Should().Be(DynamicsProfileRuntimeState.Active);

        await manager.ReplaceAsync(CreateDefinition("crm91"));

        factory.CreateCount.Should().Be(3);
        var finalSnapshot = manager.GetSnapshot();
        finalSnapshot.Profiles.Should().ContainSingle();
        finalSnapshot.Profiles[0].Key.Generation.Should().Be(3);
        finalSnapshot.Profiles[0].State.Should().Be(DynamicsProfileRuntimeState.Active);
    }

    /// <summary>
    /// 證明第一次 Replace 已發布新 Active、但 caller cancellation 在舊 Generation 尚有 Lease 時中止 drain，
    /// Manager 不會遺失仍未 Disposed 的 Draining Runtime，也不會讓下一次 Replace 直接配置第三套資源。
    /// 第二次 Replace 必須先成為唯一 replacement owner，等待舊 Lease 歸零並完成既有 Draining cleanup；
    /// 在此之前 Factory CreateCount 必須維持不變，避免 Client、Token、Handler、CTS 或 Admission Registration 疊成三代。
    /// </summary>
    [Fact]
    public async Task Unfinished_draining_runtime_is_retried_before_allocating_the_next_generation()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var originalRuntime = factory.GetRuntime("crm91", 1);
        originalRuntime.TryAcquireExecution(out var heldOriginalLease).Should().BeTrue();
        using var firstReplacementCts = new CancellationTokenSource();
        try
        {
            var firstReplacement = manager.ReplaceAsync(
                CreateDefinition("crm91"),
                firstReplacementCts.Token);
            await WaitUntilAsync(
                () => originalRuntime.State == DynamicsProfileRuntimeState.Draining,
                "original runtime entering drain before caller cancellation");
            firstReplacementCts.Cancel();

            var firstReplacementCompletion = async () => await firstReplacement;
            await firstReplacementCompletion.Should().ThrowAsync<OperationCanceledException>();
            originalRuntime.State.Should().Be(DynamicsProfileRuntimeState.Draining);
            originalRuntime.DisposeCount.Should().Be(0);
            factory.CreateCount.Should().Be(2);

            var secondReplacement = manager.ReplaceAsync(CreateDefinition("crm91"));

            secondReplacement.IsCompleted.Should().BeFalse();
            factory.CreateCount.Should().Be(2);
            await heldOriginalLease!.DisposeAsync();
            await secondReplacement;

            originalRuntime.State.Should().Be(DynamicsProfileRuntimeState.Disposed);
            originalRuntime.DisposeCount.Should().Be(1);
            factory.CreateCount.Should().Be(3);
            var finalSnapshot = manager.GetSnapshot();
            finalSnapshot.Profiles.Should().ContainSingle();
            finalSnapshot.Profiles[0].Key.Generation.Should().Be(3);
            finalSnapshot.Profiles[0].State.Should().Be(DynamicsProfileRuntimeState.Active);
        }
        finally
        {
            // RED 階段的舊實作會在第二次 Replace 入口同步拒絕；即使斷言因此提前失敗，
            // 測試仍必須歸還原始 Lease，讓 Manager cleanup 不會因測試自己保留 ownership 而等待到 drain timeout。
            await heldOriginalLease!.DisposeAsync();
        }
    }

    /// <summary>
    /// 證明 Manager Shutdown 會透過 linked cancellation 立即結束仍在等待舊 Lease 的 Replace lifecycle owner，
    /// 而不是只取消 Factory／Warm-up、卻讓發布後 drain 繼續等待完整的 caller timeout。Shutdown 仍不會提早 Dispose
    /// 使用中的 Runtime；測試在觀察 Replace 收到取消後才歸還 Lease，最終由 Manager 的唯一 Dispose owner 完成全部清理。
    /// 這可避免 Host 已進入 NotReady／停止接流量後，關閉流程仍被一個未使用 Manager shutdown token 的背景 Replace 長期保留。
    /// </summary>
    [Fact]
    public async Task Manager_shutdown_cancels_the_published_replacement_drain_owner()
    {
        await using var factory = new TrackingRuntimeFactory();
        var initial = CreateDefinition(
            "crm91",
            drainTimeout: TimeSpan.FromSeconds(30),
            cancellationGracePeriod: TimeSpan.FromSeconds(30));
        await using var manager = CreateManager(factory, initial);
        await manager.InitializeAsync(CancellationToken.None);
        var originalRuntime = factory.GetRuntime("crm91", 1);
        originalRuntime.TryAcquireExecution(out var heldOriginalLease).Should().BeTrue();
        var replacement = manager.ReplaceAsync(CreateDefinition("crm91"));
        await WaitUntilAsync(
            () => originalRuntime.State == DynamicsProfileRuntimeState.Draining,
            "published replacement waiting for the original runtime lease");

        var shutdown = manager.DisposeAsync().AsTask();
        try
        {
            var observeReplacementCancellation = async () =>
                await replacement.WaitAsync(TimeSpan.FromSeconds(2));

            await observeReplacementCancellation.Should().ThrowAsync<OperationCanceledException>();
            shutdown.IsCompleted.Should().BeFalse(
                "the Manager must still retain the old Runtime until its active execution lease is released");
        }
        finally
        {
            await heldOriginalLease!.DisposeAsync();
            try
            {
                await replacement;
            }
            catch (OperationCanceledException)
            {
                // Replace 的 owner 已正確收到 Manager shutdown；最終 Runtime cleanup 由 DisposeCoreAsync 接管。
            }

            await shutdown;
        }

        manager.GetSnapshot().Profiles.Should().BeEmpty();
        factory.CreatedRuntimes.Should().OnlyContain(runtime => runtime.DisposeCount == 1);
    }

    /// <summary>
    /// 證明自然 drain 超過期限後會取消舊 Generation 的 Retirement Token，
    /// 讓仍在執行的 Client／Stream／Retry 能離開並釋放 Lease，而不是無限等待或提早 Dispose 使用中資源。
    /// </summary>
    [Fact]
    public async Task Drain_timeout_cancels_bounded_old_generation_work()
    {
        await using var factory = new TrackingRuntimeFactory();
        var initial = CreateDefinition(
            "crm91",
            drainTimeout: TimeSpan.FromMilliseconds(50),
            cancellationGracePeriod: TimeSpan.FromSeconds(2));
        await using var manager = CreateManager(factory, initial);
        await manager.InitializeAsync(CancellationToken.None);
        var oldRuntime = factory.GetRuntime("crm91", 1);
        oldRuntime.TryAcquireExecution(out var heldOldLease).Should().BeTrue();
        var retirementObserved = WaitForCancellationAsync(heldOldLease!.RetirementToken);

        var replacementTask = manager.ReplaceAsync(CreateDefinition("crm91"));
        await retirementObserved.WaitAsync(TimeSpan.FromSeconds(2));

        await heldOldLease.DisposeAsync();
        await replacementTask;
        oldRuntime.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 證明 Manager Shutdown 可重複呼叫，會拒絕新路由、Drain 所有 Active／Draining Runtime，
    /// 清空 Alias Catalog 並讓所有 Runtime 強引用可被釋放，不留下 Timer、Semaphore 或 Background Task。
    /// </summary>
    [Fact]
    public async Task Manager_shutdown_is_idempotent_and_releases_all_strong_references()
    {
        await using var factory = new TrackingRuntimeFactory();
        var manager = CreateManager(factory, CreateDefinition("crm82"), CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);

        await manager.DisposeAsync();
        await manager.DisposeAsync();
        manager.Dispose();

        manager.GetSnapshot().IsReady.Should().BeFalse();
        manager.GetSnapshot().Profiles.Should().BeEmpty();
        factory.CreatedRuntimes.Should().OnlyContain(runtime => runtime.DisposeCount == 1);
    }

    /// <summary>
    /// 證明 queued-undispatched Request 在等待 Admission 時不會持有舊 Runtime。
    /// Generation 交換完成後才取得 Runtime Lease，因此排隊工作會送到新 Active，而不是讓舊 Generation 被 Queue 強引用。
    /// </summary>
    [Fact]
    public async Task Queued_request_uses_the_new_active_generation_after_swap()
    {
        await using var factory = new TrackingRuntimeFactory(localMaxInFlight: 1);
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var oldRuntime = factory.GetRuntime("crm91", 1);
        var blocker = await oldRuntime.AdmissionManager.AcquireAsync(
            CreateEnvelope("crm91", "queue-blocker"),
            CancellationToken.None);
        blocker.Succeeded.Should().BeTrue();

        var queuedExecution = manager.ExecuteAsync(CreateRequest("crm91", "queued-workload"));
        await WaitUntilAsync(
            () => oldRuntime.AdmissionManager.GetSnapshot().Queued == 1,
            "request entering admission queue");

        await manager.ReplaceAsync(CreateDefinition("crm91"));
        await blocker.Permit!.DisposeAsync();
        var result = await queuedExecution;

        result.Succeeded.Should().BeTrue();
        oldRuntime.Client.ExecutionCount.Should().Be(0);
        factory.GetRuntime("crm91", 2).Client.ExecutionCount.Should().Be(1);
    }

    /// <summary>
    /// 證明 Admission 已成功但 Active Runtime 無法取得 Execution Lease 時，Combined Lease Provider 會立即釋放 Permit。
    /// ActivePermits 必須回到零，否則後續流量會逐步耗盡容量並形成 Memory／Semaphore retention。
    /// </summary>
    [Fact]
    public async Task Runtime_acquisition_failure_releases_the_admission_permit()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var runtime = factory.GetRuntime("crm91", 1);
        runtime.RejectNewExecution = true;

        var result = await manager.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.NotReady);
        runtime.AdmissionManager.GetSnapshot().ActivePermits.Should().Be(0);
        runtime.Client.ExecutionCount.Should().Be(0);
    }

    /// <summary>
    /// 證明 Runtime Lease 已建立後若取得流程拋出例外，即使 Runtime Lease 自己的 Dispose 也失敗，
    /// Manager 仍必須繼續歸還 Admission Permit，並同時保留原始取得錯誤與清理錯誤。
    /// 這個 rollback 順序是容量安全邊界：若第一個 cleanup failure 中斷後續清理，ActivePermits 與 Semaphore
    /// 名額會永久遺留，使同一 Organization 在沒有任何實際外呼時仍逐步耗盡可用容量。
    /// </summary>
    [Fact]
    public async Task Acquisition_rollback_releases_permit_when_runtime_lease_cleanup_fails()
    {
        await using var factory = new TrackingRuntimeFactory();
        await using var manager = CreateManager(factory, CreateDefinition("crm91"));
        await manager.InitializeAsync(CancellationToken.None);
        var runtime = factory.GetRuntime("crm91", 1);
        var acquisitionFailure = new InvalidOperationException("Injected runtime acquisition failure.");
        var runtimeCleanupFailure = new InvalidOperationException("Injected runtime lease cleanup failure.");
        runtime.FailNextExecutionAcquisitionAfterLease(acquisitionFailure, runtimeCleanupFailure);

        var act = async () => await manager.AcquireAsync(
            CreateEnvelope("crm91", "rollback-workload"),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.Flatten().InnerExceptions.Should().Contain(acquisitionFailure);
        thrown.Which.Flatten().InnerExceptions.Should().Contain(runtimeCleanupFailure);
        runtime.ActiveExecutionCount.Should().Be(0);
        runtime.AdmissionManager.GetSnapshot().ActivePermits.Should().Be(0);
    }

    /// <summary>
    /// 證明初始 Catalog 建立失敗且候選 Runtime 清理也失敗時，Manager 仍會完成狀態回滾、
    /// 將原始 Factory failure 與 cleanup failure 一起回報，並允許下一次 Initialize 建立全新的 Generation。
    /// 若 cleanup exception 提前跳出 catch，<c>_initializationTask</c> 會永久保留失敗 Task，
    /// Local／Central Gateway 便無法在暫時性故障排除後重新進入 Ready，也可能保留候選 Runtime 強引用。
    /// </summary>
    [Fact]
    public async Task Initialization_cleanup_failure_preserves_original_error_and_allows_retry()
    {
        await using var factory = new TrackingRuntimeFactory();
        var initializationFailure = new InvalidOperationException("Injected crm91 factory failure.");
        var cleanupFailure = new InvalidOperationException("Injected crm82 runtime cleanup failure.");
        factory.FailNextRuntimeDisposal("crm82", cleanupFailure);
        factory.FailNextCreation("crm91", initializationFailure);
        await using var manager = CreateManager(factory, CreateDefinition("crm82"), CreateDefinition("crm91"));

        var firstAttempt = async () => await manager.InitializeAsync(CancellationToken.None);

        var thrown = await firstAttempt.Should().ThrowAsync<AggregateException>();
        thrown.Which.Flatten().InnerExceptions.Should().Contain(initializationFailure);
        thrown.Which.Flatten().InnerExceptions.Should().Contain(cleanupFailure);
        manager.GetSnapshot().IsReady.Should().BeFalse();
        manager.GetSnapshot().Profiles.Should().BeEmpty();
        factory.GetRuntime("crm82", 1).DisposeCount.Should().Be(1);

        await manager.InitializeAsync(CancellationToken.None);

        manager.GetSnapshot().IsReady.Should().BeTrue();
        factory.GetRuntime("crm82", 2).State.Should().Be(DynamicsProfileRuntimeState.Active);
        factory.GetRuntime("crm91", 2).State.Should().Be(DynamicsProfileRuntimeState.Active);
    }

    /// <summary>
    /// 建立 Manager 並把測試 Definitions 轉成唯讀陣列；Manager 必須自行驗證空集合、重複 Alias 與大小寫碰撞。
    /// </summary>
    private static DynamicsProfileRuntimeManager CreateManager(
        IDynamicsProfileRuntimeFactory factory,
        params DynamicsProfileDefinition[] definitions)
        => new(definitions, factory);

    /// <summary>
    /// 建立短生命週期測試 Profile。每個 Alias 使用自己的 Organization／Namespace，
    /// 除了 Queue 測試之外不共享 Admission，可讓斷言精確聚焦於 Manager 行為。
    /// </summary>
    private static DynamicsProfileDefinition CreateDefinition(
        string alias,
        TimeSpan? drainTimeout = null,
        TimeSpan? cancellationGracePeriod = null)
    {
        var aliasHash = alias.Aggregate(17, (current, character) => unchecked(current * 31 + character));
        var suffix = Math.Abs((long)aliasHash).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var organizationId = alias.Equals("crm82", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("82828282-8282-8282-8282-828282828282")
            : Guid.Parse("91919191-9191-9191-9191-919191919191");
        return new DynamicsProfileDefinition(
            alias,
            new DynamicsWebApiOptions
            {
                OrganizationBaseUri = $"https://{alias}.example.test/Org/",
                CeVersion = alias.Equals("crm82", StringComparison.OrdinalIgnoreCase) ? "8.2" : "9.1",
                MaxConnectionsPerServer = 1,
                Admission = new OrganizationAdmissionOptions
                {
                    ExpectedOrganizationId = organizationId,
                    AggregateMaxInFlight = 6,
                    MaximumRuntimeHosts = 6,
                    LocalQueueCapacity = 4,
                    MaxInFlightAndQueuedPerWorkload = 4,
                    QueueAdmissionTimeoutSeconds = 5,
                    AdmissionNamespaceId = "admission-" + suffix,
                    LeaseNamespaceId = "lease-" + suffix,
                    RequireDurableHostCoordinator = false
                }
            },
            warmUpOnActivation: false,
            drainTimeout,
            cancellationGracePeriod);
    }

    /// <summary>
    /// 建立已註冊的 WhoAmI 操作要求。WorkloadSubjectId 是測試服務工作負載，不代表使用者 Session。
    /// </summary>
    private static OperationExecutionRequest CreateRequest(
        string alias,
        string workload = "test-workload")
        => new()
        {
            ProfileAlias = alias,
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = workload,
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

    /// <summary>
    /// 建立可直接送進 Tracking Admission Manager 的有界 Dispatch Envelope，
    /// 不含 HttpContext、JWT、Cookie、Credential、Token 或任意 URL。
    /// </summary>
    private static DispatchEnvelope CreateEnvelope(string alias, string workload)
        => new()
        {
            ProfileAlias = alias,
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = workload,
            TemplateId = "WhoAmI",
            TemplateHash = new string('a', 64),
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            EstimatedEnvelopeBytes = 512
        };

    /// <summary>
    /// 以短輪詢等待非同步狀態轉換。每次延遲都可取消且總等待有硬上限，
    /// 避免測試失敗時留下無限 Background Task 或佔用測試執行緒。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        condition().Should().BeTrue(description);
    }

    /// <summary>
    /// 把 CancellationToken 轉成有界可等待 Task；Registration 在 Task 完成後立即 Dispose，
    /// 避免 Cancellation Callback 被測試長期保留。
    /// </summary>
    private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            signal);
        await signal.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 建立 Tracking Runtime 的測試 Factory。Factory 讓相同 Alias 的所有 Generation 共用一個 Tracking Admission Manager，
    /// 但每次 CreateAsync 都建立新的 Runtime 與 Client，正好模擬「只共用容量、不共用可變連線狀態」的生產契約。
    /// </summary>
    private sealed class TrackingRuntimeFactory : IDynamicsProfileRuntimeFactory, IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly int _localMaxInFlight;
        private readonly Dictionary<string, TrackingAdmissionManager> _admissions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Alias, long Generation), TrackingRuntime> _runtimes = new();
        private readonly Dictionary<string, Exception> _nextCreationFailures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Exception> _nextRuntimeDisposalFailures = new(StringComparer.OrdinalIgnoreCase);
        private int _disposed;

        /// <summary>建立 Factory；Queue 測試可把 LocalMaxInFlight 設為一，精確控制排隊時機。</summary>
        public TrackingRuntimeFactory(int localMaxInFlight = 2)
        {
            _localMaxInFlight = localMaxInFlight;
        }

        /// <summary>取得已建立 Runtime 的快照，供 shutdown 與 Dispose 次數斷言。</summary>
        public IReadOnlyCollection<TrackingRuntime> CreatedRuntimes
        {
            get
            {
                lock (_gate)
                {
                    return _runtimes.Values.ToArray();
                }
            }
        }

        /// <summary>取得 CreateAsync 呼叫次數；未知 Alias 與被拒絕的第三 replacement 不得增加此值。</summary>
        public int CreateCount
        {
            get
            {
                lock (_gate)
                {
                    return _runtimes.Count;
                }
            }
        }

        /// <summary>
        /// 安排指定 Alias 的下一次 Factory 建立在配置任何 Runtime ownership 前失敗。
        /// 例外只消耗一次，讓測試可驗證 Manager 回滾後是否真的能重新初始化，而不是重複同一注入故障。
        /// </summary>
        public void FailNextCreation(string alias, Exception failure)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(alias);
            ArgumentNullException.ThrowIfNull(failure);
            lock (_gate)
            {
                _nextCreationFailures[alias] = failure;
            }
        }

        /// <summary>
        /// 安排指定 Alias 的下一個已建立 Runtime 在完成自身狀態與資源清理後拋出一次 Dispose failure。
        /// Fake 仍先把 DisposeCount、CTS 與生命週期狀態收斂，藉此精確測試「清理已執行但回報失敗」的 rollback 語意。
        /// </summary>
        public void FailNextRuntimeDisposal(string alias, Exception failure)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(alias);
            ArgumentNullException.ThrowIfNull(failure);
            lock (_gate)
            {
                _nextRuntimeDisposalFailures[alias] = failure;
            }
        }

        /// <summary>
        /// 建立新的 Tracking Runtime。相同 Alias 共用 Admission Manager；Runtime Client／CTS／狀態一律新建且互不共享。
        /// </summary>
        public Task<IDynamicsProfileRuntime> CreateAsync(
            DynamicsProfileDefinition definition,
            long generation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                if (_nextCreationFailures.Remove(definition.ProfileAlias, out var creationFailure))
                {
                    return Task.FromException<IDynamicsProfileRuntime>(creationFailure);
                }

                if (!_admissions.TryGetValue(definition.ProfileAlias, out var admission))
                {
                    admission = new TrackingAdmissionManager(definition, _localMaxInFlight);
                    _admissions.Add(definition.ProfileAlias, admission);
                }

                _nextRuntimeDisposalFailures.Remove(
                    definition.ProfileAlias,
                    out var runtimeDisposalFailure);
                var runtime = new TrackingRuntime(
                    definition,
                    generation,
                    admission,
                    runtimeDisposalFailure);
                _runtimes.Add((definition.ProfileAlias, generation), runtime);
                return Task.FromResult<IDynamicsProfileRuntime>(runtime);
            }
        }

        /// <summary>依 Alias／Generation 取得已建立 Runtime，方便測試比較 Client 與生命週期狀態。</summary>
        public TrackingRuntime GetRuntime(string alias, long generation)
        {
            lock (_gate)
            {
                return _runtimes[(alias, generation)];
            }
        }

        /// <summary>
        /// 釋放所有 Tracking Admission Manager 的 Semaphore／CTS；Runtime 由受測 Manager 負責 Dispose，
        /// Factory 不會重複釋放 Runtime 或篡改其 DisposeCount。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            TrackingAdmissionManager[] admissions;
            lock (_gate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                admissions = _admissions.Values.ToArray();
                _admissions.Clear();
            }

            foreach (var admission in admissions)
            {
                await admission.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// 測試用 Runtime，完整模擬 Active／Draining／Disposed 與 execution reference count，
    /// 但不建立真實 HttpClient、Token 或 Socket；因此測試可精確控制 drain 與 cancellation 時機。
    /// </summary>
    private sealed class TrackingRuntime : IDynamicsProfileRuntime
    {
        private readonly object _gate = new();
        private readonly DynamicsProfileDefinition _definition;
        private readonly CancellationTokenSource _retirementCts = new();
        private TaskCompletionSource _zeroExecutions = CreateCompletedSignal();
        private int _activeExecutions;
        private int _disposeCount;
        private Exception? _disposeFailure;
        private Exception? _nextExecutionAcquisitionFailure;
        private Exception? _nextExecutionLeaseDisposalFailure;
        private DynamicsProfileRuntimeState _state = DynamicsProfileRuntimeState.Active;

        /// <summary>建立一個新 Generation 與專屬 Tracking Client。</summary>
        public TrackingRuntime(
            DynamicsProfileDefinition definition,
            long generation,
            TrackingAdmissionManager admissionManager,
            Exception? disposeFailure = null)
        {
            _definition = definition;
            _disposeFailure = disposeFailure;
            AdmissionManager = admissionManager;
            var options = definition.CreateOptionsSnapshot();
            OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
                .Should().BeTrue(error?.ErrorMessage);
            Key = new ProfileRuntimeKey(definition.ProfileAlias, generation, options.CeVersion, plan!.CanonicalKey);
            Client = new TrackingClient(Key);
        }

        /// <summary>取得此 Generation 的 Tracking Client。</summary>
        public TrackingClient Client { get; }

        /// <summary>控制後續 TryAcquireExecution 固定失敗，用來驗證 Admission Permit rollback。</summary>
        public bool RejectNewExecution { get; set; }

        /// <summary>取得實際完成 Dispose 的次數；正確 Manager 必須永遠等於一。</summary>
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        /// <summary>
        /// 取得此 Runtime 使用的 Tracking Admission Manager，讓測試觀測 Host Slot readiness 次數；
        /// 屬性只供測試，不保存 Request、Token、Credential 或 Session。
        /// </summary>
        public TrackingAdmissionManager Admission => (TrackingAdmissionManager)AdmissionManager;

        /// <summary>
        /// 安排下一次 execution acquisition 先建立 Lease、增加 active reference，再拋出指定原始錯誤；
        /// 該 Lease 第一次 Dispose 會先歸還 active reference，之後再拋出指定 cleanup error。
        /// 這可重現 production rollback 在 ownership 尚未轉給 Combined Lease 前同時遭遇兩個失敗的最危險路徑。
        /// </summary>
        public void FailNextExecutionAcquisitionAfterLease(
            Exception acquisitionFailure,
            Exception leaseDisposalFailure)
        {
            ArgumentNullException.ThrowIfNull(acquisitionFailure);
            ArgumentNullException.ThrowIfNull(leaseDisposalFailure);
            lock (_gate)
            {
                _nextExecutionAcquisitionFailure = acquisitionFailure;
                _nextExecutionLeaseDisposalFailure = leaseDisposalFailure;
            }
        }

        /// <summary>取得 Fake Runtime 的不可變 Alias／Generation／Canonical Organization Key，供隔離與路由斷言使用。</summary>
        public ProfileRuntimeKey Key { get; }

        /// <summary>在測試鎖內取得 Active、Draining 或 Disposed 狀態，避免斷言讀到不一致的生命週期轉換。</summary>
        public DynamicsProfileRuntimeState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        /// <summary>取得尚未由 Tracking Lease 歸還的 active reference 數量，用來證明 drain 與 rollback 沒有引用洩漏。</summary>
        public int ActiveExecutionCount
        {
            get
            {
                lock (_gate)
                {
                    return _activeExecutions;
                }
            }
        }

        /// <summary>取得同 Alias 各 Generation 共用的測試 Admission Manager；Fake 不擁有產品 Request 或秘密資料。</summary>
        public IOrganizationAdmissionManager AdmissionManager { get; }

        /// <summary>取得目前 Permit、Queue 與 Host Slot 的 bounded 快照，供容量清理斷言使用。</summary>
        public AdmissionMetricsSnapshot AdmissionSnapshot => AdmissionManager.GetSnapshot();

        /// <summary>
        /// 只在 Fake 仍 Active 且未設定拒絕旗標時增加 active reference 並建立 Tracking Lease。
        /// 注入失敗模式會先建立 ownership 再拋錯，以重現 production rollback 必須清理半完成 Lease 的路徑。
        /// </summary>
        public bool TryAcquireExecution(out IDynamicsProfileExecutionLease? lease)
        {
            lock (_gate)
            {
                if (RejectNewExecution || _state != DynamicsProfileRuntimeState.Active)
                {
                    lease = null;
                    return false;
                }

                if (_activeExecutions == 0)
                {
                    _zeroExecutions = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                _activeExecutions++;
                var leaseDisposalFailure = _nextExecutionLeaseDisposalFailure;
                _nextExecutionLeaseDisposalFailure = null;
                lease = new TrackingExecutionLease(this, leaseDisposalFailure);
                if (_nextExecutionAcquisitionFailure is not null)
                {
                    var acquisitionFailure = _nextExecutionAcquisitionFailure;
                    _nextExecutionAcquisitionFailure = null;
                    throw acquisitionFailure;
                }

                return true;
            }
        }

        /// <summary>以 Tracking Client 執行固定 WhoAmI，不建立真實網路、Token、Credential 或背景工作。</summary>
        public Task<OperationExecutionResult> WarmUpAsync(CancellationToken cancellationToken)
            => Client.WhoAmIAsync(cancellationToken);

        /// <summary>把 Fake Runtime 單向切換為 Draining，立即拒絕新 Lease，但保留既有 Lease 到測試主動釋放。</summary>
        public void BeginDrain()
        {
            lock (_gate)
            {
                if (_state == DynamicsProfileRuntimeState.Active)
                {
                    _state = DynamicsProfileRuntimeState.Draining;
                }
            }
        }

        /// <summary>
        /// 等待 active reference 在有界期限內歸零，逾時時取消 Retirement Token，再把狀態與 CTS 確定性收斂為 Disposed。
        /// 可選 Dispose failure 只在資源與引用已清理後拋出，讓測試驗證上層仍會繼續回收其他 ownership。
        /// </summary>
        public async Task DrainAndDisposeAsync(CancellationToken cancellationToken = default)
        {
            BeginDrain();
            Task zeroTask;
            lock (_gate)
            {
                if (_state == DynamicsProfileRuntimeState.Disposed)
                {
                    return;
                }

                zeroTask = _activeExecutions == 0 ? Task.CompletedTask : _zeroExecutions.Task;
            }

            try
            {
                await zeroTask.WaitAsync(_definition.DrainTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _retirementCts.Cancel();
                await zeroTask.WaitAsync(_definition.CancellationGracePeriod, cancellationToken);
            }

            lock (_gate)
            {
                if (_state == DynamicsProfileRuntimeState.Disposed)
                {
                    return;
                }

                _state = DynamicsProfileRuntimeState.Disposed;
                Interlocked.Increment(ref _disposeCount);
            }

            _retirementCts.Dispose();
            var disposeFailure = Interlocked.Exchange(ref _disposeFailure, null);
            if (disposeFailure is not null)
            {
                throw disposeFailure;
            }
        }

        /// <summary>同步等待相同 bounded drain，不留下背景 Task。</summary>
        public void Dispose()
            => Task.Run(async () => await DrainAndDisposeAsync()).GetAwaiter().GetResult();

        /// <summary>非同步相容路徑委派到相同 drain。</summary>
        public ValueTask DisposeAsync()
            => new(DrainAndDisposeAsync());

        /// <summary>釋放一個 Tracking Lease 的 active 引用並在歸零時喚醒 drain。</summary>
        private void ReleaseExecution()
        {
            TaskCompletionSource? zero = null;
            lock (_gate)
            {
                _activeExecutions--;
                if (_activeExecutions == 0)
                {
                    zero = _zeroExecutions;
                }
            }

            zero?.TrySetResult();
        }

        /// <summary>建立初始已完成的 zero-execution 訊號。</summary>
        private static TaskCompletionSource CreateCompletedSignal()
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.TrySetResult();
            return signal;
        }

        /// <summary>Tracking Execution Lease，使用 Interlocked 確保只釋放一次引用。</summary>
        private sealed class TrackingExecutionLease : IDynamicsProfileExecutionLease
        {
            private readonly TrackingRuntime _owner;
            private readonly Exception? _disposeFailure;
            private int _disposed;

            /// <summary>
            /// 建立已由 Runtime 增加引用的 Lease；可選錯誤只在 active reference 已歸還後拋出，
            /// 讓測試確認上層不能因第一個 cleanup failure 而跳過後續 Admission Permit 清理。
            /// </summary>
            public TrackingExecutionLease(TrackingRuntime owner, Exception? disposeFailure)
            {
                _owner = owner;
                _disposeFailure = disposeFailure;
            }

            /// <summary>取得 Lease 固定綁定的 Fake Runtime Key，供測試確認沒有跨 Generation 路由。</summary>
            public ProfileRuntimeKey RuntimeKey => _owner.Key;

            /// <summary>取得 owner 的 Tracking Client；Lease 釋放後測試不得用它模擬新的受控外呼。</summary>
            public IDynamicsWebApiClient Client => _owner.Client;

            /// <summary>取得 drain timeout 後由 Fake Runtime 發出的退休取消訊號。</summary>
            public CancellationToken RetirementToken => _owner._retirementCts.Token;

            /// <summary>同步釋放 active 引用。</summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.ReleaseExecution();
                    if (_disposeFailure is not null)
                    {
                        throw _disposeFailure;
                    }
                }
            }

            /// <summary>非同步相容路徑。</summary>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// Tracking Client 只記錄執行次數與 Runtime Key，不保存 Request Parameters、Token、Credential 或 Session。
    /// </summary>
    private sealed class TrackingClient : IDynamicsWebApiClient
    {
        private int _executionCount;

        /// <summary>建立綁定單一 Generation Key 的 Client。</summary>
        public TrackingClient(ProfileRuntimeKey key)
        {
            Key = key;
        }

        /// <summary>取得 Client 所屬 Generation Key。</summary>
        public ProfileRuntimeKey Key { get; }

        /// <summary>取得受控操作執行次數。</summary>
        public int ExecutionCount => Volatile.Read(ref _executionCount);

        /// <summary>建立並執行固定 WhoAmI Definition，確保測試仍走 Operation Registry 形狀而非任意 URL。</summary>
        public Task<OperationExecutionResult> WhoAmIAsync(CancellationToken cancellationToken = default)
            => ExecuteRegisteredOperationAsync(
                new OperationDefinition
                {
                    CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                    OperationKind = "connection-runtime",
                    TemplateKind = "odata-function",
                    TemplateId = "WhoAmI",
                    TemplateHash = new string('a', 64),
                    ResponseKind = OperationResponseKind.WhoAmI,
                    MaximumPageCount = 4,
                    MaximumPageBytes = 64 * 1024,
                    MaximumCumulativeResponseBytes = 4 * 64 * 1024,
                    MaximumResultItemCount = 4096,
                    DataClassification = "internal-operational",
                    AuditRequirement = "bounded-runtime-health",
                    IdempotencyClass = "read-only",
                    Parameters = [],
                    Package = "runtime-manager-tests"
                },
                new Dictionary<string, object?>(),
                cancellationToken);

        /// <summary>
        /// 記錄一次已註冊操作並回傳 Alias／Generation；方法只計數，不保存 Parameters、Token、Credential 或 Session。
        /// 取消在計數前被觀察，避免已取消測試留下假的執行紀錄。
        /// </summary>
        /// <remarks>
        /// 這個固定 WhoAmI definition 明確採用封閉回應類型與有限 page／byte／row policy，讓 runtime manager 的
        /// 記憶體 fake 仍遵守正式 registry 的 fail-closed 契約，並且不把 Alias 或 Generation 投影到成功資料。
        /// </remarks>
        public Task<OperationExecutionResult> ExecuteRegisteredOperationAsync(
            OperationDefinition definition,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _executionCount);
            return Task.FromResult(OperationExecutionResult.Success(
                OperationResponseData.ForWhoAmI(
                    definition.CapabilityOperationId,
                    "9.1",
                    new WhoAmIResponseData())));
        }
    }

    /// <summary>
    /// 有界 Tracking Admission Manager。它使用 SemaphoreSlim 模擬 queued＋in-flight，
    /// Permit 只保存 Manager 引用與無資料 CancellationToken，不保存 Request、Token、Credential 或 Session。
    /// </summary>
    private sealed class TrackingAdmissionManager : IOrganizationAdmissionManager
    {
        private readonly SemaphoreSlim _inFlight;
        private readonly CancellationTokenSource _stopCts = new();
        private int _activePermits;
        private int _queued;
        private int _disposed;
        private int _ensureHostSlotCount;

        /// <summary>從 Definition 建立有效 Plan，並以測試指定的 LocalMaxInFlight 配置 Semaphore。</summary>
        public TrackingAdmissionManager(DynamicsProfileDefinition definition, int localMaxInFlight)
        {
            var options = definition.CreateOptionsSnapshot();
            options.Admission.AggregateMaxInFlight = localMaxInFlight * options.Admission.MaximumRuntimeHosts;
            options.MaxConnectionsPerServer = Math.Min(options.MaxConnectionsPerServer, localMaxInFlight);
            OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
                .Should().BeTrue(error?.ErrorMessage);
            Plan = plan!;
            _inFlight = new SemaphoreSlim(localMaxInFlight, localMaxInFlight);
        }

        /// <summary>
        /// 取得 EnsureHostSlotAsync 的呼叫次數；每個初始 Profile 在 Manager Ready 前至少必須完成一次。
        /// </summary>
        public int EnsureHostSlotCount => Volatile.Read(ref _ensureHostSlotCount);

        /// <summary>取得由測試 Definition 驗證產生的不可變容量計畫，所有 Semaphore 上限都由此 Plan 對應。</summary>
        public OrganizationAdmissionPlan Plan { get; }

        /// <summary>
        /// 模擬 Host Slot readiness 前置條件並記錄呼叫次數；Manager Dispose 後 fail closed，且不啟動續租背景 Task。
        /// </summary>
        public Task EnsureHostSlotAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            Interlocked.Increment(ref _ensureHostSlotCount);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 以可取消的 SemaphoreSlim 模擬 bounded Queue 與 in-flight Permit；waiter 離開時必定遞減 Queue 計數，
        /// 成功 Permit 則由 TrackingPermit 唯一負責歸還名額，藉此偵測 rollback 容量洩漏。
        /// </summary>
        public async Task<AdmissionAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _stopCts.Token);
            Interlocked.Increment(ref _queued);
            try
            {
                await _inFlight.WaitAsync(linked.Token);
            }
            finally
            {
                Interlocked.Decrement(ref _queued);
            }

            Interlocked.Increment(ref _activePermits);
            return AdmissionAcquireResult.Success(new TrackingPermit(this));
        }

        /// <summary>建立不含 Request 或秘密的測試快照，精確公開 Queue、ActivePermits 與 Host Slot 是否仍可用。</summary>
        public AdmissionMetricsSnapshot GetSnapshot()
            => new()
            {
                LocalMaxInFlight = Plan.LocalMaxInFlight,
                InFlight = Volatile.Read(ref _activePermits),
                Queued = Volatile.Read(ref _queued),
                LocalQueueCapacity = Plan.LocalQueueCapacity,
                AcceptedCount = 0,
                RejectedCount = 0,
                TimeoutCount = 0,
                HostSlotReady = _disposed == 0,
                HostFencingToken = 1,
                HostLeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
                ActivePermits = Volatile.Read(ref _activePermits),
                RenewalLoopActive = false,
                TrackedWorkloadCount = 0
            };

        /// <summary>同步釋放測試 Semaphore／CTS；測試結束前所有 Permit 與 waiter 必須已歸零。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stopCts.Cancel();
            _stopCts.Dispose();
            _inFlight.Dispose();
        }

        /// <summary>非同步相容路徑不啟動背景工作。</summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>釋放一個 Permit 並把 Semaphore 名額歸還 Queue。</summary>
        private void ReleasePermit()
        {
            Interlocked.Decrement(ref _activePermits);
            _inFlight.Release();
        }

        /// <summary>Tracking Permit 使用 Interlocked 確保同步／非同步 Dispose 只歸還一次名額。</summary>
        private sealed class TrackingPermit : IAdmissionPermit
        {
            private readonly TrackingAdmissionManager _owner;
            private int _disposed;

            /// <summary>建立已占用一個 Semaphore 名額的 Permit。</summary>
            public TrackingPermit(TrackingAdmissionManager owner)
            {
                _owner = owner;
            }

            /// <summary>取得不含工作負載識別的隨機測試關聯值，只用來符合 Permit 契約。</summary>
            public Guid CorrelationId { get; } = Guid.NewGuid();

            /// <summary>取得固定非零 fencing token，表示此 Fake Permit 已通過測試 Host Slot。</summary>
            public long HostFencingToken => 1;

            /// <summary>Fake 不模擬中途 Lease loss，因此回傳不可取消 Token；shutdown 由 Admission Manager 的 stop CTS 負責。</summary>
            public CancellationToken LeaseLostToken => CancellationToken.None;

            /// <summary>同步歸還 Admission 名額。</summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.ReleasePermit();
                }
            }

            /// <summary>非同步相容路徑。</summary>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
