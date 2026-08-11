using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 選取一個 immutable、獨立封裝且版本固定的 Official CRM Worker graph。
/// CE 8.2 與 CE 9.1 不共享 SDK assembly、credential、client、process 或可變 Session；要求失敗時禁止跨版本 fallback。
/// </summary>
public enum OfficialWorkerVersion
{
    /// <summary>使用 CE 8.2 專用 package lock 與 Worker process。</summary>
    Ce82 = 82,
    /// <summary>使用 CE 9.1 專用 package lock 與 Worker process。</summary>
    Ce91 = 91
}

/// <summary>
/// 將 deployment-facing 版本選擇轉為 protocol WorkerKind 與固定 CE major.minor 字串。
/// 未知 enum 值一律 fail closed，不建立 process，也不嘗試另一個版本。
/// </summary>
internal static class OfficialWorkerVersionExtensions
{
    /// <summary>轉換成與 package graph 一一對應的 WorkerKind。</summary>
    internal static OfficialWorkerKind ToWorkerKind(this OfficialWorkerVersion version) =>
        version switch
        {
            OfficialWorkerVersion.Ce82 => OfficialWorkerKind.OfficialCrm82Worker,
            OfficialWorkerVersion.Ce91 => OfficialWorkerKind.OfficialCrm91Worker,
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };

    /// <summary>轉換成啟動 Ready/identity 驗證使用的固定 CE major.minor。</summary>
    internal static string ToCeVersion(this OfficialWorkerVersion version) =>
        version switch
        {
            OfficialWorkerVersion.Ce82 => "8.2",
            OfficialWorkerVersion.Ce91 => "9.1",
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
}
