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
/// CRM 位址、例外內容或 Session 狀態。
/// </summary>
internal static class Program
{
    private const string PackageLockId = "crm91-xrmtooling-9.1.1.65-core-9.0.2.60";
    private const string CeVersion = "9.1";
    private const string ProfileFileName = "worker-profile.xml";

    /// <summary>
    /// 建立一個受限 Pipe session。CRM client 與 Credential 的生命週期由 factory／adapter
    /// 及 <see cref="OfficialWorkerProcessHost"/> 唯一擁有；若 Supervisor 的啟動期限屆滿，
    /// Supervisor 會終止本程序，作為 WCF／SDK 無法合作取消時的最終清理邊界。
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
}
