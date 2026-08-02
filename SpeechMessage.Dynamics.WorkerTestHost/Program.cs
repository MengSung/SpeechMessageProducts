using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerTestHost;

/// <summary>
/// 提供 Supervisor 測試專用、無網路與無 CRM SDK 的真實子程序。
/// 它只實作固定 WhoAmI 投影，讓測試驗證 Process、Pipe、READY、Drain 與 Dispose
/// 生命週期；不讀取 Credential、Profile、環境秘密或使用者 Session。
/// </summary>
internal static class Program
{
    internal const string PackageLockId = "test-worker-package-lock-0001";
    private const string WrongResponseRequestIdGeneration =
        "profile-generation-wrong-request-id";
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
            if (string.Equals(
                    bootstrap.ProfileGenerationId,
                    WrongResponseRequestIdGeneration,
                    StringComparison.Ordinal))
            {
                return (int)RunWrongResponseRequestIdSessionAsync(bootstrap)
                    .GetAwaiter()
                    .GetResult();
            }

            var host = new OfficialWorkerProcessHost(
                OfficialWorkerKind.OfficialCrm91Worker,
                PackageLockId,
                "9.1",
                new TestClientFactory(),
                new NamedPipeOfficialWorkerConnector(),
                OfficialWorkerOperations.CreateRevisionMap());
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
                    StringComparison.Ordinal))
            {
                return new HangingTestClient();
            }

            return new TestClient();
        }
    }

    /// <summary>
    /// Models an SDK/WCF call that cannot observe managed cancellation. The Supervisor must
    /// recover only by terminating the isolated worker process after its bounded deadline.
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

    private sealed class TestClient : IOfficialCrmClient
    {
        private int _disposed;

        public bool IsReady => Volatile.Read(ref _disposed) == 0;

        public WorkerValue Execute(WorkerRequestV1 request)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                request.CapabilityOperationId != OfficialWorkerOperations.RuntimeHealthWhoAmI ||
                request.Parameters.Count != 0)
            {
                throw new InvalidOperationException();
            }

            return CreateWhoAmIResult();
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
        }
    }
}
