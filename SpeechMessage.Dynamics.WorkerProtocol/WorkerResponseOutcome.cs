namespace SpeechMessage.Dynamics.WorkerProtocol;

public enum WorkerResponseOutcome
{
    Success = 0,
    InvalidRequest = 1,
    NotReady = 2,
    Timeout = 3,
    UpstreamFailure = 4,
    ProtocolFailure = 5
}
