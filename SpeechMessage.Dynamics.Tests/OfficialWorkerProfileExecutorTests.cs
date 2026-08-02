using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 以真實子程序與 Windows named pipe 驗證 .NET 10 Supervisor 的最小官方 Worker 路徑。
/// 測試不載入 CRM SDK、不讀 Credential，也不連線到 Dynamics；只證明 process／pipe／
/// discard task／operation lease 在完成與重複 Dispose 後回到零基線。
/// </summary>
public sealed class OfficialWorkerProfileExecutorTests
{
    /// <summary>
    /// 驗證連線池驗證操作必須由 Supervisor 依 registry 產生精確 revision，並把已正規化的
    /// logicalProfileId 轉成 bounded WorkerValue。測試 Worker 只接受該 revision 與該參數，
    /// 因此硬編碼 WhoAmI revision、丟棄參數或 request-time fallback 都會使此測試失敗。
    /// </summary>
    [Fact]
    public async Task Validate_connection_preserves_registry_revision_and_typed_parameter()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-validate-connection",
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = "crm91-test",
                CapabilityOperationId = OperationIds.RuntimePoolValidateConnection,
                WorkloadSubjectId = "worker-supervisor-validate-connection-test",
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["logicalProfileId"] = "crm91-test"
                }
            },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.WhoAmI);
        result.Data.WhoAmI!.OrganizationId.Should().Be(
            Guid.Parse("33333333-3333-3333-3333-333333333333"));

        await executor.DisposeAsync();
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 驗證不合法的 identity-operation parameter count 必須在列舉或複製 caller dictionary 前
    /// fail closed。如此即使非標準呼叫端繞過 ControlPlane preparer，也不能用大型或具副作用的
    /// collection 迫使 Supervisor 配置無界 snapshot；拒絕路徑不會建立 request frame 或寫入 pipe。
    /// </summary>
    [Fact]
    public async Task Invalid_identity_parameter_count_is_rejected_before_parameter_enumeration()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-prevalidation",
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var act = () => executor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = "crm91-test",
                CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                WorkloadSubjectId = "worker-supervisor-prevalidation-test",
                Parameters = new EnumerationForbiddenParameters()
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The official Dynamics worker operation is not permitted.");

        await executor.DisposeAsync();
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 驗證 Worker 即使回傳格式正確且內容可解析，只要 RequestId 並非本次要求的識別碼，
    /// Supervisor 就必須拒絕結果、回傳固定協定錯誤並終止整個 generation，避免跨要求資料誤配。
    /// </summary>
    [Fact]
    public async Task Wrong_response_request_id_returns_protocol_failure_and_retires_generation()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-wrong-request-id",
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-request-id-test"),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("worker.operation.protocol-failure");
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 驗證 caller 取消不可中斷的 Worker 呼叫時，Supervisor 會立即以
    /// OperationCanceledException 回應、強制終止隔離程序，且所有生命週期計數歸零。
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_of_uninterruptible_call_retires_promptly_and_clears_resources()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-hang",
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var elapsed = Stopwatch.StartNew();

        Func<Task> act = () => executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-cancellation-test"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        elapsed.Stop();
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 反覆建立、執行及 drain 多個獨立 generation，證明 process、pipe、背景輸出工作與
    /// operation gate 不會跨 iteration 留存；每輪都必須回到同一個零資源基準線。
    /// </summary>
    [Fact]
    public async Task Repeated_start_execute_and_drain_returns_to_zero_every_iteration()
    {
        var executablePath = FindTestWorkerExecutable();
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var executor = await OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    $"profile-generation-loop-{iteration:D2}",
                    operationTimeout: TimeSpan.FromSeconds(5),
                    drainTimeout: TimeSpan.FromSeconds(2)),
                CancellationToken.None);
            try
            {
                var result = await executor.ExecuteAsync(
                    CreateWhoAmIRequest($"worker-supervisor-loop-{iteration:D2}"),
                    CancellationToken.None);
                result.Succeeded.Should().BeTrue();

                await executor.DisposeAsync();
                AssertFullyRetired(executor.GetLifecycleSnapshot());
            }
            finally
            {
                await executor.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// 模擬官方 SDK 呼叫永不返回時，Supervisor 不得留下半個 frame 或繼續使用已失同步的 Pipe；
    /// 它必須在有限 timeout 後淘汰整個 process generation，並把所有 owner counter 歸零。
    /// </summary>
    [Fact]
    public async Task Operation_timeout_forces_process_exit_and_returns_every_resource_to_zero()
    {
        var executablePath = FindTestWorkerExecutable();
        var options = new OfficialWorkerProfileOptions
        {
            ProfileAlias = "crm91-test",
            ProfileGenerationId = "profile-generation-hang",
            WorkerVersion = OfficialWorkerVersion.Ce91,
            WorkerExecutablePath = executablePath,
            WorkerExecutableSha256 = ComputeSha256(executablePath),
            PackageLockId = "test-worker-package-lock-0001",
            StartupTimeout = TimeSpan.FromSeconds(10),
            OperationTimeout = TimeSpan.FromMilliseconds(150),
            DrainTimeout = TimeSpan.FromSeconds(2)
        };
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            options,
            CancellationToken.None);

        var elapsed = Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = options.ProfileAlias,
                CapabilityOperationId = "runtime.health.whoami",
                WorkloadSubjectId = "worker-supervisor-timeout-test"
            },
            CancellationToken.None);
        elapsed.Stop();

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("worker.operation.timeout");
        elapsed.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(1),
            because: "a timed-out frame is already desynchronized and must force-retire without another drain wait");

        var retired = executor.GetLifecycleSnapshot();
        retired.IsReady.Should().BeFalse();
        retired.OwnedProcessCount.Should().Be(0);
        retired.OwnedPipeCount.Should().Be(0);
        retired.OwnedBackgroundTaskCount.Should().Be(0);
        retired.ActiveOperationCount.Should().Be(0);

        await executor.DisposeAsync();
    }

    /// <summary>
    /// 啟動 READY、執行一個 SDK-free WhoAmI envelope，再以 Drain 結束子程序；所有生命週期
    /// 計數必須歸零，確保不留下 Session、Pipe、Process Handle、背景讀取或強參考。
    /// </summary>
    [Fact]
    public async Task Start_execute_and_dispose_return_every_owned_resource_to_zero()
    {
        var executablePath = FindTestWorkerExecutable();
        var options = new OfficialWorkerProfileOptions
        {
            ProfileAlias = "crm91-test",
            ProfileGenerationId = "profile-generation-0001",
            WorkerVersion = OfficialWorkerVersion.Ce91,
            WorkerExecutablePath = executablePath,
            WorkerExecutableSha256 = ComputeSha256(executablePath),
            PackageLockId = "test-worker-package-lock-0001",
            StartupTimeout = TimeSpan.FromSeconds(10),
            OperationTimeout = TimeSpan.FromSeconds(10),
            DrainTimeout = TimeSpan.FromSeconds(10)
        };
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            options,
            CancellationToken.None);

        var active = executor.GetLifecycleSnapshot();
        active.IsReady.Should().BeTrue();
        active.OwnedProcessCount.Should().Be(1);
        active.OwnedPipeCount.Should().Be(1);
        active.OwnedBackgroundTaskCount.Should().Be(2);
        active.ActiveOperationCount.Should().Be(0);

        var result = await executor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = options.ProfileAlias,
                CapabilityOperationId = "runtime.health.whoami",
                WorkloadSubjectId = "worker-supervisor-test"
            },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.WhoAmI!.UserId.Should().Be(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        result.Data.WhoAmI.BusinessUnitId.Should().Be(
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        result.Data.WhoAmI.OrganizationId.Should().Be(
            Guid.Parse("33333333-3333-3333-3333-333333333333"));

        await executor.DisposeAsync();
        await executor.DisposeAsync();

        var drained = executor.GetLifecycleSnapshot();
        drained.IsReady.Should().BeFalse();
        drained.OwnedProcessCount.Should().Be(0);
        drained.OwnedPipeCount.Should().Be(0);
        drained.OwnedBackgroundTaskCount.Should().Be(0);
        drained.ActiveOperationCount.Should().Be(0);
    }

    private static string FindTestWorkerExecutable()
    {
        var root = FindRepositoryRoot();
        var executablePath = Path.Combine(
            root,
            "SpeechMessage.Dynamics.WorkerTestHost",
            "bin",
            "Debug",
            "net10.0",
            "SpeechMessage.Dynamics.WorkerTestHost.exe");
        File.Exists(executablePath).Should().BeTrue(
            because: "the test project reference must build the SDK-free worker test host");
        return executablePath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 建立只含測試允許欄位的 Supervisor 選項；每個 generation 都使用相同已雜湊的
    /// SDK-free 執行檔，且 timeout 由個別案例明確限制。
    /// </summary>
    private static OfficialWorkerProfileOptions CreateOptions(
        string executablePath,
        string profileGenerationId,
        TimeSpan operationTimeout,
        TimeSpan drainTimeout)
    {
        return new OfficialWorkerProfileOptions
        {
            ProfileAlias = "crm91-test",
            ProfileGenerationId = profileGenerationId,
            WorkerVersion = OfficialWorkerVersion.Ce91,
            WorkerExecutablePath = executablePath,
            WorkerExecutableSha256 = ComputeSha256(executablePath),
            PackageLockId = "test-worker-package-lock-0001",
            StartupTimeout = TimeSpan.FromSeconds(10),
            OperationTimeout = operationTimeout,
            DrainTimeout = drainTimeout
        };
    }

    /// <summary>
    /// 建立零參數且固定 operation/profile 的 WhoAmI 要求；workload subject 僅供單次呼叫，
    /// 不得成為 Worker 或 Supervisor 的 cache/session key。
    /// </summary>
    private static OperationExecutionRequest CreateWhoAmIRequest(string workloadSubjectId)
    {
        return new OperationExecutionRequest
        {
            ProfileAlias = "crm91-test",
            CapabilityOperationId = "runtime.health.whoami",
            WorkloadSubjectId = workloadSubjectId
        };
    }

    /// <summary>
    /// 驗證已退休 executor 的所有明確 owner 計數皆回到零，且 readiness 不再公開。
    /// </summary>
    private static void AssertFullyRetired(OfficialWorkerLifecycleSnapshot snapshot)
    {
        snapshot.IsReady.Should().BeFalse();
        snapshot.OwnedProcessCount.Should().Be(0);
        snapshot.OwnedPipeCount.Should().Be(0);
        snapshot.OwnedBackgroundTaskCount.Should().Be(0);
        snapshot.ActiveOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 代表 Count 已知但禁止列舉的測試 dictionary。若 production 在完成 operation shape
    /// prevalidation 前嘗試建立 snapshot，測試會以固定例外揭露不必要的配置與信任邊界錯誤。
    /// </summary>
    private sealed class EnumerationForbiddenParameters : IReadOnlyDictionary<string, object?>
    {
        public int Count => 1;

        public IEnumerable<string> Keys => throw EnumerationFailure();

        public IEnumerable<object?> Values => throw EnumerationFailure();

        public object? this[string key] => throw EnumerationFailure();

        public bool ContainsKey(string key) => throw EnumerationFailure();

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            throw EnumerationFailure();

        public bool TryGetValue(string key, out object? value)
        {
            value = null;
            throw EnumerationFailure();
        }

        IEnumerator IEnumerable.GetEnumerator() => throw EnumerationFailure();

        private static InvalidOperationException EnumerationFailure() =>
            new("Parameter enumeration must not occur before shape validation.");
    }

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
}
