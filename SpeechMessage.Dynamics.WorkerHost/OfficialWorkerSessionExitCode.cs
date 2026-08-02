namespace SpeechMessage.Dynamics.WorkerHost;

public enum OfficialWorkerSessionExitCode
{
    CleanDrain = 0,
    ClientNotReady = 10,
    ProtocolFailure = 11,
    Cancelled = 12,
    UpstreamFailure = 13,
    ClientDisposeFailure = 14,
    UnexpectedFailure = 15
}
