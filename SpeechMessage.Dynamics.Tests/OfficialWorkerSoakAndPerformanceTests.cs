using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WorkerSupervisor;
using Xunit.Abstractions;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 定義官方 Worker 本機 soak 測試的獨立 xUnit collection。
/// 此 collection 停用跨 collection 平行執行，避免其他測試同時啟動 WorkerTestHost，
/// 讓行程、Handle、Thread 與延遲觀測只反映本測試擁有的有界工作量。
/// </summary>
internal static class OfficialWorkerSoakTestCollection
{
    internal const string Name = "Official worker soak and performance";
}

/// <summary>
/// 將官方 Worker soak 測試與其餘測試序列化；本型別不持有行程、Pipe、Timer 或背景工作。
/// </summary>
[CollectionDefinition(OfficialWorkerSoakTestCollection.Name, DisableParallelization = true)]
public sealed class OfficialWorkerSoakTestCollectionDefinition
{
}

/// <summary>
/// 以真實 <c>SpeechMessage.Dynamics.WorkerTestHost</c> 與
/// <see cref="OfficialWorkerProfileExecutor"/> 執行有界 Package01 壓力、回收與 no-leak 驗證。
/// 測試只使用 SDK-free 假 Worker、具名 Pipe 與測試資料，不解析 CRM endpoint、Credential、Token、
/// Session 或任何網路資源；每一代 Worker 的 Process、Pipe、輸出讀取背景工作與 active operation
/// 都必須在 recycle/drain 後回到零，測試另以精確 PID 與啟動時間確認 OS 行程已退出。
/// </summary>
[Collection(OfficialWorkerSoakTestCollection.Name)]
public sealed class OfficialWorkerSoakAndPerformanceTests
{
    private const int GenerationCount = 6;
    private const int WarmUpRequestsPerGeneration = 64;
    private const int MeasuredRequestsPerGeneration = 64;
    private const int RecyclableRequestsPerGeneration =
        WarmUpRequestsPerGeneration + MeasuredRequestsPerGeneration;
    private const int ResourceSampleInterval = 16;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WholeTestTimeout = TimeSpan.FromSeconds(90);
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// 建立只輸出去識別化數值觀測的測試實例；輸出不包含 PID、Pipe 名稱、Nonce、
    /// Profile secret、CRM 位址或呼叫者 Session 資料。
    /// </summary>
    /// <param name="output">xUnit 擁有的有界測試輸出介面。</param>
    public OfficialWorkerSoakAndPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 反覆啟動六個獨立 Worker generation。每代先以相同的有界 Package01 request 完成 64 次
    /// JIT、IPC codec 與 GC heap 暖機，再量測後續 64 次 request 的 Private Bytes、Working Set、
    /// Handle、Thread、p95 與 p99。暖機與量測都走同一個 Supervisor／Worker／Pipe／Lease 路徑，
    /// 且共同計入 completed-operation recycle policy；測試絕不強制 GC 或降低 50% 趨勢門檻。
    /// 量測完成後必須由完成次數門檻觸發 sticky recycle，再送一個只應在 Supervisor 端被拒絕的 request
    /// 使該代 drain。這可將新 .NET process 的初始 heap 配置與同一代 request-result retention 分開，
    /// 同時維持 aggregate p99 位於 operation 加 drain 的有限生命週期 budget 內。
    /// </summary>
    [Fact]
    public async Task WorkerSoak_repeated_package01_recycle_returns_all_owners_to_zero_without_unbounded_trends()
    {
        var executablePath = FindTestWorkerExecutable();
        var executableHash = ComputeSha256(executablePath);
        var runId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var ownedProcesses = new HashSet<TestOwnedProcessIdentity>();
        var generations = new List<GenerationObservation>(GenerationCount);
        var supervisorManagedHeapSamples = new List<long>(GenerationCount);
        var supervisorAllocationSamples = new List<long>(GenerationCount);
        using var wholeTestCancellation = new CancellationTokenSource(WholeTestTimeout);

        try
        {
            for (var generationIndex = 0; generationIndex < GenerationCount; generationIndex++)
            {
                var allocatedBytesBeforeGeneration = GC.GetTotalAllocatedBytes(precise: true);
                var observation = await RunGenerationAsync(
                    executablePath,
                    executableHash,
                    runId,
                    generationIndex,
                    ownedProcesses,
                    wholeTestCancellation.Token);
                generations.Add(observation);
                supervisorAllocationSamples.Add(
                    GC.GetTotalAllocatedBytes(precise: true) - allocatedBytesBeforeGeneration);
                supervisorManagedHeapSamples.Add(
                    GC.GetTotalMemory(forceFullCollection: true));

                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "generation={0:D2} warmUpRequests={1} measuredRequests={2} p95Ms={3:F3} p99Ms={4:F3} privateBytes={5} workingSetBytes={6} handles={7} threads={8} activeHighWater={9} queuedHighWater={10} supervisorManagedHeapBytes={11} supervisorAllocatedBytes={12}",
                    generationIndex,
                    WarmUpRequestsPerGeneration,
                    observation.Latencies.Count,
                    GetPercentile(observation.Latencies, 0.95).TotalMilliseconds,
                    GetPercentile(observation.Latencies, 0.99).TotalMilliseconds,
                    observation.ResourceSamples[^1].PrivateBytes,
                    observation.ResourceSamples[^1].WorkingSetBytes,
                    observation.ResourceSamples[^1].HandleCount,
                    observation.ResourceSamples[^1].ThreadCount,
                    observation.ActiveOperationHighWaterMark,
                    observation.QueuedOperationHighWaterMark,
                    supervisorManagedHeapSamples[^1],
                    supervisorAllocationSamples[^1]));
            }

