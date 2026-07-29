using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 將 process-global 記憶體、handle 與 thread 基線測試設為不可平行執行。
/// 若其他 xUnit collection 同時建立 TestServer、SQL connection 或 HttpClient，整個 testhost 的資源計數會被外部 fixture 污染，
/// 造成與本測試 runtime 無關的假陽性；禁止平行化不會放寬任何資源上限，只是確保量測期間的擁有者範圍可證明。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Phase4ResourceSoakCollection
{
    public const string Name = "Phase4ResourceSoak";
}

/// <summary>
/// Phase 4 隔離與資源浸泡測試。
/// 以多 workload、多設定檔世代及兩個實體 endpoint 的高併發流量，驗證容量不倍增、Authorization/Cookie 不串流、
/// queue/permit/workload counter 排空，以及 handler、記憶體、handle、thread 與強參考在 Dispose 後回到有界基線。
/// </summary>
[Collection(Phase4ResourceSoakCollection.Name)]
public sealed class Phase4IsolationSoakTests
{
    /// <summary>
    /// 以兩個不同 Alias／Generation 指向同一 Canonical Organization，證明它們雖各自擁有 Client 與 Runtime，
    /// 仍共用唯一 Admission Manager 的 LocalMaxInFlight；第三個實體 Organization 則取得獨立容量，不受前者慢流量拖累。
    /// </summary>
    [Fact]
    public async Task Multi_profile_aliases_share_only_canonical_admission_capacity()
    {
        var sharedOrganization = Guid.Parse("81818181-8181-8181-8181-818181818181");
        var otherOrganization = Guid.Parse("92929292-9292-9292-9292-929292929292");
        var sharedCounter = new SharedConcurrencyCounter();
        var otherCounter = new SharedConcurrencyCounter();
        await using var factory = new RegistryBackedRuntimeFactory(
            new Dictionary<Guid, SharedConcurrencyCounter>
            {
                [sharedOrganization] = sharedCounter,
                [otherOrganization] = otherCounter
            });
        await using var manager = new DynamicsProfileRuntimeManager(
        [
            CreateRegistryDefinition("crm82", "8.2", sharedOrganization, "shared-org"),
            CreateRegistryDefinition("crm91", "9.1", sharedOrganization, "shared-org"),
            CreateRegistryDefinition("crm-other", "9.1", otherOrganization, "other-org")
        ], factory);
        await manager.InitializeAsync(CancellationToken.None);

        var aliases = new[] { "crm82", "crm91", "crm-other" };
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 900),
            new ParallelOptions { MaxDegreeOfParallelism = 48 },
            async (index, cancellationToken) =>
            {
                var result = await manager.ExecuteAsync(
                    new OperationExecutionRequest
                    {
                        ProfileAlias = aliases[index % aliases.Length],
                        CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                        WorkloadSubjectId = $"workload-{index % 5}"
                    },
                    cancellationToken);
                result.Succeeded.Should().BeTrue();
            });

        factory.GetRuntime("crm82", 1).Client.Should()
            .NotBeSameAs(factory.GetRuntime("crm91", 1).Client);
        factory.GetRuntime("crm82", 1).AdmissionManager.Should()
            .BeSameAs(factory.GetRuntime("crm91", 1).AdmissionManager);
        factory.GetRuntime("crm82", 1).AdmissionManager.Should()
            .NotBeSameAs(factory.GetRuntime("crm-other", 1).AdmissionManager);
        sharedCounter.MaximumObserved.Should().BeLessThanOrEqualTo(4,
            "same canonical organization aliases share LocalMaxInFlight=4");
        otherCounter.MaximumObserved.Should().BeLessThanOrEqualTo(2,
            "the other canonical organization owns its separate LocalMaxInFlight=2 budget");
        manager.GetSnapshot().Profiles.Should().OnlyContain(
            profile => profile.Admission.ActivePermits == 0 && profile.Admission.Queued == 0);
    }

    /// <summary>
    /// 暖機後執行十六次 crm82／crm91 原子替換，五個 workload 持續執行，並在選定輪次刻意讓舊 Lease 跨越新 Generation 發布。
    /// Shutdown 後以 WeakReference、GC、記憶體、Handle 與 Thread 基線證明 Manager、Runtime、Client、CTS 與模擬資源沒有被保留。
    /// </summary>
    [Fact]
    public async Task Multi_profile_replacement_cycles_release_all_generation_owned_resources()
    {
        _ = await RunMultiProfileReplacementCyclesAsync(cycleCount: 2);
        ForceFullCollection();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
        var baselineHandles = OperatingSystem.IsWindows() ? process.HandleCount : 0;
        var baselineThreads = process.Threads.Count;

        var retired = await RunMultiProfileReplacementCyclesAsync(cycleCount: 16);
        // 與本檔既有 Generation soak 採用相同方法：再執行一個不列入斷言的短週期，
        // 讓 async awaiter／JIT 最後使用的區域變數改指向新物件；這不會放寬受測 16 輪的 WeakReference 條件。
        _ = await RunMultiProfileReplacementCyclesAsync(cycleCount: 1);
        ForceFullCollection();
        process.Refresh();

        retired.Should().OnlyContain(reference => !reference.IsAlive,
            "manager shutdown must remove all strong references to retired generations and owned resources");
        GC.GetTotalMemory(forceFullCollection: true).Should().BeLessThanOrEqualTo(
            baselineMemory + 8 * 1024 * 1024,
            "multi-profile replacement memory must return to the existing bounded post-warm-up tolerance");
        process.Threads.Count.Should().BeLessThanOrEqualTo(
            baselineThreads + 8,
            "replacement and drain must not retain background workers");

        if (OperatingSystem.IsWindows())
        {
            process.HandleCount.Should().BeLessThanOrEqualTo(
                baselineHandles + 8,
                "replacement CTS、Admission Permit 與 simulated handler ownership must be deterministically released");
        }
    }

    /// <summary>
    /// 6,000 次操作跨五個 workload 與三個 runtime，證明同組織雙世代共用 aggregate 上限、不同組織互相隔離，
    /// 並在 finally 中逐一驗證 counter 歸零與 handler 僅 Dispose 一次。
    /// </summary>
    [Fact]
    public async Task Five_workloads_two_generations_and_two_endpoints_drain_without_cross_talk()
    {
        const int operationCount = 6000;
        var coordinator = new InMemoryRuntimeHostSlotCoordinator();
        var organizationConcurrency = new SharedConcurrencyCounter();
        var otherOrganizationConcurrency = new SharedConcurrencyCounter();
        var generationA = CreateRuntime(
            "https://crm-a.example.local/org/api/data/v9.1/",
            "org-a-shared-slot",
            coordinator,
            organizationConcurrency);
        var generationB = CreateRuntime(
            "https://crm-a.example.local/org/api/data/v9.1/",
            "org-a-shared-slot",
            coordinator,
            organizationConcurrency);
        var endpointB = CreateRuntime(
            "https://crm-b.example.local/other/api/data/v8.2/",
            "org-b-slot",
            coordinator,
            otherOrganizationConcurrency);
        var runtimes = new[] { generationA, generationB, endpointB };
        var workloads = new[] { "church-report", "line", "payment", "batch", "diagnostics" };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var failures = new System.Collections.Concurrent.ConcurrentBag<OperationExecutionResult>();
            await Parallel.ForEachAsync(
                Enumerable.Range(0, operationCount),
                new ParallelOptions { MaxDegreeOfParallelism = 64 },
                async (index, cancellationToken) =>
                {
                    var result = await runtimes[index % runtimes.Length].Executor.ExecuteAsync(
                    new OperationExecutionRequest
                    {
                        ProfileAlias = index % 3 == 2 ? "org-b" : "org-a",
                        CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                        WorkloadSubjectId = workloads[index % workloads.Length]
                    },
                    cancellationToken);
                    if (!result.Succeeded)
                    {
                        failures.Add(result);
                    }
                }).WaitAsync(TimeSpan.FromSeconds(30));
            failures.Should().BeEmpty();
        }
        finally
        {
            stopwatch.Stop();
            foreach (var runtime in runtimes)
            {
                var snapshot = runtime.Manager.GetSnapshot();
                snapshot.InFlight.Should().Be(0);
                snapshot.Queued.Should().Be(0);
                snapshot.ActivePermits.Should().Be(0);
                snapshot.TrackedWorkloadCount.Should().Be(0,
                    "per-workload counters must be removed after the last permit drains");
                await runtime.DisposeAsync();
            }
        }

        organizationConcurrency.MaximumObserved.Should().BeLessThanOrEqualTo(8,
            "two generations share AggregateMaxInFlight=8 for the same physical organization");
        otherOrganizationConcurrency.MaximumObserved.Should().BeLessThanOrEqualTo(4);
        runtimes.Should().OnlyContain(runtime => runtime.Handler.DisposeCount == 1);
        runtimes.Should().OnlyContain(runtime => runtime.Handler.CrossTalkCount == 0);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 先暖機固定 JIT/快取基線，再重複建立、排空與銷毀設定檔世代；WeakReference、GC、handle 與 thread 界線
    /// 用來阻擋 manager/transport/executor/handler 的持續強參考或背景工作洩漏。
    /// </summary>
    [Fact]
    public async Task Repeated_generation_cycles_return_memory_handles_threads_and_owned_objects_to_baseline()
    {
        await RunGenerationCyclesAsync(cycleCount: 2, operationsPerCycle: 100);
        ForceFullCollection();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
        var baselineHandles = OperatingSystem.IsWindows() ? process.HandleCount : 0;
        var baselineThreads = process.Threads.Count;

        var retired = await RunGenerationCyclesAsync(cycleCount: 16, operationsPerCycle: 250);
        _ = await RunSingleGenerationCycleAsync(cycle: int.MaxValue, operationsPerCycle: 1);
        ForceFullCollection();
        process.Refresh();

        retired.Should().OnlyContain(reference => !reference.IsAlive,
            "disposed profile generations must not remain strongly reachable");
        GC.GetTotalMemory(forceFullCollection: true).Should().BeLessThanOrEqualTo(
            baselineMemory + 8 * 1024 * 1024,
            "managed memory must return to a bounded post-warm-up baseline");
        process.Threads.Count.Should().BeLessThanOrEqualTo(
            baselineThreads + 8,
            "renewal and request work must not leak thread-pool workers");

        if (OperatingSystem.IsWindows())
        {
            process.HandleCount.Should().BeLessThanOrEqualTo(
                baselineHandles + 8,
                "handlers, cancellation registrations, and timers must be deterministically released");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<IReadOnlyList<WeakReference>> RunGenerationCyclesAsync(
        int cycleCount,
        int operationsPerCycle)
    {
        var retired = new List<WeakReference>(cycleCount * 4);

        for (var cycle = 0; cycle < cycleCount; cycle++)
        {
            retired.AddRange(await RunSingleGenerationCycleAsync(cycle, operationsPerCycle));
        }

        return retired;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<IReadOnlyList<WeakReference>> RunSingleGenerationCycleAsync(
        int cycle,
        int operationsPerCycle)
    {
        var coordinator = new InMemoryRuntimeHostSlotCoordinator();
        var concurrency = new SharedConcurrencyCounter();
        RuntimeFixture? runtime = CreateRuntime(
            "https://crm-soak.example.local/org/api/data/v9.1/",
            $"soak-{cycle}",
            coordinator,
            concurrency);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, operationsPerCycle),
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (index, cancellationToken) =>
            {
                var result = await runtime.Executor.ExecuteAsync(
                    new OperationExecutionRequest
                    {
                        ProfileAlias = "soak",
                        CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                        WorkloadSubjectId = $"workload-{index % 5}"
                    },
                    cancellationToken);
                result.Succeeded.Should().BeTrue();
            });

        var snapshot = runtime.Manager.GetSnapshot();
        snapshot.InFlight.Should().Be(0);
        snapshot.Queued.Should().Be(0);
        snapshot.ActivePermits.Should().Be(0);
        snapshot.TrackedWorkloadCount.Should().Be(0);

        var retired = new[]
        {
            new WeakReference(runtime.Manager),
            new WeakReference(runtime.Transport),
            new WeakReference(runtime.Executor),
            new WeakReference(runtime.Handler)
        };

        await runtime.DisposeAsync();
        runtime.Handler.DisposeCount.Should().Be(1);
        runtime.Handler.CrossTalkCount.Should().Be(0);
        runtime = null;
        return retired;
    }

    private static void ForceFullCollection()
    {
        // 多輪完整 GC 與 finalizer 等待只用於受控測試，以排除延遲 finalization；產品路徑不得主動強迫 GC。
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// 在不可內聯的方法內完整建立、替換並關閉 Multi-Profile Manager，然後只回傳 WeakReference。
    /// NoInlining 與區域變數清空可避免 JIT 延長最後一個 Runtime／Lease 的生命週期而造成測試假陽性。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<IReadOnlyList<WeakReference>> RunMultiProfileReplacementCyclesAsync(
        int cycleCount)
    {
        var sharedOrganization = Guid.Parse("73737373-7373-7373-7373-737373737373");
        var counter = new SharedConcurrencyCounter();
        RegistryBackedRuntimeFactory? factory = new(
            new Dictionary<Guid, SharedConcurrencyCounter>
            {
                [sharedOrganization] = counter
            });
        DynamicsProfileRuntimeManager? manager = new(
        [
            CreateRegistryDefinition("crm82", "8.2", sharedOrganization, "replacement-shared"),
            CreateRegistryDefinition("crm91", "9.1", sharedOrganization, "replacement-shared")
        ], factory);
        await manager.InitializeAsync(CancellationToken.None);

        for (var cycle = 0; cycle < cycleCount; cycle++)
        {
            var alias = cycle % 2 == 0 ? "crm82" : "crm91";
            var version = alias == "crm82" ? "8.2" : "9.1";
            IDynamicsProfileExecutionLease? heldOldLease = null;
            Task? replacement = null;
            if (cycle % 4 == 0)
            {
                var currentGeneration = manager.GetSnapshot().Profiles
                    .Single(profile =>
                        profile.Key.ProfileAlias == alias &&
                        profile.State == DynamicsProfileRuntimeState.Active)
                    .Key.Generation;
                factory.GetRuntime(alias, currentGeneration)
                    .TryAcquireExecution(out heldOldLease)
                    .Should().BeTrue();
                replacement = manager.ReplaceAsync(
                    CreateRegistryDefinition(alias, version, sharedOrganization, "replacement-shared"));
                await WaitForPublishedGenerationAsync(
                    manager,
                    alias,
                    currentGeneration + 1);
            }
            else
            {
                replacement = manager.ReplaceAsync(
                    CreateRegistryDefinition(alias, version, sharedOrganization, "replacement-shared"));
            }

            if (heldOldLease is not null)
            {
                await heldOldLease.DisposeAsync();
                heldOldLease = null;
            }

            await replacement;
            replacement = null;

            var executions = Enumerable.Range(0, 25)
                .Select(index => manager.ExecuteAsync(
                    new OperationExecutionRequest
                    {
                        ProfileAlias = index % 2 == 0 ? "crm82" : "crm91",
                        CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                        WorkloadSubjectId = $"workload-{index % 5}"
                    }))
                .ToArray();
            var results = await Task.WhenAll(executions);
            results.Should().OnlyContain(result => result.Succeeded);
        }

        var managerReference = new WeakReference(manager);
        await manager.DisposeAsync();
        manager.GetSnapshot().Profiles.Should().BeEmpty();
        manager = null;
        await factory.DisposeAsync();
        var references = factory.OwnedReferences
            .Append(managerReference)
            .ToArray();
        factory = null;
        return references;
    }

    /// <summary>
    /// 等待指定 Alias 的新 Generation 原子發布，總等待時間固定為五秒；
    /// 逾時代表 replacement 沒有完成發布，不會讓測試留下無限輪詢 Task。
    /// </summary>
    private static async Task WaitForPublishedGenerationAsync(
        DynamicsProfileRuntimeManager manager,
        string alias,
        long generation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!manager.GetSnapshot().Profiles.Any(profile =>
                   profile.Key.ProfileAlias == alias &&
                   profile.Key.Generation == generation &&
                   profile.State == DynamicsProfileRuntimeState.Active))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    /// <summary>
    /// 建立 Registry-backed 測試 Profile。相同 <paramref name="namespacePrefix"/>、Organization GUID 與 Base URI
    /// 會解析成同一 Canonical Admission Manager，但版本與 Alias 仍建立不同 Runtime Generation 與 Client。
    /// </summary>
    private static DynamicsProfileDefinition CreateRegistryDefinition(
        string alias,
        string ceVersion,
        Guid organizationId,
        string namespacePrefix)
        => new(
            alias,
            new DynamicsWebApiOptions
            {
                OrganizationWebApiBaseUri =
                    $"https://{namespacePrefix}.example.test/Org/api/data/v{ceVersion}/",
                CeVersion = ceVersion,
                AuthMode = DynamicsAuthMode.Windows,
                CredentialSource = DynamicsCredentialSource.HostIdentity,
                TimeoutSeconds = 5,
                MaxConnectionsPerServer = 2,
                Admission = new OrganizationAdmissionOptions
                {
                    ExpectedOrganizationId = organizationId,
                    AggregateMaxInFlight = namespacePrefix == "other-org" ? 4 : 8,
                    MaximumRuntimeHosts = 2,
                    LocalQueueCapacity = 64,
                    MaxInFlightAndQueuedPerWorkload = 64,
                    QueueAdmissionTimeoutSeconds = 5,
                    AdmissionNamespaceId = namespacePrefix + "-admission",
                    LeaseNamespaceId = namespacePrefix + "-lease",
                    RequireDurableHostCoordinator = false
                }
            },
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromSeconds(2),
            cancellationGracePeriod: TimeSpan.FromSeconds(2));

    private static RuntimeFixture CreateRuntime(
        string webApiRoot,
        string leaseNamespace,
        IRuntimeHostSlotCoordinator coordinator,
        SharedConcurrencyCounter concurrency)
    {
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationWebApiBaseUri = webApiRoot,
            CeVersion = webApiRoot.Contains("v8.2", StringComparison.Ordinal) ? "8.2" : "9.1",
            AuthMode = DynamicsAuthMode.Windows,
            CredentialSource = DynamicsCredentialSource.HostIdentity,
            TimeoutSeconds = 5,
            MaxConnectionsPerServer = 4,
            MaxResponseBytes = 4096,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = leaseNamespace == "org-a-shared-slot"
                    ? Guid.Parse("45454545-4545-4545-4545-454545454545")
                    : Guid.Parse("56565656-5656-5656-5656-565656565656"),
                AggregateMaxInFlight = leaseNamespace == "org-a-shared-slot" ? 8 : 4,
                MaximumRuntimeHosts = leaseNamespace == "org-a-shared-slot" ? 2 : 1,
                LocalQueueCapacity = 64,
                MaxInFlightAndQueuedPerWorkload = 64,
                QueueAdmissionTimeoutSeconds = 10,
                AdmissionNamespaceId = leaseNamespace + "-admission",
                LeaseNamespaceId = leaseNamespace
            }
        });
        OrganizationAdmissionPlan.TryCreate(options.Value, options.Value.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);

        var manager = new OrganizationAdmissionManager(
            plan!,
            coordinator,
            NullLogger<OrganizationAdmissionManager>.Instance);
        var handler = new TrackingHandler(new Uri(webApiRoot), concurrency);
        var transport = new DynamicsHttpTransport(
            handler,
            NullLogger<DynamicsHttpTransport>.Instance,
            disposeHandler: true);
        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new StaticTokenProvider(),
            NullLogger<DynamicsWebApiClient>.Instance);
        var executor = new ControlledOperationExecutor(client, manager);
        return new RuntimeFixture(manager, transport, executor, handler);
    }

    private sealed record RuntimeFixture(
        OrganizationAdmissionManager Manager,
        DynamicsHttpTransport Transport,
        ControlledOperationExecutor Executor,
        TrackingHandler Handler)
    {
        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            await Transport.DisposeAsync();
        }
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        private readonly Uri _approvedRoot;
        private readonly SharedConcurrencyCounter _concurrency;
        private int _disposeCount;
        private int _crossTalkCount;

        public TrackingHandler(Uri approvedRoot, SharedConcurrencyCounter concurrency)
        {
            _approvedRoot = approvedRoot;
            _concurrency = concurrency;
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int CrossTalkCount => Volatile.Read(ref _crossTalkCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var active = _concurrency.Enter();
            try
            {
                if (request.RequestUri is null ||
                    !request.RequestUri.AbsoluteUri.StartsWith(_approvedRoot.AbsoluteUri, StringComparison.OrdinalIgnoreCase) ||
                    request.Headers.Authorization is not null ||
                    request.Headers.Contains("Cookie"))
                {
                    Interlocked.Increment(ref _crossTalkCount);
                }

                await Task.Delay(1, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"UserId\":\"67676767-6767-6767-6767-676767676767\"}",
                        Encoding.UTF8,
                        "application/json")
                };
            }
            finally
            {
                _concurrency.Exit(active);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }

            base.Dispose(disposing);
        }
    }

    private sealed class SharedConcurrencyCounter
    {
        private int _active;
        private int _maximumObserved;

        public int MaximumObserved => Volatile.Read(ref _maximumObserved);

        public int Enter()
        {
            var active = Interlocked.Increment(ref _active);
            var observed = Volatile.Read(ref _maximumObserved);
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(ref _maximumObserved, active, observed);
                if (previous == observed)
                {
                    break;
                }

                observed = previous;
            }

            return active;
        }

        public void Exit(int _) => Interlocked.Decrement(ref _active);
    }

    private sealed class StaticTokenProvider : IAdfsOAuthTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("unused");
    }

    /// <summary>
    /// 以正式 <see cref="OrganizationAdmissionRegistry"/> 建立測試 Runtime 的 Factory。
    /// Factory 只保留 Runtime 的 WeakReference，讓 replace-and-drain 後可證明舊 Generation 沒有被 Catalog 或 Queue 強引用。
    /// </summary>
    private sealed class RegistryBackedRuntimeFactory : IDynamicsProfileRuntimeFactory, IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly OrganizationAdmissionRegistry _registry;
        private readonly IReadOnlyDictionary<Guid, SharedConcurrencyCounter> _counters;
        private readonly Dictionary<(string Alias, long Generation), WeakReference<RegistryBackedRuntime>> _runtimes = new();
        private readonly List<WeakReference> _ownedReferences = [];
        private int _disposed;

        /// <summary>
        /// 建立 Factory 與程序內 Canonical Admission Registry；Counter 依 Organization GUID 分離，
        /// 不以 Alias、Generation、User 或 Session 作為容量鍵。
        /// </summary>
        public RegistryBackedRuntimeFactory(
            IReadOnlyDictionary<Guid, SharedConcurrencyCounter> counters)
        {
            _counters = counters;
            _registry = new OrganizationAdmissionRegistry(
                new InMemoryRuntimeHostSlotCoordinator(),
                NullLogger<OrganizationAdmissionRegistry>.Instance,
                NullLogger<OrganizationAdmissionManager>.Instance);
        }

        /// <summary>
        /// 取得 Factory 曾建立之 Runtime／Client／CTS／模擬資源的 WeakReference 快照；
        /// 集合本身不延長目標物件生命週期。
        /// </summary>
        public IReadOnlyList<WeakReference> OwnedReferences
        {
            get
            {
                lock (_gate)
                {
                    return _ownedReferences.ToArray();
                }
            }
        }

        /// <summary>
        /// 建立新的隔離 Runtime Generation，並只透過 Registry 取得可共享的 Admission Registration。
        /// 任一設定失敗發生在配置 Runtime 前，避免留下半成品或 Host Slot 引用。
        /// </summary>
        public Task<IDynamicsProfileRuntime> CreateAsync(
            DynamicsProfileDefinition definition,
            long generation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var options = definition.CreateOptionsSnapshot();
            OrganizationAdmissionPlan.TryCreate(
                    options,
                    options.Admission,
                    out var plan,
                    out var error)
                .Should().BeTrue(error?.ErrorMessage);
            var registration = _registry.Acquire(plan!);
            var runtime = new RegistryBackedRuntime(
                definition,
                generation,
                registration,
                _counters[plan!.CanonicalKey.ExpectedOrganizationId]);

            lock (_gate)
            {
                _runtimes.Add(
                    (definition.ProfileAlias, generation),
                    new WeakReference<RegistryBackedRuntime>(runtime));
                _ownedReferences.Add(new WeakReference(runtime));
                _ownedReferences.Add(new WeakReference(runtime.Client));
                _ownedReferences.Add(new WeakReference(runtime.RetirementSource));
                foreach (var resource in runtime.OwnedResources)
                {
                    _ownedReferences.Add(new WeakReference(resource));
                }
            }

            return Task.FromResult<IDynamicsProfileRuntime>(runtime);
        }

        /// <summary>依 Alias／Generation 取得仍存活的 Runtime，供測試持有舊 Lease 或比較 Client ownership。</summary>
        public RegistryBackedRuntime GetRuntime(string alias, long generation)
        {
            lock (_gate)
            {
                _runtimes.TryGetValue((alias, generation), out var reference).Should().BeTrue();
                reference!.TryGetTarget(out var runtime).Should().BeTrue();
                return runtime!;
            }
        }

        /// <summary>
        /// 關閉 Registry 並等待任何剩餘 Admission Manager／Host Slot 清理；重複呼叫安全且不保留 Runtime 強引用。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await _registry.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Registry-backed 測試 Runtime，實作與 Production 相同的 Active→Draining→Disposed、execution reference count
    /// 與 Registration 最後釋放語意，但使用無網路 Client 與模擬 owned resources 以進行快速資源基線測試。
    /// </summary>
    private sealed class RegistryBackedRuntime : IDynamicsProfileRuntime
    {
        private readonly object _gate = new();
        private readonly DynamicsProfileDefinition _definition;
        private readonly IOrganizationAdmissionRegistration _registration;
        private readonly CancellationTokenSource _retirementSource = new();
        private TaskCompletionSource _zeroExecutions = CreateCompletedRuntimeSignal();
        private int _activeExecutions;
        private DynamicsProfileRuntimeState _state = DynamicsProfileRuntimeState.Active;

        /// <summary>建立一個新 Generation，並接管 Registration 與三個模擬 Generation-owned 資源。</summary>
        public RegistryBackedRuntime(
            DynamicsProfileDefinition definition,
            long generation,
            IOrganizationAdmissionRegistration registration,
            SharedConcurrencyCounter counter)
        {
            _definition = definition;
            _registration = registration;
            var options = definition.CreateOptionsSnapshot();
            Key = new ProfileRuntimeKey(
                definition.ProfileAlias,
                generation,
                options.CeVersion,
                registration.Plan.CanonicalKey);
            Client = new RegistryBackedClient(Key, counter);
            OwnedResources =
            [
                new TrackingOwnedResource("transport"),
                new TrackingOwnedResource("token-provider"),
                new TrackingOwnedResource("handler")
            ];
        }

        /// <summary>取得此 Generation 唯一擁有的無網路 Client。</summary>
        public RegistryBackedClient Client { get; }

        /// <summary>取得 Retirement CTS，僅供 WeakReference 生命週期驗證。</summary>
        public CancellationTokenSource RetirementSource => _retirementSource;

        /// <summary>取得模擬 Transport／Token Provider／Handler，僅供 Dispose 與 WeakReference 驗證。</summary>
        public IReadOnlyList<TrackingOwnedResource> OwnedResources { get; }

        /// <summary>取得此 Soak Generation 的不可變 Runtime Key，用來驗證 Alias 與 Canonical Organization 隔離。</summary>
        public ProfileRuntimeKey Key { get; }

        /// <summary>在測試鎖內取得單向生命週期狀態，避免 Soak 斷言與 drain 轉換發生資料競速。</summary>
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

        /// <summary>取得尚未釋放的 RegistryBackedExecutionLease 數量，供每輪 drain 回到零的生命週期斷言。</summary>
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

        /// <summary>取得 Registry Registration 綁定的共享 Admission Manager；Runtime 只擁有 Registration，不直接 Dispose Manager。</summary>
        public IOrganizationAdmissionManager AdmissionManager => _registration.Manager;

        /// <summary>取得不含 Request、Token、Credential 或 Session 的 bounded 容量快照。</summary>
        public AdmissionMetricsSnapshot AdmissionSnapshot => AdmissionManager.GetSnapshot();

        /// <summary>
        /// 只在 Active 狀態增加 execution reference 並建立 Lease；Draining 後 fail closed，
        /// 使替換 Soak 可以證明舊 Generation 不再接受新工作，也不會發生 use-after-dispose。
        /// </summary>
        public bool TryAcquireExecution(out IDynamicsProfileExecutionLease? lease)
        {
            lock (_gate)
            {
                if (_state != DynamicsProfileRuntimeState.Active)
                {
                    lease = null;
                    return false;
                }

                if (_activeExecutions == 0)
                {
                    _zeroExecutions = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }

                _activeExecutions++;
                lease = new RegistryBackedExecutionLease(this);
                return true;
            }
        }

        /// <summary>透過無網路 Client 執行固定 WhoAmI，用來涵蓋 warm-up 路徑而不接觸真實 CRM 或秘密。</summary>
        public Task<OperationExecutionResult> WarmUpAsync(CancellationToken cancellationToken)
            => Client.WhoAmIAsync(cancellationToken);

        /// <summary>把 Runtime 單向切換為 Draining，停止新 Lease 並保留既有 execution reference 到有界清理完成。</summary>
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
        /// 等待 execution reference 歸零；逾時時取消 Retirement Token，之後 Dispose 每個 Generation-owned 模擬資源、
        /// 釋放 Registry Registration 並回收 CTS。所有 ownership 都在方法返回前完成，供 WeakReference／baseline 驗證。
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

                zeroTask = _activeExecutions == 0
                    ? Task.CompletedTask
                    : _zeroExecutions.Task;
            }

            try
            {
                await zeroTask.WaitAsync(_definition.DrainTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _retirementSource.Cancel();
                await zeroTask.WaitAsync(_definition.CancellationGracePeriod, cancellationToken);
            }

            lock (_gate)
            {
                if (_state == DynamicsProfileRuntimeState.Disposed)
                {
                    return;
                }

                _state = DynamicsProfileRuntimeState.Disposed;
            }

            foreach (var resource in OwnedResources)
            {
                resource.Dispose();
            }

            await _registration.DisposeAsync();
            _retirementSource.Dispose();
        }

        /// <summary>同步等待同一個 bounded drain，不留下未觀察的背景清理。</summary>
        public void Dispose()
            => Task.Run(async () => await DrainAndDisposeAsync())
                .GetAwaiter()
                .GetResult();

        /// <summary>非同步 Dispose 委派至同一個 drain 路徑。</summary>
        public ValueTask DisposeAsync() => new(DrainAndDisposeAsync());

        /// <summary>釋放一個 Execution Lease 引用，歸零時喚醒正在等待的 drain。</summary>
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

        /// <summary>建立初始已完成的 execution-zero 訊號。</summary>
        private static TaskCompletionSource CreateCompletedRuntimeSignal()
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.TrySetResult();
            return signal;
        }

        /// <summary>只保存 Runtime owner 的 Execution Lease，Dispose 以 Interlocked 保證引用只遞減一次。</summary>
        private sealed class RegistryBackedExecutionLease : IDynamicsProfileExecutionLease
        {
            private readonly RegistryBackedRuntime _owner;
            private int _disposed;

            /// <summary>建立已由 Runtime 增加引用的 Lease。</summary>
            public RegistryBackedExecutionLease(RegistryBackedRuntime owner)
            {
                _owner = owner;
            }

            /// <summary>取得此 Lease 綁定的不可變 Runtime Key，供 Soak 驗證 queued work 不會黏住舊 Generation。</summary>
            public ProfileRuntimeKey RuntimeKey => _owner.Key;

            /// <summary>取得 owner 的無網路 Client；只允許在 Lease 有效期間使用，不保存到 Queue 或全域狀態。</summary>
            public IDynamicsWebApiClient Client => _owner.Client;

            /// <summary>取得 drain timeout 後的退休取消訊號，讓 Soak 工作可有界離開。</summary>
            public CancellationToken RetirementToken => _owner._retirementSource.Token;

            /// <summary>同步釋放 Runtime active reference。</summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.ReleaseExecution();
                }
            }

            /// <summary>非同步相容路徑不執行 I/O。</summary>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// 無網路測試 Client；每次 Operation 只進入 Organization 對應的共享併發計數器並短暫延遲，
    /// 不保存 Parameter、WorkloadSubjectId、Token、Credential 或 Session。
    /// </summary>
    private sealed class RegistryBackedClient : IDynamicsWebApiClient
    {
        private readonly SharedConcurrencyCounter _counter;

        /// <summary>建立綁定單一 Runtime Key 與 Canonical Organization Counter 的 Client。</summary>
        public RegistryBackedClient(
            ProfileRuntimeKey key,
            SharedConcurrencyCounter counter)
        {
            Key = key;
            _counter = counter;
        }

        /// <summary>取得此 Client 所屬的不可變 Runtime Generation Key。</summary>
        public ProfileRuntimeKey Key { get; }

        /// <summary>執行 Registry 中固定的 WhoAmI Definition，不允許測試傳入任意 URL、Entity 或 FetchXML。</summary>
        public Task<OperationExecutionResult> WhoAmIAsync(CancellationToken cancellationToken = default)
            => ExecuteRegisteredOperationAsync(
                Package01OperationRegistry.All.Single(definition =>
                    definition.CapabilityOperationId == OperationIds.RuntimeHealthWhoAmI),
                new Dictionary<string, object?>(),
                cancellationToken);

        /// <summary>
        /// 在共享 Canonical Organization 計數器中登記短暫外呼，並於 finally 必定歸還 active count。
        /// 方法不保存 Parameters 或工作負載識別，取消或例外也不能讓併發基準永久升高。
        /// </summary>
        public async Task<OperationExecutionResult> ExecuteRegisteredOperationAsync(
            OperationDefinition definition,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            var active = _counter.Enter();
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(2), cancellationToken);
                return OperationExecutionResult.Success(
                    new { Key.ProfileAlias, Key.Generation });
            }
            finally
            {
                _counter.Exit(active);
            }
        }
    }

    /// <summary>
    /// 模擬一個 Generation-owned Transport／Token Provider／Handler 資源；只記錄 Dispose 次數，
    /// 不含 Socket、Token、Credential、Request 或 Session 資料。
    /// </summary>
    private sealed class TrackingOwnedResource : IDisposable
    {
        private int _disposed;

        /// <summary>建立具非秘密診斷名稱的模擬資源。</summary>
        public TrackingOwnedResource(string name)
        {
            Name = name;
        }

        /// <summary>取得 bounded 診斷名稱。</summary>
        public string Name { get; }

        /// <summary>以 Interlocked 保證資源只 Dispose 一次。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                throw new InvalidOperationException(
                    $"Tracking resource '{Name}' was disposed more than once.");
            }
        }
    }
}
