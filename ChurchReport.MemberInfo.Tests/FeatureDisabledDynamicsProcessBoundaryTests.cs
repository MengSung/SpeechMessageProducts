using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 為需要啟動真實 ChurchReport 子程序的測試建立獨占集合。
///
/// 這個集合刻意禁止與同一測試組件的其他 collection 平行執行；子程序、loopback
/// listener、目前的處理序快照與暫時設定都屬於 process-wide 資源。若與其他測試同時
/// 修改或觀察它們，便無法區分「本測試建立的資源」和「其他測試建立的資源」，會破壞
/// 無 Session／Memory／Resource Leakage 的驗證意義。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FeatureDisabledDynamicsProcessBoundaryCollection
{
    /// <summary>
    /// xUnit collection 的固定名稱；只有明確加入本集合的子程序測試才會被序列化。
    /// </summary>
    public const string Name = "Feature-disabled Dynamics process boundary";
}

/// <summary>
/// 驗證 ChurchReport 在 <c>DynamicsAccess:Package01FeeReadsEnabled=false</c> 時的真實
/// 程序邊界。
///
/// 既有 unit test 可以證明 hosted service 不向替身索取 executor；本測試則啟動實際
/// Kestrel host，證明組態、DI、hosted-service 啟動順序與 shutdown 合在一起時，仍不會
/// 建立 Gateway、CE 8.2/9.1 Worker、WorkerTestHost、Gateway HTTP 連線或保留 listener。
/// 測試只送出未驗證的 <c>GET /health</c>，不載入登入表單、不建立瀏覽器 Session、更不
/// 提交登入 POST；因此它不會把測試資料或使用者身分帶進任何 CRM/Dynamics 路徑。
/// </summary>
[Collection(FeatureDisabledDynamicsProcessBoundaryCollection.Name)]
public sealed class FeatureDisabledDynamicsProcessBoundaryTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 此測試唯一允許觀察、但絕不允許終止的 Dynamics 邊界程序名稱。
    /// 名稱清單只用於判斷「本次啟動後是否出現新的未預期程序」；真正的清理由
    /// <see cref="OwnedApplicationProcess"/> 以 PID、UTC 啟動時間與預期程式名三重身分
    /// 鎖定，避免誤傷使用者已啟動的 Gateway 或 Worker。
    /// </summary>
    private static readonly HashSet<string> DynamicsBoundaryProcessNames = new(
        StringComparer.OrdinalIgnoreCase)
        {
            "SpeechMessage.Dynamics.Gateway",
            "SpeechMessage.Dynamics.Crm82Worker",
            "SpeechMessage.Dynamics.Crm91Worker",
            "SpeechMessage.Dynamics.WorkerTestHost"
        };

    /// <summary>
    /// 當 Package 1 功能旗標關閉時，實際 ChurchReport host 只能開啟自己的 loopback
    /// listener，且不得連向 Gateway `/v1`、啟動任何 Dynamics 邊界程序或在 shutdown 後
    /// 留下自己的 listener／程序。
    ///
    /// 失敗注入是把 Gateway endpoint 指向測試自行擁有的 TCP 探針。若功能旗標被意外
    /// 忽略，preflight 或產品用戶端會嘗試建立 TLS/TCP 連線，探針立刻可觀察到；正確的
    /// disabled 路徑則完全不會碰觸該 endpoint。探針不讀取、記錄或回傳任何要求內容，
    /// 因此不會保留 token、cookie、credential、CRM payload 或使用者資料。
    /// </summary>
    [Fact]
    public async Task Disabled_feature_starts_only_ChurchReport_and_never_opens_the_Dynamics_probe()
    {
        var repositoryRoot = FindRepositoryRoot();
        var applicationDirectory = Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport");
        var applicationExecutable = FindBuiltApplicationExecutable(repositoryRoot);
        var applicationPort = ReserveLoopbackPort();
        var preexistingBoundaryProcesses = CaptureDynamicsBoundaryProcesses();

        GetBoundaryListeningPorts().Should().BeEmpty(
            because: "已有 7244 或 57244 listener 時，測試無法可靠證明本次 disabled startup 沒有建立 Gateway 邊界資源");

        var gatewayProbe = new GatewayConnectionProbe();
        OwnedApplicationProcess? application = null;

        try
        {
            application = OwnedApplicationProcess.Start(
                applicationExecutable,
                applicationDirectory,
                applicationPort,
                gatewayProbe.Port);

            var healthStatus = await WaitForHealthResponseAsync(application, applicationPort);
            healthStatus.Should().Be(HttpStatusCode.OK,
                because: "本測試只以 GET /health 確認 host 就緒，不得藉由登入 POST 或任何 Package 1 operation 取得證據");

            // 多等一個極短且有上限的啟動穩定期，確保 hosted service 若錯誤地建立
            // preflight，連線嘗試有時間到達測試探針；這不是背景 timer，也不保留任何
            // request/session 資料，時間到即結束。
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            gatewayProbe.HasAcceptedConnection.Should().BeFalse(
                because: "Package01FeeReadsEnabled=false 必須在建立 ProductClient、HTTP handler、token cache 或 `/v1` 流量以前 short-circuit");
            GetBoundaryListeningPorts().Should().BeEmpty(
                because: "disabled ChurchReport 只能啟動自己的測試 loopback port，不能啟動 Gateway 7244 或測試/工作程序 57244 listener");
            GetNewDynamicsBoundaryProcesses(preexistingBoundaryProcesses).Should().BeEmpty(
                because: "disabled 路徑不得建立 Gateway、CRM worker 或 WorkerTestHost process");
        }
        finally
        {
            if (application is not null)
            {
                await application.DisposeAsync();
            }

            await gatewayProbe.DisposeAsync();
        }

        await WaitForNoListenerAsync(applicationPort);
        IsListening(applicationPort).Should().BeFalse(
            because: "shutdown 只可釋放本測試擁有的 ChurchReport listener，不能留下跨測試或跨 Session 的 socket owner");
        IsListening(gatewayProbe.Port).Should().BeFalse(
            because: "Gateway probe 的 TcpListener、accept task 與可能的 TcpClient 都必須由本測試在 finally 中確定釋放");
        GetBoundaryListeningPorts().Should().BeEmpty();
        GetNewDynamicsBoundaryProcesses(preexistingBoundaryProcesses).Should().BeEmpty(
            because: "只停止已捕獲且身分完全相符的 ChurchReport 後，不能殘留本次啟動的 Dynamics 邊界程序");
    }

    /// <summary>
    /// 從目前測試輸出所在位置向上尋找 solution root，避免依賴 caller 的 current directory。
    /// current directory 是 process-wide 可變狀態，其他 legacy 測試可能暫時變更它；使用
    /// <see cref="AppContext.BaseDirectory"/> 可讓子程序定位在目前 worktree，而不跨工作樹、
    /// 使用者 profile 或部署目錄讀取檔案。
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

        throw new DirectoryNotFoundException(
            "The ChurchReport process-boundary test could not locate the repository root.");
    }

    /// <summary>
    /// 取得與目前測試組態相同的已建置 ChurchReport apphost；先使用同組態產物，只有在
    /// focused Release verification 已建立 Release apphost 而測試 runner 未提供同組態時，
    /// 才以 Release 為明確後備。絕不從 xUnit copy-local DLL 啟動，因該位置沒有完整
    /// apphost、runtimeconfig、Views 或 content-root 契約。
    /// </summary>
    private static string FindBuiltApplicationExecutable(string repositoryRoot)
    {
        var testConfiguration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        var candidateConfigurations = new[] { testConfiguration, "Release" }
            .Where(configuration => !string.IsNullOrWhiteSpace(configuration))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var configuration in candidateConfigurations)
        {
            var candidate = Path.Combine(
                repositoryRoot,
                "SpeechMessageProducts.ChurchReport",
                "bin",
                configuration!,
                "net10.0",
                "SpeechMessageProducts.ChurchReport.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "The built ChurchReport apphost is required for the feature-disabled process-boundary test. Build the test configuration before running it.");
    }

    /// <summary>
    /// 暫時向作業系統保留一個 loopback port 以取得低碰撞候選值，關閉 reservation 後立即
    /// 交給 ChurchReport apphost。真正的 ownership 仍由 apphost PID 與 listener snapshot
    /// 驗證；若其他程序在這個極短窗口搶占 port，host 不會假裝就緒，測試會 fail closed。
    /// </summary>
    private static int ReserveLoopbackPort()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, port: 0);
        reservation.Start();
        return ((IPEndPoint)reservation.LocalEndpoint).Port;
    }

    /// <summary>
    /// 只以短生命週期、cookie 關閉且不自動 redirect 的 HTTP client 輪詢本機 `/health`。
    /// 這確保驗證本身不追蹤登入 Session、不跟隨登入導向，也不會因 redirect 觸發額外
    /// request；handler、connection pool、response stream 與 cancellation 都在本方法結束
    /// 前由唯一 owner dispose。
    /// </summary>
    private static async Task<HttpStatusCode> WaitForHealthResponseAsync(
        OwnedApplicationProcess application,
        int applicationPort)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(1)
        };
        using var timeout = new CancellationTokenSource(StartupTimeout);
        var healthUri = new Uri($"http://127.0.0.1:{applicationPort}/health", UriKind.Absolute);

        while (!timeout.IsCancellationRequested)
        {
            application.ThrowIfExited();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, healthUri);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                return response.StatusCode;
            }
            catch (HttpRequestException)
            {
                // Kestrel 尚未開啟 listener；不記錄 child output，以免測試 log 保留其組態或敏感資料。
            }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested)
            {
                // 此次一秒的 HttpClient timeout 已結束，總 startup timeout 仍由上方 CTS 唯一管理。
            }

            await Task.Delay(PollInterval, timeout.Token).ConfigureAwait(false);
        }

        throw new TimeoutException("The owned ChurchReport process did not expose GET /health before the bounded startup deadline.");
    }

    /// <summary>
    /// 以 OS listener table 識別受管制的 Gateway/worker ports。這是純觀察，不會嘗試停止
    /// 任何未知 owner；發現既有 listener 時測試選擇 fail closed，而不是修改使用者環境。
    /// </summary>
    private static IReadOnlyCollection<int> GetBoundaryListeningPorts()
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port is 7244 or 57244)
            .Select(endpoint => endpoint.Port)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// 檢查某個 port 是否仍有任何 TCP listener。方法不持有 socket handle，結果只用於
    /// shutdown 後的 resource-baseline assertion。
    /// </summary>
    private static bool IsListening(int port)
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(endpoint => endpoint.Port == port);
    }

    /// <summary>
    /// 在有限時間內等待 apphost listener 自 OS table 消失。終止程序後仍可能有極短的
    /// kernel cleanup 延遲；輪詢僅由目前測試 await，不建立 timer、static state 或背景工作。
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
    /// 取得目前已存在的 Dynamics 邊界程序身分。每一筆身分同時包含 PID 與 UTC 啟動時間，
    /// 因為 PID 可被作業系統重複使用；若無法讀取命名邊界程序的起始時間，測試無法安全
    /// 區分既有程序與新程序，必須 fail closed 而非錯誤宣稱沒有資源。
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
                        $"Could not safely inspect the existing Dynamics boundary process with PID {process.Id}.",
                        exception);
                }
            }
        }

        return processes;
    }

    /// <summary>
    /// 從啟動前後兩份身分快照找出新出現的 Dynamics 邊界程序。既有使用者程序屬於 baseline
    /// 並不會被測試處理；任何新程序都表示 flag=false 的契約被破壞。
    /// </summary>
    private static IReadOnlyCollection<ProcessIdentity> GetNewDynamicsBoundaryProcesses(
        IReadOnlySet<ProcessIdentity> baseline)
    {
        return CaptureDynamicsBoundaryProcesses()
            .Where(identity => !baseline.Contains(identity))
            .ToArray();
    }

    /// <summary>
    /// 不可變的 process identity。除了 PID 與 UTC 起始 tick，還保留程式名作為 kill 前的
    /// 第二道防線；它不含 command line、環境變數、endpoint 或 credential，避免把敏感
    /// deployment state 放進 test failure 或記憶體快照。
    /// </summary>
    private readonly record struct ProcessIdentity(int ProcessId, long StartTimeUtcTicks, string ProcessName);

    /// <summary>
    /// 測試擁有的 ChurchReport 子程序生命週期 owner。
    ///
    /// 本型別建立後只保留不可變 PID／start-time／name 三元組，不保留 <see cref="Process"/>
    /// handle。每次操作都重新取得短生命週期 handle 並立刻 dispose；DisposeAsync 只會在
    /// 三元組完全相符時終止整個 child tree，任何 PID reuse 或名稱不符都 fail closed，絕不
    /// 依名稱掃描或終止使用者既有程序。
    /// </summary>
    private sealed class OwnedApplicationProcess : IAsyncDisposable
    {
        private readonly int _processId;
        private readonly long _startTimeUtcTicks;
        private readonly string _expectedProcessName;
        private int _disposed;

        private OwnedApplicationProcess(int processId, long startTimeUtcTicks, string expectedProcessName)
        {
            _processId = processId;
            _startTimeUtcTicks = startTimeUtcTicks;
            _expectedProcessName = expectedProcessName;
        }

        /// <summary>
        /// 以 apphost 直接建立子程序，並把旗標、loopback URL 與假 Gateway endpoint 僅注入
        /// 到子程序環境。ArgumentList 避免 shell quoting/injection；不 redirect stdout/stderr
        /// 則避免未讀取 pipe 造成 deadlock，也不將 child 可能包含部署資訊的輸出保存到測試
        /// 記憶體、檔案或 log。
        /// </summary>
        public static OwnedApplicationProcess Start(
            string applicationExecutable,
            string applicationDirectory,
            int applicationPort,
            int gatewayProbePort)
        {
            var startInfo = new ProcessStartInfo(applicationExecutable)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = applicationDirectory
            };
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add($"http://127.0.0.1:{applicationPort}");
            startInfo.ArgumentList.Add("--contentRoot");
            startInfo.ArgumentList.Add(applicationDirectory);

            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["ASPNETCORE_ALLOWEDHOSTS"] = "*";
            startInfo.Environment["DynamicsAccess__Package01FeeReadsEnabled"] = "false";
            startInfo.Environment["DynamicsAccess__ConnectionMode"] = "DedicatedGateway";
            startInfo.Environment["DynamicsAccess__ProfileAlias"] = "feature-disabled-test";
            startInfo.Environment["DynamicsAccess__Gateway__Endpoint"] = $"https://127.0.0.1:{gatewayProbePort}/";
            startInfo.Environment["DynamicsAccess__Gateway__ApiPrefix"] = "/v1";

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The ChurchReport apphost process could not be started.");
            return new OwnedApplicationProcess(
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                Path.GetFileNameWithoutExtension(applicationExecutable));
        }

        /// <summary>
        /// 在 health polling 途中保留 fail-closed 語意：若 test-owned child 已提前結束，
        /// 不繼續等待不存在的 listener，也不嘗試以另一個程序或 transport 補救。
        /// </summary>
        public void ThrowIfExited()
        {
            using var process = OpenMatchingProcess();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "The owned ChurchReport process exited before GET /health became available.");
            }
        }

        /// <summary>
        /// 執行唯一、可重入的 shutdown。正常情況先以 process tree kill 取得可預期的測試
        /// cleanup 邊界，再有界等待 exit；OS process exit 是 child 內所有 Kestrel socket、
        /// HttpClient pool、timer、background task 與可能 SDK handle 的最後清理保證。
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
                // PID 已不存在代表 OS 已完成 child cleanup；不能因 PID reuse 改去終止其他程序。
            }
        }

        /// <summary>
        /// 重新取得短生命週期 Process handle 並檢查完整身分。這是所有觀察與終止操作共用的
        /// trust boundary；任何 mismatch 都拒絕操作，而不是讓 test cleanup 擴大到未知程序。
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
                        "The recorded ChurchReport process identity no longer matches; cleanup was refused.");
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

    /// <summary>
    /// 僅接受一次連線的 Gateway endpoint 探針。它持有 listener、CancellationTokenSource、
    /// accept task 與可能的 TcpClient 的唯一所有權；DisposeAsync 永遠先停止 listener，再
    /// await accept task、dispose accepted client 與 CTS，確保測試結束時不留下 handle、task
    /// 或 socket。
    /// </summary>
    private sealed class GatewayConnectionProbe : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task<TcpClient?> _acceptedClientTask;
        private int _disposed;

        public GatewayConnectionProbe()
        {
            _listener = new TcpListener(IPAddress.Loopback, port: 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptedClientTask = AcceptOnceAsync(_listener, _cancellation.Token);
        }

        /// <summary>
        /// 測試設定到 Gateway endpoint 的專屬 loopback port；它永遠不是 7244 或 57244，
        /// 因此不會冒充或干擾真正 Gateway/worker listener。
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// 是否有任何 client 對假 Gateway endpoint 建立 TCP 連線。只回傳布林值，不保存或
        /// 解析 TLS/request 內容，避免測試收集到傳輸中的敏感資料。
        /// </summary>
        public bool HasAcceptedConnection =>
            _acceptedClientTask.IsCompletedSuccessfully && _acceptedClientTask.Result is not null;

        /// <summary>
        /// 以 deterministic 順序釋放所有 probe 資源；多次呼叫是 idempotent，讓 finally
        /// 與未來 test framework cleanup 不會重複釋放同一 listener 或 CTS。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                var acceptedClient = await _acceptedClientTask.ConfigureAwait(false);
                acceptedClient?.Dispose();
            }
            finally
            {
                _cancellation.Dispose();
            }
        }

        /// <summary>
        /// 將正常 shutdown 造成的 cancellation／socket close 轉成 <see langword="null"/>；
        /// 任何非預期失敗仍會傳回 owner，避免 silently swallowing probe lifecycle defect。
        /// </summary>
        private static async Task<TcpClient?> AcceptOnceAsync(
            TcpListener listener,
            CancellationToken cancellationToken)
        {
            try
            {
                return await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
    }
}