            var allLatencies = generations.SelectMany(item => item.Latencies).ToArray();
            allLatencies.Should().HaveCount(
                GenerationCount * MeasuredRequestsPerGeneration);
            allLatencies.Should().OnlyContain(
                elapsed => elapsed >= TimeSpan.Zero &&
                    elapsed < OperationTimeout + DrainTimeout,
                because: "each successful request is bounded by the operation owner and never includes a drain wait");
            var p99Latency = GetPercentile(allLatencies, 0.99);
            p99Latency.Should().BeLessThan(
                OperationTimeout + DrainTimeout,
                because: "the measured p99 must remain inside the same bounded operation lifecycle budget");

            var stableGenerations = generations.Skip(1).ToArray();
            AssertNoConservativeSustainedGrowth(
                stableGenerations.Select(item => item.ResourceSamples[^1].PrivateBytes).ToArray(),
                relativeGrowthAllowance: 0.50,
                "cross-generation private bytes");
            AssertNoConservativeSustainedGrowth(
                stableGenerations.Select(item => item.ResourceSamples[^1].WorkingSetBytes).ToArray(),
                relativeGrowthAllowance: 0.50,
                "cross-generation working set");
            AssertNoConservativeSustainedGrowth(
                stableGenerations.Select(item => (long)item.ResourceSamples[^1].HandleCount).ToArray(),
                relativeGrowthAllowance: 0.50,
                "cross-generation handle count");
            AssertNoConservativeSustainedGrowth(
                stableGenerations.Select(item => (long)item.ResourceSamples[^1].ThreadCount).ToArray(),
                relativeGrowthAllowance: 0.50,
                "cross-generation thread count");
            AssertNoConservativeSustainedGrowth(
                stableGenerations
                    .Select(item => GetPercentile(item.Latencies, 0.95).Ticks)
                    .ToArray(),
                relativeGrowthAllowance: 2.00,
                "cross-generation p95 latency");
            AssertNoConservativeSustainedGrowth(
                stableGenerations
                    .Select(item => GetPercentile(item.Latencies, 0.99).Ticks)
                    .ToArray(),
                relativeGrowthAllowance: 2.00,
                "cross-generation p99 latency");
            AssertNoConservativeSustainedGrowth(
                supervisorManagedHeapSamples.Skip(1).ToArray(),
                relativeGrowthAllowance: 0.50,
                "cross-generation supervisor managed heap");
            AssertNoConservativeSustainedGrowth(
                supervisorAllocationSamples.Skip(1).ToArray(),
                relativeGrowthAllowance: 1.00,
                "cross-generation supervisor allocation per fixed workload");

