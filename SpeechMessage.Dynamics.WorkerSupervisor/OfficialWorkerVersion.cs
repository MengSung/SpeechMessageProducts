using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// Selects one immutable, separately packaged official CRM worker graph.
/// </summary>
public enum OfficialWorkerVersion
{
    Ce82 = 82,
    Ce91 = 91
}

internal static class OfficialWorkerVersionExtensions
{
    internal static OfficialWorkerKind ToWorkerKind(this OfficialWorkerVersion version) =>
        version switch
        {
            OfficialWorkerVersion.Ce82 => OfficialWorkerKind.OfficialCrm82Worker,
            OfficialWorkerVersion.Ce91 => OfficialWorkerKind.OfficialCrm91Worker,
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };

    internal static string ToCeVersion(this OfficialWorkerVersion version) =>
        version switch
        {
            OfficialWorkerVersion.Ce82 => "8.2",
            OfficialWorkerVersion.Ce91 => "9.1",
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
}
