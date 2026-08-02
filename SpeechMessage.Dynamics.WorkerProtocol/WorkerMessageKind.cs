namespace SpeechMessage.Dynamics.WorkerProtocol;

public enum WorkerMessageKind
{
    Request = 1,
    Ready = 2,
    Response = 3,
    Drain = 4
}