            foreach (var process in ownedProcesses)
            {
                IsMatchingProcessRunning(process).Should().BeFalse(
                    because: "every test-owned WorkerTestHost process must exit after its generation drains");
            }

            _output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "summary generations={0} requests={1} p50Ms={2:F3} p95Ms={3:F3} p99Ms={4:F3} maxMs={5:F3} maxQueued={6} finalSupervisorManagedHeapBytes={7} maxSupervisorAllocatedBytesPerGeneration={8} retiredProcesses=0 retiredPipes=0 retiredGates=0 retiredReaders=0 retiredTasks=0 retiredOutputCancellationSources=0 retiredEntrants=0 retiredActive=0",
                GenerationCount,
                allLatencies.Length,
                GetPercentile(allLatencies, 0.50).TotalMilliseconds,
                GetPercentile(allLatencies, 0.95).TotalMilliseconds,
                p99Latency.TotalMilliseconds,
                allLatencies.Max().TotalMilliseconds,
                generations.Max(item => item.QueuedOperationHighWaterMark),
                supervisorManagedHeapSamples[^1],
                supervisorAllocationSamples.Max()));
        }
        finally
        {
            // 測試失敗時仍只終止 PID、啟動時間與行程名稱完全相符的 test-owned 行程；
            // PID 若已被 OS 重用，絕不可終止新的無關行程。
            await TerminateAnyRemainingTestOwnedProcessesAsync(ownedProcesses);
        }
    }

    /// <summary>
    /// 同時保留一個 CE 8.2 與一個 CE 9.1 Worker generation，並平行執行相同的 SDK-free
    /// Package01 operation。兩個 Executor 必須擁有不同 Process 與 Pipe，結果 graph 也不得共用；
    /// finally 會依序 Dispose 兩個唯一 owner，再以 PID identity 確認沒有留下跨版本行程或 Handle。
    /// </summary>
    [Fact]
    public async Task Simultaneous_ce82_and_ce91_workers_keep_process_pipe_and_result_ownership_isolated()
    {
        var executablePath = FindTestWorkerExecutable();
        var executableHash = ComputeSha256(executablePath);
        var runId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var ownedProcesses = new HashSet<TestOwnedProcessIdentity>();
        OfficialWorkerProfileExecutor? ce82Executor = null;
        OfficialWorkerProfileExecutor? ce91Executor = null;
        using var wholeTestCancellation = new CancellationTokenSource(WholeTestTimeout);

        try
        {
            ce82Executor = await OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    executableHash,
                    $"profile-generation-package01-large-valid-ce82-{runId}",
                    OfficialWorkerVersion.Ce82,
                    "crm82-test"),
                wholeTestCancellation.Token);
            var ce82Identity = CaptureOwnedProcessIdentity(ce82Executor);
            ownedProcesses.Add(ce82Identity).Should().BeTrue();

            ce91Executor = await OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    executableHash,
                    $"profile-generation-package01-large-valid-ce91-{runId}",
                    OfficialWorkerVersion.Ce91,
                    "crm91-test"),
                wholeTestCancellation.Token);
            var ce91Identity = CaptureOwnedProcessIdentity(ce91Executor);
            ownedProcesses.Add(ce91Identity).Should().BeTrue();

            ce82Identity.Should().NotBe(ce91Identity);
            AssertActiveOwnership(ce82Executor.GetLifecycleSnapshot());
            AssertActiveOwnership(ce91Executor.GetLifecycleSnapshot());

            var results = await Task.WhenAll(
                ce82Executor.ExecuteAsync(
                    CreatePackage01Request(82, 0, "crm82-test"),
                    wholeTestCancellation.Token),
                ce91Executor.ExecuteAsync(
                    CreatePackage01Request(91, 0, "crm91-test"),
                    wholeTestCancellation.Token));

            results.Should().OnlyContain(result => result.Succeeded);
            var ce82Data = results[0].Data;
            var ce91Data = results[1].Data;
            ce82Data.Should().NotBeNull();
            ce91Data.Should().NotBeNull();
            ce82Data.Should().NotBeSameAs(ce91Data);
            ce82Data!.FeeRecords.Should().NotBeSameAs(ce91Data!.FeeRecords);
            ce82Data.FeeRecords.Should().HaveCount(30);
            ce91Data.FeeRecords.Should().HaveCount(30);
            AssertActiveOwnership(ce82Executor.GetLifecycleSnapshot());
            AssertActiveOwnership(ce91Executor.GetLifecycleSnapshot());

            await ce82Executor.DisposeAsync();
            AssertFullyRetired(ce82Executor.GetLifecycleSnapshot());
            ce82Executor = null;
            await ce91Executor.DisposeAsync();
            AssertFullyRetired(ce91Executor.GetLifecycleSnapshot());
            ce91Executor = null;

            await WaitForProcessExitAsync(ce82Identity);
            await WaitForProcessExitAsync(ce91Identity);
            IsMatchingProcessRunning(ce82Identity).Should().BeFalse();
            IsMatchingProcessRunning(ce91Identity).Should().BeFalse();
        }
        finally
        {
            if (ce91Executor is not null)
            {
                await ce91Executor.DisposeAsync();
                AssertFullyRetired(ce91Executor.GetLifecycleSnapshot());
            }

            if (ce82Executor is not null)
            {
                await ce82Executor.DisposeAsync();
                AssertFullyRetired(ce82Executor.GetLifecycleSnapshot());
            }

            await TerminateAnyRemainingTestOwnedProcessesAsync(ownedProcesses);
        }
    }

    /// <summary>
    /// 執行單一 immutable Worker generation 的同形負載 warm-up、steady-state 取樣、完成次數 recycle
    /// 與 deterministic drain。Executor 是 Process/Pipe/reader task 的唯一 production owner；測試僅保存
    /// PID identity 與純量觀測，不持有或 Dispose Executor 內部的 Process handle。
    /// </summary>
    private static async Task<GenerationObservation> RunGenerationAsync(
        string executablePath,
        string executableHash,
        string runId,
        int generationIndex,
        ISet<TestOwnedProcessIdentity> ownedProcesses,
        CancellationToken cancellationToken)
    {
        var options = CreateOptions(
            executablePath,
            executableHash,
            $"profile-generation-package01-large-valid-soak-{runId}-{generationIndex:D2}");
        OfficialWorkerProfileExecutor? executor = null;
        TestOwnedProcessIdentity? processIdentity = null;
        var latencies = new List<TimeSpan>(MeasuredRequestsPerGeneration);
        var resourceSamples = new List<ProcessResourceObservation>(
            MeasuredRequestsPerGeneration / ResourceSampleInterval);
        var activeOperationHighWaterMark = 0;
        var queuedOperationHighWaterMark = 0;

        try
        {
            executor = await OfficialWorkerProfileExecutor.StartAsync(options, cancellationToken);
            processIdentity = CaptureOwnedProcessIdentity(executor);
            ownedProcesses.Add(processIdentity.Value).Should().BeTrue();
            AssertActiveOwnership(executor.GetLifecycleSnapshot());

            for (var requestIndex = 0;
                 requestIndex < WarmUpRequestsPerGeneration;
                 requestIndex++)
            {
                var warmUpResult = await executor.ExecuteAsync(
                    CreatePackage01Request(generationIndex, requestIndex),
                    cancellationToken);
                AssertSuccessfulPackage01Result(warmUpResult);
                AssertActiveOwnership(executor.GetLifecycleSnapshot());
            }

            for (var requestIndex = 0;
                 requestIndex < MeasuredRequestsPerGeneration;
                 requestIndex++)
            {
                var startedTimestamp = Stopwatch.GetTimestamp();
                var execution = executor.ExecuteAsync(
                    CreatePackage01Request(
                        generationIndex,
                        WarmUpRequestsPerGeneration + requestIndex),
                    cancellationToken);
                var lifecycleSnapshot = executor.GetLifecycleSnapshot();
                activeOperationHighWaterMark = Math.Max(
                    activeOperationHighWaterMark,
                    lifecycleSnapshot.ActiveOperationCount);
                queuedOperationHighWaterMark = Math.Max(
                    queuedOperationHighWaterMark,
                    Math.Max(
                        0,
                        lifecycleSnapshot.OperationEntrantCount -
                        lifecycleSnapshot.ActiveOperationCount));

                var result = await execution;
                latencies.Add(Stopwatch.GetElapsedTime(startedTimestamp));
                AssertSuccessfulPackage01Result(result);
                AssertActiveOwnership(executor.GetLifecycleSnapshot());

                if ((requestIndex + 1) % ResourceSampleInterval == 0)
                {
                    resourceSamples.Add(ObserveProcessResources(processIdentity.Value));
                }
            }

            activeOperationHighWaterMark.Should().BeInRange(0, 1);
            queuedOperationHighWaterMark.Should().BeInRange(0, 1);
            resourceSamples.Should().HaveCount(
                MeasuredRequestsPerGeneration / ResourceSampleInterval);
            AssertPostWarmUpResourceTrend(resourceSamples);

            executor.RecycleReason.Should().Be(
                OfficialWorkerRecycleReason.MaximumCompletedOperations);
            executor.EvaluateRecycleForNextAdmission().Should().Be(
                OfficialWorkerRecycleReason.MaximumCompletedOperations);

            var rejected = await executor.ExecuteAsync(
                CreatePackage01Request(generationIndex, RecyclableRequestsPerGeneration),
                cancellationToken);
            rejected.Succeeded.Should().BeFalse();
            rejected.ErrorCode.Should().Be("worker.operation.recycle-required");
            AssertFullyRetired(executor.GetLifecycleSnapshot());

            await WaitForProcessExitAsync(processIdentity.Value);
            IsMatchingProcessRunning(processIdentity.Value).Should().BeFalse();
            return new GenerationObservation(
                latencies.ToArray(),
                resourceSamples.ToArray(),
                activeOperationHighWaterMark,
                queuedOperationHighWaterMark);
        }
        finally
        {
            if (executor is not null)
            {
                await executor.DisposeAsync();
                AssertFullyRetired(executor.GetLifecycleSnapshot());
            }

            if (processIdentity.HasValue)
            {
                await WaitForProcessExitAsync(processIdentity.Value);
            }
        }
    }

    /// <summary>
    /// 建立不含 endpoint、Credential、Token、Cookie 或 Session 的 Worker bootstrap options。
    /// 完成次數是本測試唯一 recycle 觸發器；記憶體門檻刻意設為遠高於測試行程，避免把機器差異誤判成 recycle。
    /// </summary>
    private static OfficialWorkerProfileOptions CreateOptions(
        string executablePath,
        string executableHash,
        string profileGenerationId,
        OfficialWorkerVersion workerVersion = OfficialWorkerVersion.Ce91,
        string profileAlias = "crm91-test")
    {
        return new OfficialWorkerProfileOptions
        {
            ProfileAlias = profileAlias,
            ProfileGenerationId = profileGenerationId,
            WorkerVersion = workerVersion,
            WorkerExecutablePath = executablePath,
            WorkerExecutableSha256 = executableHash,
            PackageLockId = "test-worker-package-lock-0001",
            StartupTimeout = StartupTimeout,
            OperationTimeout = OperationTimeout,
            DrainTimeout = DrainTimeout,
            RecyclePolicyOptions = new OfficialWorkerRecyclePolicyOptions(
                maximumWorkerAge: TimeSpan.FromMinutes(10),
                maximumCompletedOperations: RecyclableRequestsPerGeneration,
                maximumPrivateBytes: 1L << 40,
                maximumWorkingSet: 1L << 40,
                maximumConsecutiveCompleteWorkerTimeouts: 10)
        };
    }

    /// <summary>
    /// 建立固定、唯讀、SDK-free 的 Package01 request；WorkloadSubjectId 只含測試代次與序號，
    /// 不含使用者、LINE ID、JWT、瀏覽器 Session、CRM account 或 Credential。
    /// </summary>
    private static OperationExecutionRequest CreatePackage01Request(
        int generationIndex,
        int requestIndex,
        string profileAlias = "crm91-test")
    {
        return new OperationExecutionRequest
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = OperationIds.FeeDedicationRetrieveByContactDateRange,
            WorkloadSubjectId = $"official-worker-soak-{generationIndex:D2}-{requestIndex:D2}",
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ["contactName"] = "compatibility-only",
                ["startDate"] = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                ["endDate"] = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero)
            }
        };
    }

    /// <summary>
    /// 驗證 warm-up 與 measured window 都收到同一個有限、SDK-free 的 Package01 projection。
    /// 此方法不保留 response graph、CRM 型別或 request state；呼叫端在斷言後立即讓結果離開作用域，
    /// 使 measured window 的 process-resource 趨勢只反映 Worker 端實際 retention，而不是測試端集合。
    /// </summary>
    /// <param name="result">由當前 immutable Worker generation 回傳的 operation 結果。</param>
    private static void AssertSuccessfulPackage01Result(OperationExecutionResult result)
    {
        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.Package01FeeRecords);
        result.Data.FeeRecords.Should().HaveCount(30);
        result.Data.FeeRecords![0].Amount.Should().Be(123.45m);
    }

    /// <summary>
    /// 驗證 generation 對外服務期間只有一個 Process、一個 Pipe、一個 operation gate、兩個輸出
    /// reader/task 與一個 output cancellation source；已完成的循序 request 不留下 entrant 或 active operation。
    /// </summary>
    private static void AssertActiveOwnership(OfficialWorkerLifecycleSnapshot snapshot)
    {
        snapshot.IsReady.Should().BeTrue();
        snapshot.OwnedProcessCount.Should().Be(1);
        snapshot.OwnedPipeCount.Should().Be(1);
        snapshot.OwnedBackgroundTaskCount.Should().Be(2);
        snapshot.OwnedOperationGateCount.Should().Be(1);
        snapshot.OwnedOutputReaderCount.Should().Be(2);
        snapshot.OwnedOutputTaskCount.Should().Be(2);
        snapshot.OwnedOutputCancellationSourceCount.Should().Be(1);
        snapshot.OwnedProcessExitWaitCount.Should().Be(0);
        snapshot.OperationEntrantCount.Should().Be(0);
        snapshot.ActiveOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 drain/recycle 已關閉 admission，並釋放 Executor 對 Process、Pipe、operation gate、output
    /// reader/task、output cancellation source、entrant 與 active operation 的全部 ownership。
    /// </summary>
    private static void AssertFullyRetired(OfficialWorkerLifecycleSnapshot snapshot)
    {
        snapshot.IsReady.Should().BeFalse();
        snapshot.OwnedProcessCount.Should().Be(0);
        snapshot.OwnedPipeCount.Should().Be(0);
        snapshot.OwnedBackgroundTaskCount.Should().Be(0);
        snapshot.OwnedOperationGateCount.Should().Be(0);
        snapshot.OwnedOutputReaderCount.Should().Be(0);
        snapshot.OwnedOutputTaskCount.Should().Be(0);
        snapshot.OwnedOutputCancellationSourceCount.Should().Be(0);
        snapshot.OwnedProcessExitWaitCount.Should().Be(0);
        snapshot.OperationEntrantCount.Should().Be(0);
        snapshot.ActiveOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 讀取 Executor 唯一擁有之 Process 的 PID、UTC 啟動時間與行程名稱。
    /// Public lifecycle contract 刻意不暴露 Handle；本測試以 reflection 只複製不可變 identity，
    /// 不保留、關閉或 Dispose production Process 物件。
    /// </summary>
    private static TestOwnedProcessIdentity CaptureOwnedProcessIdentity(
        OfficialWorkerProfileExecutor executor)
    {
        var field = typeof(OfficialWorkerProfileExecutor).GetField(
            "_process",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull(
            because: "the no-leak test must correlate lifecycle counters with the exact executor-owned OS process");
        var process = field!.GetValue(executor)
            .Should().BeOfType<Process>().Subject;
        return new TestOwnedProcessIdentity(
            process.Id,
            process.StartTime.ToUniversalTime(),
            process.ProcessName);
    }

    /// <summary>
    /// 以新的短生命週期 <see cref="Process"/> wrapper 取樣 test-owned Worker 的 Private Bytes、
    /// Working Set、Handle 與 Thread；wrapper 在同一方法內 Dispose，不延長 Worker 或其 OS handle lifetime。
    /// </summary>
    private static ProcessResourceObservation ObserveProcessResources(
        TestOwnedProcessIdentity identity)
    {
        using var process = OpenMatchingProcess(identity) ??
            throw new InvalidOperationException("The test-owned worker exited before resource observation.");
        process.Refresh();
        var observation = new ProcessResourceObservation(
            process.PrivateMemorySize64,
            process.WorkingSet64,
            process.HandleCount,
            process.Threads.Count);
        observation.PrivateBytes.Should().BePositive();
        observation.WorkingSetBytes.Should().BePositive();
        observation.HandleCount.Should().BePositive();
        observation.ThreadCount.Should().BePositive();
        return observation;
    }

    /// <summary>
    /// 將每代第一次 resource sample 視為 JIT/IPC warm-up baseline，再檢查後續 delta。
    /// 只有所有 delta 非負、至少三次為正，且總增幅超過 baseline 的 50% 才視為持續無界趨勢；
    /// 此相對且保守的門檻避免用固定 MB、Handle 或 Thread 數綁定特定硬體。
    /// </summary>
    private static void AssertPostWarmUpResourceTrend(
        IReadOnlyList<ProcessResourceObservation> samples)
    {
        AssertNoConservativeSustainedGrowth(
            samples.Select(item => item.PrivateBytes).ToArray(),
            relativeGrowthAllowance: 0.50,
            "within-generation private bytes");
        AssertNoConservativeSustainedGrowth(
            samples.Select(item => item.WorkingSetBytes).ToArray(),
            relativeGrowthAllowance: 0.50,
            "within-generation working set");
        AssertNoConservativeSustainedGrowth(
            samples.Select(item => (long)item.HandleCount).ToArray(),
            relativeGrowthAllowance: 0.50,
            "within-generation handle count");
        AssertNoConservativeSustainedGrowth(
            samples.Select(item => (long)item.ThreadCount).ToArray(),
            relativeGrowthAllowance: 0.50,
            "within-generation thread count");
    }

    /// <summary>
    /// 使用 post-warm-up 相鄰 delta 判斷持續成長；單一尖峰、下降、平台期或小幅雜訊不會失敗。
    /// 這是趨勢 release gate，不是硬體絕對效能 benchmark。
    /// </summary>
    private static void AssertNoConservativeSustainedGrowth(
        IReadOnlyList<long> samples,
        double relativeGrowthAllowance,
        string metricName)
    {
        samples.Should().HaveCountGreaterThanOrEqualTo(4);
        samples.Should().OnlyContain(value => value >= 0);

        var deltas = new long[samples.Count - 1];
        for (var index = 1; index < samples.Count; index++)
        {
            deltas[index - 1] = samples[index] - samples[index - 1];
        }

        var sustainedIncrease = deltas.All(delta => delta >= 0) &&
            deltas.Count(delta => delta > 0) >= Math.Min(3, deltas.Length);
        var baseline = Math.Max(1L, samples[0]);
        var materialGrowth = samples[^1] - samples[0] >
            (long)Math.Ceiling(baseline * relativeGrowthAllowance);
        var samplesSummary = string.Join(
            ", ",
            samples.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        var deltasSummary = string.Join(
            ", ",
            deltas.Select(value => value.ToString(CultureInfo.InvariantCulture)));

        (sustainedIncrease && materialGrowth).Should().BeFalse(
            because: $"{metricName} must not show a material monotonic post-warm-up trend; " +
                $"samples=[{samplesSummary}], deltas=[{deltasSummary}], " +
                $"baseline={baseline.ToString(CultureInfo.InvariantCulture)}, " +
                $"allowance={relativeGrowthAllowance.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// 計算固定樣本陣列的 nearest-rank percentile；方法只配置與樣本數相同的 bounded 陣列，
    /// 不建立背景 aggregator、Timer 或長生命週期 telemetry collection。
    /// </summary>
    private static TimeSpan GetPercentile(IReadOnlyList<TimeSpan> samples, double percentile)
    {
        samples.Should().NotBeEmpty();
        percentile.Should().BeInRange(0.0, 1.0);
        var orderedTicks = samples.Select(item => item.Ticks).Order().ToArray();
        var rank = Math.Max(1, (int)Math.Ceiling(percentile * orderedTicks.Length));
        return TimeSpan.FromTicks(orderedTicks[rank - 1]);
    }

    /// <summary>
    /// 以 PID、UTC 啟動時間與行程名稱三者確認目前 OS 行程仍是本測試啟動的 Worker，
    /// 避免 PID 重用時把其他行程誤認為漏失資源。
    /// </summary>
    private static Process? OpenMatchingProcess(TestOwnedProcessIdentity identity)
    {
        try
        {
            var process = Process.GetProcessById(identity.ProcessId);
            if (process.HasExited ||
                process.StartTime.ToUniversalTime() != identity.StartTimeUtc ||
                !string.Equals(process.ProcessName, identity.ProcessName, StringComparison.Ordinal))
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 判斷完整 identity 相符的 test-owned Worker 是否仍在執行；每次查詢都 Dispose 臨時 Process wrapper。
    /// </summary>
    private static bool IsMatchingProcessRunning(TestOwnedProcessIdentity identity)
    {
        using var process = OpenMatchingProcess(identity);
        return process is not null;
    }

    /// <summary>
    /// 在五秒有界 deadline 內等待精確 test-owned Worker 退出；不使用無限 sleep 或未擁有的 Process handle。
    /// </summary>
    private static async Task WaitForProcessExitAsync(TestOwnedProcessIdentity identity)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (!IsMatchingProcessRunning(identity))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException("The test-owned official worker did not exit after drain.");
    }

    /// <summary>
    /// 測試中途失敗時的最後防線：只對完整 identity 相符的 test-owned Worker 執行 entire-tree 終止，
    /// 並等待其退出；已退出或 PID 已重用的行程一律不碰觸。
    /// </summary>
    private async Task TerminateAnyRemainingTestOwnedProcessesAsync(
        IEnumerable<TestOwnedProcessIdentity> identities)
    {
        foreach (var identity in identities)
        {
            try
            {
                using var process = OpenMatchingProcess(identity);
                if (process is null)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or TimeoutException)
            {
                _output.WriteLine(
                    $"test-owned worker cleanup failure category={exception.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// 依目前 Test assembly 的 configuration 尋找由 ProjectReference 建置的 SDK-free WorkerTestHost。
    /// </summary>
    private static string FindTestWorkerExecutable()
    {
        var root = FindRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var executablePath = Path.Combine(
            root,
            "SpeechMessage.Dynamics.WorkerTestHost",
            "bin",
            configuration,
            "net10.0",
            "SpeechMessage.Dynamics.WorkerTestHost.exe");
        File.Exists(executablePath).Should().BeTrue(
            because: "the test project reference must build the SDK-free worker test host");
        return executablePath;
    }

    /// <summary>
    /// 計算 Worker executable 的 SHA-256，讓 Executor 在 Process 啟動前驗證 immutable artifact identity。
    /// </summary>
    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 從 Test assembly 目錄向上尋找唯一 solution root；方法不建立 watcher、cache 或背景掃描。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>
    /// 保存單一 generation 的 bounded latency/resource 純量資料；不保留 Executor、Process、Pipe、Task 或 request graph。
    /// </summary>
    private sealed record GenerationObservation(
        IReadOnlyList<TimeSpan> Latencies,
        IReadOnlyList<ProcessResourceObservation> ResourceSamples,
        int ActiveOperationHighWaterMark,
        int QueuedOperationHighWaterMark);

    /// <summary>
    /// 保存一次 Worker process resource snapshot；所有欄位都是去識別化純量，不持有 OS resource。
    /// </summary>
    private readonly record struct ProcessResourceObservation(
        long PrivateBytes,
        long WorkingSetBytes,
        int HandleCount,
        int ThreadCount);

    /// <summary>
    /// 以 PID、UTC 啟動時間與行程名稱界定 test-owned Worker，防止 PID 重用造成誤判或誤殺。
    /// </summary>
    private readonly record struct TestOwnedProcessIdentity(
        int ProcessId,
        DateTime StartTimeUtc,
        string ProcessName);
}
