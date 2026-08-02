using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerTestHost;

/// <summary>
/// 提供 Supervisor 測試專用、無網路與無 CRM SDK 的真實子程序。
/// 它以獨立 Process 模擬 CE 8.2／9.1 Worker kind，並只實作固定 WhoAmI 與 Package01 投影，
/// 讓測試驗證版本隔離、Process、Pipe、READY、Drain 與 Dispose 生命週期；不讀取
/// Credential、Profile endpoint、環境秘密或使用者 Session，也不建立網路連線。
/// </summary>
internal static class Program
{
    internal const string PackageLockId = "test-worker-package-lock-0001";
    private const string WrongResponseRequestIdGeneration =
        "profile-generation-wrong-request-id";
    private const string WrongResponseRequestIdGenerationPrefix =
        "profile-generation-wrong-request-id-";
    private const string EnvironmentScrubGeneration =
        "profile-generation-environment-scrub";
    private const string StartupTimeoutGeneration =
        "profile-generation-startup-timeout";
    private const string SecondStartInvalidReadyGenerationPrefix =
        "profile-generation-second-start-invalid-ready-";
    private const string SecondStartInvalidReadyWithDescendantGenerationPrefix =
        "profile-generation-second-start-invalid-ready-never-exit-descendant-";
    private const string InvalidReadyGenerationPrefix =
        "profile-generation-invalid-ready-";
    private const string TimeoutGenerationPrefix =
        "profile-generation-timeout-";
    private const string CancellationGenerationPrefix =
        "profile-generation-cancel-";
    private const string WorkerExitGenerationPrefix =
        "profile-generation-worker-exit-";
    private const string NeverExitOutputHandleGenerationPrefix =
        "profile-generation-never-exit-descendant-";
    private const string CompleteFailureGenerationPrefix =
        "profile-generation-complete-failure-";
    private const string Package01ResultTooLargeGenerationPrefix =
        "profile-generation-package01-result-too-large-";
    private const string Package01LargeValidGenerationPrefix =
        "profile-generation-package01-large-valid-";
    private static readonly string[] SensitiveEnvironmentSentinelNames =
    {
        "DYNAMICS_TEST_SENTINEL",
        "CRM_TEST_SENTINEL",
        "WORKER_CREDENTIAL_SENTINEL",
        "WORKER_PASSWORD_SENTINEL",
        "WORKER_SECRET_SENTINEL",
        "WORKER_TOKEN_SENTINEL",
        "WORKER_AUTH_SENTINEL",
        "WORKER_CONNECTION_SENTINEL",
        "WORKER_SQL_SENTINEL",
        "WORKER_KEY_SENTINEL",
        "WORKER_SESSION_SENTINEL",
        "WORKER_COOKIE_SENTINEL",
        "SPEECHMESSAGE_ARBITRARY_PARENT_SENTINEL"
    };
    private static readonly Guid WrongResponseRequestId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// 啟動一個真實 named-pipe session。所有 Pipe 與 fake client 資源均由共用
    /// WorkerHost 契約釋放，程序本身不建立 Timer、Cache 或背景工作。
    /// </summary>
    private static int Main(string[] arguments)
    {
        try
        {
            var bootstrap = WorkerBootstrapArguments.Parse(arguments);
            if (bootstrap.ProfileGenerationId.StartsWith(
                    SecondStartInvalidReadyGenerationPrefix,
                    StringComparison.Ordinal) &&
                !TryClaimFirstWorkerStart(bootstrap.ProfileGenerationId))
            {
                return (int)RunInvalidReadySessionAsync(bootstrap)
                    .GetAwaiter()
                    .GetResult();
            }

            if (bootstrap.ProfileGenerationId.StartsWith(
                    SecondStartInvalidReadyWithDescendantGenerationPrefix,
                    StringComparison.Ordinal))
            {
                // 只有取得 first-start marker 的第一個 Worker 會走到這裡；第二個 Worker 已在上方
                // 送出 invalid READY 並返回。此 descendant 故意繼承 stdout/stderr write-end，用來
                // 驗證 Factory 必須先保留並完成上層 Worker owner，才可以釋放 admission registration。
                StartDetachedNeverExitingOutputHandleDescendant(
                    bootstrap.ProfileGenerationId);
            }

            if (bootstrap.ProfileGenerationId.StartsWith(
                    NeverExitOutputHandleGenerationPrefix,
                    StringComparison.Ordinal))
            {
                StartDetachedNeverExitingOutputHandleDescendant(
                    bootstrap.ProfileGenerationId);
            }

            if (bootstrap.ProfileGenerationId.StartsWith(
                    StartupTimeoutGeneration,
                    StringComparison.Ordinal))
            {
                WriteTestOwnedProcessEvidence(bootstrap.ProfileGenerationId);
                Thread.Sleep(Timeout.Infinite);
                return (int)OfficialWorkerSessionExitCode.UnexpectedFailure;
            }

            if (bootstrap.ProfileGenerationId.StartsWith(
                    InvalidReadyGenerationPrefix,
                    StringComparison.Ordinal))
            {
                WriteTestOwnedProcessEvidence(bootstrap.ProfileGenerationId);
                return (int)RunInvalidReadySessionAsync(bootstrap)
                    .GetAwaiter()
                    .GetResult();
            }

            if (string.Equals(
                    bootstrap.ProfileGenerationId,
                    EnvironmentScrubGeneration,
                    StringComparison.Ordinal) &&
                !HasMinimumIsolatedChildEnvironment())
            {
                return (int)OfficialWorkerSessionExitCode.UnexpectedFailure;
            }

            if (string.Equals(
                    bootstrap.ProfileGenerationId,
                    WrongResponseRequestIdGeneration,
                    StringComparison.Ordinal) ||
                bootstrap.ProfileGenerationId.StartsWith(
                    WrongResponseRequestIdGenerationPrefix,
                    StringComparison.Ordinal))
            {
                return (int)RunWrongResponseRequestIdSessionAsync(bootstrap)
                    .GetAwaiter()
                    .GetResult();
            }

            if (bootstrap.ProfileGenerationId.StartsWith(
                    WorkerExitGenerationPrefix,
                    StringComparison.Ordinal))
            {
                WriteTestOwnedProcessEvidence(bootstrap.ProfileGenerationId);
                return (int)RunExitAfterReadySessionAsync(bootstrap)
                    .GetAwaiter()
                    .GetResult();
            }

            if (!TryGetCeVersion(bootstrap.WorkerKind, out var ceVersion))
            {
                return (int)OfficialWorkerSessionExitCode.ProtocolFailure;
            }

            var host = new OfficialWorkerProcessHost(
                bootstrap.WorkerKind,
                PackageLockId,
                ceVersion,
                new TestClientFactory(),
                new NamedPipeOfficialWorkerConnector(),
                OfficialWorkerOperations.CreateRevisionMap(),
                Package01FeeWorkerContract.ProtocolLimits);
            return (int)host.RunAsync(arguments, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (WorkerProtocolException)
        {
            return (int)OfficialWorkerSessionExitCode.ProtocolFailure;
        }
        catch
        {
            return (int)OfficialWorkerSessionExitCode.UnexpectedFailure;
        }
    }

    /// <summary>
    /// 驗證 environment-scrub 測試 generation 看不到任何父行程 sentinel，同時仍保有啟動
    /// Windows worker 所需的 SystemRoot、TEMP 與 USERPROFILE。方法只檢查存在性，不讀取、
    /// 回傳或記錄值；失敗時在 pipe／client session 建立前結束，避免測試資料進入 IPC。
    /// </summary>
    private static bool HasMinimumIsolatedChildEnvironment()
    {
        foreach (var name in SensitiveEnvironmentSentinelNames)
        {
            if (Environment.GetEnvironmentVariable(name) is not null)
            {
                return false;
            }
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SystemRoot")) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEMP")) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("USERPROFILE"));
    }

