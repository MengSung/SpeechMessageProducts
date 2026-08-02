namespace SpeechMessage.Dynamics.WorkerProtocol;

public enum WorkerProtocolFailureCategory
{
    InvalidFrameLength = 1,
    FrameTooLarge = 2,
    IncompleteFrame = 3,
    TrailingFrameData = 4,
    UnsupportedProtocolVersion = 5,
    InvalidProcessNonce = 6,
    DuplicateRequestId = 7,
    ExpiredDeadline = 8,
    UnknownOperation = 9,
    InvalidEnvelope = 10,
    EnvelopeLimitExceeded = 11
}
