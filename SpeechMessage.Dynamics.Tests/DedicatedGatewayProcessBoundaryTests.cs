using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using SpeechMessage.Testing;
using Xunit;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Visual Studio DedicatedGateway launch profile 在真實 Gateway process 中的啟動、
/// HTTPS loopback readiness 與停止清理契約。
///
/// 本測試只對 test-owned localhost listener 發出匿名 <c>GET /health</c> 與 <c>GET /ready</c>；
/// 不呼叫 <c>/v1</c>、不建立 Data8 client/WCF channel、也不連線 CE、SQL、ADFS 或任何外部端點。
/// 因此它專門保護「Dedicated composition root 可啟動且可回收」的本機程序邊界，而非冒充 P6 的
/// 外部 Dynamics 相容性量測。
/// </summary>
[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
public sealed class DedicatedGatewayProcessBoundaryTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 僅用於觀察本測試是否意外建立 worker 邊界程序的名稱集合。測試從不依名稱終止程序；
    /// 真正 cleanup 必須以本案例建立時記錄的 PID、UTC start time 與 executable name 三重身分
    /// 驗證，避免 PID 重用或平行工作誤傷使用者既有的 Gateway／worker。
    /// </summary>
    private static readonly HashSet<string> DynamicsBoundaryProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpeechMessage.Dynamics.Gateway",
        "SpeechMessage.Dynamics.Crm82Worker",
        "SpeechMessage.Dynamics.Crm91Worker",
        "SpeechMessage.Dynamics.WorkerTestHost"
    };

    /// <summary>
    /// 保護 checked-in DedicatedGateway launch profile 可建立只有 Data8 runtime 的本機 HTTPS host，
    /// 且 host 停止後不遺留 Gateway 或 worker process/listener。
    ///
    /// 故障注入是在子程序環境中只提供 launch profile 的 deployment-owned scalar 與測試專用密碼；
    /// 若 Program 錯走 Official Worker/SQL 分支、缺少 Dedicated workload binding、或把 ready 誤宣稱
    /// 成外部 CE 已連通，此案例會在沒有送出 CRM operation 的情況下失敗。決定性斷言為 `/ready`
    /// 必須明確回覆 <c>runtime=configured</c>：它只代表本機 runtime 已 materialize，絕不代表
    /// CE connectivity、credential 驗證或可執行的 Data8 session。
    /// </summary>
    [Fact]
    public async Task Dedicated_launch_profile_starts_a_local_runtime_and_releases_its_process_boundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var gatewayDirectory = Path.Combine(repositoryRoot, "SpeechMessage.Dynamics.Gateway");
        var gatewayExecutable = FindBuiltGatewayExecutable(repositoryRoot);
        var gatewayPort = ReserveLoopbackPort();
        var baselineProcesses = CaptureDynamicsBoundaryProcesses();
        var launchEnvironment = ReadDedicatedLaunchEnvironment(gatewayDirectory);
        OwnedGatewayProcess? gateway = null;

        try
        {
            gateway = OwnedGatewayProcess.Start(
                gatewayExecutable,
                gatewayDirectory,
                gatewayPort,
                launchEnvironment);

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                // 此 callback 僅屬於本案例的短生命週期 client，且 URI 固定為 test-owned localhost
                // development certificate；它不會被產品 runtime、其他測試或外部 endpoint 重用。
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(1)
            };

            using var health = await WaitForResponseAsync(client, gateway, gatewayPort, "/health");
            health.StatusCode.Should().Be(HttpStatusCode.OK);
            health.Headers.CacheControl!.NoStore.Should().BeTrue();

            using var ready = await WaitForResponseAsync(client, gateway, gatewayPort, "/ready");
            ready.StatusCode.Should().Be(HttpStatusCode.OK);
            ready.Headers.CacheControl!.NoStore.Should().BeTrue();
            var readyJson = await ready.Content.ReadAsStringAsync();
            using var readyDocument = JsonDocument.Parse(readyJson);
            readyDocument.RootElement.GetProperty("status").GetString().Should().Be("ready");
            readyDocument.RootElement.GetProperty("runtime").GetString().Should().Be("configured");
            readyJson.ToLowerInvariant().Should().NotContainAny(
                "password",
                "username",
                "credential",
                "token",
                "organization.svc");

            GetNewDynamicsBoundaryProcesses(baselineProcesses)
                .Should()
                .BeEquivalentTo([gateway.Identity],
                    because: "Dedicated host 只可建立自身，不能產生 Official Worker 或任何額外 Dynamics 邊界程序");
        }
        finally
        {
            if (gateway is not null)
            {
                await gateway.DisposeAsync();
            }
        }

        await WaitForNoListenerAsync(gatewayPort);
        await WaitForProcessBaselineAsync(baselineProcesses);
        IsListening(gatewayPort).Should().BeFalse(
            because: "Dedicated runtime/pool/admission 由 host 停止流程擁有；子程序結束後不可留下其 HTTPS listener");
        GetNewDynamicsBoundaryProcesses(baselineProcesses).Should().BeEmpty(
            because: "cleanup 只能終止精確記錄的 child process，並必須讓 Gateway/worker process baseline 回復");
    }

    /// <summary>
    /// 從測試輸出位置向上定位目前 worktree，避免依賴 process-wide current directory 或使用者絕對路徑。
    /// DirectoryInfo 僅在此方法中短暫存在，不建立 watcher、cache 或檔案控制代碼。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The current worktree root was not found.");
    }

    /// <summary>
    /// 尋找與本 test runner 相同 configuration 的 Gateway apphost。只接受已建置 apphost，
    /// 不從 xUnit copy-local DLL 啟動，避免失去 runtimeconfig/content-root 與真實 host 生命周期。
    /// </summary>
    private static string FindBuiltGatewayExecutable(string repositoryRoot)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        foreach (var candidateConfiguration in new[] { configuration, "Release" }
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(
                repositoryRoot,
                "SpeechMessage.Dynamics.Gateway",
                "bin",
                candidateConfiguration!,
                "net10.0",
                "SpeechMessage.Dynamics.Gateway.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The built Dedicated Gateway apphost was not found.");
    }

    /// <summary>
    /// 讀取 checked-in DedicatedGateway launch profile 的 environment variables，讓測試驗證的正是
    /// Visual Studio F5 使用的 deployment shape，而不是複製一份容易漂移的設定常數。回傳集合只在
    /// child process 建立期間使用，之後由 ProcessStartInfo 與子程序生命週期擁有，不保存 credential。
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadDedicatedLaunchEnvironment(string gatewayDirectory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(gatewayDirectory, "Properties", "launchSettings.json")));
        var variables = document.RootElement
            .GetProperty("profiles")
            .GetProperty("DedicatedGateway")
            .GetProperty("environmentVariables");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in variables.EnumerateObject())
        {
            if (variable.Value.ValueKind == JsonValueKind.String)
            {
                values.Add(variable.Name, variable.Value.GetString()!);
            }
        }

        return values;
    }

    /// <summary>
    /// 取得低碰撞的短暫 loopback port。reservation 關閉後才交給 child process；真正 listener ownership
    /// 仍由 child PID 與 OS listener table 驗證，若其他程序搶占則 child 無法 ready，測試 fail closed。
    /// </summary>
    private static int ReserveLoopbackPort()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, port: 0);
        reservation.Start();
        return ((IPEndPoint)reservation.LocalEndpoint).Port;
    }

    /// <summary>
    /// 使用單一短生命週期、cookie/redirect/proxy 全關閉的 client 輪詢 test-owned HTTPS endpoint。
    /// 每個 request/response 都由此方法的 using 確定性釋放；總 timeout CTS 的唯一 owner 是本方法，
    /// 不建立 timer、session cache 或背景 retry task。
    /// </summary>
    private static async Task<HttpResponseMessage> WaitForResponseAsync(
        HttpClient client,
        OwnedGatewayProcess gateway,
        int port,
        string path)
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        var endpoint = new Uri($"https://localhost:{port}{path}", UriKind.Absolute);
        while (!timeout.IsCancellationRequested)
        {
            gateway.ThrowIfExited();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                return response;
            }
            catch (HttpRequestException)
            {
                // Kestrel 尚未完成 listener 開啟；不保存 child output，以免將 deployment environment 留在測試 log。
            }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested)
            {
                // 單次 client timeout 已結束，總啟動 deadline 仍繼續由上方 CTS 控制。
            }

            await Task.Delay(PollInterval, timeout.Token).ConfigureAwait(false);
        }

        throw new TimeoutException($"The Dedicated Gateway process did not expose {path} before the startup deadline.");
    }

    /// <summary>
    /// 回傳目前開啟的 TCP listener 是否包含指定 port。此方法只讀取 OS snapshot、不保留 socket handle，
    /// 用於 shutdown 後的 resource-baseline assertion。
    /// </summary>
    private static bool IsListening(int port)
        => IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(endpoint => endpoint.Port == port);

    /// <summary>
    /// 有界等待 child listener 從 OS table 消失。延遲只在目前 async flow 中 await，不建立 static timer 或
    /// fire-and-forget task；超時後 assertion 會直接揭露 cleanup fault。
    /// </summary>
    private static async Task WaitForNoListenerAsync(int port)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ShutdownTimeout)
        {
            if (!IsListening(port))
            {
                return;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 有界等待 Dynamics process snapshot 回到啟動前基線。listener 關閉與 Windows process table 移除
    /// 不是同一個原子事件：Kestrel port 可能先釋放，而 apphost 尚在執行最後的 DI/host cleanup。
    /// 因此不能以「port 已消失」推論「process 已清理」；此條件式等待保留每次 snapshot 的短生命週期
    /// Process handle，並在 timeout 後交由最終 assertion 報出殘留身分。
    /// </summary>
    private static async Task WaitForProcessBaselineAsync(IReadOnlySet<ProcessIdentity> baseline)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ShutdownTimeout)
        {
            if (GetNewDynamicsBoundaryProcesses(baseline).Count == 0)
            {
                return;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 擷取受管制 Dynamics process 的不可變身分。讀取失敗即 fail closed，因為無法安全區分使用者既有
    /// process 與本案例 process 時，不可宣稱 cleanup 沒有 leakage。
    /// </summary>
    private static IReadOnlySet<ProcessIdentity> CaptureDynamicsBoundaryProcesses()
    {
        var processes = new HashSet<ProcessIdentity>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!DynamicsBoundaryProcessNames.Contains(process.ProcessName))
                {
                    continue;
                }

                try
                {
                    processes.Add(new ProcessIdentity(
                        process.Id,
                        process.StartTime.ToUniversalTime().Ticks,
                        process.ProcessName));
                }
                catch (Exception exception) when (
                    exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    throw new InvalidOperationException(
                        $"Could not safely inspect Dynamics boundary process {process.Id}.",
                        exception);
                }
            }
        }

        return processes;
    }

    /// <summary>
    /// 比較兩份 process identity snapshot，僅回傳本案例後才出現的 process。PID、start time 與 name 三者
    /// 都必須相同才視為同一個 owner，防止 PID reuse 偽裝成已清理的 child。
    /// </summary>
    private static IReadOnlyCollection<ProcessIdentity> GetNewDynamicsBoundaryProcesses(
        IReadOnlySet<ProcessIdentity> baseline)
        => CaptureDynamicsBoundaryProcesses()
            .Where(identity => !baseline.Contains(identity))
            .ToArray();

    /// <summary>
    /// 不可變的 process identity；不保存 command line、environment、endpoint 或 credential，避免測試失敗
    /// 時把 deployment-sensitive 資料寫入記憶體或輸出。
    /// </summary>
    private readonly record struct ProcessIdentity(int ProcessId, long StartTimeUtcTicks, string ProcessName);

    /// <summary>
    /// 唯一擁有本測試所建立 Gateway child process 的 lifecycle owner。
    /// 它只保存 PID/start-time/name 三元組；每次觀察或終止時重新取得短生命週期 Process handle，並在
    /// 三元組不相符時 fail closed。這使 DisposeAsync 永遠不會依 process name 掃描或誤終止使用者程序。
    /// </summary>
    private sealed class OwnedGatewayProcess : IAsyncDisposable
    {
        private readonly int _processId;
        private readonly long _startTimeUtcTicks;
        private readonly string _expectedProcessName;
        private int _disposed;

        private OwnedGatewayProcess(ProcessIdentity identity)
        {
            Identity = identity;
            _processId = identity.ProcessId;
            _startTimeUtcTicks = identity.StartTimeUtcTicks;
            _expectedProcessName = identity.ProcessName;
        }

        /// <summary>
        /// 取得 child 的不可變身分，僅供 baseline comparison；不公開 Process handle 或 child environment。
        /// </summary>
        public ProcessIdentity Identity { get; }

        /// <summary>
        /// 建立實際 Gateway apphost，將 launch profile scalar、一次性測試密碼與動態 HTTPS loopback port
        /// 只注入 child environment。ArgumentList 不經 shell 解析；stdout/stderr 不建立 pipe，避免未讀取輸出
        /// deadlock 或將 configuration/secret 保存到測試記憶體。
        /// </summary>
        public static OwnedGatewayProcess Start(
            string gatewayExecutable,
            string gatewayDirectory,
            int gatewayPort,
            IReadOnlyDictionary<string, string> launchEnvironment)
        {
            var startInfo = new ProcessStartInfo(gatewayExecutable)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = gatewayDirectory
            };
            foreach (var pair in launchEnvironment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }

            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["Kestrel__Endpoints__LocalHttps__Url"] =
                $"https://localhost:{gatewayPort}";
            startInfo.Environment["CRM_PASSWORD"] = "test-only-dedicated-gateway-password";

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The Dedicated Gateway apphost could not be started.");
            return new OwnedGatewayProcess(new ProcessIdentity(
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                Path.GetFileNameWithoutExtension(gatewayExecutable)));
        }

        /// <summary>
        /// 若 child 在 listener ready 前結束立即失敗，避免測試繼續等待並把啟動錯誤誤判為網路慢。
        /// </summary>
        public void ThrowIfExited()
        {
            using var process = OpenMatchingProcess();
            if (process.HasExited)
            {
                throw new InvalidOperationException("The owned Dedicated Gateway process exited before it became ready.");
            }
        }

        /// <summary>
        /// 以 idempotent 且嚴格身分驗證的方式結束唯一 child tree，並有界等待 OS cleanup。child process exit
        /// 是 Kestrel listener、DI ServiceProvider、Data8 runtime、pool/admission、handler、CTS 與可能 task 的
        /// 最後 deterministic cleanup 邊界；任何 PID mismatch 皆拒絕終止未知 owner。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                using var process = OpenMatchingProcess();
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
                }
            }
            catch (ArgumentException)
            {
                // PID 已不存在代表 OS 已完成 child cleanup；不可依相同 PID 改去終止後來的未知程序。
            }
        }

        /// <summary>
        /// 重新取得短生命週期 Process handle 並驗證完整三元組。這是所有觀察與終止操作的唯一 trust boundary；
        /// mismatch 時先 dispose handle 再拒絕操作，防止 process cleanup 成為跨 Session/使用者的破壞性動作。
        /// </summary>
        private Process OpenMatchingProcess()
        {
            var process = Process.GetProcessById(_processId);
            try
            {
                var matches = string.Equals(
                                  process.ProcessName,
                                  _expectedProcessName,
                                  StringComparison.OrdinalIgnoreCase)
                              && process.StartTime.ToUniversalTime().Ticks == _startTimeUtcTicks;
                if (!matches)
                {
                    throw new InvalidOperationException(
                        "The recorded Dedicated Gateway process identity no longer matches; cleanup was refused.");
                }

                return process;
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }
    }
}
