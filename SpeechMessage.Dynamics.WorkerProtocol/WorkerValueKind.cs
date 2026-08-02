namespace SpeechMessage.Dynamics.WorkerProtocol;

public enum WorkerValueKind
{
    Null = 0,
    Boolean = 1,
    Int64 = 2,
    Decimal = 3,
    String = 4,
    Guid = 5,
    UtcDateTime = 6,
    Array = 7,
    Object = 8
}
