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
/// Phase 4 隔離與資源浸泡測試。
/// 以多 workload、多設定檔世代及兩個實體 endpoint 的高併發流量，驗證容量不倍增、Authorization/Cookie 不串流、
/// queue/permit/workload counter 排空，以及 handler、記憶體、handle、thread 與強參考在 Dispose 後回到有界基線。
/// </summary>
public sealed class Phase4IsolationSoakTests
{
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
}