    /// <summary>
    /// 建立不會自行退出、且經由已完整等待的 PowerShell intermediary 脫離 Worker live process tree
    /// 的測試 descendant。descendant 以 <c>-NoNewWindow</c> 繼承 Worker stdout/stderr handles，並把
    /// 自己的 PID 寫入 run-unique TEMP evidence；測試 finally 是唯一終止 owner。此方法不傳遞 Pipe、
    /// Nonce、Credential、Token、Session 或 caller data，intermediary 也在返回前被同步等待並釋放。
    /// </summary>
    /// <param name="profileGenerationId">含 run-unique suffix 的安全 generation identifier。</param>
    private static void StartDetachedNeverExitingOutputHandleDescendant(
        string profileGenerationId)
    {
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (string.IsNullOrWhiteSpace(systemRoot))
        {
            throw new InvalidOperationException();
        }

        var executablePath = Path.Combine(
            systemRoot,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var evidencePath = Path.Combine(
            Path.GetTempPath(),
            $"speechmessage-dynamics-worker-{profileGenerationId}.descendant.pid");
        var escapedEvidencePath = evidencePath.Replace("'", "''", StringComparison.Ordinal);
        var script =
            "$child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') " +
            "-ArgumentList '-NoProfile','-NonInteractive','-Command','[Threading.Thread]::Sleep(-1)' " +
            "-NoNewWindow -PassThru; " +
            $"[IO.File]::WriteAllText('{escapedEvidencePath}', " +
            "$child.Id.ToString([Globalization.CultureInfo]::InvariantCulture))";
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var intermediary = System.Diagnostics.Process.Start(startInfo) ??
            throw new InvalidOperationException();
        intermediary.WaitForExit();
        if (intermediary.ExitCode != 0 || !File.Exists(evidencePath))
        {
            throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// 將目前測試 Worker PID 寫入由 generation 名稱決定的 TEMP evidence 檔。內容只有十進位 PID，
    /// 不含 Pipe、Nonce、Profile endpoint、Credential、Token 或 Session；測試負責在前後刪除檔案，
    /// 因此此方法不建立跨測試 registry、cache 或背景 owner。
    /// </summary>
    /// <param name="profileGenerationId">已通過 bootstrap 驗證的安全 generation identifier。</param>
    private static void WriteTestOwnedProcessEvidence(string profileGenerationId)
    {
        var evidencePath = Path.Combine(
            Path.GetTempPath(),
            $"speechmessage-dynamics-worker-{profileGenerationId}.pid");
        File.WriteAllText(
            evidencePath,
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 以 run-unique TEMP marker 原子地選出同一 generation 的第一個 Worker process。Factory 依序啟動
    /// 多個 Worker，因此第一個 claimant 走正常 READY，後續 claimant 走 invalid READY，讓測試可在
    /// 已成功取得 Worker 與 admission registration 後重現 partial-start rollback。marker 不含秘密、
    /// Pipe、Nonce、Token、Session 或 caller state，且由測試 finally 負責刪除。
    /// </summary>
    /// <param name="profileGenerationId">含 run-unique suffix 的安全 generation identifier。</param>
    /// <returns>目前 process 是否為第一個成功建立 marker 的 Worker。</returns>
    private static bool TryClaimFirstWorkerStart(string profileGenerationId)
    {
        var markerPath = Path.Combine(
            Path.GetTempPath(),
            $"speechmessage-dynamics-worker-{profileGenerationId}.first-start");
        try
        {
            using var marker = new FileStream(
                markerPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            marker.WriteByte(1);
            marker.Flush(flushToDisk: true);
            return true;
        }
        catch (IOException) when (File.Exists(markerPath))
        {
            return false;
        }
    }

    /// <summary>
    /// 建立只供 startup ownership 負向測試使用的單次 IPC session。它送出一個 nonce 錯誤但其餘
    /// 欄位合法的 READY frame 後立即結束，讓 Supervisor 保留原始 protocol failure 並完成 bounded cleanup。
    /// </summary>
    /// <param name="bootstrap">只含非機密啟動欄位的已驗證 bootstrap。</param>
    /// <returns>送出故意無效 READY 後的測試 Worker 結束碼。</returns>
    private static async Task<OfficialWorkerSessionExitCode> RunInvalidReadySessionAsync(
        WorkerBootstrapArguments bootstrap)
    {
        using Stream pipe = new NamedPipeOfficialWorkerConnector().Connect(bootstrap.PipeName);
        var limits = WorkerProtocolLimits.Default;
        var codec = new WorkerEnvelopeCodec(limits);
        var invalidReady = new WorkerReadyV1(
            WorkerProtocolVersion.Current,
            bootstrap.ProcessNonce + "-invalid",
            bootstrap.WorkerKind,
            bootstrap.PackageLockId,
            bootstrap.ProfileGenerationId,
            "9.1");
        await WorkerFrameCodec.WriteAsync(
            pipe,
            codec.SerializeReady(invalidReady),
            limits.MaximumFrameBytes,
            CancellationToken.None).ConfigureAwait(false);
        return OfficialWorkerSessionExitCode.CleanDrain;
    }

    /// <summary>
    /// 送出合法 READY 後短暫保留 parent，讓 Supervisor 完成發布；接著在尚未收到 request 前正常退出，
    /// 使 ExecuteAsync 的 worker-exit 原始結果與 automatic retirement cleanup 可被同一案例驗證。
    /// </summary>
    /// <param name="bootstrap">只含非機密啟動欄位的已驗證 bootstrap。</param>
    /// <returns>READY 發布後主動退出的測試結束碼。</returns>
    private static async Task<OfficialWorkerSessionExitCode> RunExitAfterReadySessionAsync(
        WorkerBootstrapArguments bootstrap)
    {
        using Stream pipe = new NamedPipeOfficialWorkerConnector().Connect(bootstrap.PipeName);
        var limits = WorkerProtocolLimits.Default;
        var codec = new WorkerEnvelopeCodec(limits);
        var ready = new WorkerReadyV1(
            WorkerProtocolVersion.Current,
            bootstrap.ProcessNonce,
            bootstrap.WorkerKind,
            bootstrap.PackageLockId,
            bootstrap.ProfileGenerationId,
            "9.1");
        await WorkerFrameCodec.WriteAsync(
            pipe,
            codec.SerializeReady(ready),
            limits.MaximumFrameBytes,
            CancellationToken.None).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
        return OfficialWorkerSessionExitCode.CleanDrain;
    }

    /// <summary>
    /// 建立只供 Supervisor 負向測試使用的單次 IPC session。此路徑先送出合法 READY，
    /// 再以固定且錯誤的 RequestId 回覆一個合法結果，讓測試可確定 Supervisor 會在
    /// 信任結果前比對要求識別碼。方法不建立背景工作、不保存要求資料，也不接觸任何機密。
    /// </summary>
    /// <param name="bootstrap">已由共用協定驗證且只含非機密啟動欄位的參數。</param>
    /// <returns>完成單次錯誤回覆後的測試 Worker 結束碼。</returns>
    private static async Task<OfficialWorkerSessionExitCode> RunWrongResponseRequestIdSessionAsync(
        WorkerBootstrapArguments bootstrap)
    {
        if (bootstrap.WorkerKind != OfficialWorkerKind.OfficialCrm91Worker ||
            !string.Equals(bootstrap.PackageLockId, PackageLockId, StringComparison.Ordinal))
        {
            return OfficialWorkerSessionExitCode.ProtocolFailure;
        }

        using Stream pipe = new NamedPipeOfficialWorkerConnector().Connect(bootstrap.PipeName);
        var limits = WorkerProtocolLimits.Default;
        var codec = new WorkerEnvelopeCodec(limits);
        var ready = new WorkerReadyV1(
            WorkerProtocolVersion.Current,
            bootstrap.ProcessNonce,
            bootstrap.WorkerKind,
            bootstrap.PackageLockId,
            bootstrap.ProfileGenerationId,
            "9.1");
        await WorkerFrameCodec.WriteAsync(
            pipe,
            codec.SerializeReady(ready),
            limits.MaximumFrameBytes,
            CancellationToken.None).ConfigureAwait(false);

        var requestPayload = await WorkerFrameCodec.ReadAsync(
            pipe,
            limits.MaximumFrameBytes,
            CancellationToken.None).ConfigureAwait(false);
        _ = codec.DeserializeRequest(requestPayload);
        var response = WorkerResponseV1.Success(
            WorkerProtocolVersion.Current,
            bootstrap.ProcessNonce,
            WrongResponseRequestId,
            CreateWhoAmIResult());
        await WorkerFrameCodec.WriteAsync(
            pipe,
            codec.SerializeResponse(response),
            limits.MaximumFrameBytes,
            CancellationToken.None).ConfigureAwait(false);
        return OfficialWorkerSessionExitCode.CleanDrain;
    }

    /// <summary>
    /// 建立固定且無機密的 WhoAmI 測試投影，供正常與負向 IPC 路徑共用。
    /// 回傳物件完全由三個有界 GUID 組成，不持有 stream、session 或背景資源。
    /// </summary>
    /// <returns>符合 WorkerProtocol 限制的 WhoAmI 物件。</returns>
    private static WorkerValue CreateWhoAmIResult()
    {
        return WorkerValue.FromObject(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["userId"] = WorkerValue.FromGuid(
                Guid.Parse("11111111-1111-1111-1111-111111111111")),
            ["businessUnitId"] = WorkerValue.FromGuid(
                Guid.Parse("22222222-2222-2222-2222-222222222222")),
            ["organizationId"] = WorkerValue.FromGuid(
                Guid.Parse("33333333-3333-3333-3333-333333333333"))
        });
    }

    /// <summary>
    /// 建立一頁一列的固定 Package01 fee 結果。十欄完全符合 SDK-free ordinal contract，
    /// 不含 CRM entity、formatted-value dictionary、endpoint、credential 或跨要求 mutable state。
    /// </summary>
    private static WorkerValue CreatePackage01FeeResult(int rowCount = 1)
    {
        if (rowCount < 1 || rowCount > Package01FeeWorkerContract.MaximumRowsPerPage)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        }

        var rows = new WorkerValue[rowCount];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = WorkerValue.FromArray(new[]
            {
                WorkerValue.FromGuid(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
                WorkerValue.FromUtcDateTime(
                    new DateTimeOffset(2026, 8, 1, 1, 2, 3, TimeSpan.Zero)),
                WorkerValue.FromUtcDateTime(
                    new DateTimeOffset(2026, 8, 2, 4, 5, 6, TimeSpan.Zero)),
                WorkerValue.FromDecimal(123.45m),
                WorkerValue.FromInt64(100000001),
                WorkerValue.FromString("Credit card"),
                WorkerValue.FromString("Dedication"),
                WorkerValue.FromString("bounded-note"),
                WorkerValue.FromString("2026-08"),
                WorkerValue.FromString("FEE-0001")
            });
        }

        return WorkerValue.FromArray(new[]
        {
            WorkerValue.FromArray(rows)
        });
    }

    /// <summary>
    /// 建立只超過 page-count 的小型結果，讓共用 Worker session 產生固定
    /// <c>crm.operation.result-too-large</c>，而非在測試程序配置大量 row 或 buffer。
    /// </summary>
    private static WorkerValue CreatePackage01PageOverflowResult()
    {
        var emptyPage = WorkerValue.FromArray(Array.Empty<WorkerValue>());
        return WorkerValue.FromArray(new[]
        {
            emptyPage,
            emptyPage,
            emptyPage,
            emptyPage,
            emptyPage
        });
    }

    /// <summary>
    /// 建立每個測試 Worker process 專用的 client。Factory 不保存 Profile、Credential、Session
    /// 或 request state；hang generation 只用於驗證 Supervisor 的 bounded forced-termination 路徑。
    /// </summary>
    private sealed class TestClientFactory : IOfficialCrmClientFactory
    {
        public IOfficialCrmClient Create(string profileGenerationId)
        {
            if (string.IsNullOrWhiteSpace(profileGenerationId))
            {
                throw new InvalidOperationException();
            }

            if (string.Equals(
                    profileGenerationId,
                    "profile-generation-hang",
                    StringComparison.Ordinal) ||
                profileGenerationId.StartsWith(
                    TimeoutGenerationPrefix,
                    StringComparison.Ordinal) ||
                profileGenerationId.StartsWith(
                    CancellationGenerationPrefix,
                    StringComparison.Ordinal))
            {
                return new HangingTestClient();
            }

            if (profileGenerationId.StartsWith(
                    CompleteFailureGenerationPrefix,
                    StringComparison.Ordinal))
            {
                return new CompleteFailureTestClient();
            }

            if (profileGenerationId.StartsWith(
                    Package01ResultTooLargeGenerationPrefix,
                    StringComparison.Ordinal))
            {
                return new Package01ResultTooLargeTestClient();
            }

            if (profileGenerationId.StartsWith(
                    Package01LargeValidGenerationPrefix,
                    StringComparison.Ordinal))
            {
                return new Package01LargeValidTestClient();
            }

            return new TestClient();
        }
    }

    /// <summary>
    /// 將 bootstrap 中唯一允許的官方 Worker kind 映射為 READY frame 的 CE 版本。
    /// 測試主機因此可同時模擬 CE 8.2 與 CE 9.1 子程序，但不共享任何 SDK、Credential、
    /// Session 或 mutable client；未知 kind 在 ProcessHost 建立前 fail closed。
    /// </summary>
    /// <param name="workerKind">已由長度與 enum parser 驗證的 Worker kind。</param>
    /// <param name="ceVersion">成功時回傳固定的版本字串；失敗時為空字串。</param>
    /// <returns>是否為本測試主機明確支援的兩個官方 Worker kind。</returns>
    private static bool TryGetCeVersion(
        OfficialWorkerKind workerKind,
        out string ceVersion)
    {
        switch (workerKind)
        {
            case OfficialWorkerKind.OfficialCrm82Worker:
                ceVersion = "8.2";
                return true;
            case OfficialWorkerKind.OfficialCrm91Worker:
                ceVersion = "9.1";
                return true;
            default:
                ceVersion = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// 經由正式 Worker session 路徑產生完整但已清理的 upstream failure。
    /// 本 client 不保存 request、結果、例外、Timer、Task 或任何跨 Session 狀態；Dispose 為無資源操作。
    /// </summary>
    private sealed class CompleteFailureTestClient : IOfficialCrmClient
    {
        public bool IsReady => true;

        public WorkerValue Execute(WorkerRequestV1 request) =>
            throw new InvalidOperationException();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 模擬無法觀察 managed cancellation 的 SDK/WCF 呼叫。此 client 不建立額外 Task、Timer 或
    /// shared state；Supervisor 只能在 bounded deadline 後終止隔離 Worker process，並以 OS exit
    /// 作為最終資源回收邊界，不能在同一 pipe replay 要求或切換其他 transport。
    /// </summary>
    private sealed class HangingTestClient : IOfficialCrmClient
    {
        public bool IsReady => true;

        public WorkerValue Execute(WorkerRequestV1 request)
        {
            Thread.Sleep(Timeout.Infinite);
            throw new InvalidOperationException();
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 只回傳一個小型 page-count overflow value；正式 Session 是唯一分類與序列化 owner。
    /// client 不保存 request、結果、Timer、Task、Stream 或其他可釋放資源。
    /// </summary>
    private sealed class Package01ResultTooLargeTestClient : IOfficialCrmClient
    {
        public bool IsReady => true;

        public WorkerValue Execute(WorkerRequestV1 request)
        {
            if (!IsExpectedPackage01Request(request))
            {
                throw new InvalidOperationException();
            }

            return CreatePackage01PageOverflowResult();
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 回傳三十列、超過通用 256 array-item 預設但仍符合 Package01 固定限制的結果，
    /// 用來驗證 ProcessHost 與 Supervisor 使用同一 operation-specific codec contract。
    /// </summary>
    private sealed class Package01LargeValidTestClient : IOfficialCrmClient
    {
        public bool IsReady => true;

        public WorkerValue Execute(WorkerRequestV1 request)
        {
            if (!IsExpectedPackage01Request(request))
            {
                throw new InvalidOperationException();
            }

            return CreatePackage01FeeResult(30);
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestClient : IOfficialCrmClient
    {
        private int _disposed;

        public bool IsReady => Volatile.Read(ref _disposed) == 0;

        public WorkerValue Execute(WorkerRequestV1 request)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new InvalidOperationException();
            }

            if (OfficialWorkerOperations.IsSupportedIdentityRequest(request))
            {
                if (string.Equals(
                        request.CapabilityOperationId,
                        OfficialWorkerOperations.RuntimePoolValidateConnection,
                        StringComparison.Ordinal) &&
                    (!request.Parameters.TryGetValue(
                            "logicalProfileId",
                            out var logicalProfileId) ||
                     !string.Equals(
                         logicalProfileId.Scalar,
                         "crm91-test",
                         StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException();
                }

                return CreateWhoAmIResult();
            }

            if (IsExpectedPackage01Request(request))
            {
                return CreatePackage01FeeResult();
            }

            throw new InvalidOperationException();
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
        }
    }

    /// <summary>
    /// 驗證 WorkerHost 已在 client 邊界前移除 optional contactName，且三個 QueryExpression
    /// 輸入仍維持精確 typed scalar。方法只讀單次 request snapshot，不保存任何 caller data。
    /// </summary>
    private static bool IsExpectedPackage01Request(WorkerRequestV1 request)
    {
        return string.Equals(
                request.CapabilityOperationId,
                Package01FeeWorkerContract.CapabilityOperationId,
                StringComparison.Ordinal) &&
            request.Parameters.Count == 3 &&
            !request.Parameters.ContainsKey("contactName") &&
            request.Parameters.TryGetValue("contactId", out var contactId) &&
            contactId.Kind == WorkerValueKind.Guid &&
            string.Equals(
                contactId.Scalar,
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                StringComparison.Ordinal) &&
            request.Parameters.TryGetValue("startDate", out var startDate) &&
            startDate.Kind == WorkerValueKind.UtcDateTime &&
            request.Parameters.TryGetValue("endDate", out var endDate) &&
            endDate.Kind == WorkerValueKind.UtcDateTime;
    }
}
