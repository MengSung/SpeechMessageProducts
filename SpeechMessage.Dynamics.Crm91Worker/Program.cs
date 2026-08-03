using System;
using System.IO;
using System.Threading;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Crm91Worker;

/// <summary>
/// 組合 CE 9.1 官方 Microsoft NuGet Worker 的唯一程序進入點。
/// 程序只接受非機密 bootstrap 參數，並固定從可執行檔目錄讀取部署擁有的
/// <c>worker-profile.xml</c>；任何失敗只以結束分類回報，不輸出 Profile、Credential、
/// CRM 位址、例外內容或 Session 狀態。此型別不保存 static client、stream、timer、cancellation
/// registration 或 background callback；完整資源 graph 只存在於本次 <see cref="Main"/> 呼叫內。
/// </summary>
internal static class Program
{
    private const string PackageLockId = "crm91-xrmtooling-9.1.1.65-core-9.0.2.60";
    private const string CeVersion = "9.1";
    private const string ProfileFileName = "worker-profile.xml";

    /// <summary>
    /// 建立一個受限 Pipe session。Factory 只在建構階段暫時擁有 CRM client／Credential，成功後移交
    /// adapter；<see cref="OfficialWorkerSession"/> 在 message loop 結束後 Dispose adapter，
    /// <see cref="OfficialWorkerProcessHost"/> 則以 using 唯一擁有並關閉 named-pipe stream。
    /// <c>RunAsync</c> 的非同步工作在本方法同步等待到 terminal，不留下 fire-and-forget callback。
    /// 若同步 SDK 或 stream 無法在 Supervisor 的有限 drain deadline 內合作結束，Supervisor 會終止
    /// 本 worker process，作為 WCF channel、SDK static state、OS handle 與 process memory 的最終清理邊界。
    /// </summary>
    private static int Main(string[] arguments)
    {
        try
        {
            var profilePath = Path.Combine(AppContext.BaseDirectory, ProfileFileName);
            var factory = new OfficialCrmServiceClientFactory(
                new XmlWorkerProfileStore(profilePath),
                new WindowsCredentialManagerProvider());
            var host = new OfficialWorkerProcessHost(
                OfficialWorkerKind.OfficialCrm91Worker,
                PackageLockId,
                CeVersion,
                factory,
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
}
